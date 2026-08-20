using PanVault.Api.Crypto;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services
    .AddOptions<PanCryptoOptions>()
    .Bind(builder.Configuration.GetSection(PanCryptoOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => Convert.TryFromBase64String(o.Dek, new byte[32], out var n) && n == 32,
          "PanCrypto:Dek must be a base64-encoded 32-byte key.")
    .ValidateOnStart();

builder.Services.AddSingleton<PanCrypto>();

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