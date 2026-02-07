using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tripper.Navigation
{
    /// <summary>
    /// Post-processes paths to move waypoints away from navmesh edges.
    /// Based on Honorbuddy's MoveAwayFromEdges algorithm.
    /// This prevents characters from walking too close to walls, stair edges,
    /// and other terrain features that could cause stuck issues.
    /// </summary>
    internal static class PathPostProcessor
    {
        /// <summary>
        /// Default distance threshold for edge detection.
        /// Points closer than this to a wall will be moved.
        /// </summary>
        private const float DefaultEdgeDistance = 2.0f;

        /// <summary>
        /// Maximum recursion depth for path fixing.
        /// </summary>
        private const int MaxRecursionDepth = 5;

        /// <summary>
        /// Post-processes a path by moving waypoints away from edges.
        /// </summary>
        /// <param name="mapId">Map ID for navmesh queries.</param>
        /// <param name="points">Path points to process (modified in place).</param>
        /// <param name="flags">Path flags for each point.</param>
        /// <param name="edgeDistance">Distance threshold for edge detection.</param>
        public static void MoveAwayFromEdges(
            uint mapId,
            ref Vector3[] points,
            ref StraightPathFlags[] flags,
            float edgeDistance = DefaultEdgeDistance)
        {
            // Call overload with null polygons for backward compatibility
            PolygonReference[] polygons = null;
            MoveAwayFromEdges(mapId, ref points, ref flags, ref polygons, edgeDistance);
        }

        /// <summary>
        /// Post-processes a path by moving waypoints away from edges.
        /// Uses polygon references for more accurate wall distance queries.
        /// </summary>
        /// <param name="mapId">Map ID for navmesh queries.</param>
        /// <param name="points">Path points to process (modified in place).</param>
        /// <param name="flags">Path flags for each point.</param>
        /// <param name="polygons">Polygon references for each point (can be null).</param>
        /// <param name="edgeDistance">Distance threshold for edge detection.</param>
        public static void MoveAwayFromEdges(
            uint mapId,
            ref Vector3[] points,
            ref StraightPathFlags[] flags,
            ref PolygonReference[] polygons,
            float edgeDistance = DefaultEdgeDistance)
        {
            if (points == null || points.Length < 2)
                return;

            // Convert to lists for easier manipulation
            var pointsList = new List<Vector3>(points);
            var flagsList = new List<StraightPathFlags>(flags);
            var polyList = polygons != null ? new List<PolygonReference>(polygons) : null;

            // Pass 1: Move intermediate waypoints away from edges
            MoveWaypointsFromEdges(mapId, pointsList, flagsList, polyList, edgeDistance);

            // Pass 2: Fix any segments that became unwalkable
            FixPathWalkability(mapId, pointsList, flagsList, edgeDistance, 0);

            // Convert back to arrays
            points = pointsList.ToArray();
            flags = flagsList.ToArray();
            if (polyList != null)
                polygons = polyList.ToArray();
        }

        /// <summary>
        /// Randomizes path for more human-like movement.
        /// </summary>
        /// <param name="mapId">Map ID for navmesh queries.</param>
        /// <param name="points">Path points to process (modified in place).</param>
        /// <param name="flags">Path flags for each point.</param>
        /// <param name="minOffset">Minimum random offset distance.</param>
        /// <param name="maxOffset">Maximum random offset distance.</param>
        public static void Randomize(
            uint mapId,
            ref Vector3[] points,
            ref StraightPathFlags[] flags,
            float minOffset = 2.0f,
            float maxOffset = 6.0f)
        {
            if (points == null || points.Length < 2)
                return;

            var pointsList = new List<Vector3>(points);
            var flagsList = new List<StraightPathFlags>(flags);
            var random = new Random();

            // Randomize intermediate points
            RandomizeWaypoints(mapId, pointsList, flagsList, minOffset, maxOffset, random);

            // Fix any segments that became unwalkable
            FixPathWalkability(mapId, pointsList, flagsList, minOffset, 0);

            points = pointsList.ToArray();
            flags = flagsList.ToArray();
        }

        /// <summary>
        /// Moves waypoints away from nearby edges/walls.
        /// Based on HB's method_3.
        /// </summary>
        private static void MoveWaypointsFromEdges(
            uint mapId,
            List<Vector3> points,
            List<StraightPathFlags> flags,
            List<PolygonReference> polygons,
            float edgeDistance)
        {
            // Skip first and last points (start/end positions should not be moved)
            for (int i = 1; i < points.Count - 1; i++)
            {
                // Skip off-mesh connection points (elevators, portals, etc.)
                if ((flags[i] & StraightPathFlags.OffMeshConnection) != 0)
                    continue;
                if (i > 0 && (flags[i - 1] & StraightPathFlags.OffMeshConnection) != 0)
                    continue;

                Vector3 point = points[i];
                ulong polyRef = (polygons != null && i < polygons.Count) ? polygons[i].Id : 0;
                
                if (TryMoveAwayFromEdge(mapId, ref point, polyRef, edgeDistance))
                {
                    points[i] = point;
                }
            }
        }

        /// <summary>
        /// Tries to move a point away from the nearest edge/wall.
        /// Based on HB's method_2 (TryMoveAwayFromEdge).
        /// </summary>
        /// <returns>True if point was successfully moved.</returns>
        private static bool TryMoveAwayFromEdge(uint mapId, ref Vector3 point, ulong polyRef, float edgeDistance)
        {
            // Find distance to nearest wall
            NativeMethods.XYZ hitPoint;
            NativeMethods.XYZ hitNormal;
            float distance;
            
            // Use polygon-specific query if we have a valid polyRef (like HB WoD method_2)
            if (polyRef != 0)
            {
                distance = NativeMethods.FindDistanceToWallFromPoly(
                    mapId,
                    polyRef,
                    new NativeMethods.XYZ(point),
                    edgeDistance,
                    out hitPoint,
                    out hitNormal);
            }
            else
            {
                distance = NativeMethods.FindDistanceToWall(
                    mapId,
                    new NativeMethods.XYZ(point),
                    edgeDistance,
                    out hitPoint);
                // Calculate normal manually when using basic FindDistanceToWall
                Vector3 toWall = hitPoint.ToVector3() - point;
                float len = toWall.Length();
                hitNormal = len > 0.01f 
                    ? new NativeMethods.XYZ(-toWall.X / len, -toWall.Y / len, -toWall.Z / len)
                    : new NativeMethods.XYZ(1, 0, 0);
            }

            // If we're far enough from walls, nothing to do
            if (distance >= edgeDistance || distance < 0.01f)
                return false;

            // Use hitNormal to calculate direction away from wall (like HB WoD)
            Vector3 wallPos = hitPoint.ToVector3();
            Vector3 awayDir = hitNormal.ToVector3();
            float awayLength = awayDir.Length();
            
            if (awayLength < 0.01f)
            {
                // Fallback: calculate from point to wall
                awayDir = point - wallPos;
                awayLength = awayDir.Length();
                if (awayLength < 0.01f)
                {
                    awayDir = new Vector3(1, 0, 0);
                    awayLength = 1f;
                }
            }
            
            awayDir /= awayLength; // Normalize

            // Calculate new position: wallPos + normal * edgeDistance * 2 (like HB WoD)
            Vector3 newPos = wallPos + awayDir * edgeDistance * 2f;

            // Verify new position is on navmesh
            NativeMethods.XYZ nearestPoint;
            if (NativeMethods.FindNearestPoly(mapId, new NativeMethods.XYZ(newPos), 1.0f, out nearestPoint))
            {
                // Use the snapped navmesh point directly — it's already verified navigable.
                // HasLineOfSight check removed: both branches yielded the same result,
                // and the snapped point from FindNearestPoly is safe regardless.
                point = nearestPoint.ToVector3();
                return true;
            }

            // Fallback: just move slightly away from wall
            newPos = wallPos + awayDir * (edgeDistance * 0.5f);
            if (NativeMethods.FindNearestPoly(mapId, new NativeMethods.XYZ(newPos), 1.0f, out nearestPoint))
            {
                point = nearestPoint.ToVector3();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Randomizes waypoint positions for more human-like movement.
        /// Based on HB's method_4.
        /// </summary>
        private static void RandomizeWaypoints(
            uint mapId,
            List<Vector3> points,
            List<StraightPathFlags> flags,
            float minOffset,
            float maxOffset,
            Random random)
        {
            for (int i = 1; i < points.Count - 1; i++)
            {
                // Skip off-mesh connections
                if ((flags[i] & StraightPathFlags.OffMeshConnection) != 0)
                    continue;
                if (i > 0 && (flags[i - 1] & StraightPathFlags.OffMeshConnection) != 0)
                    continue;

                float offset = (float)(random.NextDouble() * (maxOffset - minOffset) + minOffset);
                Vector3 point = points[i];

                // First try to move away from edge
                NativeMethods.XYZ hitPoint;
                float distance = NativeMethods.FindDistanceToWall(
                    mapId,
                    new NativeMethods.XYZ(point),
                    offset,
                    out hitPoint);

                bool moved = false;
                if (distance < offset && distance >= 0.01f)
                {
                    // Near wall, move away from it
                    if (TryMoveAwayFromEdge(mapId, ref point, 0, offset))
                    {
                        moved = true;
                    }
                }
                else
                {
                    // Not near wall, find random point around
                    float randomOffset = (float)random.NextDouble() * Math.Min(offset, maxOffset * 0.5f);
                    NativeMethods.XYZ randomPoint;
                    if (NativeMethods.FindRandomPointAroundCircle(mapId, new NativeMethods.XYZ(point), randomOffset, out randomPoint))
                    {
                        point = randomPoint.ToVector3();
                        moved = true;
                    }
                }

                if (moved)
                {
                    points[i] = point;
                }
            }
        }

        /// <summary>
        /// Fixes path segments that may have become unwalkable after modifications.
        /// Based on HB's method_5/method_9 (FixPathWalkability).
        /// AUDIT FIX: When a segment is blocked (no line of sight), insert a midpoint on navmesh
        /// between the two endpoints and recurse. This prevents post-MoveAwayFromEdges paths
        /// from silently becoming invalid.
        /// </summary>
        private static void FixPathWalkability(
            uint mapId,
            List<Vector3> points,
            List<StraightPathFlags> flags,
            float edgeDistance,
            int recursionDepth)
        {
            if (recursionDepth > MaxRecursionDepth)
                return;

            for (int i = 0; i < points.Count - 1; i++)
            {
                // Skip off-mesh connections
                if ((flags[i] & StraightPathFlags.OffMeshConnection) != 0)
                    continue;
                if ((flags[i + 1] & StraightPathFlags.OffMeshConnection) != 0)
                    continue;

                Vector3 start = points[i];
                Vector3 end = points[i + 1];

                // Skip very short segments
                float segmentLengthSqr = Vector3.DistanceSquared(start, end);
                if (segmentLengthSqr < 0.01f)
                    continue;

                // Check if we can walk directly between points
                if (!NativeMethods.HasLineOfSight(mapId, new NativeMethods.XYZ(start), new NativeMethods.XYZ(end)))
                {
                    // Path segment is blocked — insert a midpoint on the navmesh between start and end.
                    // This splits the blocked segment into two, which may be individually walkable.
                    // If not, the recursion will continue splitting until MaxRecursionDepth.
                    Vector3 midpoint = (start + end) * 0.5f;
                    NativeMethods.XYZ snappedMid;
                    if (NativeMethods.FindNearestPoly(mapId, new NativeMethods.XYZ(midpoint), 5.0f, out snappedMid))
                    {
                        Vector3 newPoint = snappedMid.ToVector3();
                        // Only insert if the midpoint is meaningfully different from both endpoints
                        if (Vector3.DistanceSquared(newPoint, start) > 1.0f &&
                            Vector3.DistanceSquared(newPoint, end) > 1.0f)
                        {
                            points.Insert(i + 1, newPoint);
                            flags.Insert(i + 1, StraightPathFlags.None);
                            // Don't increment i — re-check the start→midpoint segment on next iteration
                            // Recurse on the modified path to fix remaining blocked sub-segments
                            FixPathWalkability(mapId, points, flags, edgeDistance, recursionDepth + 1);
                            return; // Recursion handles the rest
                        }
                    }
                    // If snap failed or midpoint too close, skip — can't fix this segment
                }
            }
        }
    }
}
