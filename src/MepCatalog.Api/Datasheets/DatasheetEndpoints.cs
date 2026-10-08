using Anthropic.Exceptions;
using MepCatalog.Ai;
using MepCatalog.Api.Products;
using MepCatalog.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MepCatalog.Api.Datasheets;

/// <param name="Raw">Values as printed in the datasheet, so the reviewer can compare.</param>
/// <param name="Product">Normalized values ready to save, or null if something could not be read.</param>
/// <param name="ExistingProductId">Set when the catalog already has this manufacturer + model.</param>
/// <param name="Draft">Every value that could be read; the starting point when the reviewer fixes a product by hand.</param>
public record ExtractedProductDto(
    RawValuesDto Raw,
    ProductInput? Product,
    IReadOnlyList<string> Issues,
    int? ExistingProductId,
    ProductInput Draft);

public record RawValuesDto(
    string? Manufacturer, string? Model, string? Category, string? Description,
    string? Airflow, string? Power, string? ConnectionSize, string? Weight);

public record DatasheetResponse(string FileName, string? Notes, IReadOnlyList<ExtractedProductDto> Products);

public record DatasheetStatus(bool Enabled, string? Model);

public static class DatasheetEndpoints
{
    private const long MaxPdfBytes = 20 * 1024 * 1024;

    public static void MapDatasheetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/datasheets").WithTags("Datasheets").DisableAntiforgery();

        group.MapGet("/status", (IDatasheetExtractor extractor) =>
            new DatasheetStatus(extractor.IsConfigured, extractor.IsConfigured ? ClaudeDatasheetExtractor.Model : null))
            .WithSummary("Whether AI datasheet extraction is configured");

        group.MapPost("/extract", Extract)
            .WithSummary("Read a manufacturer PDF datasheet with AI and return the products for review (nothing is saved)")
            .WithMetadata(new RequestSizeLimitAttribute(MaxPdfBytes));
    }

    private static async Task<Results<Ok<DatasheetResponse>, BadRequest<string>, ProblemHttpResult>> Extract(
        IFormFile file, IDatasheetExtractor extractor, DbProductCatalog catalog, ILogger<DatasheetResponse> logger, CancellationToken ct)
    {
        if (!extractor.IsConfigured)
            return TypedResults.Problem("AI extraction is not configured on the server.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var pdf = new byte[file.Length];
        await using (var stream = file.OpenReadStream())
            await stream.ReadExactlyAsync(pdf, ct);
        if (pdf.Length < 5 || !"%PDF-"u8.SequenceEqual(pdf.AsSpan(0, 5)))
            return TypedResults.BadRequest("Please upload a PDF file.");

        DatasheetExtraction extraction;
        try
        {
            extraction = await extractor.ExtractAsync(pdf, ct);
        }
        catch (DatasheetExtractionException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
        catch (AnthropicRateLimitException)
        {
            return TypedResults.Problem("The AI service is busy. Please try again in a minute.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (AnthropicApiException ex)
        {
            logger.LogError(ex, "Datasheet extraction failed");
            return TypedResults.Problem("The AI service returned an error. Please try again later.", statusCode: StatusCodes.Status502BadGateway);
        }

        var products = new List<ExtractedProductDto>();
        foreach (var p in extraction.Products)
        {
            var existing = p.Draft.Manufacturer.Length > 0 && p.Draft.Model.Length > 0
                ? await catalog.FindAsync(p.Draft.Manufacturer, p.Draft.Model, ct)
                : null;
            var raw = new RawValuesDto(p.Raw.Manufacturer, p.Raw.Model, p.Raw.Category, p.Raw.Description,
                p.Raw.Airflow, p.Raw.Power, p.Raw.ConnectionSize, p.Raw.Weight);
            products.Add(new ExtractedProductDto(
                raw, p.Product is null ? null : ToInput(p.Product), p.Issues, existing?.Id, ToInput(p.Draft)));
        }

        return TypedResults.Ok(new DatasheetResponse(file.FileName, extraction.Notes, products));
    }

    private static ProductInput ToInput(Core.Product p) => new(
        p.Manufacturer, p.Model, p.Category, p.Description, p.AirflowLps, p.PowerW, p.ConnectionSizeMm, p.WeightKg);
}
