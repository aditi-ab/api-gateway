# Route configuration and history

Routes are the primary configuration resource. Clusters, destinations, and policy references are generated internally for simple routes and do not need to be created first.

Route matching can combine a path with optional hostnames, HTTP methods, headers, and query parameters. Multiple configured conditions use AND semantics. Route precedence is optional, with lower numbers evaluated first. When TLS terminates at an external reverse proxy, preserve the original `Host` header so hostname restrictions continue to match the public request.

Routes preserve the incoming request path by default. To expose an upstream at a prefixed public path, select **Remove path prefix** and enter the fixed prefix. A route matching `/sub/{**remainder}` with prefix `/sub` forwards `/sub/products` to `/products` on the selected upstream. Query parameters are not removed.

A route can have multiple upstream destinations. The gateway applies the route's load-balancing policy across available destinations. Use Power of Two Choices for the general default, Round Robin for predictable rotation, Least Requests for uneven request durations, or Random for simple distribution. An optional health URL can be recorded for each destination.

Advanced upstream settings select HTTP/1.1 or HTTP/2, the version negotiation policy, and whether a busy HTTP/2 upstream may use multiple connections. When multiple connections are enabled, the gateway can open another connection after the current HTTP/2 connection reaches its concurrent request-stream limit. This can improve throughput for busy routes, but increases the number of connections to the upstream. These settings affect the gateway-to-upstream connection and are independent of inbound HTTP/2 negotiation.

## Route traffic states

Every enabled route has an operational traffic state. `online` applies the normal route features and upstream selection. `draining`, `maintenance`, and `offline` keep the route in matching but override authentication, rate limiting, validation, transforms, caching, mirroring, and normal upstream selection. Disabled routes do not match, regardless of their operational state.

Draining does not interrupt requests that entered the route while it was online. Requests arriving after the draining configuration activates receive the configured unavailable response. Observe the active request count until it reaches zero, then change the route to Maintenance or Offline before updating upstream nodes. Counts are reported through gateway heartbeats, so allow one heartbeat interval for the Management UI to converge.

Unavailable responses use status `503` by default and can include `Retry-After`. A gateway-hosted response supports encoded HTML for browsers and JSON for API clients. A dedicated maintenance upstream is an advanced alternative and is isolated from the route's normal destination pool.

Configure named unavailable-response profiles in **System > Settings**. Profiles belong to the selected environment and can contain either a gateway-hosted response or a dedicated upstream. Set a default profile for Draining, Maintenance, and Offline. Routes inherit the matching environment default unless an administrator selects a different named profile on that route. This lets many routes share one response while still allowing exceptions. Existing configurations that contain an inline route response continue to work and can be moved to a shared profile when convenient.

Each environment has a configuration publishing mode. **Immediate** preserves the default behavior, where every successful route create, update, feature change, delete, import, copy, or restore operation:

1. Starts from the active configuration.
2. Applies the requested semantic change.
3. Validates the entire result, including YARP configuration.
4. Writes an immutable history entry and audit event.
5. Makes the new entry the desired configuration atomically.

**Staged** collects those changes in one unpublished change set. Administrators can continue editing the pending result, review a semantic change list and validation issues, add an optional publication note, then publish the entire configuration atomically. Discarding removes all unpublished changes and returns the editor to the active configuration. Only one shared pending change set exists per environment, so all administrators see and contribute to the same result.

Route enable or disable actions and route traffic-state changes remain immediate in both modes. These are operational controls intended for incidents and maintenance. When a pending change set exists, the operational value is also carried into it so a later publication does not undo the live action.

A failed validation or concurrency check leaves both the active configuration and pending change set unchanged. Publishing mode cannot be changed while unpublished changes exist.

## Concurrency

Each route exposes an opaque `version`. Send it as `expectedRouteVersion` when updating or deleting that route. Changes to different routes do not conflict. A stale change to the same route returns `CONFLICT` and the current version.

Configuration-wide edit operations use the pending revision ID when one exists, otherwise they use the environment's active revision ID as `expectedConfigurationVersion`.

## Revert and restore

`revertConfigurationChange` reverses one route change on top of the current editable configuration. It preserves later unrelated changes and returns `REVERT_CONFLICT` if the same route no longer matches the selected change. In Staged mode, the revert remains unpublished until the change set is published.

`restoreConfigurationSnapshot` is an advanced operation that copies a complete historical snapshot into a new history entry or pending change set. It never makes a historical record mutable.

## Configuration schemas

Version 2 is available at `/schemas/gateway-config.v2.schema.json`. It includes route display names, generated-resource ownership, IP and request-size controls, request validation, response caching, and route operational states. Existing version 1 publications remain readable and can still be activated.

Version 3 is available at `/schemas/gateway-config.v3.schema.json`. It adds inbound scheme, certificate reference, and WebSocket controls. Versions 1 and 2 remain readable; their routes default to accepting either scheme when the corresponding listener is available. The first route edit upgrades the active document to version 3.

Configuration hashes use canonical JSON, so whitespace and object-property order do not affect the hash.

## Response caching

Response caching is local to each data-plane process. It is available only for anonymous `GET` and `HEAD` requests without authorization or cookie headers. Only successful responses without `Set-Cookie`, `Cache-Control: private`, or `Cache-Control: no-store` are stored. Configure a positive lifetime, maximum cached body size, and optional request headers included in the cache key. Configuration changes automatically produce new cache keys.

## Request validation and limits

CIDR allow and deny rules use the effective client address after trusted forwarded-header processing. Request-size limits apply before proxy body forwarding and return `413` when exceeded.

JSON request validation accepts a JSON Schema and a bounded maximum body size. Invalid JSON or a schema mismatch returns `400` with a stable, sanitized error code.
## Gateway feature switches

Route configuration can include a `disabledFeatures` array containing feature IDs such as `authorization`, `rate-limit`, or `response-cache`. The referenced policy and its settings remain in the configuration, but gateway instances do not apply it. Remove the ID from the array to restore the saved feature settings.
