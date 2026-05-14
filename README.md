# MyDiscord Server

The MyDiscord server is an ASP.NET Core 8 API that powers authentication, friends, servers, channels, messages, realtime chat, voice signaling, uploads, moderation, health checks, and background cleanup for the MyDiscord client.

## Requirements

- .NET 8 SDK.
- SQL Server or SQL Server LocalDB for local development.
- Azure CLI for Azure provisioning and deployment tasks.
- Node.js 20 or newer only when running the optional production load-test script.

## Project Layout

| Path | Purpose |
| --- | --- |
| `DiscordCloneServer.sln` | Solution containing the API and test projects. |
| `DiscordCloneServer/` | ASP.NET Core API project. |
| `DiscordCloneServer/Controllers/` | REST and WebSocket API endpoints. |
| `DiscordCloneServer/Hubs/` | SignalR hub for realtime chat. |
| `DiscordCloneServer/Models/` | EF Core entities and request/response models. |
| `DiscordCloneServer/Data/ApiContext.cs` | EF Core database context. |
| `DiscordCloneServer/Migrations/` | Database migrations. |
| `DiscordCloneServer/Services/` | Background jobs, rate limiting helpers, moderation, notifications, monitoring, and security services. |
| `DiscordCloneServer.Tests/` | xUnit test coverage for API behavior and service logic. |
| `SECURITY_SETUP.md` | Security-focused setup notes and secret handling checklist. |

## Local Setup

The commands below assume you start from the repository root, `MyDiscord`.

1. Restore dependencies:

   ```powershell
   cd DiscordCloneServer
   dotnet restore
   ```

2. Configure local secrets from the API project folder:

   ```powershell
   cd DiscordCloneServer
   dotnet user-secrets set "Jwt:Key" "replace_with_a_64_character_random_secret"
   dotnet user-secrets set "Jwt:Issuer" "DiscordCloneLocal"
   cd ..
   ```

3. Confirm the database connection string. LocalDB is configured by default in `DiscordCloneServer/appsettings.json`:

   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=DiscordClone;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
   }
   ```

   To use another SQL Server locally, set an environment variable before running the API:

   ```powershell
   $env:ConnectionStrings__DefaultConnection="Server=localhost;Database=DiscordClone;Trusted_Connection=True;TrustServerCertificate=True"
   ```

4. Start the API from the solution folder:

   ```powershell
   dotnet run --project DiscordCloneServer
   ```

The default HTTP profile listens on `http://localhost:5018`. In Development, Swagger is available at `http://localhost:5018/swagger`.

EF Core migrations run automatically at startup. To apply migrations manually instead:

```powershell
dotnet ef database update --project .\DiscordCloneServer\DiscordCloneServer.csproj
```

## Configuration

ASP.NET Core configuration can come from `appsettings.json`, `appsettings.Development.json`, user secrets, environment variables, or Azure App Service settings. Use double underscores for nested environment variables.

| Setting | Required | Purpose |
| --- | --- | --- |
| `Jwt:Key` / `Jwt__Key` | Yes | HMAC signing secret for access tokens. Store outside source control. |
| `Jwt:Issuer` / `Jwt__Issuer` | Yes | Token issuer and audience value. |
| `ConnectionStrings:DefaultConnection` / `ConnectionStrings__DefaultConnection` | Yes | SQL Server database connection string. |
| `Cors:AllowedOriginsCsv` / `Cors__AllowedOriginsCsv` | No | Comma-separated additional browser origins. Loopback origins and packaged Electron `file://` usage are handled by defaults. |
| `WebRtc:IceServers` / `WebRtc__IceServers__...` | Recommended | STUN/TURN servers for voice/video calls across networks. Replace development relay credentials for production. |
| `Cdn:BaseUrl` / `Cdn__BaseUrl` | No | Base URL for uploaded/static content when served from a CDN. |
| `RateLimiting:Policies` / `RateLimiting__Policies__...` | No | Per-policy sliding-window request limits. |
| `Monitoring:SlowRequestThresholdMs` / `Monitoring__SlowRequestThresholdMs` | No | Slow request warning threshold. |
| `Verification:Email` and `Verification:Sms` | No | Optional webhook/API-key delivery settings for contact verification. |
| `Notifications:Email` | No | Optional webhook/API-key delivery settings for email notifications. |
| `Account:ExposePasswordResetToken` / `Account__ExposePasswordResetToken` | Development only | Returns raw reset tokens in API responses for local tooling. Do not enable in production. |

Production startup enables forwarded proxy headers, HTTPS redirection, HSTS, and conservative response security headers. This keeps generated invite/webhook URLs and secure cookies correct behind Azure App Service or another TLS-terminating proxy.

## API Surface

The API exposes controller routes under `/api/...`, a SignalR hub at `/chatHub`, and a voice WebSocket endpoint through the voice controller. The client uses JWT bearer tokens and can supply tokens through authorization headers, the `access_token` query string for realtime transports, or the `token` cookie.

Useful health endpoints:

```text
GET http://localhost:5018/api/Health
GET http://localhost:5018/api/Health/live
GET http://localhost:5018/api/Health/ready
GET http://localhost:5018/api/Health/metrics
GET http://localhost:5018/api/Health/profile?top=20
```

`/api/Health/ready` returns `503` when required dependencies such as the database are unavailable. `/api/Health/metrics` returns Prometheus-style counters and gauges.

## Database And Migrations

The app uses EF Core with SQL Server. Keep generated migrations in `DiscordCloneServer/Migrations/` and review them before committing. The API calls `Database.Migrate()` on startup, so deployed instances apply pending migrations when they start.

For production, use managed database backups or the Azure SQL backup policy in `../infra/azure/main.bicep`. Do not rely on the API process to create database backup files.

## Testing And QA

Run the same checks used by CI:

```powershell
cd DiscordCloneServer
$env:Jwt__Key="ci_secret_key_that_is_long_enough_for_hmac_sha256"
$env:Jwt__Issuer="DiscordCloneCI"
dotnet restore
dotnet test --no-restore
```

Run a release-mode verification before deployment:

```powershell
dotnet test DiscordCloneServer.sln --configuration Release
dotnet publish DiscordCloneServer/DiscordCloneServer.csproj --configuration Release --output ..\_server_build_check\server
```

Manual smoke checks:

- Start the API and confirm `/api/Health/live`, `/api/Health/ready`, and Swagger load.
- Register and log in from the Electron client.
- Create a server, send messages, upload an image, and verify realtime chat updates.
- Join a voice channel from two accounts and confirm voice diagnostics report TURN readiness.
- Confirm rate-limited endpoints return HTTP `429` with a `Retry-After` header when limits are exceeded.

## Deployment

### Azure App Service

The main production path is Azure App Service plus Azure SQL. From the repository root, provision infrastructure with:

```powershell
az login
az group create --name rg-mydiscord-prod --location uksouth

az deployment group create `
  --resource-group rg-mydiscord-prod `
  --name mydiscord-prod `
  --template-file infra/azure/main.bicep `
  --parameters @infra/azure/main.parameters.example.json `
  --parameters namePrefix=mydiscord-prod `
  --parameters sqlAdminPassword="<strong SQL password>" `
  --parameters jwtKey="<64+ character random secret>"
```

The Bicep template creates the App Service, Azure SQL database, Key Vault, managed identity, Application Insights, Log Analytics, diagnostic settings, app settings, and Key Vault references. The GitHub Actions workflow `.github/workflows/azure-appservice.yml` restores, tests, publishes, and deploys the API package to App Service.

Required GitHub repository secrets:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_WEBAPP_NAME`

### Non-Azure Hosting

Publish the API and deploy the output folder to any host that supports ASP.NET Core 8:

```powershell
cd DiscordCloneServer
dotnet publish DiscordCloneServer/DiscordCloneServer.csproj --configuration Release --output .\publish
```

Set `ASPNETCORE_ENVIRONMENT=Production`, `Jwt__Key`, `Jwt__Issuer`, `ConnectionStrings__DefaultConnection`, and any optional TURN, CORS, CDN, verification, or notification settings in the host's secret/configuration store.

## Release Checklist

- Run `dotnet test` and a Release publish locally or in CI.
- Verify production secrets live in Key Vault, CI secrets, or host secret storage only.
- Confirm the database backup and restore process has been tested.
- Confirm `Cors__AllowedOriginsCsv` includes any browser-hosted frontend origins.
- Confirm the Electron client `apiBase` points at the deployed API before packaging the client.
- Run a smoke load test from `../tools/load-tests/production-load-test.mjs` against the deployed URL.

## Troubleshooting

- Startup fails with missing JWT settings: set `Jwt:Key` and `Jwt:Issuer` through user secrets, environment variables, or Key Vault.
- Login succeeds but later authenticated calls fail: confirm the session row still exists and the server clocks are in sync.
- `/api/Health/ready` returns `503`: check the SQL connection string, firewall, credentials, and database availability.
- Browser requests fail with CORS errors: add the frontend origin to `Cors__AllowedOriginsCsv`.
- Voice calls fail across networks: configure private TURN credentials under `WebRtc:IceServers`.

For the complete setup, Azure deployment, backup, monitoring, and load-test runbook, see `../DEPLOYMENT.md`.
