using System.Collections.Generic;
using Styx.Logic.Pathing;
using Tripper.XNAMath;

namespace Styx.Logic.AreaManagement;

/// <summary>
/// Represents a polygon-based area.
/// </summary>
public abstract class PolygonArea : Area
{
    private List<float> VertX { get; }
    private List<float> VertY { get; }

    protected PolygonArea(params Vector2[] areaDefinition)
    {
        VertX = new List<float>();
        VertY = new List<float>();

        foreach (var vector in areaDefinition)
        {
            VertX.Add(vector.X);
            VertY.Add(vector.Y);
        }
    }

    /// <summary>
    /// Gets whether the local player is within the polygon bounds.
    /// </summary>
    public bool LocalPlayerIsInBounds =>
        IsPointInPoly(VertX.Count, VertX.ToArray(), VertY.ToArray(), StyxWoW.Me.X, StyxWoW.Me.Y);

    /// <summary>
    /// Checks if a point is within the polygon.
    /// </summary>
    /// <param name="point">The point to check.</param>
    /// <returns>True if the point is within the polygon.</returns>
    public bool IsPointInPoly(WoWPoint point) =>
        IsPointInPoly(VertX.Count, VertX.ToArray(), VertY.ToArray(), point.X, point.Y);

    /// <summary>
    /// Point-in-polygon test using ray casting algorithm.
    /// </summary>
    private static bool IsPointInPoly(int nvert, float[] vertx, float[] verty, float testx, float testy)
    {
        bool inside = false;
        int j = nvert - 1;

        for (int i = 0; i < nvert; j = i++)
        {
            if ((verty[i] > testy) != (verty[j] > testy) &&
                testx < (vertx[j] - vertx[i]) * (testy - verty[i]) / (verty[j] - verty[i]) + vertx[i])
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
