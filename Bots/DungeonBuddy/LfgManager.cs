using System;
using System.Collections.Generic;
using System.Linq;
using Bots.DungeonBuddy.Enums;
using Styx;
using Styx.Helpers;
using Styx.WoWInternals;
using Styx.Combat.CombatRoutine; // for WoWClass enum

namespace Bots.DungeonBuddy
{
    /// <summary>
    /// Gère l'interface avec le Dungeon Finder (LFG).
    /// Compatible WotLK 3.3 - Dungeon Finder ajouté en patch 3.3.
    /// </summary>
    public static class LfgManager
    {
        // ═══════════════════════════════════════════════════════════
        // LFG STATE
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// État actuel du LFG.
        /// Utilise GetLFGMode() — API canonique confirmée WotLK 3.3.
        /// NOTE: En WotLK 3.3, GetLFGMode() ne prend AUCUN argument
        ///       (le paramètre category a été ajouté en MoP 5.x).
        /// Retourne: "queued", "proposal", "lfgparty", "abandonedInDungeon",
        ///           "suspended", "rolecheck", ou nil
        /// C'est la même méthode utilisée par HB 4.3.4 DungeonBuddy.
        /// </summary>
        public static LfgState CurrentState
        {
            get
            {
                string mode = Lua.GetReturnVal<string>(
                    "return GetLFGMode() or 'none'", 0);
                
                return mode?.ToLowerInvariant() switch
                {
                    "queued"              => LfgState.InQueue,
                    "proposal"            => LfgState.Proposal,
                    "lfgparty"            => LfgState.InDungeon,
                    "abandonedindungeon"  => LfgState.AbandonedInDungeon,
                    "suspended"           => LfgState.Suspended,
                    "rolecheck"           => LfgState.RoleCheck,
                    _                     => LfgState.NotInLfg
                };
            }
        }

        /// <summary>
        /// Temps d'attente estimé (en secondes)
        /// </summary>
        public static int EstimatedWaitTime
        {
            get
            {
                // WotLK 3.3.5a: category 1 = LFD (pas de constante LE_LFG_CATEGORY_LFD)
                // NOTE: La 16e valeur retour (waitTime estimé) n'est pas documentée sur Wowpedia 
                // pour WotLK. La position exacte est déduite de HB 3.3.5a. À valider en jeu.
                return Lua.GetReturnVal<int>(
                    "local _,_,_,_,_,_,_,_,_,_,_,_,_,_,_,w=GetLFGQueueStats(1); return w or 0", 0);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // QUEUE ACTIONS
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Queue pour un donjon aléatoire
        /// </summary>
        public static void QueueForRandomDungeon()
        {
            Logging.Write("[DungeonBuddy] Queuing for Random Dungeon via UI (Instancebuddy style)...");
            
            // Just like Instancebuddy, we rely on the user having selected their roles and the random dungeon manually.
            Lua.DoString(@"
                if not LFDQueueFrame then LoadAddOn('Blizzard_LFGUI') end
                LFDQueueFrame_Join();
            ");
        }

        /// <summary>
        /// Queue pour un héroïque aléatoire
        /// </summary>
        public static void QueueForRandomHeroic()
        {
            Logging.Write("[DungeonBuddy] Queuing for Random Heroic via UI (Instancebuddy style)...");
            
            Lua.DoString(@"
                if not LFDQueueFrame then LoadAddOn('Blizzard_LFGUI') end
                LFDQueueFrame_Join();
            ");
        }

        /// <summary>
        /// Queue pour un donjon spécifique
        /// </summary>
        public static void QueueForSpecificDungeon(uint dungeonId)
        {
            Logging.Write($"[DungeonBuddy] Queuing for dungeon ID {dungeonId}...");
            
            Lua.DoString($@"
                ClearAllLFGDungeons();
                SetLFGDungeon({dungeonId}, true);
                JoinLFG();
            ");
        }

        /// <summary>
        /// Accepter la proposition de donjon.
        /// ⚠️ AcceptProposal() est une fonction Lua RESTRICTED (Protected) en WotLK.
        /// Elle nécessite un "hardware event" (clic utilisateur) pour s'exécuter
        /// via l'interface Blizzard standard. MAIS CopilotBuddy injecte via EndScene
        /// (GreenMagic), ce qui bypass cette restriction car le code s'exécute
        /// dans le contexte du thread principal du jeu.
        /// Pattern HB: ajout d'un délai aléatoire (1-3s) avant d'accepter
        /// pour paraître plus humain et éviter la détection.
        /// </summary>
        public static void AcceptProposal()
        {
            Logging.Write("[DungeonBuddy] Accepting dungeon proposal...");
            // Le délai aléatoire est géré par le behavior tree dans DungeonBuddy.cs
            // via WaitContinue avant d'appeler cette méthode.
            Lua.DoString("AcceptProposal()");
        }

        /// <summary>
        /// Refuser la proposition
        /// </summary>
        public static void DeclineProposal()
        {
            Logging.Write("[DungeonBuddy] Declining dungeon proposal...");
            Lua.DoString("RejectProposal()");
        }

        /// <summary>
        /// Quitter la queue
        /// </summary>
        public static void LeaveQueue()
        {
            Logging.Write("[DungeonBuddy] Leaving LFG queue...");
            Lua.DoString("LeaveLFG()");
        }

        // ═══════════════════════════════════════════════════════════
        // TELEPORT
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Téléporte dans le donjon
        /// </summary>
        public static void TeleportIn()
        {
            Logging.Write("[DungeonBuddy] Teleporting into dungeon...");
            // LFGTeleport(toSafety) — false = teleport INTO dungeon
            // Wowpedia: toSafety=false to teleport to the dungeon
            Lua.DoString("LFGTeleport(false)");
        }

        /// <summary>
        /// Téléporte hors du donjon
        /// </summary>
        public static void TeleportOut()
        {
            Logging.Write("[DungeonBuddy] Teleporting out of dungeon...");
            Lua.DoString("LFGTeleport(true)");
        }

        // ═══════════════════════════════════════════════════════════
        // ROLE SELECTION
        // ═══════════════════════════════════════════════════════════

        private static PartyRole _currentRole = PartyRole.Dps;

        /// <summary>
        /// Définit le rôle pour le LFG
        /// </summary>
        public static void SetRole(PartyRole role)
        {
            // Removed to prevent "You did not select any valid slots" error.
            // The user manually selects their role in the UI, just like in the original Instancebuddy.
            _currentRole = role;
        }

        /// <summary>
        /// Obtient les rôles disponibles pour la classe actuelle
        /// </summary>
        public static PartyRole GetAvailableRoles()
        {
            var result = PartyRole.None;
            
            // Toutes les classes peuvent DPS
            result |= PartyRole.Dps;
            
            // Tanks: Warrior, Paladin, Death Knight, Druid (Bear)
            var tankClasses = new WoWClass[] { WoWClass.Warrior, WoWClass.Paladin, WoWClass.DeathKnight, WoWClass.Druid };
            if (tankClasses.Contains(StyxWoW.Me.Class))
                result |= PartyRole.Tank;
            
            // Healers: Priest, Paladin, Shaman, Druid
            var healerClasses = new WoWClass[] { WoWClass.Priest, WoWClass.Paladin, WoWClass.Shaman, WoWClass.Druid };
            if (healerClasses.Contains(StyxWoW.Me.Class))
                result |= PartyRole.Healer;
            
            return result;
        }

        // ═══════════════════════════════════════════════════════════
        // DUNGEON INFO
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Obtient le MapID du donjon actuel
        /// </summary>
        public static uint CurrentMapId => (uint)StyxWoW.Me.MapId;

        /// <summary>
        /// ID du donjon LFG actuel (depuis GetPartyLFGID()).
        /// Retourne 0 si pas dans un groupe LFG.
        /// HB 4.3.4: DungeonManager utilise cette valeur pour choisir le script de donjon.
        /// </summary>
        public static uint CurrentLfgDungeonId => Lua.GetReturnVal<uint>("return GetPartyLFGID() or 0", 0);

        /// <summary>
        /// Obtient la difficulté du donjon actuel (1=Normal, 2=Heroic)
        /// </summary>
        public static int CurrentDifficulty
        {
            get
            {
                return Lua.GetReturnVal<int>("return GetInstanceDifficulty()", 0);
            }
        }

        /// <summary>
        /// Liste des donjons disponibles pour le niveau actuel.
        /// NOTE: GetDungeonInfo(i) existe en WotLK mais n'est PAS GetLFGDungeonInfo().
        /// GetNumDungeons() et GetDungeonInfo() sont des API de LFDFrame.
        /// </summary>
        public static List<(uint Id, string Name, bool IsHeroic)> GetAvailableDungeons()
        {
            var result = new List<(uint, string, bool)>();
            
            var luaResult = Lua.GetReturnVal<string>(@"
                local dungeons = '';
                for i = 1, GetNumDungeons() do
                    local name, typeID, subtypeID, minLevel, maxLevel, _, minRecLevel, maxRecLevel,
                          _, _, _, difficulty = GetDungeonInfo(i);
                    if UnitLevel('player') >= minLevel and UnitLevel('player') <= maxLevel then
                        dungeons = dungeons .. i .. '|' .. name .. '|' .. difficulty .. ';';
                    end
                end
                return dungeons;
            ", 0);

            if (string.IsNullOrEmpty(luaResult))
                return result;

            foreach (var entry in luaResult.Split(';').Where(s => !string.IsNullOrEmpty(s)))
            {
                var parts = entry.Split('|');
                if (parts.Length >= 3)
                {
                    uint id = uint.Parse(parts[0]);
                    string name = parts[1];
                    bool isHeroic = parts[2] == "2";
                    result.Add((id, name, isHeroic));
                }
            }

            return result;
        }

        // ═══════════════════════════════════════════════════════════
        // LFG EVENTS (via Lua.Events — confirmé dans CopilotBuddy)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Flag: un proposal est en attente (set par événement Lua)
        /// </summary>
        public static bool ProposalPending { get; set; }

        /// <summary>
        /// Flag: le donjon est terminé (reward reçu)
        /// </summary>
        public static bool DungeonCompleted { get; set; }

        /// <summary>
        /// Flag: un vote kick est en cours
        /// </summary>
        public static bool BootProposalActive { get; set; }

        /// <summary>
        /// Attache les événements LFG via Lua.Events.AttachEvent
        /// Appelé dans DungeonBuddy.Start()
        /// </summary>
        public static void AttachLfgEvents()
        {
            ProposalPending = false;
            DungeonCompleted = false;
            BootProposalActive = false;

            Lua.Events.AttachEvent("LFG_PROPOSAL_SHOW", OnProposalShow);
            Lua.Events.AttachEvent("LFG_PROPOSAL_SUCCEEDED", OnProposalSucceeded);
            Lua.Events.AttachEvent("LFG_PROPOSAL_FAILED", OnProposalFailed);
            Lua.Events.AttachEvent("LFG_COMPLETION_REWARD", OnCompletionReward);
            Lua.Events.AttachEvent("LFG_OFFER_CONTINUE", OnOfferContinue);
            Lua.Events.AttachEvent("LFG_ROLE_CHECK_SHOW", OnRoleCheck);
            Lua.Events.AttachEvent("LFG_BOOT_PROPOSAL_UPDATE", OnBootProposal);
        }

        /// <summary>
        /// Détache les événements LFG.
        /// Appelé dans DungeonBuddy.Stop()
        /// </summary>
        public static void DetachLfgEvents()
        {
            Lua.Events.DetachEvent("LFG_PROPOSAL_SHOW", OnProposalShow);
            Lua.Events.DetachEvent("LFG_PROPOSAL_SUCCEEDED", OnProposalSucceeded);
            Lua.Events.DetachEvent("LFG_PROPOSAL_FAILED", OnProposalFailed);
            Lua.Events.DetachEvent("LFG_COMPLETION_REWARD", OnCompletionReward);
            Lua.Events.DetachEvent("LFG_OFFER_CONTINUE", OnOfferContinue);
            Lua.Events.DetachEvent("LFG_ROLE_CHECK_SHOW", OnRoleCheck);
            Lua.Events.DetachEvent("LFG_BOOT_PROPOSAL_UPDATE", OnBootProposal);
        }

        private static void OnProposalShow(object sender, LuaEventArgs args)
        {
            Logging.Write("[DungeonBuddy] LFG proposal received!");
            ProposalPending = true;
        }

        private static void OnProposalSucceeded(object sender, LuaEventArgs args)
        {
            Logging.Write("[DungeonBuddy] LFG proposal accepted by all! Teleporting...");
            ProposalPending = false;
            // Téléport imminent — pas d'action requise, le serveur gère
        }

        private static void OnProposalFailed(object sender, LuaEventArgs args)
        {
            Logging.Write("[DungeonBuddy] LFG proposal failed/declined");
            ProposalPending = false;
        }

        private static void OnCompletionReward(object sender, LuaEventArgs args)
        {
            Logging.Write("[DungeonBuddy] Dungeon completed! Reward received.");
            DungeonCompleted = true;
        }

        private static void OnOfferContinue(object sender, LuaEventArgs args)
        {
            Logging.Write("[DungeonBuddy] LFG offer to continue (requeue)");
            // Le behavior tree dans DungeonBuddy.cs gère le requeue
        }

        private static void OnRoleCheck(object sender, LuaEventArgs args)
        {
            Logging.Write("[DungeonBuddy] Role check requested, accepting...");
            // Accepter automatiquement le role check
            Lua.DoString("LFDRoleCheckPopupAcceptButton:Click()");
        }

        private static void OnBootProposal(object sender, LuaEventArgs args)
        {
            Logging.Write("[DungeonBuddy] Vote kick in progress");
            BootProposalActive = true;
            // Voter oui par défaut (ne pas bloquer le groupe)
            Lua.DoString("SetLFGBootVote(true)");
        }
    }
}