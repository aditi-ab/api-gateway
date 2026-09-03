var app = WebApplication.Create(args); app.Map("/{**path}", (HttpContext context) => Results.Json(new { status = "api-gateway-upstream-ok", path = context.Request.Path.Value })); app.Run();
