# -----------------------------
# Build Stage
# -----------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0-noble AS build
WORKDIR /src

COPY . .
RUN dotnet publish review-service.csproj -c Release -o /app/out

# -----------------------------
# Runtime Stage
# -----------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled AS runtime
WORKDIR /app

COPY --from=build /app/out .

ENTRYPOINT ["dotnet", "review-service.dll"]
