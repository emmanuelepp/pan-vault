using PanVault.Api.Crypto;
using PanVault.Api.Tokens;
using PanVault.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

var enableApiDocs = builder.Configuration.GetValue<bool>("PanVault:EnableApiDocs");

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

if (enableApiDocs) builder.Services.AddOpenApi();

builder.Services
    .AddOptions<PanCryptoOptions>()
    .Bind(builder.Configuration.GetSection(PanCryptoOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => Convert.TryFromBase64String(o.Dek, new byte[32], out var n) && n == 32,
          "PanCrypto:Dek must be a base64-encoded 32-byte key.")
    .ValidateOnStart();

builder.Services.AddSingleton<PanCrypto>();
builder.Services.AddSingleton<TokenStore>();

var app = builder.Build();

app.UseSadGuard();

if (enableApiDocs)
{
    app.MapOpenApi();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "PanVault v1");
        o.RoutePrefix = "swagger";
    });
}

app.MapPost("/tokens", (TokenizeRequest request, PanCrypto crypto, TokenStore store, ILogger<Program> logger) =>
{
    if (!LuhnValidator.IsValid(request.Pan))
        return Results.BadRequest(new { error = "invalid_pan" });

    var brand = PanMasker.Brand(request.Pan);
    var last4 = PanMasker.Last4(request.Pan);
    var token = store.Add(crypto.Encrypt(request.Pan), PanMasker.Mask(request.Pan), brand);

    logger.LogInformation("Token issued {Token} {Brand} {Last4}", token, brand, last4);

    return Results.Created($"/tokens/{token}", new TokenizeResponse(token, last4, brand));
});

app.MapGet("/tokens/{token}", (string token, TokenStore store) =>
{
    var entry = store.Get(token);

    return entry is null
        ? Results.NotFound()
        : Results.Ok(new TokenDetails(token, entry.MaskedPan, entry.Brand, entry.CreatedAt));
});

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();
