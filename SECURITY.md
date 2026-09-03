# Security

Do not include credentials, private keys, access tokens, customer configuration, or internal hostnames in issue reports or diagnostic attachments.

Report suspected vulnerabilities with GitHub private vulnerability reporting for this repository. Include the affected version, deployment mode, reproduction steps, impact, and any temporary mitigation that has been tested.

Operators should keep the management plane on a restricted administrative network, use SQL Server for replicated deployments, persist data-protection keys, configure HTTPS at the ingress, rotate management and consumer keys regularly, and store TLS certificate material outside configuration revisions.

Supported security controls and deployment guidance are documented in the VitePress security and operations guides.
