using Tripper.XNAMath;

namespace Styx.Logic.AreaManagement;

/// <summary>
/// Represents a PvP area.
/// </summary>
public class PvPArea : PolygonArea
{
    /// <summary>
    /// The name of the PvP area.
    /// </summary>
    public readonly string Name;

    public PvPArea(string name, params Vector2[] areaDefinition) : base(areaDefinition)
    {
        Name = name;
    }

    public PvPArea(params Vector2[] areaDefinition) : this("Unknown", areaDefinition)
    {
    }

    /// <summary>
    /// Gets the area type.
    /// </summary>
    public override AreaType Type => AreaType.PvP;
}
