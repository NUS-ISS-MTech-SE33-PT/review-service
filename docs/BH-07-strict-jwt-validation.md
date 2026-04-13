# BH-07 Strict JWT Validation

## Goal

Harden authenticated review APIs so they only accept properly validated Cognito access tokens.

## Functional Design

- Add standard ASP.NET Core `JwtBearer` authentication to `review-service`.
- Validate:
  - JWT signature
  - issuer
  - lifetime
  - allowed `client_id`
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
- Unit tests confirm `JwtSubjectResolver` only returns `sub` from an authenticated principal.

## TODO List

- Configure `JwtBearer` with strict Cognito validation rules.
- Move user resolution to validated claims.
- Protect authenticated routes with `RequireAuthorization()`.
- Add integration tests for valid and invalid JWT cases.
- Run `dotnet test` and `dotnet build` before commit.
