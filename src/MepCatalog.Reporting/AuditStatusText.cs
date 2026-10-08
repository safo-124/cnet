using MepCatalog.Core.Auditing;

namespace MepCatalog.Reporting;

/// <summary>Human-readable names and next steps for each audit status, shared by every report format.</summary>
public static class AuditStatusText
{
    public static readonly AuditStatus[] DisplayOrder =
    [
        AuditStatus.Ok,
        AuditStatus.NeedsUpdate,
        AuditStatus.Unidentified,
        AuditStatus.NotInCatalog,
        AuditStatus.CategoryMismatch,
    ];

    public static bool NeedsDesigner(AuditStatus status) =>
        status is AuditStatus.Unidentified or AuditStatus.NotInCatalog or AuditStatus.CategoryMismatch;

    public static string Label(AuditStatus status) => status switch
    {
        AuditStatus.Ok => "OK",
        AuditStatus.NeedsUpdate => "Needs update",
        AuditStatus.Unidentified => "Unidentified",
        AuditStatus.NotInCatalog => "Not in catalog",
        AuditStatus.CategoryMismatch => "Wrong product type",
        _ => status.ToString(),
    };

    public static string Action(AuditStatus status) => status switch
    {
        AuditStatus.Ok => "No action needed.",
        AuditStatus.NeedsUpdate => "Run the auditor with --fix (or use \"Download fixed model\") to fill values from the catalog.",
        AuditStatus.Unidentified => "Choose the product and set Manufacturer and ModelLabel in Pset_ManufacturerTypeInformation.",
        AuditStatus.NotInCatalog => "Check the model code for typos, or ask for the product to be added to the catalog.",
        AuditStatus.CategoryMismatch => "The linked product is a different kind of device. Choose a product that matches the element type.",
        _ => "",
    };
}
