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
/// (the sample's length unit). An element's shape can combine several solids, e.g. a diffuser's face plate and neck.
/// </summary>
internal sealed class SampleGeometry
{
    public enum Axis { X, Y, Z }

    private readonly IModel _model;

    public SampleGeometry(IModel model)
    {
        _model = model;
        Context = model.Instances.New<IfcGeometricRepresentationContext>(c =>
        {
            c.ContextType = "Model";
            c.CoordinateSpaceDimension = 3;
            c.Precision = 1e-5;
            c.WorldCoordinateSystem = Placement3D(0, 0, 0);
        });
    }

    public IfcGeometricRepresentationContext Context { get; }

    /// <summary>A placement relative to <paramref name="parent"/> (or to the world when null).</summary>
    public IfcLocalPlacement Place(IfcObjectPlacement? parent, double x, double y, double z) =>
        _model.Instances.New<IfcLocalPlacement>(p =>
        {
            p.PlacementRelTo = parent;
            p.RelativePlacement = Placement3D(x, y, z);
        });

    /// <summary>One element shape made of one or more solids.</summary>
    public IfcProductDefinitionShape Shape(params IfcExtrudedAreaSolid[] solids) =>
        _model.Instances.New<IfcProductDefinitionShape>(s =>
            s.Representations.Add(_model.Instances.New<IfcShapeRepresentation>(r =>
            {
                r.ContextOfItems = Context;
                r.RepresentationIdentifier = "Body";
                r.RepresentationType = "SweptSolid";
                foreach (var solid in solids)
                    r.Items.Add(solid);
            })));

    /// <summary>A single box, centred in x and y on the placement, from z = 0 upwards.</summary>
    public IfcProductDefinitionShape Box(double width, double depth, double height) => Shape(BoxSolid(width, depth, height));

    /// <summary>A box of <paramref name="width"/> (x) × <paramref name="depth"/> (y) × <paramref name="height"/> (z), centred in x and y on (dx, dy), from z = dz upwards.</summary>
    public IfcExtrudedAreaSolid BoxSolid(double width, double depth, double height, double dx = 0, double dy = 0, double dz = 0) =>
        Extrude(_model.Instances.New<IfcRectangleProfileDef>(r =>
        {
            r.ProfileType = IfcProfileTypeEnum.AREA;
            r.XDim = width;
            r.YDim = depth;
            r.Position = _model.Instances.New<IfcAxis2Placement2D>(a => a.Location = Point2(0, 0));
        }), height, Placement3D(dx, dy, dz));

    /// <summary>
    /// A cylinder along <paramref name="axis"/>. Along X or Y it is centred on (dx, dy, dz); along Z it starts at
    /// (dx, dy, dz) and goes up, like a duct drop or a downlight.
    /// </summary>
    public IfcExtrudedAreaSolid CylinderSolid(double diameter, double length, Axis axis, double dx = 0, double dy = 0, double dz = 0)
    {
        var circle = _model.Instances.New<IfcCircleProfileDef>(c =>
        {
            c.ProfileType = IfcProfileTypeEnum.AREA;
            c.Radius = diameter / 2;
            c.Position = _model.Instances.New<IfcAxis2Placement2D>(a => a.Location = Point2(0, 0));
        });

        var position = axis switch
        {
            Axis.X => _model.Instances.New<IfcAxis2Placement3D>(a =>
            {
                a.Location = Point(dx - length / 2, dy, dz);
                a.Axis = Direction(1, 0, 0);
                a.RefDirection = Direction(0, 0, 1);
            }),
            Axis.Y => _model.Instances.New<IfcAxis2Placement3D>(a =>
            {
                a.Location = Point(dx, dy - length / 2, dz);
                a.Axis = Direction(0, 1, 0);
                a.RefDirection = Direction(1, 0, 0);
            }),
            _ => Placement3D(dx, dy, dz),
        };
        return Extrude(circle, length, position);
    }

    private IfcExtrudedAreaSolid Extrude(IfcProfileDef profile, double depth, IfcAxis2Placement3D position) =>
        _model.Instances.New<IfcExtrudedAreaSolid>(s =>
        {
            s.SweptArea = profile;
            s.Depth = depth;
            s.ExtrudedDirection = Direction(0, 0, 1);
            s.Position = position;
        });

    private IfcAxis2Placement3D Placement3D(double x, double y, double z) =>
        _model.Instances.New<IfcAxis2Placement3D>(a => a.Location = Point(x, y, z));

    private IfcCartesianPoint Point(double x, double y, double z) =>
        _model.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(x, y, z));

    private IfcCartesianPoint Point2(double x, double y) =>
        _model.Instances.New<IfcCartesianPoint>(p => p.SetXY(x, y));

    private IfcDirection Direction(double x, double y, double z) =>
        _model.Instances.New<IfcDirection>(d => d.SetXYZ(x, y, z));
}
