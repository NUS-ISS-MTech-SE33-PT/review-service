# BH-07 Strict JWT Validation

## Goal

Harden authenticated review APIs so they only accept properly validated Cognito access tokens.

## Architecture Review Impact

- Shared API Gateway and Cognito now live in the separate `terraform/` repository.
- The review service no longer owns edge authorizer configuration, but it must stay aligned with the shared Cognito audiences.
- Both mobile and admin web Cognito app clients can now appear at the edge, so the service allowlist must include both client IDs by default.

## Functional Design

- Add standard ASP.NET Core `JwtBearer` authentication to `review-service`.
- Validate:
  - JWT signature
  - issuer
  - lifetime
  - allowed `client_id` for both active app clients
  - `token_use == access`
  - presence of `sub`
- Require authorization at the route layer for all authenticated endpoints.
- Resolve the caller identity from the authenticated principal only.
- Remove the previous fallback that parsed an unvalidated bearer token payload to extract `sub`.

## Test Design

- `GET /reviews/health` remains public and returns `200`.
- `GET /users/me/reviews` returns `401` when no token is present.
- `GET /users/me/reviews` returns `401` when `client_id` is not on the allowlist.
- `GET /users/me/reviews` returns `401` when `token_use` is `id`.
- `GET /users/me/reviews` returns `200` for a valid signed access token.
- `GET /users/me/reviews` returns `200` for the secondary allowed app client as well.
- Unit tests confirm `JwtSubjectResolver` only returns `sub` from an authenticated principal.

## TODO List

- Configure `JwtBearer` with strict Cognito validation rules.
- Move user resolution to validated claims.
- Keep the default allowlist aligned with shared Cognito mobile + admin web client IDs.
- Protect authenticated routes with `RequireAuthorization()`.
- Add integration tests for valid and invalid JWT cases.
- Run `dotnet test` and `dotnet build` before commit.
