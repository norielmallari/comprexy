namespace Comprexy.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var environment = app.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (environment.IsDevelopment())
        {
            app.MapGet("/", () => Results.Ok(new { status = "ok", service = "comprexy" }));
        }

        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        return app;
    }
}
