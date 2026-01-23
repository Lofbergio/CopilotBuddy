using Styx.Logic.Pathing;

namespace Styx.Logic.AreaManagement;

/// <summary>
/// Extension methods for Hotspot.
/// </summary>
public static class HotspotExtensions
{
    /// <summary>
    /// Converts a WoWPoint to a Hotspot.
    /// </summary>
    /// <param name="value">The WoWPoint to convert.</param>
    /// <returns>A Hotspot at the specified location.</returns>
    public static Hotspot ToHotspot(this WoWPoint value)
    {
        return value;
    }
}
