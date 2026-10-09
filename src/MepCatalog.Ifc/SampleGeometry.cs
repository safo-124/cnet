using Xbim.Common;
using Xbim.Ifc4.GeometricConstraintResource;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.ProfileResource;
using Xbim.Ifc4.RepresentationResource;

namespace MepCatalog.Ifc;

/// <summary>
/// Simple solid geometry for the sample building: placements and extruded boxes and cylinders, in millimetres
/// (the sample's length unit). Enough for a 3D viewer to show where each device is.
/// </summary>
internal sealed class SampleGeometry
{
    private readonly IModel _model;

    public SampleGeometry(IModel model)
    {
        _model = model;
        Context = model.Instances.New<IfcGeometricRepresentationContext>(c =>
        {
            c.ContextType = "Model";
            c.CoordinateSpaceDimension = 3;
            c.Precision = 1e-5;
            c.WorldCoordinateSystem = Axis(0, 0, 0);
        });
    }

    public IfcGeometricRepresentationContext Context { get; }

    /// <summary>A placement relative to <paramref name="parent"/> (or to the world when null).</summary>
    public IfcLocalPlacement Place(IfcObjectPlacement? parent, double x, double y, double z) =>
        _model.Instances.New<IfcLocalPlacement>(p =>
        {
            p.PlacementRelTo = parent;
            p.RelativePlacement = Axis(x, y, z);
        });

    /// <summary>A box of <paramref name="width"/> (x) × <paramref name="depth"/> (y), extruded upwards by <paramref name="height"/>, centred on the placement.</summary>
    public IfcProductDefinitionShape Box(double width, double depth, double height) =>
        Shape(Extrude(_model.Instances.New<IfcRectangleProfileDef>(r =>
        {
            r.ProfileType = IfcProfileTypeEnum.AREA;
            r.XDim = width;
            r.YDim = depth;
            r.Position = _model.Instances.New<IfcAxis2Placement2D>(a => a.Location = Point2(0, 0));
        }), height, Axis(0, 0, 0)));

    /// <summary>A vertical cylinder, e.g. a downlight.</summary>
    public IfcProductDefinitionShape VerticalCylinder(double diameter, double height) =>
        Shape(Extrude(Circle(diameter), height, Axis(0, 0, 0)));

    /// <summary>A cylinder lying along the x axis, centred on the placement, e.g. a duct fan or a round damper.</summary>
    public IfcProductDefinitionShape HorizontalCylinder(double diameter, double length) =>
        Shape(Extrude(Circle(diameter), length, _model.Instances.New<IfcAxis2Placement3D>(a =>
        {
            a.Location = Point(-length / 2, 0, 0);
            a.Axis = Direction(1, 0, 0);
            a.RefDirection = Direction(0, 0, 1);
        })));

    private IfcCircleProfileDef Circle(double diameter) => _model.Instances.New<IfcCircleProfileDef>(c =>
    {
        c.ProfileType = IfcProfileTypeEnum.AREA;
        c.Radius = diameter / 2;
        c.Position = _model.Instances.New<IfcAxis2Placement2D>(a => a.Location = Point2(0, 0));
    });

    private IfcExtrudedAreaSolid Extrude(IfcProfileDef profile, double depth, IfcAxis2Placement3D position) =>
        _model.Instances.New<IfcExtrudedAreaSolid>(s =>
        {
            s.SweptArea = profile;
            s.Depth = depth;
            s.ExtrudedDirection = Direction(0, 0, 1);
            s.Position = position;
        });

    private IfcProductDefinitionShape Shape(IfcExtrudedAreaSolid solid) => _model.Instances.New<IfcProductDefinitionShape>(s =>
        s.Representations.Add(_model.Instances.New<IfcShapeRepresentation>(r =>
        {
            r.ContextOfItems = Context;
            r.RepresentationIdentifier = "Body";
            r.RepresentationType = "SweptSolid";
            r.Items.Add(solid);
        })));

    private IfcAxis2Placement3D Axis(double x, double y, double z) =>
        _model.Instances.New<IfcAxis2Placement3D>(a => a.Location = Point(x, y, z));

    private IfcCartesianPoint Point(double x, double y, double z) =>
        _model.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(x, y, z));

    private IfcCartesianPoint Point2(double x, double y) =>
        _model.Instances.New<IfcCartesianPoint>(p => p.SetXY(x, y));

    private IfcDirection Direction(double x, double y, double z) =>
        _model.Instances.New<IfcDirection>(d => d.SetXYZ(x, y, z));
}
