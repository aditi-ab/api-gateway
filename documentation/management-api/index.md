# Management API

The GraphQL endpoint is `/graphql`. Use `X-Management-Api-Key` for automation. Management credentials and consumer credentials are separate. Administrators can manage `localUsers`, `inboundCertificates`, and `inboundSecuritySettings`; certificate mutations accept bounded base64-encoded PKCS#12 data and return metadata only.

Let's Encrypt automation uses `acmeDirectories`, `acmeAccounts`, `dnsProviderProfiles`, and `managedCertificates`. The singular `acmeDirectory` and `acmeAccount` queries remain compatibility views of the production directory and default account. Register an account for a configured directory with `registerAcmeAccount`; update, select the default, or delete unused accounts with `updateAcmeAccount`, `setDefaultAcmeAccount`, and `deleteAcmeAccount`. Manage encrypted DNS profiles with the create, update, test, and delete DNS-provider mutations. `issueAcmeCertificate` accepts an optional `acmeAccountId` and queues asynchronous issuance through that account; omitting it uses the default account. Use `MANUAL_DNS01` without a provider profile to request manual TXT handling. While any DNS-01 attempt is active, `managedCertificateDnsChallenges` returns the non-secret TXT name, value, and expiry to administrators. This also permits manual recovery of an automatic provider-backed attempt. `renewAcmeCertificate` always retains the certificate's original account and validation method. `deleteManagedCertificate` removes automation plus an unassigned certificate. Managed-certificate results include account and staging metadata, status, DNS names, validation method, next attempt, sanitized failure details, and optional inbound-certificate metadata. Secret fields are accepted only as inputs and are never returned.

Certificate automation may instead use `GET /api/admin/certificates/`, `POST /api/admin/certificates/`, `PUT /api/admin/certificates/{id}`, and `DELETE /api/admin/certificates/{id}`. Upload and replacement use bounded `multipart/form-data` with a `file` field containing at most 5 MiB of PKCS#12 data, an optional transient `password`, and `name` for upload or `expectedVersion` for replacement. Certificate bytes and passwords are never returned.

Use `config:read` for queries and `config:manage` for route and configuration changes. Existing `config:publish` credentials remain accepted as a compatibility alias for `config:manage`. The old `config:write` scope does not grant permission to change or publish configuration. Other scopes remain `instances:read`, `credentials:read`, `credentials:write`, `audit:read`, and `system:admin`.

## Route workflow

Create a route without creating a cluster. The result is live in an Immediate environment and unpublished in a Staged environment:

```graphql
mutation {
  createRoute(
    environmentId: "00000000-0000-0000-0000-000000000000"
    input: {
      name: "Orders API"
      path: "/orders/{**remainder}"
      upstreamUrl: "https://orders.example/"
    }
  ) {
    route { id name version }
    revision { id changeSummary }
  }
}
```

Query `routes(environmentId, filter)` or `route(environmentId, routeId)` to obtain typed matching, upstream, and feature fields. New routes preserve the incoming `Host` header by default. Use `updateRouteBasics` for common routing fields, `updateRouteFeatures` for the complete typed feature set, or `updateRoute` for the complete route aggregate. Set `preserveOriginalHost` on `UpdateManagedRouteBasicsInput` to add or remove YARP's `RequestHeaderOriginalHost` transform without replacing unrelated transforms. Updates and deletes require the route's opaque `expectedRouteVersion`.

Routes may alternatively reference a reusable Upstream. Use `createUpstream`, `updateUpstream`, and `deleteUpstream` to manage named destination groups, then pass its ID as `upstreamId` instead of `upstreamUrl` to `createRoute` or `updateRouteBasics`. Query `upstreams(environmentId)` or `upstream(environmentId, upstreamId)` for the current editable configuration. Upstream updates and deletes require the opaque `expectedUpstreamVersion`, and deletion is rejected while any route still uses the Upstream.

```graphql
mutation {
  createUpstream(
    environmentId: "00000000-0000-0000-0000-000000000000"
    input: {
      name: "Orders pool"
      loadBalancingPolicy: "RoundRobin"
      destinations: [
        { key: "orders-1", value: { address: "https://orders-1.example/" } }
        { key: "orders-2", value: { address: "https://orders-2.example/" } }
      ]
      health: { activeEnabled: true, path: "/healthz", interval: "PT10S", timeout: "PT5S" }
    }
  ) { revision { id changeSummary } }
}
```

`routeFeatureCatalog` supplies stable feature identifiers, categories, display names, and descriptions for management applications.

Use `routeUnavailableResponseProfiles(environmentId)` and `routeOperationalDefaults(environmentId)` to read shared unavailable responses and per-state defaults. `saveRouteUnavailableResponseProfile`, `deleteRouteUnavailableResponseProfile`, and `updateRouteOperationalDefaults` use the environment configuration version because they can affect multiple routes.

Use `setRouteOperationalState` to select `ONLINE`, `DRAINING`, `MAINTENANCE`, or `OFFLINE`. For a non-online state, set `useEnvironmentDefault` or provide `responseProfileId`. Existing clients can still submit an inline status code, optional ISO 8601 `retryAfter`, title, message, and dedicated `upstreamUrl` by setting `useEnvironmentDefault: false`. `routeRuntimeStatuses(environmentId)` reports active request counts aggregated from live gateway instances.

```graphql
mutation {
  setRouteOperationalState(
    environmentId: "00000000-0000-0000-0000-000000000000"
    routeId: "orders-api"
    expectedRouteVersion: "opaque-route-version"
    input: {
      state: DRAINING
      statusCode: 503
      retryAfter: "PT5M"
      message: "The service is being updated."
    }
  ) {
    route { id version operations { state } }
    revision { id changeSummary }
  }
}
```

## History and advanced operations

Administrators select `IMMEDIATE` or `STAGED` with `setEnvironmentPublishingMode`. The mode cannot change while a pending revision exists. In Staged mode, `pendingConfiguration(environmentId)` returns the revision version, semantic changes, and validation report. Publish it with `publishPendingConfiguration(environmentId, expectedVersion, comment)` or remove it with `discardPendingConfiguration(environmentId, expectedVersion)`. Use the pending revision ID as `expectedConfigurationVersion` for additional broad edits.

Route enable or disable mutations and `setRouteOperationalState` always activate immediately. If an unpublished change set exists, API Gateway also applies the operational value to that pending document.

`configurationHistory(environmentId)` returns immutable, newest-first entries with a summary, actor, affected resource, parent, and revert reference. `revertConfigurationChange` creates a new entry that reverses the selected route change. `restoreConfigurationSnapshot` performs a full restore as a new entry.

`exportConfiguration` returns canonical active JSON. `importConfiguration`, `copyConfiguration`, snapshot restore, and revert follow the target environment's publishing mode. Broad changes require `expectedConfigurationVersion` or `expectedTargetVersion`.

Stable errors include `UNAUTHENTICATED`, `FORBIDDEN`, `VALIDATION_FAILED`, `CONFLICT`, `REVERT_CONFLICT`, `NOT_FOUND`, `INVALID_STATE`, and `SECRET_REFERENCE_UNAVAILABLE`. Correctable validation errors include stable codes and paths. Configuration documents, credentials, secret values, stack traces, and SQL are never returned in errors.

Production disables schema introspection and the interactive GraphQL tool. Requests are bounded by the configured request size, execution timeout, field cost, and type cost limits.
