using MepCatalog.Core;

namespace MepCatalog.Ai;

/// <summary>One product found in a datasheet.</summary>
/// <param name="Raw">Values exactly as printed in the datasheet, e.g. "180 m3/h".</param>
/// <param name="Product">The normalized product, or null if a value could not be understood.</param>
/// <param name="Issues">Problems found while normalizing; a person should check these before saving.</param>
/// <param name="Draft">Every value that could be read, even when others could not. The starting point for a manual fix.</param>
public record ExtractedProduct(RawProductRow Raw, Product? Product, IReadOnlyList<string> Issues, Product Draft);

/// <param name="Notes">Anything the model wants the reviewer to know, e.g. "Airflow given as a range; used the nominal value".</param>
public record DatasheetExtraction(IReadOnlyList<ExtractedProduct> Products, string? Notes);

public interface IDatasheetExtractor
{
    /// <summary>False when no API key is configured; the feature is then switched off instead of failing.</summary>
    bool IsConfigured { get; }

    Task<DatasheetExtraction> ExtractAsync(byte[] pdf, CancellationToken ct = default);
}

/// <summary>A problem the user should see, such as an unreadable PDF or a declined request.</summary>
public class DatasheetExtractionException(string message) : Exception(message);

/// <summary>Used when no API key is configured.</summary>
public sealed class DisabledDatasheetExtractor : IDatasheetExtractor
{
    public bool IsConfigured => false;

    public Task<DatasheetExtraction> ExtractAsync(byte[] pdf, CancellationToken ct = default) =>
        throw new DatasheetExtractionException("AI extraction is not configured. Set the Anthropic API key to enable it.");
}
