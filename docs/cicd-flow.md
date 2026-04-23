# CI/CD Flow Design

## Goals

- Prevent unauthorised modification of CI/CD workflow files from reaching production
- CI tests the exact SHA that gets deployed (no merge drift)
- Multiple independent security controls so no single point of bypass

The primary threat is not an unauthorised person triggering a deploy — it is someone modifying `.github/workflows/` to bypass or exploit the pipeline. Every other control in this design flows from that threat model.

---

## Security Controls

| Control | Enforced by | Threat addressed |
|---|---|---|
| Workflow file changes require PR approval | GitHub branch protection + CODEOWNERS | Prevents silent tampering of CI/CD config |
| CI must pass before merge | GitHub required status checks | Prevents broken code reaching main |
| Only main branch code can deploy | AWS IAM OIDC `sub` condition | Prevents deploys from feature branches |
| Only the specific CD workflow file can deploy | AWS IAM OIDC `job_workflow_ref` condition | Prevents a rogue workflow from assuming the deploy role |

These controls are layered. The GitHub-side controls (branch protection, CODEOWNERS) are guardrails enforced within GitHub. The AWS-side controls (OIDC conditions) are enforced externally by AWS and cannot be bypassed by anything running inside a GitHub Actions workflow.

---

## Flow

```
feature/* push
    │
    ├─ [optional] manual CI trigger (workflow_dispatch)
    │   for developer self-testing before raising PR
    │
    ▼
open PR against main
    │
    ▼
PR event → CI runs on merge preview SHA
    │
    ├─ CI must pass         (GitHub required status check — blocks merge if red)
    └─ manual PR approval   (GitHub branch protection — N approvals required)
    │
    ▼ both conditions met
merge → push to main
    │
    ▼
CI runs on merged SHA
(triggered by push to main — this is the SHA that will be deployed)
    │
    ├─ CI fails → stop, CD is not triggered
    └─ CI passes
            │
            ▼
        CD triggered via workflow_run
        (conclusion == 'success' check gates the deploy job)
            │
            ▼
        CD assumes AWS role via OIDC
        (IAM condition rejects any token not from refs/heads/main)
            │
            ▼
        deploy to prod
```

---

## Why Tag-Based Promotion Was Rejected

An earlier design used Git tags to drive environment promotion (`ci-passed/vX.Y.Z` → `rc/vX.Y.Z` → `live`). This was abandoned for the following reasons.

### Tag creation is not reliably gateable on the free tier

GitHub tag protection rules can restrict which actors can create tags, but the `GITHUB_TOKEN` issued to every workflow can be granted tag write permission. A compromised workflow could create its own trigger tag, bypassing the intended gate. There is no way to say "only workflow A may create tags matching this pattern" — the controls are actor-based and coarse.

This is a **product maturity gap**, not a fundamental architectural limitation. GitHub does have the chokepoint for its own resources (tags, branches, PRs) in the same way AWS IAM controls access to AWS resources. The gap is that GitHub's controls are less composable and granular than AWS IAM, and the most useful ones (environment required reviewers for private repos) are paywalled behind the Team plan.

### The enforcement boundary matters

AWS IAM works as a security primitive because it is evaluated entirely outside the system being controlled. A workflow cannot modify its own IAM trust policy at runtime. GitHub tag protection is enforced by GitHub — the same platform the workflows run on — which creates a weaker trust boundary.

For external deployment targets like AWS, enforcement must live at the destination. GitHub-side controls are guardrails; the real gate must be at the system with independent authority.

### Conclusion

Tag-based promotion is a useful deployment pattern but not a reliable security control on GitHub's free tier. The current design instead uses:
1. PR approval (human gate on what enters main) — enforced by GitHub branch protection
2. OIDC `sub` + `job_workflow_ref` conditions (deployment gate) — enforced by AWS IAM independently of GitHub

---

## Key Design Decisions

### CI runs twice: on the PR, and again after merge

- **PR CI** — runs on the merge preview SHA; gives the reviewer confidence before approving
- **Post-merge CI** — runs on the actual merged SHA on main; this is what CD deploys

This eliminates merge drift: the SHA that CI validates is identical to the SHA that CD receives.

### Required status checks are mandatory, not advisory

Requiring CI pass before merge is enforced by branch protection, not left to reviewer discretion. This removes a class of human error where a reviewer approves without noticing CI is red.

### CD workflow has no `workflow_dispatch` trigger

CD can only be triggered by a successful CI run on main. There is no manual shortcut to kick off a deployment, which prevents accidental or unauthorised deploys.

### Why public repo is acceptable for this project

GitHub Environment required reviewers (a GitHub-native manual gate before a deployment job runs) requires the Team plan for private repositories. Since this is a school project, repositories are public, which makes environment protection rules available on the free plan.

In a production context, this would be justified as: upgrade to GitHub Team (or GitHub Enterprise) to keep repositories private while retaining environment-level approval gates.

---

## GitHub Configuration

### Branch Protection Settings (main)

- Require a pull request before merging
  - Required approvals: 1 (or more)
  - Dismiss stale reviews when new commits are pushed
  - Require review from Code Owners
- Require status checks to pass before merging
  - Required check: CI workflow
  - Require branches to be up to date before merging
- Restrict who can push directly to main (no direct pushes)

### CODEOWNERS

Create `.github/CODEOWNERS` to require that changes to workflow files are approved by a specific trusted person, regardless of who else reviews the PR:

```
# Any change to CI/CD workflow files must be approved by a designated owner.
# Combined with "Require review from Code Owners" in branch protection,
# this prevents workflow tampering from being merged without targeted review.
.github/workflows/ @trusted-github-username
```

This is more targeted than general PR approval. A reviewer approving application code changes may not scrutinise a workflow file change closely — CODEOWNERS ensures someone designated explicitly reviews it.

**Attack path this closes:** An attacker submits a large PR with a subtle malicious change buried in `.github/workflows/cd.yml`. A general reviewer approves the application code without noticing the workflow change. CODEOWNERS blocks the merge until the designated workflow owner also approves.

---

## AWS IAM OIDC Trust Policy

```json
{
    "Version": "2012-10-17",
    "Statement": [
        {
            "Effect": "Allow",
            "Principal": {
                "Federated": "arn:aws:iam::921142537307:oidc-provider/token.actions.githubusercontent.com"
            },
            "Action": "sts:AssumeRoleWithWebIdentity",
            "Condition": {
                "StringEquals": {
                    "token.actions.githubusercontent.com:aud": "sts.amazonaws.com"
                },
                "StringLike": {
                    "token.actions.githubusercontent.com:sub": "repo:NUS-ISS-MTech-SE33-PT/review-service:ref:refs/heads/main"
                }
            }
        }
    ]
}
```

The `sub` condition locks deployment to the `main` branch of this specific repository. A token issued to any other branch or repository cannot assume this role.

### Future Hardening — Workflow-Scoped OIDC (not yet applied)

The current policy trusts any workflow running on main. A more precise condition is to also pin the specific workflow file that is allowed to deploy, using the `job_workflow_ref` claim:

```json
{
    "Version": "2012-10-17",
    "Statement": [
        {
            "Effect": "Allow",
            "Principal": {
                "Federated": "arn:aws:iam::921142537307:oidc-provider/token.actions.githubusercontent.com"
            },
            "Action": "sts:AssumeRoleWithWebIdentity",
            "Condition": {
                "StringEquals": {
                    "token.actions.githubusercontent.com:aud": "sts.amazonaws.com",
                    "token.actions.githubusercontent.com:job_workflow_ref": "NUS-ISS-MTech-SE33-PT/review-service/.github/workflows/cd.yml@refs/heads/main"
                },
                "StringLike": {
                    "token.actions.githubusercontent.com:sub": "repo:NUS-ISS-MTech-SE33-PT/review-service:ref:refs/heads/main"
                }
            }
        }
    ]
}
```

With `job_workflow_ref`, only the exact `cd.yml` workflow file on main can assume the role. Any other workflow on main — even one manually crafted to trigger a deploy — will be rejected by AWS. This brings GitHub workflow identity closer to the granularity of an AWS ECS task role: not just "which repo and branch" but "which specific workflow."

Both conditions must be satisfied simultaneously:
- `sub` — the token comes from main branch of this repo
- `job_workflow_ref` — the token was issued to the specific CD workflow file on main

---

## CD Workflow — Critical Snippet

```yaml
on:
  workflow_run:
    workflows: ["CI"]
    types: [completed]
    branches: [main]

jobs:
  deploy:
    # Guard: only run if CI actually passed, not just completed
    if: ${{ github.event.workflow_run.conclusion == 'success' }}
    ...
```

Without the `conclusion == 'success'` check, a failed CI run on main would still trigger the deploy job — and it would succeed. The OIDC condition only checks the branch ref, not the CI result, so the assume-role call would go through and CD would deploy broken code.

---

## Audit Trail

### Current State

The `prod` git tag was originally designed as part of a tag-based promotion security control — a single pointer answering "what commit is running in prod right now?" It is intentionally force-pushed on each deployment because only the current state matters for that purpose. It is not an audit log and was never intended to be one.

The tag-based security approach was abandoned (see above), but the `prod` tag retains its operational value as a current-state pointer.

The actual audit trail gap is the absence of deployment history — no record of past deployments, no linkage between a deployment and the PR that caused it, and no way to answer "what was running in prod two weeks ago?" without manually scrolling GHA run history.

### Audit-Generating Operations and Immutability

| Operation | Where recorded | Visible in GitHub? | Mutable? |
|---|---|---|---|
| Every workflow run (CI and CD) | GitHub Actions run history | Yes | Deletable by repo admin; auto-purged after 90 days |
| Every `git push` to main (merge commits) | Git history | Yes | Immutable — SHA-addressed, cannot be rewritten without force push (which branch protection blocks) |
| Every Docker image push | AWS ECR | No — AWS only | SHA-tagged images are permanent until explicitly deleted; `latest` tag is overwritten each deploy |
| Every AWS API call (assume-role, ECR push, ECS update) | AWS CloudTrail (S3 bucket) | No — AWS only | Immutable by default; can be hardened further with S3 Object Lock on the CloudTrail bucket |
| `prod` git tag | Git tag | Yes | Mutable — force-pushed on each deploy; only reflects current state |
| GitHub deployment objects | GitHub Deployments API | Yes | Deletable by a repo admin. If repo admin access is compromised, records can be removed — but at that point the entire repository is compromised anyway |

### Immutability Summary

- **Strongest:** AWS CloudTrail — records every AWS API call with actor, timestamp, and parameters. Tamper-proof if S3 Object Lock is enabled on the trail bucket. A compromised GHA workflow cannot delete its own CloudTrail entries.
- **Strong:** Git commit history and SHA-tagged ECR images — content-addressed, cannot be silently altered.
- **Moderate:** GitHub Actions run history — complete logs per run, but deletable by a repo admin.
- **Weak:** `prod` git tag — intentionally mutable, not an audit record.

### Recommended Improvement — GitHub Deployment Objects

Adding `environment: production` to the CD job causes GitHub to automatically create a deployment record for every run:

```yaml
jobs:
  deploy:
    name: Deploy to Production
    environment: production    # creates a GitHub deployment record automatically
```

This provides:
- Full deployment history linked to SHA, timestamp, and triggering workflow run
- Visual deployment timeline on the repo's main page
- Answer to "what is currently deployed" natively in GitHub — making the `prod` tag redundant

GitHub deployment records are append-only. Each deployment creates a new record; past records are not overwritten.
