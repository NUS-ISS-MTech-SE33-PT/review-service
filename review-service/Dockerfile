# -----------------------------
# Build Stage
# -----------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

COPY . .
RUN dotnet publish -c Release -o /app/out

# -----------------------------
# Runtime Stage
# -----------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
# temporarlly pin zlib to 1.3.2-r0 to avoid scanning failure in Snyk, see
# https://security.snyk.io/vuln/SNYK-ALPINE323-ZLIB-15435528
RUN apk update && apk add --no-cache zlib=1.3.2-r0
WORKDIR /app

COPY --from=build /app/out .

ENTRYPOINT ["dotnet", "review-service.dll"]
