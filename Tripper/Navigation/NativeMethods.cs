using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Tripper.Navigation
{
    /// <summary>
    /// P/Invoke declarations for Navigation.dll C exports.
    /// Provides low-level access to Detour navmesh pathfinding functionality.
    /// </summary>
    internal static class NativeMethods
    {
        private const string DllName = "Navigation.dll";

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        internal struct XYZ_C
        {
            public float X;
            public float Y;
            public float Z;

            public XYZ_C(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public XYZ_C(Vector3 vec)
            {
                X = vec.X;
                Y = vec.Y;
                Z = vec.Z;
            }

            public Vector3 ToVector3()
            {
                return new Vector3(X, Y, Z);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NavStats_C
        {
            public float PathfindTimeMs;
            public int PolysVisited;
            public float PathLength;
            public int ShortcutsApplied;
            public int StuckRecoveries;
            public int PathRecalculations;
        }

        /// <summary>
        /// Native PathResult structure from Navigation.dll.
        /// Matches C++ PathResult struct.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct PathResult_C
        {
            public IntPtr Points;              // XYZ_C*
            public IntPtr StraightPathFlags;   // StraightPathFlags* (byte*)
            public IntPtr PolyTypes;           // unsigned char* (AreaType)
            public IntPtr AbilityFlags;        // unsigned char* (AbilityFlags)
            public int Length;
            public uint Status;                // NavStatusFlag bits
            public int FailStep;               // NavPathFindStep
        }

        #endregion

        #region Mesh Loading

        /// <summary>
        /// Loads navigation meshes from the standard mmaps directory.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool Nav_LoadMaps();

        /// <summary>
        /// Unloads all navigation meshes and releases resources.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Nav_UnloadMaps();

        #endregion

        #region Basic Pathfinding

        /// <summary>
        /// Calculates a path from start to end position.
        /// Returns array of path points that must be freed with FreePathArr_C.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr CalculatePath_C(
            uint mapId,
            XYZ_C start,
            XYZ_C end,
            [MarshalAs(UnmanagedType.I1)] bool straightPath,
            out int outLength);

        /// <summary>
        /// Frees path array allocated by CalculatePath_C.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FreePathArr_C(IntPtr arr);

        /// <summary>
        /// Calculates extended path result with detailed information.
        /// Returns PathResult structure that must be freed with FreePathResult.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr CalculatePathEx(
            uint mapId,
            XYZ_C start,
            XYZ_C end,
            [MarshalAs(UnmanagedType.I1)] bool straightPath);

        /// <summary>
        /// Frees PathResult allocated by CalculatePathEx.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FreePathResult(IntPtr result);

        #endregion

        #region Extended Navigation API

        /// <summary>
        /// Performs a raycast from start to end, returning hit position and distance.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool Raycast_C(
            uint mapId,
            XYZ_C start,
            XYZ_C end,
            out XYZ_C hitPos,
            out float tHit);

        /// <summary>
        /// Finds the nearest valid navmesh point to a given position.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool FindNearestPoint_C(
            uint mapId,
            XYZ_C position,
            out XYZ_C nearest);

        /// <summary>
        /// Finds nearest navmesh point with custom search extents.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool FindNearestPointEx_C(
            uint mapId,
            XYZ_C position,
            float extentX,
            float extentY,
            float extentZ,
            out XYZ_C nearest);

        /// <summary>
        /// Finds a random navigable point within radius of center position.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool FindRandomPoint_C(
            uint mapId,
            XYZ_C center,
            float radius,
            out XYZ_C randomPoint);

        /// <summary>
        /// Sets the traversal cost for a specific area type on a map.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SetAreaCost_C(
            uint mapId,
            int areaType,
            float cost);

        #endregion

        #region OffMesh Connections

        /// <summary>
        /// Checks if position is an offmesh connection and retrieves metadata.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool IsOffMeshConnection_C(
            uint mapId,
            XYZ_C position,
            out XYZ_C outEnd,
            out byte outType,
            out uint outInteractId);

        /// <summary>
        /// Adds a custom offmesh connection at runtime.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void AddOffMeshConnection_C(
            uint mapId,
            XYZ_C start,
            XYZ_C end,
            float radius,
            byte flags,
            byte type,
            uint interactId);

        /// <summary>
        /// Loads offmesh connections for a specific tile from .offmesh file.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool LoadTileOffMesh_C(
            uint mapId,
            int tileX,
            int tileY);

        #endregion

        #region Query Filter

        /// <summary>
        /// Sets polygon flags to include in pathfinding queries.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SetIncludeFlags_C(ushort flags);

        /// <summary>
        /// Sets polygon flags to exclude from pathfinding queries.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SetExcludeFlags_C(ushort flags);

        #endregion

        #region Tile Management

        /// <summary>
        /// Converts world coordinates to tile coordinates.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void WorldToTile_C(
            float worldX,
            float worldZ,
            out int tileX,
            out int tileY);

        /// <summary>
        /// Ensures tiles within specified ring distance are loaded.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void EnsureTiles_C(
            uint mapId,
            XYZ_C position,
            int ring);

        /// <summary>
        /// Ensures tiles are loaded in direction of movement.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void EnsureTilesDirectional_C(
            uint mapId,
            XYZ_C position,
            XYZ_C velocity,
            int ring);

        #endregion

        #region Detour Low-Level API

        /// <summary>
        /// Finds the nearest polygon reference to a position.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool FindNearestPolyRef_C(
            uint mapId,
            XYZ_C position,
            XYZ_C extents,
            out ulong outPolyRef,
            out XYZ_C nearestPoint);

        /// <summary>
        /// Gets the height at a position on a specific polygon.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool GetPolyHeight_C(
            uint mapId,
            ulong polyRef,
            XYZ_C position,
            out float outHeight);

        /// <summary>
        /// Finds the closest point on a polygon to a given position.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool ClosestPointOnPoly_C(
            uint mapId,
            ulong polyRef,
            XYZ_C position,
            out XYZ_C closestPoint);

        /// <summary>
        /// Finds the closest point on a polygon boundary.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool ClosestPointOnPolyBoundary_C(
            uint mapId,
            ulong polyRef,
            XYZ_C position,
            out XYZ_C closestPoint);

        /// <summary>
        /// Queries polygons within a bounding box.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int QueryPolygons_C(
            uint mapId,
            XYZ_C center,
            XYZ_C extents,
            [Out] ulong[] outPolys,
            int maxPolys);

        /// <summary>
        /// Finds local polygon neighbourhood around a position.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FindLocalNeighbourhood_C(
            uint mapId,
            ulong startPolyRef,
            XYZ_C center,
            float radius,
            [Out] ulong[] outPolys,
            [Out] ulong[] outParents,
            int maxResults);

        /// <summary>
        /// Gets wall segments for a polygon.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetPolyWallSegments_C(
            uint mapId,
            ulong polyRef,
            [Out] XYZ_C[] outSegmentStart,
            [Out] XYZ_C[] outSegmentEnd,
            int maxSegments);

        #endregion

        #region Statistics

        /// <summary>
        /// Gets current navigation statistics.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern NavStats_C GetNavStats_C();

        /// <summary>
        /// Resets navigation statistics counters.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ResetNavStats_C();

        #endregion
    }
}
