using System.Text.Json.Serialization;
using Anthropic;
using MepCatalog.Ai;
using MepCatalog.Api;
using MepCatalog.Api.Audits;
using MepCatalog.Api.Datasheets;
using MepCatalog.Api.Products;
using MepCatalog.Data;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Demo mode is for the public deployment: sample data is loaded and paid AI features are switched off.
var demoMode = builder.Configuration.GetValue<bool>("Demo:Enabled");
var samplesPath = Path.GetFullPath(builder.Configuration["Samples:Path"] ?? "../../data", builder.Environment.ContentRootPath);

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Catalog")));
builder.Services.AddScoped<ProductImportService>();
builder.Services.AddScoped<DbProductCatalog>();

// AI datasheet extraction is optional: without an API key the feature is switched off, not broken.
// Locally the key comes from user-secrets (Anthropic:ApiKey) or the ANTHROPIC_API_KEY environment variable.
// On the public demo it is always off, so strangers can't spend the key owner's credits.
var anthropicKey = builder.Configuration["Anthropic:ApiKey"] ?? builder.Configuration["ANTHROPIC_API_KEY"];
builder.Services.AddSingleton<IDatasheetExtractor>(
    demoMode ? new DisabledDatasheetExtractor(
        "AI datasheet reading is switched off in this public demo to avoid API costs. Run the project locally with your own Anthropic API key to try it.")
    : string.IsNullOrWhiteSpace(anthropicKey) ? new DisabledDatasheetExtractor(
        "AI datasheet reading is not configured. Add an Anthropic API key with: dotnet user-secrets set \"Anthropic:ApiKey\" \"<key>\" --project src/MepCatalog.Api, then restart the API.")
    : new ClaudeDatasheetExtractor(new AnthropicClient { ApiKey = anthropicKey }));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddUploadRateLimit(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
          .AllowAnyHeader()
          .AllowAnyMethod()));

// Behind the Caddy reverse proxy: trust its X-Forwarded-* headers so client IPs (rate limiting) and https are right.
// The app port is only reachable from Caddy inside the Docker network, so trusting any proxy address is safe here.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    if (db.Database.IsRelational())
        db.Database.Migrate();
    if (demoMode)
        await DemoSeeder.SeedIfEmptyAsync(scope.ServiceProvider, Path.Combine(samplesPath, "sample-products.csv"), app.Logger);
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();

// The built React app (wwwroot) is served from the same address as the API, so there is one thing to deploy.
app.UseDefaultFiles();
app.UseStaticFiles();
// Sample files visitors can download to try the app: the IFC model, the CSV and the datasheet PDF.
if (Directory.Exists(samplesPath))
{
    // .ifc is not a known web file type; without this mapping the file would be refused with 404.
    var contentTypes = new FileExtensionContentTypeProvider { Mappings = { [".ifc"] = "application/x-step" } };
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(samplesPath),
        RequestPath = "/samples",
        ContentTypeProvider = contentTypes,
    });
}

app.MapOpenApi();
app.MapScalarApiReference();
app.MapGet("/healthz", () => Results.Ok("healthy")).ExcludeFromDescription();

app.MapProductEndpoints();
app.MapAuditEndpoints();
app.MapDatasheetEndpoints();

// Unknown /api routes stay 404s; every other path is a client-side route in the React app.
app.Map("/api/{**rest}", () => Results.NotFound()).ExcludeFromDescription();
if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "index.html")))
    app.MapFallbackToFile("index.html");
else
    app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();

app.Run();

public partial class Program;
