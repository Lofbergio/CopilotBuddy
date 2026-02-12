namespace Styx.WoWInternals.WoWObjects
{
    /// <summary>
    /// FEAT-17: Glyph slot information for WotLK (6 slots: 3 major, 3 minor).
    /// </summary>
    public readonly struct WoWGlyphInfo
    {
        /// <summary>Slot index (0-5).</summary>
        public int Slot { get; }

        /// <summary>Glyph slot type ID (defines major/minor).</summary>
        public uint SlotType { get; }

        /// <summary>Glyph spell ID (0 = empty slot).</summary>
        public uint GlyphId { get; }

        /// <summary>Whether this slot has a glyph socketed.</summary>
        public bool HasGlyph => GlyphId != 0;

        /// <summary>Whether this slot is enabled.</summary>
        public bool IsEnabled { get; }

        public WoWGlyphInfo(int slot, uint slotType, uint glyphId, bool isEnabled)
        {
            Slot = slot;
            SlotType = slotType;
            GlyphId = glyphId;
            IsEnabled = isEnabled;
        }

        public override string ToString() =>
            $"Glyph[{Slot}]: {(HasGlyph ? $"ID={GlyphId}" : "Empty")} ({(IsEnabled ? "Enabled" : "Disabled")})";
    }
}
