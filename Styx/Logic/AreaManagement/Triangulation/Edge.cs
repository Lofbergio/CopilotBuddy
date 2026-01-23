using System;

namespace Styx.Logic.AreaManagement.Triangulation;

/// <summary>
/// Represents an edge defined by two point indices.
/// </summary>
public struct Edge : IEquatable<Edge>
{
    /// <summary>
    /// First point index.
    /// </summary>
    public int P1;

    /// <summary>
    /// Second point index.
    /// </summary>
    public int P2;

    public Edge(int p1, int p2)
    {
        P1 = p1;
        P2 = p2;
    }

    public bool Equals(Edge other)
    {
        if (P1 == other.P1 && P2 == other.P2)
            return true;

        return P1 == other.P2 && P2 == other.P1;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null)
            return false;

        if (obj.GetType() != typeof(Edge))
            return false;

        return Equals((Edge)obj);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            // Hash should be the same regardless of point order
            return P1.GetHashCode() ^ P2.GetHashCode();
        }
    }

    public static bool operator ==(Edge left, Edge right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Edge left, Edge right)
    {
        return !left.Equals(right);
    }
}
