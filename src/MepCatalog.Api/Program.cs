using System.Text.Json.Serialization;
using Anthropic;
using MepCatalog.Ai;
using MepCatalog.Api.Audits;
using MepCatalog.Api.Datasheets;
using MepCatalog.Api.Products;
using MepCatalog.Data;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Catalog")));
builder.Services.AddScoped<ProductImportService>();
builder.Services.AddScoped<DbProductCatalog>();

// AI datasheet extraction is optional: without an API key the feature is switched off, not broken.
// Locally the key comes from user-secrets (Anthropic:ApiKey) or the ANTHROPIC_API_KEY environment variable.
var anthropicKey = builder.Configuration["Anthropic:ApiKey"] ?? builder.Configuration["ANTHROPIC_API_KEY"];
builder.Services.AddSingleton<IDatasheetExtractor>(string.IsNullOrWhiteSpace(anthropicKey)
    ? new DisabledDatasheetExtractor()
    : new ClaudeDatasheetExtractor(new AnthropicClient { ApiKey = anthropicKey }));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
          .AllowAnyHeader()
          .AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    if (db.Database.IsRelational())
        db.Database.Migrate();
}

app.UseExceptionHandler();
app.UseCors();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();

app.MapProductEndpoints();
app.MapAuditEndpoints();
app.MapDatasheetEndpoints();

app.Run();

public partial class Program;
