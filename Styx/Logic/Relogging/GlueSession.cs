using System;
using System.Collections.Generic;
using System.Linq;
using Styx.Helpers;
using Styx.WoWInternals;

namespace Styx.Logic.Relogging
{
    /// <summary>
    /// Snapshot of the glue (login) screen state, taken via one injected Lua roundtrip.
    /// </summary>
    public sealed class GlueSnapshot
    {
        public GlueScreen Screen { get; init; } = GlueScreen.Unknown;
        public bool DialogShown { get; init; }
        public string DialogText { get; init; } = "";
        public string DialogWhich { get; init; } = "";
        public int NumCharacters { get; init; }
        public IReadOnlyList<string> CharacterNames { get; init; } = Array.Empty<string>();
        public string ServerName { get; init; } = "";
        public DateTime TakenUtc { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Queries and drives the WoW glue (login) screen through injected Lua.
    /// Glue-safe: FrameScript_Load/PCall work at the login screen (same Lua state as in-game),
    /// and every referenced global is nil-guarded because GlueXML is only loaded at glue.
    /// State detection is by frame visibility (AccountLogin/RealmList/CharacterSelect/CharacterCreate)
    /// rather than memory offsets — verifiable against the live client, nothing asserted from memory.
    /// </summary>
    public static class GlueSession
    {
        private static GlueSnapshot _cached = new GlueSnapshot { TakenUtc = DateTime.MinValue };

        // One script returns everything: screen, dialog state, char list, server name.
        private const string SnapshotScript = @"
local s='unknown'
if AccountLogin and AccountLogin:IsShown() then s='login'
elseif RealmList and RealmList:IsShown() then s='realmlist'
elseif CharacterCreate and CharacterCreate:IsShown() then s='charcreate'
elseif CharacterSelect and CharacterSelect:IsShown() then s='charselect' end
local d,dt,dw=0,'',''
if GlueDialog and GlueDialog:IsShown() then
  d=1
  dt=(GlueDialogText and GlueDialogText:GetText()) or ''
  dw=tostring(GlueDialog.which or '')
end
local n,names=0,''
if s=='charselect' and GetNumCharacters then
  n=GetNumCharacters()
  for i=1,n do local nm=GetCharacterInfo(i); names=names..(nm or '?')..'|' end
end
local sv=(GetServerName and GetServerName()) or ''
return s,d,dt,dw,n,names,sv";

        /// <summary>
        /// Returns the current glue state, re-querying at most once per maxAgeMs.
        /// Returns an Unknown snapshot when in-game or when the executor is unavailable.
        /// </summary>
        public static GlueSnapshot Query(int maxAgeMs = 1000)
        {
            if (ObjectManager.Wow == null || ObjectManager.IsInGame)
                return new GlueSnapshot();

            if ((DateTime.UtcNow - _cached.TakenUtc).TotalMilliseconds < maxAgeMs)
                return _cached;

            var vals = Lua.GetReturnValues(SnapshotScript);
            if (vals == null || vals.Count < 7)
            {
                // Executor unavailable or script failed — keep Unknown; caller's dwell timeout handles persistence.
                _cached = new GlueSnapshot();
                return _cached;
            }

            _cached = new GlueSnapshot
            {
                Screen = ParseScreen(vals[0]),
                DialogShown = vals[1] == "1",
                DialogText = vals[2],
                DialogWhich = vals[3],
                NumCharacters = int.TryParse(vals[4], out int n) ? n : 0,
                CharacterNames = vals[5].Split('|', StringSplitOptions.RemoveEmptyEntries),
                ServerName = vals[6],
            };
            return _cached;
        }

        private static GlueScreen ParseScreen(string s) => s switch
        {
            "login" => GlueScreen.Login,
            "realmlist" => GlueScreen.RealmList,
            "charselect" => GlueScreen.CharSelect,
            "charcreate" => GlueScreen.CharCreate,
            _ => GlueScreen.Unknown,
        };

        /// <summary>Submits account credentials on the login screen.</summary>
        public static void Login(string account, string password)
        {
            Lua.DoString(string.Format("if DefaultServerLogin then DefaultServerLogin('{0}','{1}') end",
                Lua.Escape(account), Lua.Escape(password)));
            Invalidate();
        }

        /// <summary>Clicks the first glue dialog button (Okay/Cancel) to dismiss an error dialog.</summary>
        public static void DismissDialog()
        {
            Lua.DoString("if GlueDialogButton1 and GlueDialogButton1:IsShown() then GlueDialogButton1:Click() end");
            Invalidate();
        }

        /// <summary>
        /// On the realm list, picks the realm by name (empty = first realm found). Returns true if a realm was clicked.
        /// </summary>
        public static bool SelectRealm(string realmName)
        {
            var vals = Lua.GetReturnValues(string.Format(@"
local target='{0}'
local picked=''
if GetRealmCategories and GetNumRealms and GetRealmInfo and ChangeRealm then
  local cats=GetRealmCategories()
  if type(cats)=='table' then cats=#cats end
  for c=1,(cats or 0) do
    for i=1,(GetNumRealms(c) or 0) do
      local name=GetRealmInfo(c,i)
      if picked=='' and name and (target=='' or name==target) then ChangeRealm(c,i); picked=name end
    end
  end
end
return picked", Lua.Escape(realmName)));
            string picked = vals != null && vals.Count > 0 ? vals[0] : "";
            Invalidate();
            if (picked.Length > 0)
            {
                Logging.Write("[Relogger] Selected realm '{0}'.", picked);
                return true;
            }
            return false;
        }

        /// <summary>
        /// At character select, picks the character by name (empty = keep current selection) and enters world.
        /// Returns false when a name was configured but not found on the list.
        /// </summary>
        public static bool EnterWorld(string characterName)
        {
            var vals = Lua.GetReturnValues(string.Format(@"
local target='{0}'
local n=(GetNumCharacters and GetNumCharacters()) or 0
local idx=0
if target~='' then
  for i=1,n do local nm=GetCharacterInfo(i); if nm and nm:lower()==target:lower() then idx=i end end
  if idx==0 then return 'notfound',n end
  if CharacterSelect_SelectCharacter then CharacterSelect_SelectCharacter(idx) else SelectCharacter(idx) end
end
if EnterWorld then EnterWorld() end
return 'ok',n", Lua.Escape(characterName)));
            Invalidate();
            if (vals != null && vals.Count > 0 && vals[0] == "notfound")
            {
                Logging.Write(System.Windows.Media.Colors.Red,
                    "[Relogger] Character '{0}' not found at character select ({1} on list).",
                    characterName, vals.Count > 1 ? vals[1] : "?");
                return false;
            }
            return true;
        }

        private static void Invalidate() => _cached = new GlueSnapshot { TakenUtc = DateTime.MinValue };
    }
}
