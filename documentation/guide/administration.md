# Administration UI

Open `/admin/` on the management host. The first visit asks you to create the local administrator. Deployments configured for Microsoft Entra ID can also use Entra identities. The sign-in page adapts to narrow screens without horizontal scrolling, and its centered product header identifies the management console consistently with signed-in pages.

The Overview page separates the selected environment into gateway posture, service health, and recent activity. Gateway posture summarizes configured and online routes, reporting instances, and unhealthy, drifted, or stale runtime signals. Service health shows publication state, instance health, configuration convergence, and the management version. Recent activity lists the latest configuration changes and links to the complete immutable history. Select a posture card to open its corresponding management page, or use **Add a route** when the environment has no activity.

The second header row contains the primary navigation followed by a compact environment selector at the far right. The current environment is shown directly in the control without a separate visible label. Overview, Routes, and Activity are direct links. Access groups users plus consumer and management keys. System groups environments, gateway instances, certificates, audit history, and settings. Each page starts with a consistent section label, title, supporting text, and right-aligned actions that wrap below the heading on narrow screens. The header remains horizontally accessible on smaller screens and supports English, Swedish, and system, light, or dark themes.

## Users and identity

Open **Access > Users** to manage local accounts and LDAP, OpenID Connect (OIDC), and Microsoft Entra providers on one page. LDAP providers use username and password sign-in. OIDC providers use the authorization-code flow and an HTTPS authority or metadata URL. Administrators can set default product roles and map directory or token role values to `Reader`, `Publisher`, or `Administrator`. Automatic provisioning must be enabled before a previously unknown external user can sign in. Local sign-in remains available as a recovery path.

LDAP bind passwords and OIDC client secrets are encrypted with the management service data-protection keys. Persist those keys across restarts. For OIDC, register `/admin/auth/external/{provider-id}/callback` on the management origin as the redirect URI. Test a provider before enabling it.

The user and provider workflow is shared with the other Aditi administration consoles. It uses the same responsive tables, display names, one-time password dialog, role selector, provider form, validation, and confirmation behavior. Changing only a local user's display name does not end that user's current sessions. The existing Entra connection is disabled instead of deleted.

Microsoft Entra users and groups remain in the organization's directory. Assign them the `Reader`, `Publisher`, or `Administrator` application role in Microsoft Entra. The gateway validates those role assignments at sign-in and does not copy directory accounts into its local-user table.

Choose English or Swedish from the language menu. The shell, every active administration page, dialogs, field guidance, empty states, validation messages, and confirmation actions follow the selected language. Cards, tables, dialogs, expansion panels, grouped settings, and scrollbars use matching theme-aware colors in both light and dark themes. Contextual alerts use a softer tinted treatment so they remain distinct without competing with primary actions. Page changes use a brief transition, which is removed when the operating system requests reduced motion.

Interactive route-state controls use semantic colors so online, enabled, draining, maintenance, offline, and disabled states remain distinguishable alongside their text and icons.

Controls use a compact, minimal treatment with visible keyboard focus, subdued borders, and clear enabled, loading, validation, and destructive states. Contextual actions in tables use the same compact tonal treatment throughout the administration UI, with semantic styling for destructive actions. Dismissible dialogs provide a close button in addition to their workflow actions, while required decisions remain persistent. Confirmations remain in focused application dialogs. Long dialogs keep their title and actions visible while their content scrolls. Menus close after a choice so keyboard and pointer workflows behave consistently.

## Create a route

Open **Routes**, select the environment, and choose **Add route**. A new installation has no environments. Until one is created, environment-specific pages such as Overview, Routes, Activity, route details, and route traffic settings show a **Create environment** action, and route actions remain unavailable. Enter:

- A descriptive name.
- The incoming path, such as `/orders`.
- Either an absolute HTTP or HTTPS upstream URL, or a reusable named Upstream.

**Match this path and all subpaths** is enabled for new routes. API Gateway appends the terminal `/{**remainder}` catch-all pattern when it saves the route, so `/orders` matches `/orders`, `/orders/123`, and deeper paths. Disable the switch when only the exact path should match. Existing routes show the switch according to their saved path pattern.

In an Immediate environment, choose **Create and activate**. In a Staged environment, choose **Create as unpublished**. API Gateway generates the technical identifiers and validates the complete configuration. New routes accept every HTTP method, preserve the request path, and allow anonymous traffic until you configure additional restrictions.

Configure the mode from **System > Environments**. Staged environments show a header badge and, after the first edit, a persistent unpublished-changes drawer at the bottom of the administration UI. Expand **Review** to inspect the combined change list and validation result without leaving the current page. **Publish** activates the complete set as one revision. **Discard changes** removes the complete unpublished set. Publication notes appear with the resulting history entry.

Use the switch in the Routes overview to enable or disable a route without opening it. The change is validated, activated, and recorded in Activity immediately. Disabling a route removes it from matching while retaining its settings.

On the route detail page, use the **Enabled** or **Disabled** header action to change availability. The Routing card groups the incoming path with incoming hosts, and the upstream URL with upstream path handling, so each side of the traffic mapping can be reviewed together.

Use **Traffic state** when the route must continue matching but should not use its normal features or upstream destinations. **Online** forwards normally. **Draining** lets requests already in flight finish while every newly arriving request receives the configured unavailable response. **Maintenance** and **Offline** apply the same route override with different operational labels. Active request counts are aggregated from live gateway-instance heartbeats and may take one heartbeat interval to reach zero.

The recommended unavailable response is a shared gateway-hosted `503 Service Unavailable` profile. In **System > Settings**, configure an optional `Retry-After` duration, page title, and message, then assign the profile as the environment default for one or more traffic states. Browsers that accept HTML receive a small encoded status page, while other clients receive a JSON error. `HEAD` receives headers without a body. As an alternative, create a profile with a dedicated maintenance upstream URL. It replaces the normal route destinations while the override is active and must return the intended status response itself.

The route traffic-state dialog selects the state and either inherits its environment default or names a profile override. Editing a shared profile updates every inheriting or explicitly linked route in one atomic configuration change.

Use **Incoming hosts** in the main Routing section to restrict the route to one or more hostnames. Add each hostname as a chip, such as `example.com` or `*.example.com`, without a scheme or path. Internationalized hostnames can be entered in Unicode or punycode form and are stored in canonical punycode form. Equivalent Unicode and punycode entries are treated as the same hostname. The gateway converts them as required for route publication and normalizes the TLS SNI hostname before selecting the route certificate. Leave the field empty to accept every host. Open **Advanced matching** to restrict HTTP methods, set precedence, or add header and query-parameter conditions. All configured conditions must match.

Use **Upstream path handling** in the main Routing section when the public path and upstream path differ. **Preserve incoming path** forwards the matched path unchanged. **Remove path prefix** strips the configured leading segment before forwarding and preserves the query string. For example, use incoming path `/sub/{**remainder}`, upstream URL `https://another-example.com/`, and prefix `/sub` to forward `/sub/orders?active=true` as `https://another-example.com/orders?active=true`.

**Preserve incoming Host header** appears directly below **Allow WebSocket upgrades** and is enabled by default for new routes. It sends the incoming public hostname as the upstream `Host` header, which supports upstreams that select a site by hostname, such as an IIS site with host bindings. Disable it when the upstream expects the hostname from its destination URL instead. YARP continues to provide the public hostname in `X-Forwarded-Host`.

The Routes overview summarizes each incoming-to-upstream flow. Select a row to expand or collapse its traffic details, and use **Edit** to open the route configuration. Use **Duplicate** to enter a name and copy the route's complete matching, upstream, feature, traffic-state, and inbound TLS configuration. The copy receives a new technical ID and is always created disabled, allowing it to be reviewed before it receives traffic. Incoming test URLs include the configured HTTP or HTTPS scheme and omit a trailing catch-all placeholder such as `{**remainder}`. Select an incoming URL or upstream destination to open it in a new browser tab. Routes without a concrete incoming hostname, including routes that only use wildcard hosts, cannot provide a test link. Expanded details also show allowed methods, every destination, load-balancing policy, and enabled gateway features. The route header summarizes the incoming hosts, path, allowed methods, upstream destinations, technical ID, route version, traffic state, and active request count.

Open **Advanced upstream** to add destinations and select a load-balancing policy. The Upstream URL remains the primary destination. Each additional destination has a route-local name, absolute HTTP or HTTPS URL, optional health URL, and pool. Destination names must be unique within the route.

## Reusable upstreams

Open **Upstreams** to create a named server group that can be selected by multiple routes. Each Upstream contains one or more uniquely named HTTP or HTTPS servers, optional per-server health URLs, a YARP load-balancing policy, and active or passive health-check settings. Editing an Upstream changes the effective destinations for every route that selects it in the same validated configuration change.

In the route form, select **Enter a URL directly** to retain the route-local workflow, or select a named Upstream. A route that uses a named Upstream continues to own its incoming matching, path handling, TLS, traffic state, and gateway features. Servers, upstream protocol behavior, health checks, and load balancing are managed on the Upstream instead. An Upstream cannot be deleted while a route references it.

## Inbound HTTPS and certificates

Administrators manage PKCS#12 certificates under **System > Certificates**. Certificate passwords are used only while importing the file. Use the edit action to change the display name of an uploaded or managed certificate. The certificate material, covered hostnames, issuer, validity period, ACME account, and renewal settings are not changed by renaming it. Save and validation errors appear inside the active certificate dialog, which remains open with the entered values available for correction. Select incoming hosts before assigning a certificate to a route. Certificate assignments are validated against certificate DNS names, and routes using the same hostname must select the same certificate.

The same page can register separate Let's Encrypt production and staging accounts and issue managed certificates through either account. Each directory has its own protected account key. Production is selected by default, while staging certificates are clearly marked as untrusted test certificates. A managed certificate retains its selected account for every renewal. HTTP-01 supports ordinary DNS names and requires every requested hostname to reach an ApiGateway HTTP listener on public port 80. Automatic DNS-01 supports wildcard names and uses a reusable Cloudflare, Amazon Route 53, Azure DNS, Google Cloud DNS, DigitalOcean, Loopia, or Simply.com profile. Internationalized domain names may be entered in their Unicode form; ApiGateway matches them to DNS-provider zones using their canonical ASCII representation. Provider credentials are verified by listing manageable zones before they are saved. Use the profile's test action to create and immediately remove a randomized TXT record in one managed zone. This verifies both write and cleanup permissions. Create a Simply.com API key under **Account > API keys** and enter it in the API token field. ApiGateway uses the key to list active products with DNS domains and to create and remove only its own TXT challenge values. Loopia API users require `getDomains`, `getSubdomains`, `addSubdomain`, `addZoneRecord`, `getZoneRecords`, and `removeZoneRecord` permissions. ApiGateway creates a missing Loopia challenge subdomain, publishes challenge records with a 300-second TTL for compatibility with Loopia's authoritative DNS service, and removes only its own TXT value while preserving unrelated records.

Choose **DNS-01 (manual)** when no supported provider can update the zone. Open certificate details after the attempt starts, then copy the displayed TXT name and value into the DNS control panel. Append the value without replacing unrelated TXT values. ApiGateway observes authoritative DNS and continues automatically. The challenge value disappears from the UI after validation or expiry. Manual DNS certificates still receive scheduled renewal attempts, but every renewal requires the new displayed TXT value to be added manually.

Certificate details also show the active TXT name and value for automatic DNS-01. If the provider reports that it created a record but the value is absent from authoritative DNS, append the displayed value manually while the attempt is still active.

Issuance and renewal run in the background. Up to four certificate orders are processed concurrently by default, so a slow DNS provider does not block unrelated certificates. **Issuing** and **Renewing** cover the entire active ACME operation, including creating an order, publishing and observing a challenge, waiting for CA validation and finalization, and storing the resulting certificate. For DNS-01, ApiGateway checks every authoritative name server every ten seconds for up to six hours by default before asking the CA to validate. It falls back to public DNS-over-HTTPS resolvers when direct authoritative queries are unavailable. The Management log records propagation progress and distinguishes a missing value from unavailable outbound DNS connectivity. It follows the CA's requested polling interval while validation and certificate finalization are processing. Finalization waits for up to five minutes by default. ApiGateway stores the leaf certificate and the exact issuer chain returned by the selected ACME directory in the resulting PKCS#12 certificate. The certificate list shows pending, issuing, renewing, valid, and failed states together with the next attempt and a sanitized failure message. A CA rejection includes its sanitized problem detail. Open certificate details to see persisted, timestamped issuance activity with the latest event first, plus recovery guidance. Activity refreshes while the dialog is open, and an overdue pending request identifies the Management log message to inspect. Use **Renew now** or **Retry** when an immediate attempt is required. Deleting a certificate during DNS propagation stops that attempt, removes its TXT value, and frees its processing slot. A failed renewal leaves the currently active certificate in service. Assign a successfully issued certificate through the existing route editor.

**HTTP and HTTPS when available** preserves earlier routes, **HTTP only** rejects HTTPS requests, and **HTTPS, redirect HTTP** returns a permanent `308` redirect that preserves the host, path, and query. WebSocket upgrades are allowed by default and can be disabled per route. The HTTPS listener supports HTTP/1.1 and HTTP/2.

HSTS is disabled by default. Configure explicit hosts, max-age, subdomain coverage, and preload under **System > Settings > Inbound security**. HSTS is a global policy shared across environments and keyed by hostname because browsers apply it to a host, not to an individual route or path. The route form shows whether its incoming hosts are covered and identifies other affected routes in the selected environment. Do not enable HSTS for a hostname that must remain available over HTTP.

## Add gateway features

Configured gateway features have an enable switch on the route page. Switch a feature off to stop applying it while retaining all of its settings. Switching it on again restores the saved configuration, so temporary changes do not require removing and recreating the feature. Removing a feature still deletes its configuration.

Open a route and choose **Add feature**. Features are grouped by security, traffic control, transformations, reliability, and validation. Configured features appear in a table with their current summary and contextual configure and remove actions. Each feature has a focused form that reloads its saved values when reopened. Saving validates the route, then activates it immediately or adds it to the environment's unpublished change set according to the publishing mode. Editing a header or path transform preserves unrelated YARP transforms on the route.

Available features include API-key and JWT authentication, CIDR restrictions, rate limiting, request-size limits, request and response header manipulation, path and query transforms, CORS, timeouts, safe retries, circuit breaking, request validation, response caching, and traffic mirroring.

Response caching can cache eligible anonymous `GET` and `HEAD` assets in each gateway process. API Gateway does not include a signature-based web application firewall. Place a maintained WAF in front of the gateway when exploit-signature inspection is required.

## Local users

Open **Access > Local users** to create local accounts and assign `Reader`, `Publisher`, or `Administrator`. New and reset passwords are generated and shown once. Disabling, deleting, resetting, or changing roles ends existing sessions. The final enabled administrator cannot be disabled, deleted, or demoted.

Feature dialogs use selections, bounded numeric fields, and examples where the gateway knows the valid shape. For traffic mirroring, choose another configured route from the list or enter a route ID that is not currently visible in the selected environment.

Each feature dialog starts with a compact information note that explains when to use the feature, what its main values control, and relevant request-handling or safety behavior.

Destructive and state-changing actions use an in-application confirmation dialog that identifies the action and its effect. Activity, audit, credential, instance, and revision timestamps use the consistent `yyyy-MM-dd HH:mm:ss` format.

## Activity and rollback

Activity shows who changed a route, when it changed, and whether gateway instances have converged. Choose **Revert this change** to apply the inverse change to the current configuration. Unrelated later changes are preserved. If the same route changed later, API Gateway reports a conflict instead of overwriting that work.

Full configuration restore, canonical JSON export, import, and environment copy are advanced operations. Restoring creates another immutable history entry, so it can also be undone.

## Credentials and operations

Consumer keys authenticate proxy requests and can be restricted by environment, route, expiry, claims, and source CIDR. Management keys authenticate GraphQL automation. Generated and rotated secrets are shown once.

Gateway instances shows heartbeat, deployment convergence, drift, and sanitized activation failures. Audit contains security and administrative operations. Settings separates route traffic defaults, inbound hostname security, and management information using dedicated tabs.
