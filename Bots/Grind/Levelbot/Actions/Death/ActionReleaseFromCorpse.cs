using Styx;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using System.Threading;
using TreeSharp;

namespace Levelbot.Actions.Death
{
    public class ActionReleaseFromCorpse : TreeSharp.Action
    {
        protected override RunStatus Run(object context)
        {
            if (!StyxWoW.IsInGame)
                return RunStatus.Running;

            // Log death
            Logging.Write("I died!");
            Navigator.Clear();

            // Release spirit
            ReleaseSpirit();

            if (ObjectManager.Me == null)
                return RunStatus.Failure;

            if (ObjectManager.Me.Dead)
                return RunStatus.Running;

            if (ObjectManager.Me.IsGhost)
                return RunStatus.Success;

            return RunStatus.Failure;
        }

        private static void ReleaseSpirit()
        {
            WoWMovement.MoveStop();

            // Check for soulstone
            if (Lua.GetReturnVal<bool>("return HasSoulstone()", 0U))
            {
                Lua.DoString("UseSoulstone()");
                int startTime = System.Environment.TickCount;
                while (ObjectManager.Me != null && !ObjectManager.Me.IsAlive 
                    && System.Environment.TickCount - startTime < 7500)
                {
                    Thread.Sleep(100);
                }
                if (ObjectManager.Me != null && ObjectManager.Me.IsAlive)
                    return;
            }

            // Release spirit (RepopMe)
            if (ObjectManager.Me != null && ObjectManager.Me.Dead && !ObjectManager.Me.IsGhost)
            {
                Lua.DoString("RepopMe()");
                int startTime = System.Environment.TickCount;
                while (System.Environment.TickCount - startTime < 5000 
                    && ((ObjectManager.Me != null && ObjectManager.Me.Dead && !ObjectManager.Me.IsGhost) 
                        || !StyxWoW.IsInGame))
                {
                    Thread.Sleep(200);
                }
            }
        }
    }
}
