namespace Styx.Logic.AreaManagement.Triangulation;

/// <summary>
/// Represents a triangle defined by three point indices.
/// </summary>
public struct Triangle
{
    /// <summary>
    /// First point index.
    /// </summary>
    public int P1;

    /// <summary>
    /// Second point index.
    /// </summary>
    public int P2;

    /// <summary>
    /// Third point index.
    /// </summary>
    public int P3;

    public Triangle(int p1, int p2, int p3)
    {
        P1 = p1;
        P2 = p2;
        P3 = p3;
    }
}
