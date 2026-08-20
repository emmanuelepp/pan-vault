using System.Text.Json;

namespace PanVault.Api.Validation;

public static class SadGuardMiddleware
{
    public static IApplicationBuilder UseSadGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!context.Request.HasJsonContentType())
            {
                await next();
                return;
            }

            context.Request.EnableBuffering();

            try
            {
                using var document = await JsonDocument.ParseAsync(context.Request.Body);

                if (SadGuard.ContainsSad(document.RootElement, out var field))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(
                        new { error = "sensitive_authentication_data", field });
                    return;
                }
            }
            catch (JsonException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "invalid_json" });
                return;
            }

            context.Request.Body.Position = 0;
            await next();
        });
}
