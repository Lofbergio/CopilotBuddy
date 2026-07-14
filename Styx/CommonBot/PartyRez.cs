namespace Styx.CommonBot
{
    /// <summary>
    /// Neutral botbase→routine channel for party resurrection. VibeParty (which owns the LOGIC) sets
    /// <see cref="Target"/> to the GUID the routine should rez — a dead player UNIT (unreleased) or a
    /// WoWCorpse (released) — and the combat routine (which owns the CAST) reads it and casts its class
    /// res via CastSpellById(resId, Target). 0 = nothing to do.
    ///
    /// Decoupled exactly like BotPoi: the routine never references VibeParty, so it stays standalone — a
    /// routine that ignores PartyRez simply never rezzes, and PartyRez stays 0 under every other botbase.
    /// Per-process (one bot); the botbase writes, the routine reads, the botbase clears on done/timeout.
    /// </summary>
    public static class PartyRez
    {
        /// <summary>GUID to resurrect (dead player unit, or that player's corpse). 0 = no rez requested.</summary>
        public static ulong Target { get; set; }
    }
}
