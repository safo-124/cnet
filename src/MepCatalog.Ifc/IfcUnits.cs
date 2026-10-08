using Xbim.Common;
using Xbim.Ifc4.Interfaces;

namespace MepCatalog.Ifc;

/// <summary>
/// Converts IFC measure values between a model's declared units and SI base units.
/// </summary>
/// <remarks>
/// IFC measures carry no unit of their own: an <c>IfcVolumetricFlowRateMeasure</c> of 36 means 36 of whatever the
/// project declared for VOLUMETRICFLOWRATEUNIT, or m³/s when nothing is declared. Exporters differ (m³/s, l/s,
/// m³/h, W, kW), so every standard value is converted through the model's IfcUnitAssignment. Supported unit forms:
/// IfcSIUnit with prefixes, IfcConversionBasedUnit (e.g. litre, hour) and IfcDerivedUnit (e.g. m³ · h⁻¹).
/// </remarks>
public sealed class IfcUnits
{
    private readonly IReadOnlyList<IIfcUnit> _assigned;

    private IfcUnits(IReadOnlyList<IIfcUnit> assigned) => _assigned = assigned;

    public static IfcUnits Of(IModel model) =>
        new(model.Instances.FirstOrDefault<IIfcProject>()?.UnitsInContext?.Units.ToList() ?? []);

    /// <summary>SI value (m³/s) of one unit of the project's volumetric flow rate unit.</summary>
    public double FlowRateFactor => FactorFor(IfcDerivedUnitEnum.VOLUMETRICFLOWRATEUNIT);

    /// <summary>SI value (W) of one unit of the project's power unit.</summary>
    public double PowerFactor => FactorFor(IfcUnitEnum.POWERUNIT);

    private double FactorFor(IfcUnitEnum type) =>
        _assigned.OfType<IIfcNamedUnit>().FirstOrDefault(u => u.UnitType == type) is { } unit ? ToSi(unit) : 1;

    private double FactorFor(IfcDerivedUnitEnum type) =>
        _assigned.FirstOrDefault(u => u switch
        {
            IIfcDerivedUnit d => d.UnitType == type,
            // A conversion-based unit like "litre per second" is declared with UnitType USERDEFINED.
            IIfcConversionBasedUnit c => c.UnitType == IfcUnitEnum.USERDEFINED && IsFlowRate(c),
            _ => false,
        }) is { } unit ? ToSi(unit) : 1;

    private static bool IsFlowRate(IIfcConversionBasedUnit unit) =>
        unit.ConversionFactor.UnitComponent is IIfcDerivedUnit { UnitType: IfcDerivedUnitEnum.VOLUMETRICFLOWRATEUNIT };

    /// <summary>How many SI base units one of <paramref name="unit"/> is, e.g. 0.001 for litre (m³).</summary>
    public static double ToSi(IIfcUnit? unit) => unit switch
    {
        null => 1,
        IIfcSIUnit si => Math.Pow(PrefixFactor(si.Prefix), Dimension(si.Name)),
        IIfcConversionBasedUnit converted =>
            Convert.ToDouble(converted.ConversionFactor.ValueComponent.Value) * ToSi(converted.ConversionFactor.UnitComponent),
        IIfcDerivedUnit derived => derived.Elements.Aggregate(1.0, (factor, e) => factor * Math.Pow(ToSi(e.Unit), e.Exponent)),
        _ => throw new NotSupportedException($"Unsupported IFC unit {unit.GetType().Name}."),
    };

    /// <summary>A prefixed square or cubic unit scales by the prefix squared or cubed: MILLI SQUARE_METRE = mm².</summary>
    private static int Dimension(IfcSIUnitName name) => name switch
    {
        IfcSIUnitName.SQUARE_METRE => 2,
        IfcSIUnitName.CUBIC_METRE => 3,
        _ => 1,
    };

    private static double PrefixFactor(IfcSIPrefix? prefix) => Math.Pow(10, prefix switch
    {
        null => 0,
        IfcSIPrefix.EXA => 18,
        IfcSIPrefix.PETA => 15,
        IfcSIPrefix.TERA => 12,
        IfcSIPrefix.GIGA => 9,
        IfcSIPrefix.MEGA => 6,
        IfcSIPrefix.KILO => 3,
        IfcSIPrefix.HECTO => 2,
        IfcSIPrefix.DECA => 1,
        IfcSIPrefix.DECI => -1,
        IfcSIPrefix.CENTI => -2,
        IfcSIPrefix.MILLI => -3,
        IfcSIPrefix.MICRO => -6,
        IfcSIPrefix.NANO => -9,
        IfcSIPrefix.PICO => -12,
        IfcSIPrefix.FEMTO => -15,
        IfcSIPrefix.ATTO => -18,
        _ => 0,
    });
}
