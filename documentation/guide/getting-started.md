# Getting started

API Gateway consists of the gateway runtime and a management service. Management serves the administration UI at `/admin/`, documentation at `/docs/`, and GraphQL at `/graphql`. ApiGateway accepts application traffic. If a reverse proxy replaces Management's Content Security Policy, allow the generated VitePress inline bootstrap scripts under `/docs/` or configure equivalent script hashes.

For local development, start Management first, create an environment and a route, then start ApiGateway with the same SQLite connection string and environment slug. Environments use Immediate publishing by default. Select Staged publishing when several configuration edits should be reviewed and activated together.

```powershell
dotnet run --project src/ApiGateway.Management
dotnet run --project src/ApiGateway
```

SQLite supports one Management and one ApiGateway process for development. With the default Visual Studio project working directories, ApiGateway uses `../ApiGateway.Management/apigateway.db` so both processes share Management's database. If either working directory changes, configure both connection strings to resolve to the same SQLite file. Use SQL Server for production and multi-instance deployments.

To run the administration UI with Vite hot reload, start the management project using its default VS or `dotnet run` launch profile, then run:

```powershell
yarn workspace api-gateway-admin dev
```

Vite proxies authentication and GraphQL requests to `http://localhost:61551`, the management project's default HTTP launch address. Set `API_GATEWAY_MANAGEMENT_URL` before starting Vite when the management host uses another address.

## Docker Compose

Copy `.env.example` to `.env`, replace `MSSQL_SA_PASSWORD` with a strong local password, and start the deployment:

```powershell
Copy-Item .env.example .env
docker compose up --build --detach --wait
```

Compose starts SQL Server, Management, two ApiGateway instances, and a sample upstream. Open `http://localhost:5080/admin/` to create the local administrator. The gateway endpoints are `http://localhost:5070/` and `http://localhost:5071/`.
