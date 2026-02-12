namespace Styx
{
    /// <summary>
    /// Game state enumeration — matches HB 4.3.4 values.
    /// Read from memory at 0x00B6A9E0 (WotLK 3.3.5a).
    /// Note: Some states (ChangingFactionOrRace, ScanDllScanning) are Cata+ only
    /// but kept for enum compatibility.
    /// </summary>
    public enum GameState
    {
        Idling,
        LoggingIn,
        ChangingRealm,
        SelectingCharacter,
        QueryingRealmList,
        CreatingCharacter,
        DeletingCharacter,
        CharacterRename,
        CharacterDecline,
        ChangingFactionOrRace,
        Zoning,
        Exiting,
        NonPersonalInfoSurvey,
        Unknown,
        ScanDllScanning
    }
}
