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
        /// <summary>WoW's own `GlueParent.currentScreen` string — often meaningful when the
        /// frame-visibility `Screen` reads Unknown (e.g. mid-load "charselect", "login").</summary>
        public string CurrentScreen { get; init; } = "";
        /// <summary>Name of the character currently selected at char select (the one EnterWorld
        /// would enter), so a log can prove the RIGHT character was chosen.</summary>
        public string SelectedName { get; init; } = "";
        public DateTime TakenUtc { get; init; } = DateTime.UtcNow;

        /// <summary>One-line, human-readable summary of everything CB observed this tick —
        /// the diagnostic the relogger prints so a "stuck at Unknown" span isn't a black box.</summary>
        public string Describe()
        {
            var parts = new System.Collections.Generic.List<string> { "screen=" + Screen };
            if (CurrentScreen.Length > 0) parts.Add("glueScreen=" + CurrentScreen);
            parts.Add(DialogShown
                ? string.Format("dialog=\"{0}\" (which={1})", DialogText, DialogWhich)
                : "no dialog");
            if (Screen == GlueScreen.CharSelect)
                parts.Add(string.Format("chars={0}[{1}] selected=\"{2}\"",
                    NumCharacters, string.Join(",", CharacterNames), SelectedName));
            if (ServerName.Length > 0) parts.Add("server=" + ServerName);
            return string.Join(", ", parts);
        }
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
        // OVERLAYS FIRST: RealmList (and CharacterCreate) render on TOP of a still-IsShown backdrop
        // frame — with AccountLogin checked first, a realm list open over the login screen read as
        // 'login' and the relogger typed credentials at realm select instead of picking the realm
        // (07:06:42 in log 2026-07-04_0013, the 3am half-up server: auth up, realm still down).
        private const string SnapshotScript = @"
local s='unknown'
if RealmList and RealmList:IsShown() then s='realmlist'
elseif CharacterCreate and CharacterCreate:IsShown() then s='charcreate'
elseif CharacterSelect and CharacterSelect:IsShown() then s='charselect'
elseif AccountLogin and AccountLogin:IsShown() then s='login' end
local d,dt,dw=0,'',''
if GlueDialog and GlueDialog:IsShown() then
  d=1
  dt=(GlueDialogText and GlueDialogText:GetText()) or ''
  dw=tostring(GlueDialog.which or '')
end
local n,names=0,''
local sel=''
if s=='charselect' and GetNumCharacters then
  n=GetNumCharacters()
  for i=1,n do local nm=GetCharacterInfo(i); names=names..(nm or '?')..'|' end
  local ci=(CharacterSelect and CharacterSelect.selectedIndex) or 0
  if ci>=1 then sel=GetCharacterInfo(ci) or '' end
end
local sv=(GetServerName and GetServerName()) or ''
-- WoW's own screen tracker: often names the real screen ('charselect','login') while the
-- frame-visibility check above still reads 'unknown' during a load/transition.
local cs=(GlueParent and GlueParent.currentScreen) or ''
return s,d,dt,dw,n,names,sv,cs,sel";

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
                CurrentScreen = vals.Count > 7 ? vals[7] : "",
                SelectedName = vals.Count > 8 ? vals[8] : "",
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

        public enum EnterWorldResult { Sent, Selecting, ListEmpty, NotFound }

        /// <summary>
        /// At character select, picks the character by name (empty = keep current selection) and enters world.
        ///
        /// Selecting a character is a SERVER ROUND-TRIP. Calling CharacterSelect_SelectCharacter and
        /// EnterWorld in the same Lua chunk sends the login before the selection is acknowledged: the
        /// server rejects it and the client drops back to character select, forever. That never showed up
        /// while CharacterName was empty (no selection call at all — the standalone default), and it broke
        /// the moment Warband started configuring a character per box. So: select, return Selecting, and
        /// let the next relogger tick (~2s later) send EnterWorld against the settled selection.
        ///
        /// ListEmpty ≠ NotFound: during a worldserver boot the char list populates ASYNCHRONOUSLY (and stays
        /// empty while the world is still down) — reading that as "character doesn't exist" turned every 3am
        /// server restart into a terminal GiveUp for a named-character config. Only a POPULATED list that
        /// lacks the name is a real NotFound.
        /// </summary>
        public static EnterWorldResult EnterWorld(string characterName)
        {
            // On this server, explicitly calling CharacterSelect_SelectCharacter(idx) before
            // EnterWorld() gets the world entry SILENTLY REJECTED — the client goes to the
            // loading screen and bounces straight back to character select, no dialog (verified
            // 2026-07-10 20:11 Warband-managed vs 20:12 manual). A plain EnterWorld() on the
            // client's DEFAULT selection works every time (that's what the manual "no character
            // configured" path does). So: only force a selection when the account has MORE than
            // one character AND ours isn't already the client's current selection. For a
            // single-character account the default IS our character — enter directly, exactly
            // like the working manual path.
            var vals = Lua.GetReturnValues(string.Format(@"
local target='{0}'
local n=(GetNumCharacters and GetNumCharacters()) or 0
if target~='' then
  if n==0 then return 'empty',n,'' end
  local idx=0
  for i=1,n do local nm=GetCharacterInfo(i); if nm and nm:lower()==target:lower() then idx=i end end
  if idx==0 then return 'notfound',n,'' end
  if n>1 then
    local cur=(CharacterSelect and CharacterSelect.selectedIndex) or 0
    local curname=(cur>=1 and GetCharacterInfo(cur)) or ''
    if curname:lower()~=target:lower() then
      if CharacterSelect_SelectCharacter then CharacterSelect_SelectCharacter(idx) else SelectCharacter(idx) end
      return 'selecting',n,curname
    end
  end
end
return 'ready',n,''", Lua.Escape(characterName)));
            Invalidate();
            string r = vals != null && vals.Count > 0 ? vals[0] : "ready";
            if (r == "empty")
                return EnterWorldResult.ListEmpty;
            if (r == "selecting")
                return EnterWorldResult.Selecting;
            if (r == "notfound")
            {
                Logging.Write(System.Windows.Media.Colors.Red,
                    "[Relogger] Character '{0}' not found at character select ({1} on list).",
                    characterName, vals.Count > 1 ? vals[1] : "?");
                return EnterWorldResult.NotFound;
            }
            // The right character is selected. Enter the world the HUMAN way — press Enter so the
            // client runs its own CharacterSelect_EnterWorld() on its input path. Injecting
            // EnterWorld() via the executor is what this server rejects mid-zone-in.
            KeyboardManager.PressEnter();
            return EnterWorldResult.Sent;
        }

        private static void Invalidate() => _cached = new GlueSnapshot { TakenUtc = DateTime.MinValue };
    }
}
