using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using ApiGateway.Domain;
using ApiGateway.Management;
using ApiGateway.Persistence;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApiGateway.UnitTests;

public sealed class AcmeCertificateTests
{
    [Fact]
    public void Acme_defaults_allow_four_concurrent_orders_and_six_hours_for_dns()
    {
        var options = new AcmeOptions();

        Assert.Equal(4, options.MaxConcurrentOrders);
        Assert.Equal(TimeSpan.FromHours(6), options.DnsPropagationTimeout);
    }

    [Fact]
    public void Dns_challenge_expiry_matches_the_configured_propagation_timeout()
    {
        var createdAt = new DateTimeOffset(2026, 8, 26, 16, 17, 2, TimeSpan.Zero);

        var expiresAt = AcmeOrderProcessor.DnsChallengeExpiresAt(createdAt, TimeSpan.FromHours(6));

        Assert.Equal(createdAt.AddHours(6), expiresAt);
    }

    [Fact]
    public void Due_certificate_selection_fills_the_available_concurrency_slots()
    {
        var now = new DateTimeOffset(2026, 8, 26, 15, 0, 0, TimeSpan.Zero);
        var certificates = Enumerable.Range(0, 6).Select(index => new ManagedCertificate
        {
            Name = $"certificate-{index}", CreatedBy = "test", NextAttemptAtUtc = now.AddMinutes(index - 6)
        }).ToList();
        certificates[0].State = ManagedCertificateState.Issuing;

        var selected = AcmeOrderProcessor.SelectDueCertificates(certificates, now, 4);

        Assert.Equal(4, selected.Length);
        Assert.DoesNotContain(certificates[0], selected);
        Assert.Equal(certificates.Skip(1).Take(4), selected);
    }

    [Fact]
    public void Interrupted_attempt_recovery_excludes_certificates_active_in_this_worker()
    {
        var active = new ManagedCertificate { Name = "active", CreatedBy = "test" };
        var interrupted = new ManagedCertificate { Name = "interrupted", CreatedBy = "test" };

        var selected = AcmeOrderProcessor.ExcludeActiveAttempts([active, interrupted], [active.Id]);

        Assert.Single(selected);
        Assert.Same(interrupted, selected[0]);
    }

    [Fact]
    public void Dns_names_are_normalized_and_deduplicated()
    {
        var names = ManagedCertificateService.NormalizeDnsNames(["Example.COM.", "example.com", "*.Täst.se"]);

        Assert.Equal(["*.xn--tst-qla.se", "example.com"], names);
    }

    [Fact]
    public void Http_validation_rejects_wildcard_input_at_service_boundary()
    {
        Assert.Throws<ArgumentException>(() => ManagedCertificateService.NormalizeDnsNames(["foo.*.example.com"]));
    }

    [Fact]
    public void Longest_matching_dns_zone_is_selected()
    {
        var zone = DnsChallengeProviderFactory.SelectZone(
            [new DnsManagedZone("example.com", "one"), new DnsManagedZone("sub.example.com", "two")],
            "_acme-challenge.api.sub.example.com");

        Assert.Equal("two", zone.Id);
    }

    [Fact]
    public void Unicode_dns_zone_matches_punycode_challenge_name()
    {
        var zone = DnsChallengeProviderFactory.SelectZone(
            [new DnsManagedZone("sjögrässtigen.se", "unicode-zone")],
            "_acme-challenge.xn--sjgrsstigen-o8a5u.se");

        Assert.Equal("unicode-zone", zone.Id);
    }

    [Fact]
    public void Relative_record_name_normalizes_unicode_zone_and_punycode_record()
    {
        var relative = DigitalOceanDnsProvider.Relative("sjögrässtigen.se",
            "_acme-challenge.xn--sjgrsstigen-o8a5u.se");

        Assert.Equal("_acme-challenge", relative);
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 60)]
    [InlineData(3, 360)]
    [InlineData(4, 1440)]
    public void Renewal_retry_uses_expected_backoff(int attempt, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes),
            ManagedCertificateService.RetryDelay(attempt, DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Rate_limit_retry_honors_the_server_timestamp_with_a_safety_margin()
    {
        var now = new DateTimeOffset(2026, 8, 26, 8, 0, 0, TimeSpan.Zero);
        var exception = new AcmeRequestException("rate limited", new AcmeError
        {
            Type = "urn:ietf:params:acme:error:rateLimited",
            Detail = "too many requests, retry after 2026-08-26 10:00:00 UTC"
        });

        var retryAt = AcmeOrderProcessor.RetryAt(exception, 1, null, now);

        Assert.Equal(new DateTimeOffset(2026, 8, 26, 10, 1, 0, TimeSpan.Zero), retryAt);
    }

    [Fact]
    public void Rate_limit_without_a_server_timestamp_waits_twenty_four_hours()
    {
        var now = new DateTimeOffset(2026, 8, 26, 8, 0, 0, TimeSpan.Zero);
        var exception = new AcmeRequestException("rate limited", new AcmeError
        {
            Type = "urn:ietf:params:acme:error:rateLimited", Detail = "service busy; retry later"
        });

        Assert.Equal(now.AddHours(24), AcmeOrderProcessor.RetryAt(exception, 1, null, now));
    }

    [Fact]
    public void Dns_json_response_finds_the_exact_txt_value()
    {
        const string response =
            """{"Status":0,"Answer":[{"name":"_acme-challenge.example.com","type":16,"data":"\"expected-value\""},{"name":"_acme-challenge.example.com","type":16,"data":"\"unrelated-value\""}]}""";

        Assert.True(AcmeOrderProcessor.DnsResponseContainsTxt(response, "expected-value"));
        Assert.False(AcmeOrderProcessor.DnsResponseContainsTxt(response, "missing-value"));
    }

    [Fact]
    public async Task Dns_wait_stops_when_the_managed_certificate_is_deleted()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var certificate = new ManagedCertificate
        {
            Name = "deleted issuance", CreatedBy = "test",
            AcmeAccount = Account("Production", "https://acme.example/production", true, false)
        };
        db.ManagedCertificates.Add(certificate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ManagedCertificates.Remove(certificate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ManagedCertificateDeletedException>(() =>
            AcmeOrderProcessor.EnsureManagedCertificateExists(db, certificate.Id,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Finalization_polls_processing_orders_until_the_certificate_is_valid()
    {
        var order = new StubOrderContext(
            new Order
            {
                Status = OrderStatus.Ready,
                Identifiers = [new Identifier { Type = IdentifierType.Dns, Value = "example.com" }]
            },
            new Order { Status = OrderStatus.Processing },
            new Order { Status = OrderStatus.Processing },
            new Order { Status = OrderStatus.Valid });

        var certificate = await AcmeOrderProcessor.FinalizeOrderAsync(order,
            new CsrInfo { CommonName = "example.com" }, KeyFactory.NewKey(KeyAlgorithm.ES256),
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1), TestContext.Current.CancellationToken);

        Assert.Null(certificate);
        Assert.Equal(3, order.ResourceCalls);
        Assert.Equal(1, order.DownloadCalls);
    }

    [Fact]
    public async Task Finalization_surfaces_the_certificate_authority_error_detail()
    {
        var order = new StubOrderContext(
            new Order
            {
                Status = OrderStatus.Ready,
                Identifiers = [new Identifier { Type = IdentifierType.Dns, Value = "example.com" }]
            },
            new Order
            {
                Status = OrderStatus.Invalid,
                Error = JsonDocument.Parse(
                        """{"type":"urn:ietf:params:acme:error:badCSR","detail":"CSR was rejected."}""")
                    .RootElement.Clone()
            });

        var exception = await Assert.ThrowsAsync<AcmeFinalizationException>(() =>
            AcmeOrderProcessor.FinalizeOrderAsync(order, new CsrInfo { CommonName = "example.com" },
                KeyFactory.NewKey(KeyAlgorithm.ES256), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1),
                TestContext.Current.CancellationToken));

        Assert.Contains("CSR was rejected.", exception.Message);
    }

    [Fact]
    public void Pkcs12_packaging_uses_the_returned_chain_without_issuer_discovery()
    {
        var key = KeyFactory.NewKey(KeyAlgorithm.ES256);
        using var certificateKey = ECDsa.Create();
        certificateKey.ImportFromPem(key.ToPem());
        var leafRequest = new CertificateRequest("CN=example.com", certificateKey, HashAlgorithmName.SHA256);
        using var leaf = leafRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30));
        using var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuerRequest = new CertificateRequest("CN=Unrecognized staging issuer", issuerKey,
            HashAlgorithmName.SHA256);
        using var issuer = issuerRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));
        var chain = new CertificateChain(leaf.ExportCertificatePem() + issuer.ExportCertificatePem());
        const string password = "test-password";

        var pfx = AcmeOrderProcessor.BuildPkcs12(chain, key, password);

        var imported = X509CertificateLoader.LoadPkcs12Collection(pfx, password,
            X509KeyStorageFlags.EphemeralKeySet);
        try
        {
            Assert.Equal(2, imported.Count);
            Assert.Contains(imported.Cast<X509Certificate2>(), certificate => certificate.HasPrivateKey);
            Assert.Contains(imported.Cast<X509Certificate2>(), certificate =>
                certificate.Subject == "CN=Unrecognized staging issuer");
        }
        finally
        {
            foreach (var certificate in imported) certificate.Dispose();
        }
    }

    [Fact]
    public async Task Loopia_cleanup_recovers_the_record_id_when_presentation_did_not_persist_it()
    {
        var requests = new List<string>();
        var responses = new Queue<string>(
        [
            """<methodResponse><params><param><value><array><data><value><struct><member><name>record_id</name><value><int>42</int></value></member><member><name>type</name><value><string>TXT</string></value></member><member><name>rdata</name><value><string>challenge-value</string></value></member></struct></value></data></array></value></param></params></methodResponse>""",
            """<methodResponse><params><param><value><string>OK</string></value></param></params></methodResponse>"""
        ]);
        var provider = new LoopiaDnsProvider(new StubHttpClientFactory(new StubHandler(async request =>
        {
            requests.Add(await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "text/xml")
            };
        })));

        await provider.CleanupAsync(new DnsProviderCredentials(Username: "user", Password: "password"),
            new DnsManagedZone("example.com", "example.com"), "_acme-challenge.example.com", "challenge-value",
            null, TestContext.Current.CancellationToken);

        Assert.Equal(2, requests.Count);
        Assert.Contains("getZoneRecords", requests[0]);
        Assert.Contains("removeZoneRecord", requests[1]);
        Assert.Contains("<int>42</int>", requests[1]);
    }

    [Fact]
    public async Task Loopia_presentation_requires_an_ok_status_and_verifies_the_created_record()
    {
        var requests = new List<string>();
        var responses = new Queue<string>(
        [
            """<methodResponse><params><param><value><array><data /></array></value></param></params></methodResponse>""",
            """<methodResponse><params><param><value><string>OK</string></value></param></params></methodResponse>""",
            """<methodResponse><params><param><value><string>OK</string></value></param></params></methodResponse>""",
            """<methodResponse><params><param><value><array><data><value><struct><member><name>record_id</name><value><int>42</int></value></member><member><name>type</name><value><string>TXT</string></value></member><member><name>rdata</name><value><string>challenge-value</string></value></member></struct></value></data></array></value></param></params></methodResponse>"""
        ]);
        var provider = new LoopiaDnsProvider(new StubHttpClientFactory(new StubHandler(async request =>
        {
            requests.Add(await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "text/xml")
            };
        })));

        var recordId = await provider.PresentAsync(
            new DnsProviderCredentials(Username: "user", Password: "password"),
            new DnsManagedZone("example.com", "example.com"), "_acme-challenge.example.com", "challenge-value",
            TestContext.Current.CancellationToken);

        Assert.Equal("42", recordId);
        Assert.Equal(4, requests.Count);
        Assert.Contains("getSubdomains", requests[0]);
        Assert.Contains("addSubdomain", requests[1]);
        Assert.Contains("addZoneRecord", requests[2]);
        Assert.Contains("<name>ttl</name><value><int>300</int></value>", requests[2]);
        Assert.Contains("getZoneRecords", requests[3]);
    }

    [Fact]
    public async Task Loopia_presentation_rejects_an_application_error_response()
    {
        var responses = new Queue<string>(
        [
            """<methodResponse><params><param><value><array><data><value><string>_acme-challenge</string></value></data></array></value></param></params></methodResponse>""",
            """<methodResponse><params><param><value><string>AUTH_ERROR</string></value></param></params></methodResponse>"""
        ]);
        var provider = new LoopiaDnsProvider(new StubHttpClientFactory(new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "text/xml")
            }))));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.PresentAsync(
            new DnsProviderCredentials(Username: "user", Password: "password"),
            new DnsManagedZone("example.com", "example.com"), "_acme-challenge.example.com", "challenge-value",
            TestContext.Current.CancellationToken));

        Assert.Contains("AUTH_ERROR", exception.Message);
    }

    [Fact]
    public async Task Simply_lists_active_products_with_dns_domains()
    {
        HttpRequestMessage? captured = null;
        var provider = new SimplyDnsProvider(new StubHttpClientFactory(new StubHandler(request =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                                            {"products":[
                                              {"object":"example.com","cancelled":false,"domain":{"name":"example.com"}},
                                              {"object":"cancelled.example","cancelled":true,"domain":{"name":"cancelled.example"}},
                                              {"object":"hosting-without-domain","cancelled":false}
                                            ]}
                                            """, Encoding.UTF8, "application/json")
            });
        })));

        var zones = await provider.ListZonesAsync(new DnsProviderCredentials("api-key"),
            TestContext.Current.CancellationToken);

        var zone = Assert.Single(zones);
        Assert.Equal("example.com", zone.Name);
        Assert.Equal("example.com", zone.Id);
        Assert.Equal("Bearer", captured!.Headers.Authorization?.Scheme);
        Assert.Equal("api-key", captured.Headers.Authorization?.Parameter);
        Assert.Equal("https://api.simply.com/2/my/products/", captured.RequestUri?.ToString());
    }

    [Fact]
    public async Task Simply_presents_a_relative_txt_record_and_returns_its_id()
    {
        string? body = null;
        Uri? uri = null;
        var provider = new SimplyDnsProvider(new StubHttpClientFactory(new StubHandler(async request =>
        {
            uri = request.RequestUri;
            body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"record":{"id":123}}""", Encoding.UTF8, "application/json")
            };
        })));

        var recordId = await provider.PresentAsync(new DnsProviderCredentials("api-key"),
            new DnsManagedZone("example.com", "product/example.com"), "_acme-challenge.example.com",
            "challenge-value", TestContext.Current.CancellationToken);

        Assert.Equal("123", recordId);
        Assert.Equal("https://api.simply.com/2/my/products/product%2Fexample.com/dns/records/", uri?.ToString());
        using var payload = JsonDocument.Parse(body!);
        Assert.Equal("TXT", payload.RootElement.GetProperty("type").GetString());
        Assert.Equal("_acme-challenge", payload.RootElement.GetProperty("name").GetString());
        Assert.Equal("challenge-value", payload.RootElement.GetProperty("data").GetString());
        Assert.Equal(300, payload.RootElement.GetProperty("ttl").GetInt32());
    }

    [Fact]
    public async Task Simply_cleanup_recovers_and_deletes_only_the_matching_txt_record()
    {
        var requests = new List<(HttpMethod Method, string Uri)>();
        var provider = new SimplyDnsProvider(new StubHttpClientFactory(new StubHandler(request =>
        {
            requests.Add((request.Method, request.RequestUri!.ToString()));
            var content = request.Method == HttpMethod.Get
                ? """{"records":[{"record_id":41,"name":"_acme-challenge","type":"TXT","data":"other"},{"record_id":42,"name":"_acme-challenge","type":"TXT","data":"challenge-value"},{"record_id":43,"name":"_acme-challenge","type":"A","data":"challenge-value"}]}"""
                : """{"status":200,"message":"success"}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        })));

        await provider.CleanupAsync(new DnsProviderCredentials("api-key"),
            new DnsManagedZone("example.com", "example.com"), "_acme-challenge.example.com", "challenge-value",
            null, TestContext.Current.CancellationToken);

        Assert.Equal(2, requests.Count);
        Assert.Equal(HttpMethod.Get, requests[0].Method);
        Assert.Equal(HttpMethod.Delete, requests[1].Method);
        Assert.EndsWith("/dns/records/42/", requests[1].Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Acme_worker_lease_allows_only_one_owner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var firstDb = new GatewayDbContext(options);
        await firstDb.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await using var secondDb = new GatewayDbContext(options);
        var first = new AcmeWorkerLeaseService(firstDb);
        var second = new AcmeWorkerLeaseService(secondDb);

        Assert.True(await first.TryAcquireAsync("first", TimeSpan.FromMinutes(2),
            TestContext.Current.CancellationToken));
        Assert.False(await second.TryAcquireAsync("second", TimeSpan.FromMinutes(2),
            TestContext.Current.CancellationToken));

        await first.ReleaseAsync("first", TestContext.Current.CancellationToken);

        Assert.True(await second.TryAcquireAsync("second", TimeSpan.FromMinutes(2),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Interrupted_attempt_lookup_supports_sqlite_datetime_offsets()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var account = Account("Production", "https://acme.example/production", true, false);
        var cutoff = new DateTimeOffset(2026, 8, 26, 8, 0, 0, TimeSpan.Zero);
        var interrupted = new ManagedCertificate
        {
            Name = "interrupted", CreatedBy = "test", AcmeAccount = account,
            State = ManagedCertificateState.Issuing, LastAttemptAtUtc = cutoff.AddMinutes(-1)
        };
        var active = new ManagedCertificate
        {
            Name = "active", CreatedBy = "test", AcmeAccount = account,
            State = ManagedCertificateState.Renewing, LastAttemptAtUtc = cutoff.AddMinutes(1)
        };
        var pending = new ManagedCertificate
        {
            Name = "pending", CreatedBy = "test", AcmeAccount = account,
            State = ManagedCertificateState.Pending, LastAttemptAtUtc = cutoff.AddHours(-1)
        };
        db.ManagedCertificates.AddRange(interrupted, active, pending);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var values = await AcmeOrderProcessor.FindInterruptedAttempts(db, cutoff,
            TestContext.Current.CancellationToken);

        Assert.Single(values);
        Assert.Equal(interrupted.Id, values[0].Id);
    }

    [Fact]
    public async Task Managed_certificate_keeps_the_selected_acme_account()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var production = Account("Production", "https://acme.example/production", true, false);
        var staging = Account("Staging", "https://acme.example/staging", false, true);
        db.AcmeAccounts.AddRange(production, staging);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var managed = await new ManagedCertificateService(db).IssueAsync("staging certificate", ["api.example.com"],
            AcmeChallengeKind.Http01, null, staging.Id, "test", TestContext.Current.CancellationToken);

        Assert.Equal(staging.Id, managed.AcmeAccountId);
    }

    [Fact]
    public async Task Manual_dns_accepts_wildcards_without_a_provider_profile()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var account = Account("Production", "https://acme.example/production", true, false);
        db.AcmeAccounts.Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var managed = await new ManagedCertificateService(db).IssueAsync("manual wildcard", ["*.example.com"],
            AcmeChallengeKind.ManualDns01, null, account.Id, "test", TestContext.Current.CancellationToken);

        Assert.Equal(AcmeChallengeKind.ManualDns01, managed.ChallengeKind);
        Assert.Null(managed.DnsProviderProfileId);
    }

    [Fact]
    public async Task Active_dns_challenge_query_supports_sqlite_datetime_offsets()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var account = Account("Production", "https://acme.example/production", true, false);
        var managed = new ManagedCertificate { Name = "target", CreatedBy = "test", AcmeAccount = account };
        var order = new AcmeOrder { ManagedCertificate = managed };
        db.AcmeChallenges.AddRange(
            new AcmeChallenge
            {
                AcmeOrder = order, Kind = AcmeChallengeKind.Dns01, Host = "example.com",
                DnsRecordName = "_acme-challenge.example.com", DnsRecordValue = "active",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
            },
            new AcmeChallenge
            {
                AcmeOrder = order, Kind = AcmeChallengeKind.ManualDns01, Host = "expired.example.com",
                DnsRecordName = "_acme-challenge.expired.example.com", DnsRecordValue = "expired",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
            },
            new AcmeChallenge
            {
                AcmeOrder = order, Kind = AcmeChallengeKind.Http01, Host = "http.example.com",
                Token = "token", KeyAuthorization = "key", ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
            });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var values = await new Query().GetManagedCertificateDnsChallenges(managed.Id, db,
            TestContext.Current.CancellationToken);

        var value = Assert.Single(values);
        Assert.Equal("active", value.RecordValue);
    }

    [Fact]
    public async Task Http_challenges_are_host_and_token_scoped()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var account = new AcmeAccount
        {
            Name = "Let's Encrypt Production", DirectoryUrl = "https://acme.example/directory",
            ContactEmail = "admin@example.com", ProtectedAccountKey = [1], IsDefault = true
        };
        var managed = new ManagedCertificate { Name = "test", CreatedBy = "test", AcmeAccount = account };
        var order = new AcmeOrder { ManagedCertificate = managed };
        db.AcmeChallenges.Add(new AcmeChallenge
        {
            AcmeOrder = order, Kind = AcmeChallengeKind.Http01, Host = "api.example.com", Token = "token",
            KeyAuthorization = "key-authorization"
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var store = new AcmeHttpChallengeStore();

        await store.RefreshAsync(db, TestContext.Current.CancellationToken);

        Assert.True(store.TryGet("api.example.com", "token", out var value));
        Assert.Equal("key-authorization", value);
        Assert.False(store.TryGet("other.example.com", "token", out _));
        Assert.False(store.TryGet("api.example.com", "other", out _));
    }

    private static AcmeAccount Account(string name, string directoryUrl, bool isDefault, bool isStaging)
    {
        return new AcmeAccount
        {
            Name = name, DirectoryUrl = directoryUrl, IsDefault = isDefault, IsStaging = isStaging,
            ContactEmail = "admin@example.com", ProtectedAccountKey = [1]
        };
    }

    [Fact]
    public void Ari_certificate_identifier_uses_authority_key_identifier_and_serial()
    {
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest("CN=Root", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        using var root =
            rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafRequest = new CertificateRequest("CN=api.example.com", leafKey, HashAlgorithmName.SHA256);
        leafRequest.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(root, true, false));
        var serial = new byte[] { 1, 2, 3, 4 };
        using var leaf = leafRequest.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90),
            serial);

        var identifier = AcmeProtocol.CertificateId(leaf);

        Assert.Contains('.', identifier);
        Assert.DoesNotContain('+', identifier);
        Assert.DoesNotContain('/', identifier);
        Assert.DoesNotContain('=', identifier);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, false);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return send(request);
        }
    }

    private sealed class StubOrderContext(params Order[] resources) : IOrderContext
    {
        private readonly Queue<Order> values = new(resources);
        public int ResourceCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public Uri Location { get; } = new("https://acme.example/order/1");
        public int RetryAfter => 0;

        public Task<Order> Resource()
        {
            ResourceCalls++;
            return Task.FromResult(values.Dequeue());
        }

        public Task<IEnumerable<IAuthorizationContext>> Authorizations()
        {
            return Task.FromResult<IEnumerable<IAuthorizationContext>>([]);
        }

        public Task<Order> Finalize(byte[] csr)
        {
            return Task.FromResult(values.Dequeue());
        }

        public Task<CertificateChain> Download(string preferredChain = null!)
        {
            DownloadCalls++;
            return Task.FromResult<CertificateChain>(null!);
        }
    }
}
