using ApiGateway.Management;
using Xunit;

namespace ApiGateway.UnitTests;

public sealed class DocumentationContentSecurityPolicyTests
{
    [Fact]
    public void DocumentationResponsesAllowVitePressBootstrapScripts()
    {
        var policy = DocumentationContentSecurityPolicy.Resolve("/docs/guide/getting-started.html");

        Assert.NotNull(policy);
        Assert.Contains("script-src 'self' 'unsafe-inline'", policy);
    }

    [Fact]
    public void NonDocumentationResponsesDoNotReceiveDocumentationPolicy()
    {
        Assert.Null(DocumentationContentSecurityPolicy.Resolve("/admin/"));
    }
}