# Repository Guidelines

## Project Structure & Ownership
- `review-service.sln` is the solution entry point.
- `review-service/` contains the minimal API, DTOs, JWT/auth helpers, and DynamoDB repository code.
- `review-service.UnitTests/` contains NUnit-based integration-style tests using `WebApplicationFactory`.
- Shared API Gateway, Cognito, and other edge infrastructure are owned by the separate `../terraform` repository, not by a service-local Terraform folder.

## Build, Test, and Development Commands
- `dotnet restore review-service.sln`
- `dotnet build review-service.sln`
- `dotnet test review-service.sln`
- `dotnet run --project review-service/review-service.csproj`

## Coding Rules
- Keep minimal API route logic in `Program.cs` readable; move auth/config logic into dedicated helper classes when it grows.
- Treat `HttpContext.User` and API Gateway-injected headers as downstream inputs from validated edge auth. Do not reintroduce raw JWT payload parsing.
- Keep JWT validation options configurable through `appsettings.*` and test overrides instead of hardcoding client IDs in endpoint code.

## Security Notes
- `review-service` protected endpoints must only trust validated access tokens.
- If Cognito app clients or audiences change, update the matching config here and the shared `terraform/` repo together.
