# One-URL Azure Container demo image for DBAIAzure.Web (Blazor Server). Single Kestrel process — no
# nginx/supervisor (the reference needs those only to multiplex Python processes). All demo state is
# ephemeral: the SQLite file and the Data Protection key ring live under a writable HOME and are NOT
# persisted, so every cold start is a fresh demo (FR-016). No secret value is baked into any layer.

# ── Build stage ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy source and restore only the web app's dependency graph (Core/Connectors/Storage/Processes).
COPY src/ ./src/
RUN dotnet restore src/DBAIAzure.Web/DBAIAzure.Web.csproj
RUN dotnet publish src/DBAIAzure.Web/DBAIAzure.Web.csproj -c Release -o /app/publish --no-restore

# ── Runtime stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# The aspnet:8.0 image already ships a non-root 'app' user. Give it a writable HOME that backs both
# the Data Protection key ring ($HOME/.config/...) and the ephemeral SQLite file — neither is mounted,
# so both reset on every cold start (FR-016).
ENV HOME=/home/app \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Storage__SqlitePath=/home/app/pipeline.db \
    DataProtection__KeyRingPath=/home/app/keys
RUN mkdir -p /home/app && chown -R app:app /home/app

WORKDIR /app
COPY --from=build /app/publish ./
USER app

EXPOSE 8080
ENTRYPOINT ["dotnet", "DBAIAzure.Web.dll"]
