namespace ApiGateway.Management;

internal static class DocumentationContentSecurityPolicy
{
    internal const string Value =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'self'";

    internal static void Use(IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            var policy = Resolve(context.Request.Path);
            if (policy is not null)
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["Content-Security-Policy"] = policy;
                    return Task.CompletedTask;
                });

            await next(context);
        });
    }

    internal static string? Resolve(PathString requestPath)
    {
        return requestPath.StartsWithSegments("/docs") ? Value : null;
    }
}