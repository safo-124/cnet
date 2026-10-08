namespace MepCatalog.Data;

/// <summary>
/// The summary of one model audit. Runs are grouped by the IFC project's GlobalId, which stays the same when a
/// model is fixed, re-exported or renamed, so the history shows the same model improving over time.
/// </summary>
public class AuditRun
{
    public int Id { get; set; }

    /// <summary>GlobalId of the model's IfcProject.</summary>
    public required string ProjectGlobalId { get; set; }

    public string? ProjectName { get; set; }

    public required string FileName { get; set; }

    public DateTime AuditedUtc { get; set; }

    public int Total { get; set; }

    public int Ok { get; set; }

    public int NeedsUpdate { get; set; }

    public int Unidentified { get; set; }

    public int NotInCatalog { get; set; }

    public int CategoryMismatch { get; set; }
}
