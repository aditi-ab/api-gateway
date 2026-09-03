# API Gateway

API Gateway is a .NET 10 and YARP reverse proxy with route-first configuration, immutable change history, a Hot Chocolate GraphQL management API, and a Vue 3 and Vuetify 4 administration application.

## Prerequisites

- .NET SDK 10
- Node.js 20.19 or later, or Node.js 22.12 or later
- Yarn 1.22
- Docker Desktop when using SQL Server, Docker Compose, or container tests

Clone the repository together with its Aditify dependency:

```sh
git clone --recurse-submodules https://github.com/aditi-ab/api-gateway.git
```

For an existing clone, run `git submodule update --init --recursive` before installing dependencies.

Run all commands below from the `ApiGateway` directory unless stated otherwise.

## Install and build

```powershell
dotnet restore ApiGateway.slnx
yarn install --frozen-lockfile
dotnet build ApiGateway.slnx --no-restore
yarn build
```

`yarn build` builds the administration application into the Management host and builds the VitePress documentation site.

## Local development with SQLite

SQLite is intended for one Management process and one ApiGateway process. Both processes must use the same database file. When Visual Studio starts each process from its project directory, ApiGateway's default connection points to `../ApiGateway.Management/apigateway.db`, which is the database created by Management. An absolute path remains the most reliable option for command-line launches from other working directories.

In every terminal that starts a backend process, set the same connection string:

```powershell
$gatewayDatabase = Join-Path (Resolve-Path .) "apigateway.db"
$env:DatabaseProvider = "Sqlite"
$env:ConnectionStrings__Gateway = "Data Source=$gatewayDatabase"
```

### 1. Migrate the database

Development startup applies migrations automatically. You can inspect and apply migrations explicitly:

```powershell
dotnet run --project .\src\ApiGateway.Management -- database status
dotnet run --project .\src\ApiGateway.Management -- database migrate
```

To create the standard `development`, `staging`, and `production` environment records, run the optional idempotent development seed:

```powershell
dotnet run --project .\src\ApiGateway.Management -- database seed-development
```

The seed does not create an administrator, credentials, drafts, or published configuration.

### 2. Start Management

```powershell
dotnet run --project .\src\ApiGateway.Management
```

The default launch profile serves:

- Admin and authentication backend: `http://localhost:61551/admin/`
- GraphQL: `http://localhost:61551/graphql`
- Documentation: `http://localhost:61551/docs/`
- Liveness and readiness: `http://localhost:61551/healthz` and `http://localhost:61551/readyz`

On a new database, the first visit to Admin creates the one local administrator. Later visits show the normal sign-in form. Passwords must contain 12 to 128 characters and must not contain the username.

### 3. Start the Admin development server

In another terminal:

```powershell
yarn workspace api-gateway-admin dev
```

Open `http://localhost:5173/admin/`. Vite proxies authentication, configuration, and GraphQL requests to Management at `http://localhost:61551`.

If Management is listening elsewhere, set the target before starting Vite:

```powershell
$env:API_GATEWAY_MANAGEMENT_URL = "http://localhost:YOUR_PORT"
yarn workspace api-gateway-admin dev
```

If the sign-in page reports `Unexpected token '<'`, the Vite server is returning its HTML fallback because it cannot proxy to Management. Confirm that Management is running, verify its HTTP port, set `API_GATEWAY_MANAGEMENT_URL` if necessary, and restart Vite.

### 4. Create a route

Sign in to Admin, select or create an environment whose slug matches the ApiGateway `Gateway__Environment` value, then create a route with a name, incoming path, and upstream URL. The route is validated and activated immediately. The default gateway environment is `development`.

### 5. Start ApiGateway

In another terminal, set the same absolute SQLite connection string shown above, then run:

```powershell
dotnet run --project .\src\ApiGateway
```

The default launch profile serves:

- Proxy traffic: `http://localhost:61553/`
- Liveness: `http://localhost:61553/healthz`
- Readiness: `http://localhost:61553/readyz`

ApiGateway is live but not ready until it installs a valid published revision for its configured environment. Configuration and consumer-key changes normally converge within five seconds.

## Running from Visual Studio

Set `ApiGateway.Management` and `ApiGateway` as startup projects. Their default HTTP ports are `61551` and `61553` respectively.

The checked-in development defaults share `src/ApiGateway.Management/apigateway.db`: Management uses `Data Source=apigateway.db`, while ApiGateway uses `Data Source=../ApiGateway.Management/apigateway.db`. If you change the working directories or database location, configure both launch profiles with connection strings that resolve to the same file. Start Management first, complete administrator bootstrap and create a route, then start ApiGateway. Run the Admin Vite server separately when hot reload is required.

Files written during local execution include the SQLite database, Management Data Protection keys, and ApiGateway last-known-good state. Do not commit these files.

## SQL Server and migrations

SQL Server is required for production, high availability, or multiple Management or ApiGateway replicas. Set the provider explicitly. Provider selection is never inferred from the connection string.

```powershell
$env:DatabaseProvider = "SqlServer"
$env:ConnectionStrings__Gateway = "Server=localhost;Database=ApiGateway;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True"

dotnet run --project .\src\ApiGateway.Management -- database status
dotnet run --project .\src\ApiGateway.Management -- database migrate
```

Run `database migrate` as a deployment step before starting updated production replicas. Production startup does not apply migrations automatically. Only Management owns the migration command. ApiGateway never migrates the database.

For a published Management artifact, use the equivalent commands:

```powershell
dotnet ApiGateway.Management.dll database status
dotnet ApiGateway.Management.dll database migrate
```

`database status` exits with code 2 when migrations are pending.

## Docker Compose development environment

Compose starts SQL Server, Management, two ApiGateway instances, and a sample upstream:

```powershell
Copy-Item .env.example .env
# Replace MSSQL_SA_PASSWORD in .env with a strong local password.
docker compose up --build --detach --wait
```

Open `http://localhost:5080/admin/`. ApiGateway is available at `http://localhost:5070/` and `http://localhost:5071/`.

Useful commands:

```powershell
docker compose ps
docker compose logs -f management gateway-one gateway-two
docker compose down
```

## Tests and validation

```powershell
dotnet test ApiGateway.slnx
yarn type-check
yarn test
yarn build:admin
yarn docs:build
```

Install the Playwright browser once before running browser tests:

```powershell
yarn workspace api-gateway-browser-tests playwright install chromium
yarn test:e2e
```

The browser workflow starts its own isolated Management host and database.

## Common development notes

- Restart Vite after changing `vite.config.ts` or `API_GATEWAY_MANAGEMENT_URL`.
- A database migration does not remove an existing administrator. Bootstrap appears only when no local administrator exists.
- Management must be ready before Admin API calls work.
- ApiGateway readiness returning 503 usually means no usable revision has been published for its environment, or a required secret reference cannot be resolved.
- SQLite is not supported for multiple replicas. Use SQL Server to test convergence, heartbeats, drift, and high availability.
- Keep Management Data Protection keys and ApiGateway last-known-good state persistent when testing restarts or database outages.

Customer and operator documentation is under [`documentation/`](documentation/). Build it with `yarn docs:build`.

## License

API Gateway is licensed under the [Apache License 2.0](LICENSE).
