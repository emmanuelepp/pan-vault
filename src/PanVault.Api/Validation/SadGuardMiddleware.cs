using System.Diagnostics.Metrics;
using System.Text.Json;

namespace PanVault.Api.Validation;

public static class SadGuardMiddleware
{
    public const string MeterName = "PanVault.Api";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> SadRejections = Meter.CreateCounter<long>(
        "panvault.sad.rejections",
        description: "Requests rejected because the payload carried sensitive authentication data.");

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
                    SadRejections.Add(1);
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
