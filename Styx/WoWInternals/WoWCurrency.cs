// WoW 3.3.5a does NOT have a currency system.
// Currency was introduced in Cataclysm (4.0).
// This stub class provides API compatibility for HB profiles that reference it.
// All methods return null or empty values.

#nullable disable
namespace Styx.WoWInternals
{
    /// <summary>
    /// Represents a WoW currency.
    /// NOTE: Currency system does not exist in WoW 3.3.5a (WotLK).
    /// This is a stub class for API compatibility with HB 4.3.4 profiles.
    /// All methods will return null or default values.
    /// </summary>
    public class WoWCurrency
    {
        private WoWCurrency()
        {
            // Private constructor - cannot be instantiated
        }

        /// <summary>
        /// Gets a currency by its ID.
        /// Always returns null in WoW 3.3.5a as currency system doesn't exist.
        /// </summary>
        public static WoWCurrency GetCurrencyById(uint id)
        {
            // Currency system doesn't exist in 3.3.5a
            return null;
        }

        /// <summary>
        /// Gets a currency by its type.
        /// Always returns null in WoW 3.3.5a as currency system doesn't exist.
        /// </summary>
        public static WoWCurrency GetCurrencyByType(WoWCurrencyType type)
        {
            return GetCurrencyById((uint)type);
        }

        /// <summary>
        /// Whether this currency instance is valid.
        /// Always false as currency doesn't exist in 3.3.5a.
        /// </summary>
        public bool IsValid => false;

        /// <summary>
        /// The entry ID of the currency.
        /// </summary>
        public uint Entry => 0;

        /// <summary>
        /// The name of the currency.
        /// </summary>
        public string Name => string.Empty;

        /// <summary>
        /// The currency category entry.
        /// </summary>
        public uint CurrencyCategoryEntry => 0;

        /// <summary>
        /// The current amount of this currency.
        /// </summary>
        public uint Amount => 0;

        /// <summary>
        /// The maximum total of this currency.
        /// </summary>
        public uint TotalMax => 0;

        /// <summary>
        /// The weekly maximum of this currency.
        /// </summary>
        public uint WeeklyMax => 0;

        /// <summary>
        /// The currency type.
        /// </summary>
        public WoWCurrencyType CurrencyType => WoWCurrencyType.None;

        public override string ToString()
        {
            return "[Currency system not available in WoW 3.3.5a]";
        }
    }

    /// <summary>
    /// Currency types from Cataclysm onwards.
    /// Not used in WoW 3.3.5a but included for API compatibility.
    /// </summary>
    public enum WoWCurrencyType
    {
        None = 0,
        // Cataclysm currencies (not in 3.3.5a)
        JusticePoints = 395,
        ValorPoints = 396,
        HonorPoints = 392,
        ConquestPoints = 390
    }
}
