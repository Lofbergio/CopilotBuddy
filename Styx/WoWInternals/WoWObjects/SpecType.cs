namespace Styx.WoWInternals.WoWObjects
{
    /// <summary>
    /// FEAT-16: Player specialization type for role detection.
    /// In WotLK, this is determined by analyzing talent point distribution.
    /// Matches HB 4.3.4 enum values: None=0, RangedDps=1, MeleeDps=2, Healer=3, Tank=4.
    /// </summary>
    public enum SpecType
    {
        None = 0,
        RangedDps,
        MeleeDps,
        Healer,
        Tank
    }
}
