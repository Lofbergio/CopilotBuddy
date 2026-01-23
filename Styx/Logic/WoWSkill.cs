using System;
using System.Runtime.InteropServices;
using GreenMagic;
using Styx.Patchables;
using Styx.WoWInternals;

namespace Styx.Logic
{
    /// <summary>
    /// Represents a skill known by the player.
    /// Ported from HonorBuddy 3.3.5a.
    /// </summary>
    public class WoWSkill
    {
        #region Internal Structure

        /// <summary>
        /// Memory layout for skill data in 3.3.5a (12 bytes).
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct SkillInfo
        {
            public ushort SkillId;       // 0x00 - Skill line ID
            public ushort Unknown1;      // 0x02 - Unknown
            public ushort CurrentValue;  // 0x04 - Current skill value
            public ushort MaxValue;      // 0x06 - Maximum skill value
            public short Modifier;       // 0x08 - Skill modifier
            public ushort Bonus;         // 0x0A - Skill bonus
        }

        #endregion

        #region Fields

        private readonly long _address;
        private readonly SkillInfo _data;
        private readonly WoWDb.Row? _row;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new WoWSkill from a memory address.
        /// </summary>
        public WoWSkill(uint ptr)
        {
            _address = ptr;

            if (_address != 0)
            {
                Memory? wow = ObjectManager.Wow;
                if (wow != null)
                {
                    _data = wow.Read<SkillInfo>(ptr);

                    if (IsValid)
                    {
                        _row = StyxWoW.Db[ClientDb.SkillLine].GetRow(_data.SkillId);
                    }
                }
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets whether this skill is valid (has a skill ID).
        /// </summary>
        public bool IsValid => _data.SkillId != 0;

        /// <summary>
        /// Gets the skill line ID.
        /// </summary>
        public int Id
        {
            get
            {
                if (_row == null) return 0;
                return _row.GetField<int>(0);
            }
        }

        /// <summary>
        /// Gets the skill line ID directly from memory.
        /// </summary>
        public int SkillLineId => _data.SkillId;

        /// <summary>
        /// Gets the category ID of this skill.
        /// </summary>
        public int CategoryId
        {
            get
            {
                if (_row == null) return 0;
                return _row.GetField<int>(1);
            }
        }

        /// <summary>
        /// Gets the name of this skill.
        /// </summary>
        public string Name
        {
            get
            {
                if (_row == null) return $"Skill_{_data.SkillId}";
                return _row.GetField<string>(3) ?? $"Skill_{_data.SkillId}";
            }
        }

        /// <summary>
        /// Gets the spell icon ID for this skill.
        /// </summary>
        public int SpellIcon
        {
            get
            {
                if (_row == null) return 0;
                return _row.GetField<int>(37);
            }
        }

        /// <summary>
        /// Gets whether this skill can be linked in chat.
        /// </summary>
        public bool CanLink
        {
            get
            {
                if (_row == null) return false;
                return _row.GetField<int>(55) != 0;
            }
        }

        /// <summary>
        /// Gets the maximum value for this skill.
        /// </summary>
        public int MaxValue => _data.MaxValue;

        /// <summary>
        /// Gets the current value for this skill.
        /// </summary>
        public int CurrentValue => _data.CurrentValue;

        /// <summary>
        /// Gets the bonus value for this skill.
        /// </summary>
        public uint Bonus => _data.Bonus;

        /// <summary>
        /// Gets the modifier for this skill.
        /// </summary>
        public short Modifier => _data.Modifier;

        /// <summary>
        /// Gets the current value plus bonus.
        /// </summary>
        public int CurrentValueWithBonus => CurrentValue + (int)Bonus;

        #endregion

        #region Methods

        public override string ToString()
        {
            return $"{Name}: {CurrentValue}/{MaxValue} (+{Bonus})";
        }

        #endregion
    }
}
