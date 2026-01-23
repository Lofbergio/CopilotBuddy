#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Styx.Helpers;
using Styx.WoWInternals.WoWObjects;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Provides information about the player's group (party/raid).
    /// WotLK 3.3.5a implementation using Lua calls.
    /// </summary>
    public class WoWGroupInfo
    {
        #region Singleton

        private static WoWGroupInfo _instance;

        /// <summary>
        /// Gets the singleton instance.
        /// </summary>
        public static WoWGroupInfo Instance => _instance ?? (_instance = new WoWGroupInfo());

        #endregion

        #region Group State Properties

        /// <summary>
        /// Returns true if the player is in a party (not raid).
        /// </summary>
        public bool IsInParty
        {
            get
            {
                var result = Lua.GetReturnVal<int>("return GetNumPartyMembers() > 0 and 1 or 0", 0);
                return result == 1;
            }
        }

        /// <summary>
        /// Returns true if the player is in a raid.
        /// </summary>
        public bool IsInRaid
        {
            get
            {
                var result = Lua.GetReturnVal<int>("return GetNumRaidMembers() > 0 and 1 or 0", 0);
                return result == 1;
            }
        }

        /// <summary>
        /// Returns true if the player is in any group (party or raid).
        /// </summary>
        public bool IsInGroup => IsInParty || IsInRaid;

        /// <summary>
        /// Returns true if the player is in a battleground group.
        /// </summary>
        public bool IsInBattlegroundParty
        {
            get
            {
                var result = Lua.GetReturnVal<int>("local _,_,_,_,_,_,_,_,_,_,_,bgMap = GetInstanceInfo(); return bgMap and 1 or 0", 0);
                return result == 1 && IsInGroup;
            }
        }

        /// <summary>
        /// Returns true if the player is in a LFG group.
        /// </summary>
        public bool IsInLfgParty
        {
            get
            {
                // WotLK LFG check
                var result = Lua.GetReturnVal<int>("local inLFG = select(1, GetLFGMode()); return inLFG and 1 or 0", 0);
                return result == 1;
            }
        }

        #endregion

        #region Group Size

        /// <summary>
        /// Gets the number of party members (excluding self).
        /// </summary>
        public int NumPartyMembers => Lua.GetReturnVal<int>("return GetNumPartyMembers()", 0);

        /// <summary>
        /// Gets the number of raid members (including self).
        /// </summary>
        public int NumRaidMembers => Lua.GetReturnVal<int>("return GetNumRaidMembers()", 0);

        /// <summary>
        /// Gets the total group size.
        /// </summary>
        public int GroupSize
        {
            get
            {
                if (IsInRaid)
                    return NumRaidMembers;
                if (IsInParty)
                    return NumPartyMembers + 1; // +1 for self
                return 1;
            }
        }

        #endregion

        #region Leader

        /// <summary>
        /// Returns true if the player is the group leader.
        /// </summary>
        public bool IsLeader
        {
            get
            {
                var result = Lua.GetReturnVal<int>("return IsPartyLeader() and 1 or 0", 0);
                return result == 1;
            }
        }

        /// <summary>
        /// Gets the GUID of the group leader.
        /// </summary>
        public ulong GroupLeaderGuid
        {
            get
            {
                if (!IsInGroup)
                    return StyxWoW.Me?.Guid ?? 0;

                // Try to find leader in raid
                if (IsInRaid)
                {
                    foreach (var member in RaidMembers)
                    {
                        if (member.GroupLeader)
                            return member.Guid;
                    }
                }
                // In party, party leader is usually party1 or self
                else if (IsInParty)
                {
                    var result = Lua.GetReturnVal<int>("return IsPartyLeader() and 1 or 0", 0);
                    if (result == 1)
                        return StyxWoW.Me?.Guid ?? 0;

                    // Check party members
                    foreach (var member in PartyMembers)
                    {
                        if (member.GroupLeader)
                            return member.Guid;
                    }
                }

                return StyxWoW.Me?.Guid ?? 0;
            }
        }

        /// <summary>
        /// Gets the group leader as a WoWPlayer.
        /// </summary>
        public WoWPlayer GroupLeader => ObjectManager.GetObjectByGuid<WoWPlayer>(GroupLeaderGuid);

        #endregion

        #region Party Members

        private List<WoWPartyMember> _cachedPartyMembers;
        private DateTime _partyMembersCacheTime;
        private static readonly TimeSpan CacheTimeout = TimeSpan.FromMilliseconds(400);

        /// <summary>
        /// Gets party members (excluding self).
        /// </summary>
        public IEnumerable<WoWPartyMember> PartyMembers
        {
            get
            {
                if (_cachedPartyMembers != null && DateTime.Now - _partyMembersCacheTime < CacheTimeout)
                    return _cachedPartyMembers;

                _cachedPartyMembers = new List<WoWPartyMember>();
                _partyMembersCacheTime = DateTime.Now;

                int numParty = NumPartyMembers;
                for (int i = 1; i <= numParty; i++)
                {
                    var member = WoWPartyMember.FromPartyIndex(i);
                    if (member != null)
                        _cachedPartyMembers.Add(member);
                }

                return _cachedPartyMembers;
            }
        }

        /// <summary>
        /// Gets party member GUIDs.
        /// </summary>
        public ulong[] PartyMemberGuids => PartyMembers.Select(m => m.Guid).ToArray();

        #endregion

        #region Raid Members

        private List<WoWPartyMember> _cachedRaidMembers;
        private DateTime _raidMembersCacheTime;

        /// <summary>
        /// Gets raid members (including self).
        /// </summary>
        public IEnumerable<WoWPartyMember> RaidMembers
        {
            get
            {
                if (_cachedRaidMembers != null && DateTime.Now - _raidMembersCacheTime < CacheTimeout)
                    return _cachedRaidMembers;

                _cachedRaidMembers = new List<WoWPartyMember>();
                _raidMembersCacheTime = DateTime.Now;

                int numRaid = NumRaidMembers;
                for (int i = 1; i <= numRaid; i++)
                {
                    var member = WoWPartyMember.FromRaidIndex(i);
                    if (member != null)
                        _cachedRaidMembers.Add(member);
                }

                return _cachedRaidMembers;
            }
        }

        /// <summary>
        /// Gets raid member GUIDs.
        /// </summary>
        public ulong[] RaidMemberGuids => RaidMembers.Select(m => m.Guid).ToArray();

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets all group members (works for both party and raid).
        /// </summary>
        public IEnumerable<WoWPartyMember> AllMembers
        {
            get
            {
                if (IsInRaid)
                    return RaidMembers;
                if (IsInParty)
                    return PartyMembers;
                return Enumerable.Empty<WoWPartyMember>();
            }
        }

        /// <summary>
        /// Gets all tanks in the group.
        /// </summary>
        public IEnumerable<WoWPartyMember> Tanks => AllMembers.Where(m => m.HasRole(WoWPartyMember.GroupRole.Tank));

        /// <summary>
        /// Gets all healers in the group.
        /// </summary>
        public IEnumerable<WoWPartyMember> Healers => AllMembers.Where(m => m.HasRole(WoWPartyMember.GroupRole.Healer));

        /// <summary>
        /// Invalidates the member cache.
        /// </summary>
        public void InvalidateCache()
        {
            _cachedPartyMembers = null;
            _cachedRaidMembers = null;
        }

        #endregion

        #region Flags Enum

        [Flags]
        public enum GroupFlags : uint
        {
            None = 0,
            Raid = 1,
            Battlegrounds = 2,
            Lfg = 4,
            LfgRestricted = 8,
            LfgSuspended = 16,
            AllowChronieMode = 32,
        }

        #endregion
    }
}
