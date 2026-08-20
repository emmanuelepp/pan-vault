var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("PanVault:EnableApiDocs"))
{
    app.MapOpenApi();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "PanVault v1");
        o.RoutePrefix = "swagger";
    });
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();