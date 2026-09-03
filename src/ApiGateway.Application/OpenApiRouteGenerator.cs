using System.Text.Json;
using System.Text.RegularExpressions;
using ApiGateway.Domain;
using YamlDotNet.Serialization;

namespace ApiGateway.Application;

public sealed record OpenApiGenerationResult(IReadOnlyList<GatewayRoute> Routes, IReadOnlyList<ValidationIssue> Issues);

public static partial class OpenApiRouteGenerator
{
    private static readonly HashSet<string> Methods = new(StringComparer.OrdinalIgnoreCase)
        { "get", "put", "post", "delete", "options", "head", "patch", "trace" };

    public static OpenApiGenerationResult Generate(string source, string clusterId, string? prefix = null)
    {
        using var document = Parse(source);
        var issues = new List<ValidationIssue>();
        var routes = new List<GatewayRoute>();
        if (!document.RootElement.TryGetProperty("openapi", out _))
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "OPENAPI_VERSION", "/openapi",
                "An OpenAPI 3.x document is required."));
        if (!document.RootElement.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "OPENAPI_PATHS", "/paths",
                "The document must contain paths."));
            return new OpenApiGenerationResult(routes, issues);
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.EnumerateObject())
        foreach (var operation in path.Value.EnumerateObject().Where(x => Methods.Contains(x.Name)))
        {
            var operationId = operation.Value.TryGetProperty("operationId", out var idValue)
                ? idValue.GetString()
                : null;
            var id = NormalizeId($"{prefix}{operationId ?? $"{operation.Name}-{path.Name}"}");
            if (!ids.Add(id))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "OPENAPI_DUPLICATE_ID",
                    $"/paths/{path.Name}/{operation.Name}", $"Generated route ID '{id}' is duplicated."));
                continue;
            }

            routes.Add(new GatewayRoute
            {
                Id = id, ClusterId = clusterId,
                Match = new RouteMatch { Path = ConvertPath(path.Name), Methods = [operation.Name.ToUpperInvariant()] },
                AuthorizationPolicy = "Anonymous",
                Metadata = new GatewayMetadata(Description: operation.Value.TryGetProperty("summary", out var summary)
                    ? summary.GetString()
                    : null)
            });
        }

        return new OpenApiGenerationResult(routes, issues);
    }

    private static JsonDocument Parse(string source)
    {
        try
        {
            return JsonDocument.Parse(source);
        }
        catch (JsonException)
        {
            var yaml = new DeserializerBuilder().Build().Deserialize<object>(source);
            return JsonDocument.Parse(JsonSerializer.Serialize(yaml));
        }
    }

    private static string ConvertPath(string path)
    {
        return ParameterRegex().Replace(path, "{$1}");
    }

    private static string NormalizeId(string value)
    {
        var id = InvalidIdRegex().Replace(value.ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrEmpty(id) ? "openapi-route" : id[..Math.Min(100, id.Length)];
    }

    [GeneratedRegex("\\{([^}:]+)(?::[^}]+)?\\}")]
    private static partial Regex ParameterRegex();

    [GeneratedRegex("[^a-z0-9-]+", RegexOptions.IgnoreCase)]
    private static partial Regex InvalidIdRegex();
}