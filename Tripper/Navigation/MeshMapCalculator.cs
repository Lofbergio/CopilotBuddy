using System;

namespace Tripper.Navigation
{
    /// <summary>
    /// Converts between WoW ADT tile coordinates and Detour sub-tile coordinates.
    ///
    /// Port of HB 6.2.3 Tripper/MeshMisc/MeshMapCalculator.cs.
    ///
    /// WoW uses a 64×64 ADT grid; each ADT is 533.333 yards. With the V5 mmtile format
    /// (MMAP_MULTI_TILE_VERSION = 5), each ADT is subdivided into a 4×4 grid of 16 Detour
    /// tiles, each 133.333 yards. Navigation.dll loads all 16 sub-tiles when a single
    /// <c>loadMap(mapId, x, y)</c> call is made, and removes them all on unload.
    ///
    /// The ADT grid origin is at tile [32, 32] (world position 0, 0).
    /// Detour sub-tile coordinates are offset from that: detourX = (adt.X - 32) × 4 + subX.
    /// </summary>
    public sealed class MeshMapCalculator
    {
        #region Fields

        private readonly int _subTilesPerAdt;

        #endregion

        #region Constructor

        /// <param name="subTilesPerAdt">Number of Detour sub-tiles per ADT along one axis (use 4).</param>
        public MeshMapCalculator(int subTilesPerAdt)
        {
            if (subTilesPerAdt <= 0)
                throw new ArgumentOutOfRangeException(nameof(subTilesPerAdt), "Must be > 0");
            _subTilesPerAdt = subTilesPerAdt;
        }

        #endregion

        #region Properties

        /// <summary>Sub-tiles per ADT along one axis. 4 × 4 = 16 total sub-tiles per ADT.</summary>
        public int SubTilesPerAdt => _subTilesPerAdt;

        /// <summary>
        /// Detour tile size in yards. AdtTileSize / SubTilesPerAdt.
        /// For the default 4-subtile config: 533.333 / 4 = 133.333 yards.
        /// </summary>
        public float DetourTileSize => MapConsts.TileSize / _subTilesPerAdt;

        /// <summary>
        /// Default calculator — 4 sub-tiles per ADT side (16 per ADT).
        /// Matches HB 6.2.3 and CopilotBuddy V5 mmtile format.
        /// </summary>
        public static MeshMapCalculator Default { get; } = new MeshMapCalculator(4);

        #endregion

        #region Coordinate Conversion

        /// <summary>
        /// Converts an ADT tile + sub-tile offset to a Detour tile identifier.
        /// </summary>
        /// <param name="adt">ADT tile in WoW tile space (origin at [32, 32]).</param>
        /// <param name="subX">Sub-tile column within the ADT (0 … SubTilesPerAdt-1).</param>
        /// <param name="subY">Sub-tile row within the ADT (0 … SubTilesPerAdt-1).</param>
        /// <returns>Detour tile identifier at sub-tile resolution.</returns>
        public TileIdentifier GetDetourTile(TileIdentifier adt, int subX, int subY)
        {
            int detourX = (adt.X - 32) * _subTilesPerAdt + subX;
            int detourY = (adt.Y - 32) * _subTilesPerAdt + subY;
            return new TileIdentifier(detourX, detourY);
        }

        /// <summary>
        /// Converts a Detour sub-tile coordinate back to its parent ADT tile identifier.
        /// </summary>
        public TileIdentifier GetWowTile(int detourX, int detourY)
        {
            GetWowTile(detourX, detourY, out TileIdentifier adt, out _, out _);
            return adt;
        }

        /// <summary>
        /// Converts a Detour sub-tile coordinate to an ADT tile identifier plus the sub-tile offsets.
        /// </summary>
        /// <param name="detourX">Detour tile X coordinate.</param>
        /// <param name="detourY">Detour tile Y coordinate.</param>
        /// <param name="adt">Receives the parent ADT tile identifier.</param>
        /// <param name="subX">Receives the sub-tile column within the ADT (0-based).</param>
        /// <param name="subY">Receives the sub-tile row within the ADT (0-based).</param>
        public void GetWowTile(int detourX, int detourY, out TileIdentifier adt, out int subX, out int subY)
        {
            subX = detourX % _subTilesPerAdt;
            subY = detourY % _subTilesPerAdt;
            // C# modulo preserves sign — normalise to [0, SubTilesPerAdt).
            if (subX < 0) subX += _subTilesPerAdt;
            if (subY < 0) subY += _subTilesPerAdt;
            adt = new TileIdentifier(
                (detourX - subX) / _subTilesPerAdt + 32,
                (detourY - subY) / _subTilesPerAdt + 32);
        }

        /// <summary>Returns a string describing the calculator configuration.</summary>
        public override string ToString() => $"SubTilesPerAdt: {_subTilesPerAdt}, DetourTileSize: {DetourTileSize:F3}";

        #endregion
    }
}
