using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Styx.Helpers;
using Styx.Logic.AreaManagement.Triangulation;
using Styx.Logic.Pathing;
using Styx.Logic.Questing;

namespace Styx.Logic.AreaManagement;

/// <summary>
/// Represents a quest area with dynamic hotspot generation.
/// </summary>
public class QuestArea : GrindArea
{
    private readonly List<List<Vector3>> _areaDefinitions;
    private readonly CircularQueue<Hotspot> _circularHotspots = new();

    public QuestArea(PlayerQuest quest, IList<WoWQuestStep> questSteps)
    {
        Quest = quest;
        _areaDefinitions = new List<List<Vector3>>(questSteps.Count);

        for (int i = 0; i < questSteps.Count; i++)
        {
            var areaPoints = questSteps[i].AreaPoints.ToList();
            _areaDefinitions.Add(areaPoints.ConvertAll(v2 => new Vector3(v2.X, v2.Y, 0f)));
        }
    }

    /// <summary>
    /// Gets the associated quest.
    /// </summary>
    public PlayerQuest Quest { get; }

    /// <summary>
    /// Gets whether hotspots have been created.
    /// </summary>
    public bool HotspotsCreated { get; private set; }

    /// <summary>
    /// Gets the area type.
    /// </summary>
    public override AreaType Type => AreaType.Quest;

    /// <summary>
    /// Gets the area definitions.
    /// </summary>
    public List<List<Vector3>> AreaDefinitions => _areaDefinitions;

    /// <summary>
    /// Creates hotspots from the quest area definitions.
    /// </summary>
    public void CreateHotspots()
    {
        if (HotspotsCreated)
            return;

        for (int i = 0; i < _areaDefinitions.Count; i++)
        {
            var polys = _areaDefinitions.ConvertAll(lv3 => lv3.ConvertAll(v3 => new Vector2(v3.X, v3.Y)));

            foreach (var pnt in GenerateHotspots(polys))
            {
                WoWPoint woWPoint = new WoWPoint(pnt.X, pnt.Y, pnt.Z);
                _circularHotspots.Enqueue(woWPoint.ToHotspot());
                Hotspots.Add(woWPoint);
            }
        }

        if (_circularHotspots.Count <= 0)
        {
            Logging.Write($"No hotspots created for quest: {Quest.Name}");
        }

        CircledHotspots = _circularHotspots;
        HotspotsCreated = true;
    }

    /// <summary>
    /// Generates hotspots from polygon definitions using triangulation.
    /// </summary>
    private static List<Vector3> GenerateHotspots(IList<List<Vector2>> polys)
    {
        var result = new List<Vector3>();

        for (int i = 0; i < polys.Count; i++)
        {
            var poly = polys[i];
            if (poly.Count == 0)
                continue;

            if (poly.Count >= 3)
            {
                // Use triangulation for complex polygons
                var triangles = Triangulate(poly);
                foreach (var triangle in triangles)
                {
                    var v1 = poly[triangle.P1];
                    var v2 = poly[triangle.P2];
                    var v3 = poly[triangle.P3];
                    var centroid = (v1 + v2 + v3) / 3f;

                    var xnaPos = new Tripper.XNAMath.Vector3(centroid.X, centroid.Y, 0f);
                    if (Navigator.FindMeshHeight(ref xnaPos))
                    {
                        result.Add(new Vector3(xnaPos.X, xnaPos.Y, xnaPos.Z));
                    }
                }
            }
            else
            {
                // For 1-2 points, just use them directly
                foreach (var point in poly)
                {
                    var xnaPos = new Tripper.XNAMath.Vector3(point.X, point.Y, 0f);
                    if (Navigator.FindMeshHeight(ref xnaPos))
                    {
                        result.Add(new Vector3(xnaPos.X, xnaPos.Y, xnaPos.Z));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Simple ear-clipping triangulation algorithm.
    /// </summary>
    private static List<Triangle> Triangulate(List<Vector2> polygon)
    {
        var triangles = new List<Triangle>();
        
        if (polygon.Count < 3)
            return triangles;

        // Simple fan triangulation for convex polygons
        // TODO: Implement proper ear-clipping for concave polygons
        for (int i = 1; i < polygon.Count - 1; i++)
        {
            triangles.Add(new Triangle(0, i, i + 1));
        }

        return triangles;
    }
}
