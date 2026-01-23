using System;
using System.Runtime.InteropServices;

namespace Tripper.Navigation
{
    /// <summary>
    /// Represents a reference to a polygon in the Detour navmesh.
    /// This is a lightweight struct wrapping the polygon's unique ID.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PolygonReference : IEquatable<PolygonReference>
    {
        /// <summary>
        /// The unique identifier for this polygon.
        /// </summary>
        public readonly uint Id;

        /// <summary>
        /// Initializes a new polygon reference with the specified ID.
        /// </summary>
        /// <param name="id">Polygon ID.</param>
        public PolygonReference(uint id)
        {
            Id = id;
        }

        /// <summary>
        /// Gets an invalid polygon reference (ID = 0).
        /// </summary>
        public static PolygonReference Invalid => new PolygonReference(0);

        /// <summary>
        /// Gets a value indicating whether this polygon reference is valid.
        /// </summary>
        public bool IsValid => Id != 0;

        public bool Equals(PolygonReference other) => Id == other.Id;

        public override bool Equals(object? obj) => obj is PolygonReference other && Equals(other);

        public override int GetHashCode() => Id.GetHashCode();

        public override string ToString() => $"PolyRef({Id})";

        public static bool operator ==(PolygonReference left, PolygonReference right) => left.Equals(right);
        public static bool operator !=(PolygonReference left, PolygonReference right) => !left.Equals(right);

        public static implicit operator PolygonReference(uint id) => new PolygonReference(id);
        public static implicit operator uint(PolygonReference polyRef) => polyRef.Id;
    }
}
