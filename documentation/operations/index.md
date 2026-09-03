# Operations

`/healthz` reports process liveness. Data-plane `/readyz` succeeds when a validated snapshot is installed, even during a temporary database outage. Management readiness requires a database connection.

Production uses SQL Server, persistent Data Protection keys, persistent per-instance last-known-good storage, and HTTPS. Set `CertificateProtection__KeysPath` to one persistent key ring shared by Management and every ApiGateway replica. The data plane explicitly listens for HTTP on port `8080` and HTTPS on port `8443` by default. Change them with `Gateway__InboundTls__HttpPort` and `Gateway__InboundTls__HttpsPort`; the two values must be different. Unknown SNI hostnames are refused on the HTTPS listener.

For Let's Encrypt HTTP-01, map public TCP port 80 to the configured HTTP listener on every load-balanced gateway replica. DNS for each requested hostname must reach that listener. Management requires outbound HTTPS access to Let's Encrypt and the configured DNS-provider APIs. DNS-01 uses direct authoritative DNS queries over port 53 when available and falls back to Cloudflare and Google DNS-over-HTTPS over port 443. Allow both outbound DNS and HTTPS for the clearest propagation result.

The ACME worker checks queued work once per minute and ACME Renewal Information every twelve hours. A renewable database lease allows only one Management replica to coordinate ACME work, while that replica processes up to four certificate orders concurrently by default. Configure the limit with `Acme__MaxConcurrentOrders`. Deleting a certificate during DNS propagation is detected during the next propagation poll. The worker then removes its TXT value and frees that processing slot. If a pending certificate remains overdue, inspect the Management logs for `ACME certificate maintenance failed`. If an issuing or renewing process is interrupted, another replica marks the attempt as failed after `Acme__InProgressTimeout`, cleans up persisted DNS challenge values when possible, and applies the normal retry delay instead of immediately creating another order. Recovery is supported with both SQLite and SQL Server. The default timeout is thirty minutes. Lease duration and renewal defaults are controlled by `Acme__LeaseDuration` and `Acme__LeaseRenewalInterval`.

Production and staging directories are available concurrently through `Acme__DirectoryUrl` and `Acme__StagingDirectoryUrl`. They default to the corresponding Let's Encrypt directories. Changing a configured URL does not move existing accounts or managed certificates to another ACME server. Optional duration overrides are `Acme__WorkerInterval`, `Acme__RenewalInfoInterval`, `Acme__HttpChallengePropagationDelay`, `Acme__DnsPropagationTimeout`, `Acme__DnsPropagationPollInterval`, `Acme__OrderFinalizationTimeout`, and `Acme__OrderFinalizationPollInterval`. DNS propagation is checked every ten seconds for up to six hours by default. After automatic TXT creation, the Management log records the DNS name, exact challenge value, provider, and managed certificate ID at `Information` level. Progress is then logged initially and every five minutes, and a timeout includes the last sanitized lookup result. Order finalization honors the CA's `Retry-After` value and otherwise polls every two seconds for up to five minutes. Renewal failures are visible in Certificates, persisted certificate activity, audit history, and management logs. The activity view refreshes while open and reports query failures in the dialog. CA finalization rejections preserve a sanitized problem detail. Ordinary failures retry after increasing delays with randomized jitter. ACME rate-limit responses honor a supplied retry time and otherwise wait twenty-four hours. A manual retry cannot bypass an active rate-limit delay, and attempts cannot be requested more than once every five minutes.

Manual DNS-01 keeps the issuance attempt active while an administrator adds the displayed TXT value. It does not call a configured DNS-provider API and does not remove the manually created value. Remove obsolete challenge values from the DNS control panel after issuance. Each scheduled renewal produces a new value and requires another manual action. Use automatic DNS-01 for unattended renewal.

Management and gateway processes log EF Core database commands only at `Warning` level or above by default. Override `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command` when temporary SQL command diagnostics are required.

Run database migrations as a deployment step before replacing Management or ApiGateway instances:

```powershell
dotnet ApiGateway.Management.dll database status
dotnet ApiGateway.Management.dll database migrate
```

`database status` exits with code 2 when migrations are pending. Production startup never applies migrations. `Gateway__ApplyMigrationsOnStartup=true` is supported only in the Development environment. Data-plane instances refuse database-backed activation when known migrations are pending, while retaining an already installed or last-known-good snapshot.

For a local Development environment, `database seed-development` idempotently creates the `development`, `staging`, and `production` environment records. It does not create users, credentials, drafts, or published configuration.

The container images default to SQLite at `/app/state/apigateway.db`. They also default cryptographic keys, certificate state, and the data plane's last-known-good bundle to paths below `/app/state`. A single named volume can therefore be shared by one Management container and one ApiGateway container without repeating those settings:

```bash
docker volume create api-gateway-data
docker run --rm -v api-gateway-data:/app/state \
  ghcr.io/aditi-ab/api-gateway-management:1.0.42 database migrate
docker run -d --name api-gateway-management --restart unless-stopped \
  --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m \
  -p 9000:8080 -e ASPNETCORE_ENVIRONMENT=Development \
  -v api-gateway-data:/app/state \
  ghcr.io/aditi-ab/api-gateway-management:1.0.42
docker run -d --name api-gateway --restart unless-stopped \
  --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m \
  -p 80:8080 -p 443:8443 \
  -e Gateway__InstanceId=api-gateway-local \
  -v api-gateway-data:/app/state \
  ghcr.io/aditi-ab/api-gateway:1.0.42
```

This SQLite layout supports one Management process and one data-plane process. Use SQL Server when running multiple replicas. Development permits direct HTTP first-login setup. Set `ASPNETCORE_ENVIRONMENT=Production` only when Management is accessed through trusted HTTPS, because its production authentication cookies are secure-only.

### Windows Server Core LTSC 2025 images

Both API Gateway images are also published as `windows/amd64` images based on `mcr.microsoft.com/windows/servercore:ltsc2025`. Windows tags have the `-windowsservercore-ltsc2025` suffix, for example `1.0.42-windowsservercore-ltsc2025`. The rolling `windowsservercore-ltsc2025` tag is available for evaluation. Run them on a compatible Windows container host.

```powershell
docker pull ghcr.io/aditi-ab/api-gateway-management:1.0.42-windowsservercore-ltsc2025
docker pull ghcr.io/aditi-ab/api-gateway:1.0.42-windowsservercore-ltsc2025
docker volume create api-gateway-data
docker run --rm `
  --mount type=volume,src=api-gateway-data,dst=C:\app\state `
  ghcr.io/aditi-ab/api-gateway-management:1.0.42-windowsservercore-ltsc2025 database migrate
docker run -d --name api-gateway-management --restart unless-stopped `
  -p 9000:8080 `
  -e ASPNETCORE_ENVIRONMENT=Development `
  --mount type=volume,src=api-gateway-data,dst=C:\app\state `
  ghcr.io/aditi-ab/api-gateway-management:1.0.42-windowsservercore-ltsc2025
docker run -d --name api-gateway --restart unless-stopped `
  -p 80:8080 -p 443:8443 `
  -e Gateway__InstanceId=api-gateway-local `
  --mount type=volume,src=api-gateway-data,dst=C:\app\state `
  ghcr.io/aditi-ab/api-gateway:1.0.42-windowsservercore-ltsc2025
```

The Windows images run as the restricted built-in Network Service identity, include self-contained .NET runtimes, and keep their shared SQLite and cryptographic state below `C:\app\state`.

Set `DataProtection__KeysPath` to storage shared by all management replicas. The directory contains the keys used to protect login cookies, antiforgery tokens, and OpenAPI preview tokens. Losing or separating this key ring invalidates those protected values. Restrict access to the management service identity and back it up with the rest of the management state.

The ApiGateway development Compose deployment mounts this directory from the `management-keys` volume. Each ApiGateway instance also needs durable storage for its configured last-known-good bundle path. Copy `.env.example` to `.env` and set `MSSQL_SA_PASSWORD` before starting that Compose deployment. The application containers run as non-root users with read-only root filesystems; only their state volumes and temporary filesystems are writable.

The Instances page shows each instance heartbeat, activated revision, and latest activation result. Failed activation records contain a stable error code and a sanitized operator message. Detailed exception information remains in ApiGateway logs. A failed activation does not replace the last working proxy snapshot.

Database backups include active history and any unpublished configuration change set. Restore the database consistently before starting Management or gateway replicas. In a Staged environment, review a restored pending change set before publishing or discard it if it is no longer intended. Operational route state changes are already present in the active history even when other edits remain unpublished.

Each instance heartbeat also reports its current active request counts by route. Management aggregates counts from instances with a recent heartbeat. During route draining, wait for the aggregated count to reach zero before stopping or updating the normal upstream destinations. Long-running streams and WebSocket requests remain active until they finish or the client disconnects.

Retention maintenance runs once per day by default. It keeps activation events for 30 days and audit events for 365 days. Configure `Retention__Enabled`, `Retention__ActivationDays`, `Retention__AuditDays`, and `Retention__Interval` on the management service. A database lease ensures that one replica performs each cleanup, and every completed cleanup writes an audit event with deleted counts. Administrators can also run the same leased cleanup from Settings after confirming the cutoff values.

Run `dev/verify-compose.ps1` from PowerShell to build the containers and verify publication and proxy traffic through both SQL Server-backed ApiGateway instances. The script removes its containers and volumes after a successful or failed run. Pass `-KeepRunning` to leave the deployment available for inspection.

If SQL Server becomes unavailable after activation, ApiGateway readiness remains successful and the installed snapshot continues serving. On restart, an ApiGateway instance loads its protected last-known-good bundle from durable state. If neither the database nor a usable bundle can provide a validated configuration, liveness succeeds but readiness returns an error.
