# Contract: Container runtime (Dockerfile)

**Type**: Infrastructure artifacts at repo root — `Dockerfile`, `.dockerignore`.

**Purpose**: Produce a single Linux container that runs the Blazor Server app directly on Kestrel,
binds the agreed port, and keeps all demo state ephemeral (resets on cold start).

## Build contract

| # | Requirement | Maps to |
|---|-------------|---------|
| 1 | Multi-stage: build with `mcr.microsoft.com/dotnet/sdk:8.0` (`dotnet restore` + `dotnet publish -c Release`), run with `mcr.microsoft.com/dotnet/aspnet:8.0` | Article VIII (publish) |
| 2 | SDK pinned to the repo's `global.json` | reproducibility, FR-014 |
| 3 | Entry point runs the web app directly (`dotnet DBAIAzure.Web.dll`) — no nginx/supervisor (single Kestrel process) | research Decision 6 |
| 4 | `ASPNETCORE_URLS=http://+:8080` (or the agreed port); `EXPOSE 8080` | binding, FR-001 |
| 5 | No volume mounts; SQLite path resolves to the container's writable working dir (ephemeral) | FR-016 |
| 6 | No secret values baked into any image layer; only the published app + runtime | FR-006, Article IX |
| 7 | `.dockerignore` excludes `bin/`, `obj/`, `.git/`, `tests/`, `specs/`, any `team.env`/`.env`, and local DB files | hygiene, Article IX |

## Runtime contract

| # | Requirement | Maps to |
|---|-------------|---------|
| 8 | On start, the app builds an empty SQLite schema (`EnsureCreatedAsync`) and runs `DemoConnectorSeeder` before serving | FR-005, FR-016 |
| 9 | The container serves HTTP on the bound port for ACA external ingress to front with HTTPS | FR-001, FR-017 |
| 10 | No login/auth is added at the container layer | FR-017 |
| 11 | Data Protection uses a pinned application name but an ephemeral (non-persisted) key location | research Decision 3 |

## Verification (local smoke test — Article X)

```
docker build -t dbai-azure-demo .
docker run --rm -p 8080:8080 \
  -e ASPNETCORE_URLS=http://+:8080 \
  -e <ServiceNow/ADO/Messaging demo env vars with throwaway values> \
  dbai-azure-demo
# Then: GET http://localhost:8080 returns the app; back-office connectors show configured;
#       the LLM connector prompts for a key; no secret value appears in container logs.
```
