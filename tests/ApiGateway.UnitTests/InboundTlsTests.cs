using ApiGateway.Domain;
using ApiGateway.Persistence;
using Xunit;

namespace ApiGateway.UnitTests;

public sealed class InboundTlsTests
{
    [Theory]
    [InlineData("sjögrässtigen.se")]
    [InlineData("xn--sjgrsstigen-o8a5u.se")]
    [InlineData("SJÖGRÄSSTIGEN.SE.")]
    public void Certificate_selector_normalizes_exact_idn_sni_hosts(string host)
    {
        var certificate = new object();
        var certificates = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["xn--sjgrsstigen-o8a5u.se"] = certificate
        };

        Assert.Same(certificate, InboundCertificateRegistry.Select(certificates, host));
    }

    [Theory]
    [InlineData("www.sjögrässtigen.se")]
    [InlineData("www.xn--sjgrsstigen-o8a5u.se")]
    public void Certificate_selector_normalizes_www_idn_sni_hosts(string host)
    {
        var certificate = new object();
        var certificates = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["www.xn--sjgrsstigen-o8a5u.se"] = certificate
        };

        Assert.Same(certificate, InboundCertificateRegistry.Select(certificates, host));
    }

    [Theory]
    [InlineData("api.example.com", "api.example.com", true)]
    [InlineData("*.example.com", "api.example.com", true)]
    [InlineData("*.example.com", "deep.api.example.com", false)]
    [InlineData("*.example.com", "example.com", false)]
    [InlineData("xn--sjgrsstigen-o8a5u.se", "sjögrässtigen.se", true)]
    [InlineData("sjögrässtigen.se", "xn--sjgrsstigen-o8a5u.se", true)]
    [InlineData("*.xn--sjgrsstigen-o8a5u.se", "api.sjögrässtigen.se", true)]
    public void Certificate_names_cover_only_expected_hosts(string certificateName, string host, bool expected)
    {
        Assert.Equal(expected, InboundCertificateService.Covers(certificateName, host));
    }

    [Fact]
    public void Existing_routes_default_to_any_scheme_and_websockets()
    {
        var route = new GatewayRoute
        {
            Id = "test", Match = new RouteMatch { Path = "/{**path}" }, ClusterId = "cluster"
        };
        Assert.Equal(InboundScheme.Any, route.Inbound.Scheme);
        Assert.True(route.Inbound.WebSocketsAllowed);
        Assert.Null(route.Inbound.CertificateId);
    }
}