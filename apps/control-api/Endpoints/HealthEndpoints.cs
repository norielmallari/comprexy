namespace Comprexy.ControlApi.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var environment = app.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (environment.IsDevelopment())
        {
            app.MapGet("/", () => Results.Ok(new { status = "ok", service = "comprexy-control-api" }));
        }

        app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "comprexy-control-api" }));
        return app;
    }
}
