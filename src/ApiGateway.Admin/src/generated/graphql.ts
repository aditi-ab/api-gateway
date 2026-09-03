export type Maybe<T> = T | null;
export type InputMaybe<T> = T | null;
/** All built-in and custom scalars, mapped to their actual values */
export interface Scalars {
  ID: { input: string; output: string };
  String: { input: string; output: string };
  Boolean: { input: boolean; output: boolean };
  Int: { input: number; output: number };
  Float: { input: number; output: number };
  /** The `DateTime` scalar type represents a date and time with time zone offset information. */
  DateTime: { input: string; output: string };
  /** The `Duration` scalar type represents a duration of time. */
  Duration: { input: string; output: string };
  /** The `Long` scalar type represents a signed 64-bit integer. */
  Long: { input: number; output: number };
  /** The `UUID` scalar type represents a Universally Unique Identifier (UUID) as defined by RFC 9562. */
  UUID: { input: string; output: string };
}

export interface AcmeAccountInfo {
  readonly __typename?: 'AcmeAccountInfo';
  readonly accountUrl?: Maybe<Scalars['String']['output']>;
  readonly contactEmail: Scalars['String']['output'];
  readonly directoryUrl: Scalars['String']['output'];
  readonly id: Scalars['UUID']['output'];
  readonly isDefault: Scalars['Boolean']['output'];
  readonly isStaging: Scalars['Boolean']['output'];
  readonly name: Scalars['String']['output'];
  readonly termsAcceptedAtUtc: Scalars['DateTime']['output'];
  readonly termsOfServiceUrl?: Maybe<Scalars['String']['output']>;
  readonly version: Scalars['UUID']['output'];
}

export type AcmeChallengeKind
  = | 'DNS01'
    | 'HTTP01'
    | 'MANUAL_DNS01';

export interface AcmeDirectorySnapshot {
  readonly __typename?: 'AcmeDirectorySnapshot';
  readonly accountId?: Maybe<Scalars['UUID']['output']>;
  readonly directoryUrl: Scalars['String']['output'];
  readonly isStaging: Scalars['Boolean']['output'];
  readonly name: Scalars['String']['output'];
  readonly registered: Scalars['Boolean']['output'];
  readonly termsOfServiceUrl?: Maybe<Scalars['String']['output']>;
}

/** A connection to a list of items. */
export interface ActivationConnectionConnection {
  readonly __typename?: 'ActivationConnectionConnection';
  /** A list of edges. */
  readonly edges?: Maybe<ReadonlyArray<ActivationConnectionEdge>>;
  /** A flattened list of the nodes. */
  readonly nodes?: Maybe<ReadonlyArray<GatewayActivationEvent>>;
  /** Information to aid in pagination. */
  readonly pageInfo: PageInfo;
}

/** An edge in a connection. */
export interface ActivationConnectionEdge {
  readonly __typename?: 'ActivationConnectionEdge';
  /** A cursor for use in pagination. */
  readonly cursor: Scalars['String']['output'];
  /** The item at the end of the edge. */
  readonly node: GatewayActivationEvent;
}

export type ActivationOutcome
  = | 'FAILED'
    | 'SUCCEEDED';

export interface ApiKeyInfo {
  readonly __typename?: 'ApiKeyInfo';
  readonly expiresAtUtc?: Maybe<Scalars['DateTime']['output']>;
  readonly id: Scalars['UUID']['output'];
  readonly lastUsedAtUtc?: Maybe<Scalars['DateTime']['output']>;
  readonly name: Scalars['String']['output'];
  readonly prefix: Scalars['String']['output'];
  readonly revokedAtUtc?: Maybe<Scalars['DateTime']['output']>;
}

/** Defines when a policy shall be executed. */
export type ApplyPolicy
  /** After the resolver was executed. */
  = | 'AFTER_RESOLVER'
  /** Before the resolver was executed. */
    | 'BEFORE_RESOLVER'
  /** The policy is applied in the validation step before the execution. */
    | 'VALIDATION';

/** A connection to a list of items. */
export interface AuditConnectionConnection {
  readonly __typename?: 'AuditConnectionConnection';
  /** A list of edges. */
  readonly edges?: Maybe<ReadonlyArray<AuditConnectionEdge>>;
  /** A flattened list of the nodes. */
  readonly nodes?: Maybe<ReadonlyArray<AuditEvent>>;
  /** Information to aid in pagination. */
  readonly pageInfo: PageInfo;
}

/** An edge in a connection. */
export interface AuditConnectionEdge {
  readonly __typename?: 'AuditConnectionEdge';
  /** A cursor for use in pagination. */
  readonly cursor: Scalars['String']['output'];
  /** The item at the end of the edge. */
  readonly node: AuditEvent;
}

export interface AuditEvent {
  readonly __typename?: 'AuditEvent';
  readonly action: Scalars['String']['output'];
  readonly actorId: Scalars['String']['output'];
  readonly actorType: Scalars['String']['output'];
  readonly correlationId: Scalars['String']['output'];
  readonly detailsJson: Scalars['String']['output'];
  readonly environmentId?: Maybe<Scalars['UUID']['output']>;
  readonly id: Scalars['Long']['output'];
  readonly occurredAtUtc: Scalars['DateTime']['output'];
  readonly targetId: Scalars['String']['output'];
  readonly targetType: Scalars['String']['output'];
}

export interface AuthorizationPolicy {
  readonly __typename?: 'AuthorizationPolicy';
  readonly audiences?: Maybe<ReadonlyArray<Scalars['String']['output']>>;
  readonly authority?: Maybe<Scalars['String']['output']>;
  readonly clockSkew?: Maybe<Scalars['Duration']['output']>;
  readonly issuer?: Maybe<Scalars['String']['output']>;
  readonly policies?: Maybe<ReadonlyArray<Scalars['String']['output']>>;
  readonly requiredClaims?: Maybe<ReadonlyArray<KeyValuePairOfStringAndString>>;
  readonly requiredScopes?: Maybe<ReadonlyArray<Scalars['String']['output']>>;
  readonly type: Scalars['String']['output'];
}

export interface AuthorizationPolicyInput {
  readonly audiences?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  readonly authority?: InputMaybe<Scalars['String']['input']>;
  readonly clockSkew?: InputMaybe<Scalars['Duration']['input']>;
  readonly issuer?: InputMaybe<Scalars['String']['input']>;
  readonly policies?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  readonly requiredClaims?: InputMaybe<ReadonlyArray<KeyValuePairOfStringAndStringInput>>;
  readonly requiredScopes?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  readonly type: Scalars['String']['input'];
}

/** A connection to a list of items. */
export interface ClustersConnection {
  readonly __typename?: 'ClustersConnection';
  /** A list of edges. */
  readonly edges?: Maybe<ReadonlyArray<ClustersEdge>>;
  /** A flattened list of the nodes. */
  readonly nodes?: Maybe<ReadonlyArray<GatewayCluster>>;
  /** Information to aid in pagination. */
  readonly pageInfo: PageInfo;
}

/** An edge in a connection. */
export interface ClustersEdge {
  readonly __typename?: 'ClustersEdge';
  /** A cursor for use in pagination. */
  readonly cursor: Scalars['String']['output'];
  /** The item at the end of the edge. */
  readonly node: GatewayCluster;
}

export interface ConfigRevision {
  readonly __typename?: 'ConfigRevision';
  readonly changeKind?: Maybe<Scalars['String']['output']>;
  readonly changeSummary?: Maybe<Scalars['String']['output']>;
  readonly changedResourceId?: Maybe<Scalars['String']['output']>;
  readonly changedResourceType?: Maybe<Scalars['String']['output']>;
  readonly comment?: Maybe<Scalars['String']['output']>;
  readonly concurrencyVersion: Scalars['UUID']['output'];
  readonly configJson: Scalars['String']['output'];
  readonly contentHash: Scalars['String']['output'];
  readonly createdAtUtc: Scalars['DateTime']['output'];
  readonly createdBy: Scalars['String']['output'];
  readonly environmentId: Scalars['UUID']['output'];
  readonly id: Scalars['UUID']['output'];
  readonly number: Scalars['Long']['output'];
  readonly parentRevisionId?: Maybe<Scalars['UUID']['output']>;
  readonly publishedAtUtc?: Maybe<Scalars['DateTime']['output']>;
  readonly publishedBy?: Maybe<Scalars['String']['output']>;
  readonly revertsRevisionId?: Maybe<Scalars['UUID']['output']>;
  readonly state: RevisionState;
}

export interface ConfigurationChangeResult {
  readonly __typename?: 'ConfigurationChangeResult';
  readonly revision: ConfigRevision;
  readonly route?: Maybe<ManagedRoute>;
}

export type ConfigurationPublishingMode
  = | 'IMMEDIATE'
    | 'STAGED';

export interface CorsPolicy {
  readonly __typename?: 'CorsPolicy';
  readonly allowCredentials: Scalars['Boolean']['output'];
  readonly exposedHeaders?: Maybe<ReadonlyArray<Scalars['String']['output']>>;
  readonly headers: ReadonlyArray<Scalars['String']['output']>;
  readonly methods: ReadonlyArray<Scalars['String']['output']>;
  readonly origins: ReadonlyArray<Scalars['String']['output']>;
  readonly preflightMaxAge?: Maybe<Scalars['Duration']['output']>;
}

export interface CorsPolicyInput {
  readonly allowCredentials?: Scalars['Boolean']['input'];
  readonly exposedHeaders?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  readonly headers: ReadonlyArray<Scalars['String']['input']>;
  readonly methods: ReadonlyArray<Scalars['String']['input']>;
  readonly origins: ReadonlyArray<Scalars['String']['input']>;
  readonly preflightMaxAge?: InputMaybe<Scalars['Duration']['input']>;
}

export interface CreateManagedRouteInput {
  readonly name: Scalars['String']['input'];
  readonly path: Scalars['String']['input'];
  readonly upstreamId?: InputMaybe<Scalars['String']['input']>;
  readonly upstreamUrl?: InputMaybe<Scalars['String']['input']>;
}

export interface DnsManagedZone {
  readonly __typename?: 'DnsManagedZone';
  readonly id: Scalars['String']['output'];
  readonly name: Scalars['String']['output'];
}

export interface DnsProviderCredentialsInput {
  readonly accessKeyId?: InputMaybe<Scalars['String']['input']>;
  readonly apiToken?: InputMaybe<Scalars['String']['input']>;
  readonly clientId?: InputMaybe<Scalars['String']['input']>;
  readonly clientSecret?: InputMaybe<Scalars['String']['input']>;
  readonly customerNumber?: InputMaybe<Scalars['String']['input']>;
  readonly password?: InputMaybe<Scalars['String']['input']>;
  readonly projectId?: InputMaybe<Scalars['String']['input']>;
  readonly resourceGroup?: InputMaybe<Scalars['String']['input']>;
  readonly secretAccessKey?: InputMaybe<Scalars['String']['input']>;
  readonly serviceAccountJson?: InputMaybe<Scalars['String']['input']>;
  readonly sessionToken?: InputMaybe<Scalars['String']['input']>;
  readonly subscriptionId?: InputMaybe<Scalars['String']['input']>;
  readonly tenantId?: InputMaybe<Scalars['String']['input']>;
  readonly username?: InputMaybe<Scalars['String']['input']>;
}

export type DnsProviderKind
  = | 'AZURE_DNS'
    | 'CLOUDFLARE'
    | 'DIGITAL_OCEAN'
    | 'GOOGLE_CLOUD_DNS'
    | 'LOOPIA'
    | 'ROUTE53'
    | 'SIMPLY';

export interface DnsProviderProfileInfo {
  readonly __typename?: 'DnsProviderProfileInfo';
  readonly id: Scalars['UUID']['output'];
  readonly managedZones: ReadonlyArray<Scalars['String']['output']>;
  readonly name: Scalars['String']['output'];
  readonly provider: DnsProviderKind;
  readonly updatedAtUtc: Scalars['DateTime']['output'];
  readonly version: Scalars['UUID']['output'];
}

export interface EntraConnectionInfo {
  readonly __typename?: 'EntraConnectionInfo';
  readonly audience: Scalars['String']['output'];
  readonly authority: Scalars['String']['output'];
  readonly clientId: Scalars['String']['output'];
  readonly configured: Scalars['Boolean']['output'];
  readonly enabled: Scalars['Boolean']['output'];
  readonly scope: Scalars['String']['output'];
  readonly version: Scalars['UUID']['output'];
}

/** A connection to a list of items. */
export interface EnvironmentConnectionConnection {
  readonly __typename?: 'EnvironmentConnectionConnection';
  /** A list of edges. */
  readonly edges?: Maybe<ReadonlyArray<EnvironmentConnectionEdge>>;
  /** A flattened list of the nodes. */
  readonly nodes?: Maybe<ReadonlyArray<GatewayEnvironment>>;
  /** Information to aid in pagination. */
  readonly pageInfo: PageInfo;
}

/** An edge in a connection. */
export interface EnvironmentConnectionEdge {
  readonly __typename?: 'EnvironmentConnectionEdge';
  /** A cursor for use in pagination. */
  readonly cursor: Scalars['String']['output'];
  /** The item at the end of the edge. */
  readonly node: GatewayEnvironment;
}

export interface GatewayActivationEvent {
  readonly __typename?: 'GatewayActivationEvent';
  readonly completedAtUtc: Scalars['DateTime']['output'];
  readonly contentHash?: Maybe<Scalars['String']['output']>;
  readonly desiredRevisionId?: Maybe<Scalars['UUID']['output']>;
  readonly environmentId: Scalars['UUID']['output'];
  readonly errorCode?: Maybe<Scalars['String']['output']>;
  readonly errorMessage?: Maybe<Scalars['String']['output']>;
  readonly id: Scalars['Long']['output'];
  readonly instanceId: Scalars['String']['output'];
  readonly outcome: ActivationOutcome;
  readonly startedAtUtc: Scalars['DateTime']['output'];
}

export interface GatewayCluster {
  readonly __typename?: 'GatewayCluster';
  readonly destinations: ReadonlyArray<KeyValuePairOfStringAndGatewayDestination>;
  readonly health: HealthPolicy;
  readonly httpClient: UpstreamHttpPolicy;
  readonly id: Scalars['String']['output'];
  readonly loadBalancingPolicy: Scalars['String']['output'];
  readonly metadata: GatewayMetadata;
  readonly resiliencePolicy?: Maybe<Scalars['String']['output']>;
  readonly sessionAffinity?: Maybe<SessionAffinityPolicy>;
  readonly tls?: Maybe<UpstreamTlsPolicy>;
  readonly traffic?: Maybe<TrafficPolicy>;
}

export interface GatewayDestination {
  readonly __typename?: 'GatewayDestination';
  readonly address: Scalars['String']['output'];
  readonly healthAddress?: Maybe<Scalars['String']['output']>;
  readonly metadata?: Maybe<ReadonlyArray<KeyValuePairOfStringAndString>>;
  readonly pool: Scalars['String']['output'];
}

export interface GatewayDestinationInput {
  readonly address: Scalars['String']['input'];
  readonly healthAddress?: InputMaybe<Scalars['String']['input']>;
  readonly metadata?: InputMaybe<ReadonlyArray<KeyValuePairOfStringAndStringInput>>;
  readonly pool?: Scalars['String']['input'];
}

export interface GatewayDiagnostics {
  readonly __typename?: 'GatewayDiagnostics';
  readonly desiredRevisionId?: Maybe<Scalars['UUID']['output']>;
  readonly driftedCount: Scalars['Int']['output'];
  readonly environmentId: Scalars['UUID']['output'];
  readonly healthyCount: Scalars['Int']['output'];
  readonly instanceCount: Scalars['Int']['output'];
  readonly recentFailures: ReadonlyArray<GatewayActivationEvent>;
  readonly staleCount: Scalars['Int']['output'];
}

export interface GatewayEnvironment {
  readonly __typename?: 'GatewayEnvironment';
  readonly activeRevisionId?: Maybe<Scalars['UUID']['output']>;
  readonly archivedAtUtc?: Maybe<Scalars['DateTime']['output']>;
  readonly concurrencyVersion: Scalars['UUID']['output'];
  readonly createdAtUtc: Scalars['DateTime']['output'];
  readonly description?: Maybe<Scalars['String']['output']>;
  readonly displayName: Scalars['String']['output'];
  readonly id: Scalars['UUID']['output'];
  readonly pendingRevisionId?: Maybe<Scalars['UUID']['output']>;
  readonly publishingMode: ConfigurationPublishingMode;
  readonly slug: Scalars['String']['output'];
  readonly updatedAtUtc: Scalars['DateTime']['output'];
}

export interface GatewayInstance {
  readonly __typename?: 'GatewayInstance';
  readonly activatedContentHash?: Maybe<Scalars['String']['output']>;
  readonly activatedRevisionId?: Maybe<Scalars['UUID']['output']>;
  readonly activeRouteRequestsJson: Scalars['String']['output'];
  readonly advertisedAddress?: Maybe<Scalars['String']['output']>;
  readonly displayName: Scalars['String']['output'];
  readonly environmentId: Scalars['UUID']['output'];
  readonly id: Scalars['UUID']['output'];
  readonly instanceId: Scalars['String']['output'];
  readonly lastActivationAtUtc?: Maybe<Scalars['DateTime']['output']>;
  readonly lastActivationErrorCode?: Maybe<Scalars['String']['output']>;
  readonly lastActivationStatus?: Maybe<Scalars['String']['output']>;
  readonly lastHeartbeatAtUtc: Scalars['DateTime']['output'];
  readonly runtimeVersion: Scalars['String']['output'];
  readonly startedAtUtc: Scalars['DateTime']['output'];
  readonly stoppedAtUtc?: Maybe<Scalars['DateTime']['output']>;
}

export interface GatewayMetadata {
  readonly __typename?: 'GatewayMetadata';
  readonly criticality?: Maybe<Scalars['String']['output']>;
  readonly deprecationMessage?: Maybe<Scalars['String']['output']>;
  readonly description?: Maybe<Scalars['String']['output']>;
  readonly displayName?: Maybe<Scalars['String']['output']>;
  readonly managedByRouteId?: Maybe<Scalars['String']['output']>;
  readonly owner?: Maybe<Scalars['String']['output']>;
  readonly sunsetAt?: Maybe<Scalars['DateTime']['output']>;
  readonly tags?: Maybe<ReadonlyArray<Scalars['String']['output']>>;
}

export interface GatewayMetadataInput {
  readonly criticality?: InputMaybe<Scalars['String']['input']>;
  readonly deprecationMessage?: InputMaybe<Scalars['String']['input']>;
  readonly description?: InputMaybe<Scalars['String']['input']>;
  readonly displayName?: InputMaybe<Scalars['String']['input']>;
  readonly managedByRouteId?: InputMaybe<Scalars['String']['input']>;
  readonly owner?: InputMaybe<Scalars['String']['input']>;
  readonly sunsetAt?: InputMaybe<Scalars['DateTime']['input']>;
  readonly tags?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
}

export interface GatewayRoute {
  readonly __typename?: 'GatewayRoute';
  readonly access?: Maybe<RouteAccessPolicy>;
  readonly authorizationPolicy?: Maybe<Scalars['String']['output']>;
  readonly clusterId: Scalars['String']['output'];
  readonly corsPolicy?: Maybe<Scalars['String']['output']>;
  readonly disabledFeatures: ReadonlyArray<Scalars['String']['output']>;
  readonly enabled: Scalars['Boolean']['output'];
  readonly id: Scalars['String']['output'];
  readonly inbound: InboundRoutePolicy;
  readonly match: RouteMatch;
  readonly metadata: GatewayMetadata;
  readonly mirror?: Maybe<MirrorPolicy>;
  readonly operations: RouteOperationalPolicy;
  readonly order?: Maybe<Scalars['Int']['output']>;
  readonly rateLimitPolicy?: Maybe<Scalars['String']['output']>;
  readonly requestValidation?: Maybe<RequestValidationPolicy>;
  readonly responseCache?: Maybe<ResponseCachePolicy>;
  readonly timeoutPolicy?: Maybe<Scalars['String']['output']>;
  readonly transforms: ReadonlyArray<ReadonlyArray<KeyValuePairOfStringAndString>>;
}

export interface HealthPolicy {
  readonly __typename?: 'HealthPolicy';
  readonly activeEnabled: Scalars['Boolean']['output'];
  readonly activePolicy: Scalars['String']['output'];
  readonly availableDestinationsPolicy: Scalars['String']['output'];
  readonly interval?: Maybe<Scalars['Duration']['output']>;
  readonly passiveEnabled: Scalars['Boolean']['output'];
  readonly passivePolicy: Scalars['String']['output'];
  readonly path: Scalars['String']['output'];
  readonly query?: Maybe<Scalars['String']['output']>;
  readonly reactivationPeriod?: Maybe<Scalars['Duration']['output']>;
  readonly timeout?: Maybe<Scalars['Duration']['output']>;
}

export interface HealthPolicyInput {
  readonly activeEnabled?: Scalars['Boolean']['input'];
  readonly activePolicy?: Scalars['String']['input'];
  readonly availableDestinationsPolicy?: Scalars['String']['input'];
  readonly interval?: InputMaybe<Scalars['Duration']['input']>;
  readonly passiveEnabled?: Scalars['Boolean']['input'];
  readonly passivePolicy?: Scalars['String']['input'];
  readonly path?: Scalars['String']['input'];
  readonly query?: InputMaybe<Scalars['String']['input']>;
  readonly reactivationPeriod?: InputMaybe<Scalars['Duration']['input']>;
  readonly timeout?: InputMaybe<Scalars['Duration']['input']>;
}

export interface InboundCertificateInfo {
  readonly __typename?: 'InboundCertificateInfo';
  readonly dnsNames: ReadonlyArray<Scalars['String']['output']>;
  readonly id: Scalars['UUID']['output'];
  readonly name: Scalars['String']['output'];
  readonly notAfterUtc: Scalars['DateTime']['output'];
  readonly notBeforeUtc: Scalars['DateTime']['output'];
  readonly subject: Scalars['String']['output'];
  readonly thumbprint: Scalars['String']['output'];
  readonly version: Scalars['UUID']['output'];
}

export interface InboundRoutePolicy {
  readonly __typename?: 'InboundRoutePolicy';
  readonly certificateId?: Maybe<Scalars['UUID']['output']>;
  readonly scheme: InboundScheme;
  readonly webSocketsAllowed: Scalars['Boolean']['output'];
}

export interface InboundRoutePolicyInput {
  readonly certificateId?: InputMaybe<Scalars['UUID']['input']>;
  readonly scheme?: InboundScheme;
  readonly webSocketsAllowed?: Scalars['Boolean']['input'];
}

export type InboundScheme
  = | 'ANY'
    | 'HTTPS_REDIRECT'
    | 'HTTP_ONLY';

export interface InboundSecuritySettingsInfo {
  readonly __typename?: 'InboundSecuritySettingsInfo';
  readonly hstsEnabled: Scalars['Boolean']['output'];
  readonly hstsHosts: ReadonlyArray<Scalars['String']['output']>;
  readonly hstsIncludeSubDomains: Scalars['Boolean']['output'];
  readonly hstsMaxAgeSeconds: Scalars['Int']['output'];
  readonly hstsPreload: Scalars['Boolean']['output'];
  readonly version: Scalars['UUID']['output'];
}

/** A connection to a list of items. */
export interface InstanceConnectionConnection {
  readonly __typename?: 'InstanceConnectionConnection';
  /** A list of edges. */
  readonly edges?: Maybe<ReadonlyArray<InstanceConnectionEdge>>;
  /** A flattened list of the nodes. */
  readonly nodes?: Maybe<ReadonlyArray<GatewayInstance>>;
  /** Information to aid in pagination. */
  readonly pageInfo: PageInfo;
}

/** An edge in a connection. */
export interface InstanceConnectionEdge {
  readonly __typename?: 'InstanceConnectionEdge';
  /** A cursor for use in pagination. */
  readonly cursor: Scalars['String']['output'];
  /** The item at the end of the edge. */
  readonly node: GatewayInstance;
}

export interface InstanceDrift {
  readonly __typename?: 'InstanceDrift';
  readonly activatedRevisionId?: Maybe<Scalars['UUID']['output']>;
  readonly desiredRevisionId?: Maybe<Scalars['UUID']['output']>;
  readonly instanceId: Scalars['String']['output'];
  readonly instanceRecordId: Scalars['UUID']['output'];
  readonly state: Scalars['String']['output'];
}

export interface KeyValuePairOfStringAndGatewayDestination {
  readonly __typename?: 'KeyValuePairOfStringAndGatewayDestination';
  readonly key: Scalars['String']['output'];
  readonly value: GatewayDestination;
}

export interface KeyValuePairOfStringAndGatewayDestinationInput {
  readonly key: Scalars['String']['input'];
  readonly value: GatewayDestinationInput;
}

export interface KeyValuePairOfStringAndInt32 {
  readonly __typename?: 'KeyValuePairOfStringAndInt32';
  readonly key: Scalars['String']['output'];
  readonly value: Scalars['Int']['output'];
}

export interface KeyValuePairOfStringAndInt32Input {
  readonly key: Scalars['String']['input'];
  readonly value: Scalars['Int']['input'];
}

export interface KeyValuePairOfStringAndString {
  readonly __typename?: 'KeyValuePairOfStringAndString';
  readonly key: Scalars['String']['output'];
  readonly value: Scalars['String']['output'];
}

export interface KeyValuePairOfStringAndStringInput {
  readonly key: Scalars['String']['input'];
  readonly value: Scalars['String']['input'];
}

export interface LocalUserInfo {
  readonly __typename?: 'LocalUserInfo';
  readonly createdAtUtc: Scalars['DateTime']['output'];
  readonly displayName?: Maybe<Scalars['String']['output']>;
  readonly enabled: Scalars['Boolean']['output'];
  readonly id: Scalars['UUID']['output'];
  readonly lastLoginAtUtc?: Maybe<Scalars['DateTime']['output']>;
  readonly mustChangePassword: Scalars['Boolean']['output'];
  readonly roles: ReadonlyArray<Scalars['String']['output']>;
  readonly username: Scalars['String']['output'];
  readonly version: Scalars['UUID']['output'];
}

export interface LocalUserSecretPayload {
  readonly __typename?: 'LocalUserSecretPayload';
  readonly temporaryPassword: Scalars['String']['output'];
  readonly user: LocalUserInfo;
}

export interface ManagedCertificateActivityInfo {
  readonly __typename?: 'ManagedCertificateActivityInfo';
  readonly action: Scalars['String']['output'];
  readonly detailsJson: Scalars['String']['output'];
  readonly id: Scalars['Long']['output'];
  readonly occurredAtUtc: Scalars['DateTime']['output'];
}

export interface ManagedCertificateDnsChallengeInfo {
  readonly __typename?: 'ManagedCertificateDnsChallengeInfo';
  readonly expiresAtUtc: Scalars['DateTime']['output'];
  readonly id: Scalars['UUID']['output'];
  readonly recordName: Scalars['String']['output'];
  readonly recordValue: Scalars['String']['output'];
}

export interface ManagedCertificateInfo {
  readonly __typename?: 'ManagedCertificateInfo';
  readonly acmeAccountId: Scalars['UUID']['output'];
  readonly acmeAccountName: Scalars['String']['output'];
  readonly certificate?: Maybe<InboundCertificateInfo>;
  readonly challengeKind: AcmeChallengeKind;
  readonly dnsNames: ReadonlyArray<Scalars['String']['output']>;
  readonly dnsProviderProfileId?: Maybe<Scalars['UUID']['output']>;
  readonly dnsProviderProfileName?: Maybe<Scalars['String']['output']>;
  readonly failedAttemptCount: Scalars['Int']['output'];
  readonly id: Scalars['UUID']['output'];
  readonly isStaging: Scalars['Boolean']['output'];
  readonly lastAttemptAtUtc?: Maybe<Scalars['DateTime']['output']>;
  readonly lastErrorCode?: Maybe<Scalars['String']['output']>;
  readonly lastErrorMessage?: Maybe<Scalars['String']['output']>;
  readonly lastSuccessAtUtc?: Maybe<Scalars['DateTime']['output']>;
  readonly name: Scalars['String']['output'];
  readonly nextAttemptAtUtc: Scalars['DateTime']['output'];
  readonly state: ManagedCertificateState;
  readonly version: Scalars['UUID']['output'];
}

export type ManagedCertificateState
  = | 'ACTIVE'
    | 'FAILED'
    | 'ISSUING'
    | 'PENDING'
    | 'RENEWING';

export interface ManagedOpenApiPreview {
  readonly __typename?: 'ManagedOpenApiPreview';
  readonly issues: ReadonlyArray<ValidationIssue>;
  readonly routes: ReadonlyArray<ManagedOpenApiRoutePreview>;
  readonly token: Scalars['String']['output'];
}

export interface ManagedOpenApiRoutePreview {
  readonly __typename?: 'ManagedOpenApiRoutePreview';
  readonly conflicts: Scalars['Boolean']['output'];
  readonly id: Scalars['String']['output'];
  readonly methods: ReadonlyArray<Scalars['String']['output']>;
  readonly path: Scalars['String']['output'];
}

export interface ManagedRoute {
  readonly __typename?: 'ManagedRoute';
  readonly enabled: Scalars['Boolean']['output'];
  readonly features: ManagedRouteFeatures;
  readonly id: Scalars['String']['output'];
  readonly inbound?: Maybe<InboundRoutePolicy>;
  readonly match: RouteMatch;
  readonly metadata?: Maybe<GatewayMetadata>;
  readonly name: Scalars['String']['output'];
  readonly operations: RouteOperationalPolicy;
  readonly order?: Maybe<Scalars['Int']['output']>;
  readonly upstream: ManagedUpstream;
  readonly version: Scalars['String']['output'];
}

export interface ManagedRouteFeatures {
  readonly __typename?: 'ManagedRouteFeatures';
  readonly access?: Maybe<RouteAccessPolicy>;
  readonly authorization?: Maybe<AuthorizationPolicy>;
  readonly cors?: Maybe<CorsPolicy>;
  readonly disabledFeatures?: Maybe<ReadonlyArray<Scalars['String']['output']>>;
  readonly mirror?: Maybe<MirrorPolicy>;
  readonly rateLimit?: Maybe<RateLimitPolicy>;
  readonly requestValidation?: Maybe<RequestValidationPolicy>;
  readonly resilience?: Maybe<ResiliencePolicy>;
  readonly responseCache?: Maybe<ResponseCachePolicy>;
  readonly timeout?: Maybe<TimeoutPolicy>;
  readonly transforms?: Maybe<ReadonlyArray<ReadonlyArray<KeyValuePairOfStringAndString>>>;
}

export interface ManagedRouteFeaturesInput {
  readonly access?: InputMaybe<RouteAccessPolicyInput>;
  readonly authorization?: InputMaybe<AuthorizationPolicyInput>;
  readonly cors?: InputMaybe<CorsPolicyInput>;
  readonly disabledFeatures?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  readonly mirror?: InputMaybe<MirrorPolicyInput>;
  readonly rateLimit?: InputMaybe<RateLimitPolicyInput>;
  readonly requestValidation?: InputMaybe<RequestValidationPolicyInput>;
  readonly resilience?: InputMaybe<ResiliencePolicyInput>;
  readonly responseCache?: InputMaybe<ResponseCachePolicyInput>;
  readonly timeout?: InputMaybe<TimeoutPolicyInput>;
  readonly transforms?: InputMaybe<ReadonlyArray<ReadonlyArray<KeyValuePairOfStringAndStringInput>>>;
}

export interface ManagedUpstream {
  readonly __typename?: 'ManagedUpstream';
  readonly destinations?: Maybe<ReadonlyArray<KeyValuePairOfStringAndGatewayDestination>>;
  readonly health?: Maybe<HealthPolicy>;
  readonly httpClient?: Maybe<UpstreamHttpPolicy>;
  readonly loadBalancingPolicy: Scalars['String']['output'];
  readonly sessionAffinity?: Maybe<SessionAffinityPolicy>;
  readonly tls?: Maybe<UpstreamTlsPolicy>;
  readonly traffic?: Maybe<TrafficPolicy>;
  readonly upstreamId?: Maybe<Scalars['String']['output']>;
  readonly upstreamName?: Maybe<Scalars['String']['output']>;
  readonly url: Scalars['String']['output'];
}

export interface ManagedUpstreamInput {
  readonly destinations?: InputMaybe<ReadonlyArray<KeyValuePairOfStringAndGatewayDestinationInput>>;
  readonly health?: InputMaybe<HealthPolicyInput>;
  readonly httpClient?: InputMaybe<UpstreamHttpPolicyInput>;
  readonly loadBalancingPolicy?: Scalars['String']['input'];
  readonly sessionAffinity?: InputMaybe<SessionAffinityPolicyInput>;
  readonly tls?: InputMaybe<UpstreamTlsPolicyInput>;
  readonly traffic?: InputMaybe<TrafficPolicyInput>;
  readonly upstreamId?: InputMaybe<Scalars['String']['input']>;
  readonly upstreamName?: InputMaybe<Scalars['String']['input']>;
  readonly url: Scalars['String']['input'];
}

export interface ManagementIdentity {
  readonly __typename?: 'ManagementIdentity';
  readonly authenticationType?: Maybe<Scalars['String']['output']>;
  readonly id?: Maybe<Scalars['String']['output']>;
  readonly name?: Maybe<Scalars['String']['output']>;
  readonly roles: ReadonlyArray<Scalars['String']['output']>;
  readonly scopes: ReadonlyArray<Scalars['String']['output']>;
}

export interface MirrorPolicy {
  readonly __typename?: 'MirrorPolicy';
  readonly allowedMethods?: Maybe<ReadonlyArray<Scalars['String']['output']>>;
  readonly clusterId: Scalars['String']['output'];
  readonly maximumBufferedBodyBytes: Scalars['Long']['output'];
  readonly percentage: Scalars['Float']['output'];
  readonly removeHeaders?: Maybe<ReadonlyArray<Scalars['String']['output']>>;
  readonly timeout?: Maybe<Scalars['Duration']['output']>;
}

export interface MirrorPolicyInput {
  readonly allowedMethods?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  readonly clusterId: Scalars['String']['input'];
  readonly maximumBufferedBodyBytes?: Scalars['Long']['input'];
  readonly percentage?: Scalars['Float']['input'];
  readonly removeHeaders?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  readonly timeout?: InputMaybe<Scalars['Duration']['input']>;
}

export interface Mutation {
  readonly __typename?: 'Mutation';
  readonly applyOpenApiRoutes: ConfigurationChangeResult;
  readonly copyConfiguration: ConfigurationChangeResult;
  readonly createConsumerApiKey: SecretPayload;
  readonly createDnsProviderProfile: DnsProviderProfileInfo;
  readonly createEnvironment: GatewayEnvironment;
  readonly createLocalUser: LocalUserSecretPayload;
  readonly createManagementApiKey: SecretPayload;
  readonly createRoute: ConfigurationChangeResult;
  readonly createUpstream: ConfigurationChangeResult;
  readonly decommissionInstance: Scalars['Boolean']['output'];
  readonly deleteAcmeAccount: Scalars['Boolean']['output'];
  readonly deleteDnsProviderProfile: Scalars['Boolean']['output'];
  readonly deleteEnvironment: Scalars['Boolean']['output'];
  readonly deleteInboundCertificate: Scalars['Boolean']['output'];
  readonly deleteLocalUser: Scalars['Boolean']['output'];
  readonly deleteManagedCertificate: Scalars['Boolean']['output'];
  readonly deleteRoute: ConfigurationChangeResult;
  readonly deleteRouteUnavailableResponseProfile: ConfigurationChangeResult;
  readonly deleteUpstream: ConfigurationChangeResult;
  readonly discardPendingConfiguration: Scalars['Boolean']['output'];
  readonly duplicateRoute: ConfigurationChangeResult;
  readonly importConfiguration: ConfigurationChangeResult;
  readonly issueAcmeCertificate: ManagedCertificateInfo;
  readonly previewOpenApiRoutes: ManagedOpenApiPreview;
  readonly publishPendingConfiguration: ConfigRevision;
  readonly registerAcmeAccount: AcmeAccountInfo;
  readonly renameInboundCertificate: InboundCertificateInfo;
  readonly renameManagedCertificate: ManagedCertificateInfo;
  readonly renewAcmeCertificate: ManagedCertificateInfo;
  readonly replaceInboundCertificate: InboundCertificateInfo;
  readonly resetLocalUserPassword: LocalUserSecretPayload;
  readonly restoreConfigurationSnapshot: ConfigurationChangeResult;
  readonly revertConfigurationChange: ConfigurationChangeResult;
  readonly revokeConsumerApiKey: Scalars['Boolean']['output'];
  readonly revokeManagementApiKey: Scalars['Boolean']['output'];
  readonly rotateConsumerApiKey: SecretPayload;
  readonly rotateManagementApiKey: SecretPayload;
  readonly runRetentionMaintenance: RetentionResult;
  readonly saveRouteUnavailableResponseProfile: ConfigurationChangeResult;
  readonly setDefaultAcmeAccount: AcmeAccountInfo;
  readonly setEnvironmentArchived: GatewayEnvironment;
  readonly setEnvironmentPublishingMode: GatewayEnvironment;
  readonly setRouteEnabled: ConfigurationChangeResult;
  readonly setRouteOperationalState: ConfigurationChangeResult;
  readonly testDnsProviderProfile: ReadonlyArray<DnsManagedZone>;
  readonly updateAcmeAccount: AcmeAccountInfo;
  readonly updateAcmeAccountContact: AcmeAccountInfo;
  readonly updateConsumerApiKey: Scalars['Boolean']['output'];
  readonly updateDnsProviderProfile: DnsProviderProfileInfo;
  readonly updateEntraConnection: EntraConnectionInfo;
  readonly updateEnvironment: GatewayEnvironment;
  readonly updateInboundSecuritySettings: InboundSecuritySettingsInfo;
  readonly updateLocalUser: LocalUserInfo;
  readonly updateManagementApiKey: Scalars['Boolean']['output'];
  readonly updateRoute: ConfigurationChangeResult;
  readonly updateRouteBasics: ConfigurationChangeResult;
  readonly updateRouteFeatures: ConfigurationChangeResult;
  readonly updateRouteOperationalDefaults: ConfigurationChangeResult;
  readonly updateUpstream: ConfigurationChangeResult;
  readonly uploadInboundCertificate: InboundCertificateInfo;
}

export interface MutationApplyOpenApiRoutesArgs {
  previewToken: Scalars['String']['input'];
  routeIds: ReadonlyArray<Scalars['String']['input']>;
}

export interface MutationCopyConfigurationArgs {
  expectedTargetVersion: Scalars['UUID']['input'];
  sourceEnvironmentId: Scalars['UUID']['input'];
  targetEnvironmentId: Scalars['UUID']['input'];
}

export interface MutationCreateConsumerApiKeyArgs {
  allowedCidrs?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  claims: ReadonlyArray<KeyValuePairOfStringAndStringInput>;
  environmentIds: ReadonlyArray<Scalars['UUID']['input']>;
  expiresAtUtc?: InputMaybe<Scalars['DateTime']['input']>;
  name: Scalars['String']['input'];
  routeIds: ReadonlyArray<Scalars['String']['input']>;
}

export interface MutationCreateDnsProviderProfileArgs {
  credentials: DnsProviderCredentialsInput;
  name: Scalars['String']['input'];
  provider: DnsProviderKind;
}

export interface MutationCreateEnvironmentArgs {
  description?: InputMaybe<Scalars['String']['input']>;
  displayName: Scalars['String']['input'];
  slug: Scalars['String']['input'];
}

export interface MutationCreateLocalUserArgs {
  displayName?: InputMaybe<Scalars['String']['input']>;
  roles: ReadonlyArray<Scalars['String']['input']>;
  username: Scalars['String']['input'];
}

export interface MutationCreateManagementApiKeyArgs {
  allowedCidrs?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  expiresAtUtc?: InputMaybe<Scalars['DateTime']['input']>;
  name: Scalars['String']['input'];
  scopes: ReadonlyArray<Scalars['String']['input']>;
}

export interface MutationCreateRouteArgs {
  environmentId: Scalars['UUID']['input'];
  input: CreateManagedRouteInput;
}

export interface MutationCreateUpstreamArgs {
  environmentId: Scalars['UUID']['input'];
  input: SaveNamedUpstreamInput;
}

export interface MutationDecommissionInstanceArgs {
  id: Scalars['UUID']['input'];
}

export interface MutationDeleteAcmeAccountArgs {
  id: Scalars['UUID']['input'];
}

export interface MutationDeleteDnsProviderProfileArgs {
  id: Scalars['UUID']['input'];
}

export interface MutationDeleteEnvironmentArgs {
  expectedVersion: Scalars['UUID']['input'];
  id: Scalars['UUID']['input'];
}

export interface MutationDeleteInboundCertificateArgs {
  id: Scalars['UUID']['input'];
}

export interface MutationDeleteLocalUserArgs {
  id: Scalars['UUID']['input'];
}

export interface MutationDeleteManagedCertificateArgs {
  id: Scalars['UUID']['input'];
}

export interface MutationDeleteRouteArgs {
  environmentId: Scalars['UUID']['input'];
  expectedRouteVersion: Scalars['String']['input'];
  routeId: Scalars['String']['input'];
}

export interface MutationDeleteRouteUnavailableResponseProfileArgs {
  environmentId: Scalars['UUID']['input'];
  expectedConfigurationVersion?: InputMaybe<Scalars['UUID']['input']>;
  profileId: Scalars['String']['input'];
}

export interface MutationDeleteUpstreamArgs {
  environmentId: Scalars['UUID']['input'];
  expectedUpstreamVersion: Scalars['String']['input'];
  upstreamId: Scalars['String']['input'];
}

export interface MutationDiscardPendingConfigurationArgs {
  environmentId: Scalars['UUID']['input'];
  expectedVersion: Scalars['UUID']['input'];
}

export interface MutationDuplicateRouteArgs {
  environmentId: Scalars['UUID']['input'];
  expectedRouteVersion: Scalars['String']['input'];
  name: Scalars['String']['input'];
  routeId: Scalars['String']['input'];
}

export interface MutationImportConfigurationArgs {
  environmentId: Scalars['UUID']['input'];
  expectedConfigurationVersion: Scalars['UUID']['input'];
  json: Scalars['String']['input'];
}

export interface MutationIssueAcmeCertificateArgs {
  acmeAccountId?: InputMaybe<Scalars['UUID']['input']>;
  challengeKind: AcmeChallengeKind;
  dnsNames: ReadonlyArray<Scalars['String']['input']>;
  dnsProviderProfileId?: InputMaybe<Scalars['UUID']['input']>;
  name: Scalars['String']['input'];
}

export interface MutationPreviewOpenApiRoutesArgs {
  environmentId: Scalars['UUID']['input'];
  expectedConfigurationVersion: Scalars['UUID']['input'];
  routeIdPrefix?: InputMaybe<Scalars['String']['input']>;
  source: Scalars['String']['input'];
  upstreamUrl: Scalars['String']['input'];
}

export interface MutationPublishPendingConfigurationArgs {
  comment?: InputMaybe<Scalars['String']['input']>;
  environmentId: Scalars['UUID']['input'];
  expectedVersion: Scalars['UUID']['input'];
}

export interface MutationRegisterAcmeAccountArgs {
  contactEmail: Scalars['String']['input'];
  directoryUrl?: InputMaybe<Scalars['String']['input']>;
  termsAccepted: Scalars['Boolean']['input'];
}

export interface MutationRenameInboundCertificateArgs {
  expectedVersion: Scalars['UUID']['input'];
  id: Scalars['UUID']['input'];
  name: Scalars['String']['input'];
}

export interface MutationRenameManagedCertificateArgs {
  expectedVersion: Scalars['UUID']['input'];
  id: Scalars['UUID']['input'];
  name: Scalars['String']['input'];
}

export interface MutationRenewAcmeCertificateArgs {
  expectedVersion: Scalars['UUID']['input'];
  id: Scalars['UUID']['input'];
}

export interface MutationReplaceInboundCertificateArgs {
  expectedVersion: Scalars['UUID']['input'];
  id: Scalars['UUID']['input'];
  password?: InputMaybe<Scalars['String']['input']>;
  pkcs12Base64: Scalars['String']['input'];
}

export interface MutationResetLocalUserPasswordArgs {
  id: Scalars['UUID']['input'];
}

export interface MutationRestoreConfigurationSnapshotArgs {
  environmentId: Scalars['UUID']['input'];
  expectedConfigurationVersion: Scalars['UUID']['input'];
  revisionId: Scalars['UUID']['input'];
}

export interface MutationRevertConfigurationChangeArgs {
  changeId: Scalars['UUID']['input'];
  environmentId: Scalars['UUID']['input'];
  expectedConfigurationVersion: Scalars['UUID']['input'];
}

export interface MutationRevokeConsumerApiKeyArgs {
  id: Scalars['UUID']['input'];
}

export interface MutationRevokeManagementApiKeyArgs {
  id: Scalars['UUID']['input'];
}

export interface MutationRotateConsumerApiKeyArgs {
  id: Scalars['UUID']['input'];
}

export interface MutationRotateManagementApiKeyArgs {
  id: Scalars['UUID']['input'];
}

export interface MutationRunRetentionMaintenanceArgs {
  activationBeforeUtc: Scalars['DateTime']['input'];
  auditBeforeUtc: Scalars['DateTime']['input'];
}

export interface MutationSaveRouteUnavailableResponseProfileArgs {
  environmentId: Scalars['UUID']['input'];
  expectedConfigurationVersion?: InputMaybe<Scalars['UUID']['input']>;
  input: SaveRouteUnavailableResponseProfileInput;
}

export interface MutationSetDefaultAcmeAccountArgs {
  expectedVersion: Scalars['UUID']['input'];
  id: Scalars['UUID']['input'];
}

export interface MutationSetEnvironmentArchivedArgs {
  archived: Scalars['Boolean']['input'];
  expectedVersion: Scalars['UUID']['input'];
  id: Scalars['UUID']['input'];
}

export interface MutationSetEnvironmentPublishingModeArgs {
  expectedVersion: Scalars['UUID']['input'];
  id: Scalars['UUID']['input'];
  mode: ConfigurationPublishingMode;
}

export interface MutationSetRouteEnabledArgs {
  enabled: Scalars['Boolean']['input'];
  environmentId: Scalars['UUID']['input'];
  expectedRouteVersion: Scalars['String']['input'];
  routeId: Scalars['String']['input'];
}

export interface MutationSetRouteOperationalStateArgs {
  environmentId: Scalars['UUID']['input'];
  expectedRouteVersion: Scalars['String']['input'];
  input: UpdateRouteOperationalStateInput;
  routeId: Scalars['String']['input'];
}

export interface MutationTestDnsProviderProfileArgs {
  id: Scalars['UUID']['input'];
}

export interface MutationUpdateAcmeAccountArgs {
  contactEmail: Scalars['String']['input'];
  expectedVersion: Scalars['UUID']['input'];
  id: Scalars['UUID']['input'];
}

export interface MutationUpdateAcmeAccountContactArgs {
  contactEmail: Scalars['String']['input'];
  expectedVersion: Scalars['UUID']['input'];
}

export interface MutationUpdateConsumerApiKeyArgs {
  allowedCidrs?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  claims: ReadonlyArray<KeyValuePairOfStringAndStringInput>;
  environmentIds: ReadonlyArray<Scalars['UUID']['input']>;
  expiresAtUtc?: InputMaybe<Scalars['DateTime']['input']>;
  id: Scalars['UUID']['input'];
  name: Scalars['String']['input'];
  routeIds: ReadonlyArray<Scalars['String']['input']>;
}

export interface MutationUpdateDnsProviderProfileArgs {
  credentials?: InputMaybe<DnsProviderCredentialsInput>;
  expectedVersion: Scalars['UUID']['input'];
  id: Scalars['UUID']['input'];
  name: Scalars['String']['input'];
}

export interface MutationUpdateEntraConnectionArgs {
  audience: Scalars['String']['input'];
  authority: Scalars['String']['input'];
  clientId: Scalars['String']['input'];
  enabled: Scalars['Boolean']['input'];
  expectedVersion: Scalars['UUID']['input'];
  scope: Scalars['String']['input'];
}

export interface MutationUpdateEnvironmentArgs {
  description?: InputMaybe<Scalars['String']['input']>;
  displayName: Scalars['String']['input'];
  expectedVersion: Scalars['UUID']['input'];
  id: Scalars['UUID']['input'];
}

export interface MutationUpdateInboundSecuritySettingsArgs {
  enabled: Scalars['Boolean']['input'];
  expectedVersion?: InputMaybe<Scalars['UUID']['input']>;
  hosts: ReadonlyArray<Scalars['String']['input']>;
  includeSubDomains: Scalars['Boolean']['input'];
  maxAgeSeconds: Scalars['Int']['input'];
  preload: Scalars['Boolean']['input'];
}

export interface MutationUpdateLocalUserArgs {
  displayName?: InputMaybe<Scalars['String']['input']>;
  enabled: Scalars['Boolean']['input'];
  expectedVersion: Scalars['UUID']['input'];
  id: Scalars['UUID']['input'];
  roles: ReadonlyArray<Scalars['String']['input']>;
}

export interface MutationUpdateManagementApiKeyArgs {
  allowedCidrs?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  expiresAtUtc?: InputMaybe<Scalars['DateTime']['input']>;
  id: Scalars['UUID']['input'];
  name: Scalars['String']['input'];
  scopes: ReadonlyArray<Scalars['String']['input']>;
}

export interface MutationUpdateRouteArgs {
  environmentId: Scalars['UUID']['input'];
  expectedRouteVersion: Scalars['String']['input'];
  input: UpdateManagedRouteInput;
  routeId: Scalars['String']['input'];
}

export interface MutationUpdateRouteBasicsArgs {
  environmentId: Scalars['UUID']['input'];
  expectedRouteVersion: Scalars['String']['input'];
  input: UpdateManagedRouteBasicsInput;
  routeId: Scalars['String']['input'];
}

export interface MutationUpdateRouteFeaturesArgs {
  environmentId: Scalars['UUID']['input'];
  expectedRouteVersion: Scalars['String']['input'];
  input: ManagedRouteFeaturesInput;
  routeId: Scalars['String']['input'];
}

export interface MutationUpdateRouteOperationalDefaultsArgs {
  environmentId: Scalars['UUID']['input'];
  expectedConfigurationVersion?: InputMaybe<Scalars['UUID']['input']>;
  input: UpdateRouteOperationalDefaultsInput;
}

export interface MutationUpdateUpstreamArgs {
  environmentId: Scalars['UUID']['input'];
  expectedUpstreamVersion: Scalars['String']['input'];
  input: SaveNamedUpstreamInput;
  upstreamId: Scalars['String']['input'];
}

export interface MutationUploadInboundCertificateArgs {
  name: Scalars['String']['input'];
  password?: InputMaybe<Scalars['String']['input']>;
  pkcs12Base64: Scalars['String']['input'];
}

export interface NamedUpstream {
  readonly __typename?: 'NamedUpstream';
  readonly destinations: ReadonlyArray<KeyValuePairOfStringAndGatewayDestination>;
  readonly health?: Maybe<HealthPolicy>;
  readonly httpClient?: Maybe<UpstreamHttpPolicy>;
  readonly id: Scalars['String']['output'];
  readonly loadBalancingPolicy: Scalars['String']['output'];
  readonly name: Scalars['String']['output'];
  readonly sessionAffinity?: Maybe<SessionAffinityPolicy>;
  readonly tls?: Maybe<UpstreamTlsPolicy>;
  readonly traffic?: Maybe<TrafficPolicy>;
  readonly version: Scalars['String']['output'];
}

/** Information about pagination in a connection. */
export interface PageInfo {
  readonly __typename?: 'PageInfo';
  /** When paginating forwards, the cursor to continue. */
  readonly endCursor?: Maybe<Scalars['String']['output']>;
  /** Indicates whether more edges exist following the set defined by the clients arguments. */
  readonly hasNextPage: Scalars['Boolean']['output'];
  /** Indicates whether more edges exist prior the set defined by the clients arguments. */
  readonly hasPreviousPage: Scalars['Boolean']['output'];
  /** When paginating backwards, the cursor to continue. */
  readonly startCursor?: Maybe<Scalars['String']['output']>;
}

export interface PendingConfigurationChange {
  readonly __typename?: 'PendingConfigurationChange';
  readonly kind: Scalars['String']['output'];
  readonly resourceId?: Maybe<Scalars['String']['output']>;
  readonly summary: Scalars['String']['output'];
}

export interface PendingConfigurationInfo {
  readonly __typename?: 'PendingConfigurationInfo';
  readonly baseRevisionId?: Maybe<Scalars['UUID']['output']>;
  readonly changes: ReadonlyArray<PendingConfigurationChange>;
  readonly createdAtUtc: Scalars['DateTime']['output'];
  readonly createdBy: Scalars['String']['output'];
  readonly revisionId: Scalars['UUID']['output'];
  readonly validation: ValidationReport;
  readonly version: Scalars['UUID']['output'];
}

export interface Query {
  readonly __typename?: 'Query';
  readonly acmeAccount?: Maybe<AcmeAccountInfo>;
  readonly acmeAccounts: ReadonlyArray<AcmeAccountInfo>;
  readonly acmeDirectories: ReadonlyArray<AcmeDirectorySnapshot>;
  readonly acmeDirectory: AcmeDirectorySnapshot;
  readonly activationConnection?: Maybe<ActivationConnectionConnection>;
  readonly activationHistory: ReadonlyArray<GatewayActivationEvent>;
  readonly activeClusters: ReadonlyArray<GatewayCluster>;
  readonly activeRevision?: Maybe<ConfigRevision>;
  readonly activeRoutes: ReadonlyArray<GatewayRoute>;
  readonly auditConnection?: Maybe<AuditConnectionConnection>;
  readonly auditEvents: ReadonlyArray<AuditEvent>;
  readonly clusters?: Maybe<ClustersConnection>;
  readonly configurationHistory: ReadonlyArray<ConfigRevision>;
  readonly configurationSchema: Scalars['String']['output'];
  readonly consumerApiKey?: Maybe<ApiKeyInfo>;
  readonly consumerApiKeys: ReadonlyArray<ApiKeyInfo>;
  readonly dnsProviderProfiles: ReadonlyArray<DnsProviderProfileInfo>;
  readonly drift: ReadonlyArray<InstanceDrift>;
  readonly entraConnection: EntraConnectionInfo;
  readonly environment?: Maybe<GatewayEnvironment>;
  readonly environmentConnection?: Maybe<EnvironmentConnectionConnection>;
  readonly environments: ReadonlyArray<GatewayEnvironment>;
  readonly exportConfiguration: Scalars['String']['output'];
  readonly gatewayDiagnostics: GatewayDiagnostics;
  readonly inboundCertificates: ReadonlyArray<InboundCertificateInfo>;
  readonly inboundSecuritySettings: InboundSecuritySettingsInfo;
  readonly instanceConnection?: Maybe<InstanceConnectionConnection>;
  readonly instances: ReadonlyArray<GatewayInstance>;
  readonly localRoleCatalog: ReadonlyArray<Scalars['String']['output']>;
  readonly localUsers: ReadonlyArray<LocalUserInfo>;
  readonly managedCertificateActivity: ReadonlyArray<ManagedCertificateActivityInfo>;
  readonly managedCertificateDnsChallenges: ReadonlyArray<ManagedCertificateDnsChallengeInfo>;
  readonly managedCertificates: ReadonlyArray<ManagedCertificateInfo>;
  readonly managementApiKey?: Maybe<ApiKeyInfo>;
  readonly managementApiKeys: ReadonlyArray<ApiKeyInfo>;
  readonly me: ManagementIdentity;
  readonly pendingConfiguration?: Maybe<PendingConfigurationInfo>;
  readonly revision?: Maybe<ConfigRevision>;
  readonly revisionConnection?: Maybe<RevisionConnectionConnection>;
  readonly revisionDiff: RevisionDiff;
  readonly revisionRoutes?: Maybe<RevisionRoutesConnection>;
  readonly revisions: ReadonlyArray<ConfigRevision>;
  readonly route?: Maybe<ManagedRoute>;
  readonly routeFeatureCatalog: ReadonlyArray<RouteFeatureDescriptor>;
  readonly routeOperationalDefaults: RouteOperationalDefaults;
  readonly routeRuntimeStatuses: ReadonlyArray<RouteRuntimeStatus>;
  readonly routeUnavailableResponseProfiles: ReadonlyArray<RouteUnavailableResponseProfile>;
  readonly routes: ReadonlyArray<ManagedRoute>;
  readonly systemStatus: SystemStatus;
  readonly upstream?: Maybe<NamedUpstream>;
  readonly upstreams: ReadonlyArray<NamedUpstream>;
  readonly validateRevision: ValidationReport;
}

export interface QueryActivationConnectionArgs {
  after?: InputMaybe<Scalars['String']['input']>;
  before?: InputMaybe<Scalars['String']['input']>;
  environmentId: Scalars['UUID']['input'];
  first?: InputMaybe<Scalars['Int']['input']>;
  last?: InputMaybe<Scalars['Int']['input']>;
}

export interface QueryActivationHistoryArgs {
  environmentId: Scalars['UUID']['input'];
  take: Scalars['Int']['input'];
}

export interface QueryActiveClustersArgs {
  environmentId: Scalars['UUID']['input'];
}

export interface QueryActiveRevisionArgs {
  environmentId: Scalars['UUID']['input'];
}

export interface QueryActiveRoutesArgs {
  environmentId: Scalars['UUID']['input'];
}

export interface QueryAuditConnectionArgs {
  after?: InputMaybe<Scalars['String']['input']>;
  before?: InputMaybe<Scalars['String']['input']>;
  environmentId?: InputMaybe<Scalars['UUID']['input']>;
  first?: InputMaybe<Scalars['Int']['input']>;
  last?: InputMaybe<Scalars['Int']['input']>;
}

export interface QueryAuditEventsArgs {
  environmentId?: InputMaybe<Scalars['UUID']['input']>;
  take: Scalars['Int']['input'];
}

export interface QueryClustersArgs {
  after?: InputMaybe<Scalars['String']['input']>;
  before?: InputMaybe<Scalars['String']['input']>;
  filter?: InputMaybe<Scalars['String']['input']>;
  first?: InputMaybe<Scalars['Int']['input']>;
  last?: InputMaybe<Scalars['Int']['input']>;
  revisionId: Scalars['UUID']['input'];
}

export interface QueryConfigurationHistoryArgs {
  environmentId: Scalars['UUID']['input'];
}

export interface QueryConfigurationSchemaArgs {
  version: Scalars['Int']['input'];
}

export interface QueryConsumerApiKeyArgs {
  id: Scalars['UUID']['input'];
}

export interface QueryDriftArgs {
  environmentId: Scalars['UUID']['input'];
}

export interface QueryEnvironmentArgs {
  id: Scalars['UUID']['input'];
}

export interface QueryEnvironmentConnectionArgs {
  after?: InputMaybe<Scalars['String']['input']>;
  before?: InputMaybe<Scalars['String']['input']>;
  first?: InputMaybe<Scalars['Int']['input']>;
  last?: InputMaybe<Scalars['Int']['input']>;
}

export interface QueryExportConfigurationArgs {
  environmentId: Scalars['UUID']['input'];
}

export interface QueryGatewayDiagnosticsArgs {
  environmentId: Scalars['UUID']['input'];
}

export interface QueryInstanceConnectionArgs {
  after?: InputMaybe<Scalars['String']['input']>;
  before?: InputMaybe<Scalars['String']['input']>;
  environmentId: Scalars['UUID']['input'];
  first?: InputMaybe<Scalars['Int']['input']>;
  last?: InputMaybe<Scalars['Int']['input']>;
}

export interface QueryInstancesArgs {
  environmentId: Scalars['UUID']['input'];
}

export interface QueryManagedCertificateActivityArgs {
  managedCertificateId: Scalars['UUID']['input'];
  take: Scalars['Int']['input'];
}

export interface QueryManagedCertificateDnsChallengesArgs {
  managedCertificateId: Scalars['UUID']['input'];
}

export interface QueryManagementApiKeyArgs {
  id: Scalars['UUID']['input'];
}

export interface QueryPendingConfigurationArgs {
  environmentId: Scalars['UUID']['input'];
}

export interface QueryRevisionArgs {
  id: Scalars['UUID']['input'];
}

export interface QueryRevisionConnectionArgs {
  after?: InputMaybe<Scalars['String']['input']>;
  before?: InputMaybe<Scalars['String']['input']>;
  environmentId: Scalars['UUID']['input'];
  first?: InputMaybe<Scalars['Int']['input']>;
  last?: InputMaybe<Scalars['Int']['input']>;
}

export interface QueryRevisionDiffArgs {
  fromRevisionId: Scalars['UUID']['input'];
  toRevisionId: Scalars['UUID']['input'];
}

export interface QueryRevisionRoutesArgs {
  after?: InputMaybe<Scalars['String']['input']>;
  before?: InputMaybe<Scalars['String']['input']>;
  filter?: InputMaybe<Scalars['String']['input']>;
  first?: InputMaybe<Scalars['Int']['input']>;
  last?: InputMaybe<Scalars['Int']['input']>;
  revisionId: Scalars['UUID']['input'];
}

export interface QueryRevisionsArgs {
  environmentId: Scalars['UUID']['input'];
  state?: InputMaybe<RevisionState>;
}

export interface QueryRouteArgs {
  environmentId: Scalars['UUID']['input'];
  routeId: Scalars['String']['input'];
}

export interface QueryRouteOperationalDefaultsArgs {
  environmentId: Scalars['UUID']['input'];
}

export interface QueryRouteRuntimeStatusesArgs {
  environmentId: Scalars['UUID']['input'];
}

export interface QueryRouteUnavailableResponseProfilesArgs {
  environmentId: Scalars['UUID']['input'];
}

export interface QueryRoutesArgs {
  environmentId: Scalars['UUID']['input'];
  filter?: InputMaybe<Scalars['String']['input']>;
}

export interface QueryUpstreamArgs {
  environmentId: Scalars['UUID']['input'];
  upstreamId: Scalars['String']['input'];
}

export interface QueryUpstreamsArgs {
  environmentId: Scalars['UUID']['input'];
}

export interface QueryValidateRevisionArgs {
  id: Scalars['UUID']['input'];
}

export interface RateLimitPolicy {
  readonly __typename?: 'RateLimitPolicy';
  readonly partitionBy: Scalars['String']['output'];
  readonly partitionName?: Maybe<Scalars['String']['output']>;
  readonly permitLimit: Scalars['Int']['output'];
  readonly queueLimit: Scalars['Int']['output'];
  readonly queueOrder: Scalars['String']['output'];
  readonly segmentsPerWindow: Scalars['Int']['output'];
  readonly tokensPerPeriod?: Maybe<Scalars['Int']['output']>;
  readonly type: Scalars['String']['output'];
  readonly window?: Maybe<Scalars['Duration']['output']>;
}

export interface RateLimitPolicyInput {
  readonly partitionBy?: Scalars['String']['input'];
  readonly partitionName?: InputMaybe<Scalars['String']['input']>;
  readonly permitLimit: Scalars['Int']['input'];
  readonly queueLimit?: Scalars['Int']['input'];
  readonly queueOrder?: Scalars['String']['input'];
  readonly segmentsPerWindow?: Scalars['Int']['input'];
  readonly tokensPerPeriod?: InputMaybe<Scalars['Int']['input']>;
  readonly type: Scalars['String']['input'];
  readonly window?: InputMaybe<Scalars['Duration']['input']>;
}

export interface RequestValidationPolicy {
  readonly __typename?: 'RequestValidationPolicy';
  readonly jsonSchema: Scalars['String']['output'];
  readonly maximumBodyBytes: Scalars['Long']['output'];
}

export interface RequestValidationPolicyInput {
  readonly jsonSchema: Scalars['String']['input'];
  readonly maximumBodyBytes?: Scalars['Long']['input'];
}

export interface ResiliencePolicy {
  readonly __typename?: 'ResiliencePolicy';
  readonly allowedMethods?: Maybe<ReadonlyArray<Scalars['String']['output']>>;
  readonly attemptTimeout?: Maybe<Scalars['Duration']['output']>;
  readonly backoff?: Maybe<Scalars['Duration']['output']>;
  readonly breakDuration?: Maybe<Scalars['Duration']['output']>;
  readonly failureRatio?: Maybe<Scalars['Float']['output']>;
  readonly jitter: Scalars['Boolean']['output'];
  readonly maximumBufferedRequestBytes: Scalars['Long']['output'];
  readonly minimumThroughput?: Maybe<Scalars['Int']['output']>;
  readonly retryCount: Scalars['Int']['output'];
  readonly retryTransportFailures: Scalars['Boolean']['output'];
  readonly samplingDuration?: Maybe<Scalars['Duration']['output']>;
  readonly statusCodes?: Maybe<ReadonlyArray<Scalars['Int']['output']>>;
}

export interface ResiliencePolicyInput {
  readonly allowedMethods?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  readonly attemptTimeout?: InputMaybe<Scalars['Duration']['input']>;
  readonly backoff?: InputMaybe<Scalars['Duration']['input']>;
  readonly breakDuration?: InputMaybe<Scalars['Duration']['input']>;
  readonly failureRatio?: InputMaybe<Scalars['Float']['input']>;
  readonly jitter?: Scalars['Boolean']['input'];
  readonly maximumBufferedRequestBytes?: Scalars['Long']['input'];
  readonly minimumThroughput?: InputMaybe<Scalars['Int']['input']>;
  readonly retryCount?: Scalars['Int']['input'];
  readonly retryTransportFailures?: Scalars['Boolean']['input'];
  readonly samplingDuration?: InputMaybe<Scalars['Duration']['input']>;
  readonly statusCodes?: InputMaybe<ReadonlyArray<Scalars['Int']['input']>>;
}

export interface ResponseCachePolicy {
  readonly __typename?: 'ResponseCachePolicy';
  readonly maximumBodyBytes: Scalars['Long']['output'];
  readonly timeToLive: Scalars['Duration']['output'];
  readonly varyByHeaders?: Maybe<ReadonlyArray<Scalars['String']['output']>>;
}

export interface ResponseCachePolicyInput {
  readonly maximumBodyBytes?: Scalars['Long']['input'];
  readonly timeToLive: Scalars['Duration']['input'];
  readonly varyByHeaders?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
}

export interface RetentionResult {
  readonly __typename?: 'RetentionResult';
  readonly activationEventsDeleted: Scalars['Int']['output'];
  readonly auditEventsDeleted: Scalars['Int']['output'];
  readonly leaseAcquired: Scalars['Boolean']['output'];
}

export interface RevisionChange {
  readonly __typename?: 'RevisionChange';
  readonly afterJson?: Maybe<Scalars['String']['output']>;
  readonly beforeJson?: Maybe<Scalars['String']['output']>;
  readonly path: Scalars['String']['output'];
}

/** A connection to a list of items. */
export interface RevisionConnectionConnection {
  readonly __typename?: 'RevisionConnectionConnection';
  /** A list of edges. */
  readonly edges?: Maybe<ReadonlyArray<RevisionConnectionEdge>>;
  /** A flattened list of the nodes. */
  readonly nodes?: Maybe<ReadonlyArray<ConfigRevision>>;
  /** Information to aid in pagination. */
  readonly pageInfo: PageInfo;
}

/** An edge in a connection. */
export interface RevisionConnectionEdge {
  readonly __typename?: 'RevisionConnectionEdge';
  /** A cursor for use in pagination. */
  readonly cursor: Scalars['String']['output'];
  /** The item at the end of the edge. */
  readonly node: ConfigRevision;
}

export interface RevisionDiff {
  readonly __typename?: 'RevisionDiff';
  readonly changedPaths: ReadonlyArray<Scalars['String']['output']>;
  readonly changes: ReadonlyArray<RevisionChange>;
  readonly fromRevisionId: Scalars['UUID']['output'];
  readonly toRevisionId: Scalars['UUID']['output'];
}

/** A connection to a list of items. */
export interface RevisionRoutesConnection {
  readonly __typename?: 'RevisionRoutesConnection';
  /** A list of edges. */
  readonly edges?: Maybe<ReadonlyArray<RevisionRoutesEdge>>;
  /** A flattened list of the nodes. */
  readonly nodes?: Maybe<ReadonlyArray<GatewayRoute>>;
  /** Information to aid in pagination. */
  readonly pageInfo: PageInfo;
}

/** An edge in a connection. */
export interface RevisionRoutesEdge {
  readonly __typename?: 'RevisionRoutesEdge';
  /** A cursor for use in pagination. */
  readonly cursor: Scalars['String']['output'];
  /** The item at the end of the edge. */
  readonly node: GatewayRoute;
}

export type RevisionState
  = | 'ABANDONED'
    | 'DRAFT'
    | 'PUBLISHED';

export interface RouteAccessPolicy {
  readonly __typename?: 'RouteAccessPolicy';
  readonly allowedCidrs?: Maybe<ReadonlyArray<Scalars['String']['output']>>;
  readonly deniedCidrs?: Maybe<ReadonlyArray<Scalars['String']['output']>>;
  readonly maximumRequestBodyBytes?: Maybe<Scalars['Long']['output']>;
}

export interface RouteAccessPolicyInput {
  readonly allowedCidrs?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  readonly deniedCidrs?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  readonly maximumRequestBodyBytes?: InputMaybe<Scalars['Long']['input']>;
}

export interface RouteFeatureDescriptor {
  readonly __typename?: 'RouteFeatureDescriptor';
  readonly category: Scalars['String']['output'];
  readonly description: Scalars['String']['output'];
  readonly displayName: Scalars['String']['output'];
  readonly id: Scalars['String']['output'];
}

export interface RouteMatch {
  readonly __typename?: 'RouteMatch';
  readonly headers: ReadonlyArray<RouteValueMatch>;
  readonly hosts: ReadonlyArray<Scalars['String']['output']>;
  readonly methods: ReadonlyArray<Scalars['String']['output']>;
  readonly path: Scalars['String']['output'];
  readonly queryParameters: ReadonlyArray<RouteValueMatch>;
}

export interface RouteMatchInput {
  readonly headers: ReadonlyArray<RouteValueMatchInput>;
  readonly hosts: ReadonlyArray<Scalars['String']['input']>;
  readonly methods: ReadonlyArray<Scalars['String']['input']>;
  readonly path: Scalars['String']['input'];
  readonly queryParameters: ReadonlyArray<RouteValueMatchInput>;
}

export interface RouteOperationalDefaults {
  readonly __typename?: 'RouteOperationalDefaults';
  readonly drainingProfileId?: Maybe<Scalars['String']['output']>;
  readonly for?: Maybe<Scalars['String']['output']>;
  readonly maintenanceProfileId?: Maybe<Scalars['String']['output']>;
  readonly offlineProfileId?: Maybe<Scalars['String']['output']>;
}

export interface RouteOperationalDefaultsForArgs {
  state: RouteOperationalState;
}

export interface RouteOperationalPolicy {
  readonly __typename?: 'RouteOperationalPolicy';
  readonly response?: Maybe<RouteUnavailableResponse>;
  readonly responseProfileId?: Maybe<Scalars['String']['output']>;
  readonly state: RouteOperationalState;
}

export interface RouteOperationalPolicyInput {
  readonly response?: InputMaybe<RouteUnavailableResponseInput>;
  readonly responseProfileId?: InputMaybe<Scalars['String']['input']>;
  readonly state?: RouteOperationalState;
}

export type RouteOperationalState
  = | 'DRAINING'
    | 'MAINTENANCE'
    | 'OFFLINE'
    | 'ONLINE';

export interface RouteRuntimeStatus {
  readonly __typename?: 'RouteRuntimeStatus';
  readonly activeRequests: Scalars['Long']['output'];
  readonly reportingInstances: Scalars['Int']['output'];
  readonly routeId: Scalars['String']['output'];
}

export interface RouteUnavailableResponse {
  readonly __typename?: 'RouteUnavailableResponse';
  readonly message?: Maybe<Scalars['String']['output']>;
  readonly retryAfter?: Maybe<Scalars['Duration']['output']>;
  readonly statusCode: Scalars['Int']['output'];
  readonly title?: Maybe<Scalars['String']['output']>;
  readonly upstreamUrl?: Maybe<Scalars['String']['output']>;
}

export interface RouteUnavailableResponseInput {
  readonly message?: InputMaybe<Scalars['String']['input']>;
  readonly retryAfter?: InputMaybe<Scalars['Duration']['input']>;
  readonly statusCode?: Scalars['Int']['input'];
  readonly title?: InputMaybe<Scalars['String']['input']>;
  readonly upstreamUrl?: InputMaybe<Scalars['String']['input']>;
}

export interface RouteUnavailableResponseProfile {
  readonly __typename?: 'RouteUnavailableResponseProfile';
  readonly id: Scalars['String']['output'];
  readonly name: Scalars['String']['output'];
  readonly response: RouteUnavailableResponse;
}

export interface RouteValueMatch {
  readonly __typename?: 'RouteValueMatch';
  readonly isCaseSensitive: Scalars['Boolean']['output'];
  readonly mode: Scalars['String']['output'];
  readonly name: Scalars['String']['output'];
  readonly pattern: Scalars['String']['output'];
}

export interface RouteValueMatchInput {
  readonly isCaseSensitive?: Scalars['Boolean']['input'];
  readonly mode?: Scalars['String']['input'];
  readonly name: Scalars['String']['input'];
  readonly pattern: Scalars['String']['input'];
}

export interface SaveNamedUpstreamInput {
  readonly destinations: ReadonlyArray<KeyValuePairOfStringAndGatewayDestinationInput>;
  readonly health?: InputMaybe<HealthPolicyInput>;
  readonly httpClient?: InputMaybe<UpstreamHttpPolicyInput>;
  readonly loadBalancingPolicy?: Scalars['String']['input'];
  readonly name: Scalars['String']['input'];
  readonly sessionAffinity?: InputMaybe<SessionAffinityPolicyInput>;
  readonly tls?: InputMaybe<UpstreamTlsPolicyInput>;
  readonly traffic?: InputMaybe<TrafficPolicyInput>;
}

export interface SaveRouteUnavailableResponseProfileInput {
  readonly id?: InputMaybe<Scalars['String']['input']>;
  readonly message?: InputMaybe<Scalars['String']['input']>;
  readonly name: Scalars['String']['input'];
  readonly retryAfter?: InputMaybe<Scalars['Duration']['input']>;
  readonly statusCode?: Scalars['Int']['input'];
  readonly title?: InputMaybe<Scalars['String']['input']>;
  readonly upstreamUrl?: InputMaybe<Scalars['String']['input']>;
}

export interface SecretPayload {
  readonly __typename?: 'SecretPayload';
  readonly id: Scalars['UUID']['output'];
  readonly prefix: Scalars['String']['output'];
  readonly secret: Scalars['String']['output'];
}

export interface SessionAffinityPolicy {
  readonly __typename?: 'SessionAffinityPolicy';
  readonly cookieName: Scalars['String']['output'];
  readonly domain?: Maybe<Scalars['String']['output']>;
  readonly enabled: Scalars['Boolean']['output'];
  readonly expiration?: Maybe<Scalars['Duration']['output']>;
  readonly failurePolicy: Scalars['String']['output'];
  readonly path?: Maybe<Scalars['String']['output']>;
  readonly policy: Scalars['String']['output'];
  readonly sameSite: Scalars['String']['output'];
  readonly securePolicy: Scalars['String']['output'];
}

export interface SessionAffinityPolicyInput {
  readonly cookieName?: Scalars['String']['input'];
  readonly domain?: InputMaybe<Scalars['String']['input']>;
  readonly enabled?: Scalars['Boolean']['input'];
  readonly expiration?: InputMaybe<Scalars['Duration']['input']>;
  readonly failurePolicy?: Scalars['String']['input'];
  readonly path?: InputMaybe<Scalars['String']['input']>;
  readonly policy?: Scalars['String']['input'];
  readonly sameSite?: Scalars['String']['input'];
  readonly securePolicy?: Scalars['String']['input'];
}

export interface SystemStatus {
  readonly __typename?: 'SystemStatus';
  readonly checkedAtUtc: Scalars['DateTime']['output'];
  readonly version: Scalars['String']['output'];
}

export interface TimeoutPolicy {
  readonly __typename?: 'TimeoutPolicy';
  readonly total: Scalars['Duration']['output'];
}

export interface TimeoutPolicyInput {
  readonly total: Scalars['Duration']['input'];
}

export interface TrafficPolicy {
  readonly __typename?: 'TrafficPolicy';
  readonly allocations: ReadonlyArray<KeyValuePairOfStringAndInt32>;
  readonly fallbackToHealthyPool: Scalars['Boolean']['output'];
  readonly key?: Maybe<Scalars['String']['output']>;
  readonly keySource?: Maybe<Scalars['String']['output']>;
  readonly mode: Scalars['String']['output'];
}

export interface TrafficPolicyInput {
  readonly allocations: ReadonlyArray<KeyValuePairOfStringAndInt32Input>;
  readonly fallbackToHealthyPool?: Scalars['Boolean']['input'];
  readonly key?: InputMaybe<Scalars['String']['input']>;
  readonly keySource?: InputMaybe<Scalars['String']['input']>;
  readonly mode?: Scalars['String']['input'];
}

export interface UpdateManagedRouteBasicsInput {
  readonly destinations?: InputMaybe<ReadonlyArray<KeyValuePairOfStringAndGatewayDestinationInput>>;
  readonly enabled?: Scalars['Boolean']['input'];
  readonly headers?: InputMaybe<ReadonlyArray<RouteValueMatchInput>>;
  readonly hosts?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  readonly httpClient?: InputMaybe<UpstreamHttpPolicyInput>;
  readonly inbound?: InputMaybe<InboundRoutePolicyInput>;
  readonly loadBalancingPolicy?: InputMaybe<Scalars['String']['input']>;
  readonly methods?: InputMaybe<ReadonlyArray<Scalars['String']['input']>>;
  readonly name: Scalars['String']['input'];
  readonly order?: InputMaybe<Scalars['Int']['input']>;
  readonly path: Scalars['String']['input'];
  readonly pathHandling?: InputMaybe<UpstreamPathHandling>;
  readonly pathPrefixToRemove?: InputMaybe<Scalars['String']['input']>;
  readonly preserveOriginalHost?: InputMaybe<Scalars['Boolean']['input']>;
  readonly queryParameters?: InputMaybe<ReadonlyArray<RouteValueMatchInput>>;
  readonly upstreamId?: InputMaybe<Scalars['String']['input']>;
  readonly upstreamUrl?: InputMaybe<Scalars['String']['input']>;
}

export interface UpdateManagedRouteInput {
  readonly enabled: Scalars['Boolean']['input'];
  readonly features: ManagedRouteFeaturesInput;
  readonly inbound?: InputMaybe<InboundRoutePolicyInput>;
  readonly match: RouteMatchInput;
  readonly metadata?: InputMaybe<GatewayMetadataInput>;
  readonly name: Scalars['String']['input'];
  readonly operations?: InputMaybe<RouteOperationalPolicyInput>;
  readonly order?: InputMaybe<Scalars['Int']['input']>;
  readonly upstream: ManagedUpstreamInput;
}

export interface UpdateRouteOperationalDefaultsInput {
  readonly drainingProfileId?: InputMaybe<Scalars['String']['input']>;
  readonly maintenanceProfileId?: InputMaybe<Scalars['String']['input']>;
  readonly offlineProfileId?: InputMaybe<Scalars['String']['input']>;
}

export interface UpdateRouteOperationalStateInput {
  readonly message?: InputMaybe<Scalars['String']['input']>;
  readonly responseProfileId?: InputMaybe<Scalars['String']['input']>;
  readonly retryAfter?: InputMaybe<Scalars['Duration']['input']>;
  readonly state: RouteOperationalState;
  readonly statusCode?: Scalars['Int']['input'];
  readonly title?: InputMaybe<Scalars['String']['input']>;
  readonly upstreamUrl?: InputMaybe<Scalars['String']['input']>;
  readonly useEnvironmentDefault?: Scalars['Boolean']['input'];
}

export interface UpstreamHttpPolicy {
  readonly __typename?: 'UpstreamHttpPolicy';
  readonly allowAutoRedirect: Scalars['Boolean']['output'];
  readonly automaticDecompression: Scalars['Boolean']['output'];
  readonly enableMultipleHttp2Connections: Scalars['Boolean']['output'];
  readonly maxConnectionsPerServer?: Maybe<Scalars['Int']['output']>;
  readonly pooledConnectionLifetime?: Maybe<Scalars['Duration']['output']>;
  readonly version: Scalars['String']['output'];
  readonly versionPolicy: Scalars['String']['output'];
}

export interface UpstreamHttpPolicyInput {
  readonly allowAutoRedirect?: Scalars['Boolean']['input'];
  readonly automaticDecompression?: Scalars['Boolean']['input'];
  readonly enableMultipleHttp2Connections?: Scalars['Boolean']['input'];
  readonly maxConnectionsPerServer?: InputMaybe<Scalars['Int']['input']>;
  readonly pooledConnectionLifetime?: InputMaybe<Scalars['Duration']['input']>;
  readonly version?: Scalars['String']['input'];
  readonly versionPolicy?: Scalars['String']['input'];
}

export type UpstreamPathHandling
  = | 'PRESERVE'
    | 'STRIP_PREFIX';

export interface UpstreamTlsPolicy {
  readonly __typename?: 'UpstreamTlsPolicy';
  readonly clientCertificateRef?: Maybe<Scalars['String']['output']>;
  readonly trustBundleRef?: Maybe<Scalars['String']['output']>;
}

export interface UpstreamTlsPolicyInput {
  readonly clientCertificateRef?: InputMaybe<Scalars['String']['input']>;
  readonly trustBundleRef?: InputMaybe<Scalars['String']['input']>;
}

export interface ValidationIssue {
  readonly __typename?: 'ValidationIssue';
  readonly clusterId?: Maybe<Scalars['String']['output']>;
  readonly code: Scalars['String']['output'];
  readonly jsonPath: Scalars['String']['output'];
  readonly message: Scalars['String']['output'];
  readonly routeId?: Maybe<Scalars['String']['output']>;
  readonly severity: ValidationSeverity;
}

export interface ValidationReport {
  readonly __typename?: 'ValidationReport';
  readonly isValid: Scalars['Boolean']['output'];
  readonly issues: ReadonlyArray<ValidationIssue>;
}

export type ValidationSeverity
  = | 'ERROR'
    | 'WARNING';
