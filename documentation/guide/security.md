# Security

Management supports multiple local users, LDAP, OpenID Connect, Microsoft Entra ID, and scoped management API keys. LDAP and OIDC providers are configured from **Access > Users**, can automatically provision users, and can assign default or mapped product roles. Provider secrets are encrypted with the management data-protection keys. Proxy routes can be anonymous or require consumer API keys or JWTs. Store generated keys immediately because the plaintext secret is returned only once.

Microsoft Entra ID is optional and runs alongside local authentication. Configure it from **Access > Users** in the administration console. The deployment values `Authentication__Entra__Authority`, `Authentication__Entra__Audience`, `Authentication__Entra__ClientId`, and `Authentication__Entra__Scope` provide initial defaults when no administration-managed connection has been saved. The Microsoft sign-in button appears only when the connection is enabled and all four values are present. Register the management origin with `/admin/` as a single-page application redirect URI, expose the configured delegated scope, and assign users or groups the `Reader`, `Publisher`, or `Administrator` app role. `Publisher` is the compatibility role name for live configuration management.

The administration UI keeps a newly generated secret visible until you confirm that it has been saved. After that confirmation, only its non-secret prefix remains available. Revocation takes effect in Management immediately and converges to ApiGateway instances on their credential polling interval.

Management API keys use one or more of these exact scopes:

- `config:read`
- `config:manage`
- `instances:read`
- `credentials:read`
- `credentials:write`
- `audit:read`
- `system:admin`

Unknown scopes are rejected. Use the smallest scope set needed by the automation. `system:admin` grants unrestricted administrative access.

Existing keys with `config:publish` retain live configuration access as a compatibility alias. `config:write` does not grant live changes and should be replaced with `config:manage` when rotating older keys.

Keys may be restricted to one or more IPv4 or IPv6 CIDR ranges. Source-address checks run only after the secret has matched a stored verifier. When API Gateway is behind a reverse proxy, list each trusted proxy IP in `ForwardedHeaders__KnownProxies__0`, `ForwardedHeaders__KnownProxies__1`, and so on before relying on source-network restrictions. Forwarded headers from other sources are ignored. An empty CIDR list permits requests from any source address.

Use HTTPS in deployed environments. Store connection strings, certificate passwords, and other secrets in the deployment secret store. Published gateway documents refer to upstream certificates by operator-defined name and never contain private keys or passwords.

Inbound PKCS#12 material is encrypted with a dedicated Data Protection key ring configured by `CertificateProtection__KeysPath`. Mount the same protected, persistent location into Management and every data-plane replica, and back it up with the database. Losing the key ring makes uploaded certificates unrecoverable.

Let's Encrypt account keys, generated certificate private keys, and DNS-provider credentials use separate protection purposes in that same key ring. Every production or staging ACME account has an independent key and can be deleted only when no managed certificate references it. Secrets are never returned by the management API. Restrict DNS credentials to the zones and TXT-record permissions required for ACME validation. Rotating a DNS profile replaces its encrypted credentials only after the provider connection succeeds.

HTTP-01 challenge responses are available only for an active token, the expected `Host`, and `GET /.well-known/acme-challenge/{token}`. They expire automatically and are evaluated before normal proxy routing. DNS-01 removes only the TXT value created for the current challenge, preserving unrelated TXT values at the same name.

HSTS is configured under **System > Settings > Inbound security** as one global policy containing exact or wildcard hostnames. The policy is shared across environments. Every HTTPS response for a covered hostname receives the same policy, regardless of which route handles the path. The route editor reports effective coverage and shared-host usage in the selected environment, but does not create a conflicting route-local policy. Verify HTTPS and certificate coverage for every affected hostname before enabling HSTS, especially when using `includeSubDomains` or preload.

JWT-protected routes accept only trusted HTTPS authorities. Configure issuer, audience, required claims, and a clock skew from zero through fifteen minutes in the route authorization policy. Consumer API keys and JWT bearer tokens are evaluated from the active in-memory policy catalog, so proxy requests do not query the management database.
