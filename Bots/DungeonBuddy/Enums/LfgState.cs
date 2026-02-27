namespace Bots.DungeonBuddy.Enums
{
    /// <summary>
    /// État actuel du Dungeon Finder
    /// </summary>
    public enum LfgState
    {
        None,
        NotInLfg,
        InQueue,
        Proposal,               // Popup accepter/refuser
        InDungeon,
        Suspended,              // Queue pausée (teleport out)
        RoleCheck,              // Sélection de rôle
        AbandonedInDungeon      // Groupe quitté mais toujours dans l'instance
    }
}