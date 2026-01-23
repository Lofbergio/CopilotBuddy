using System;

namespace Styx.Logic.Combat
{
    public class WoWPetSpell
    {
        public WoWSpell? Spell { get; private set; }

        public PetSpellType SpellType { get; private set; }

        public PetAction Action { get; private set; }

        public PetStance Stance { get; private set; }

        public bool Cooldown => this.Spell != null && this.Spell.Cooldown;

        public int ActionBarIndex { get; private set; }

        private WoWPetSpell()
        {
            this.ActionBarIndex = -1;
            this.SpellType = PetSpellType.Unknown;
            this.Action = PetAction.None;
            this.Stance = PetStance.None;
        }

        internal WoWPetSpell(uint spellMask, int index)
            : this()
        {
            this.ActionBarIndex = index;
            uint id = spellMask & 16777215U;
            switch (spellMask >> 24 & 63U)
            {
                case 1:
                case 8:
                case 9:
                case 10:
                case 11:
                case 12:
                case 13:
                case 14:
                case 15:
                case 16:
                case 17:
                    this.SpellType = PetSpellType.Spell;
                    this.Spell = WoWSpell.FromId((int)id);
                    break;
                case 6:
                    this.SpellType = PetSpellType.Stance;
                    this.Stance = (PetStance)((int)spellMask & 16777215);
                    break;
                case 7:
                    this.SpellType = PetSpellType.Action;
                    this.Action = (PetAction)((int)spellMask & 16777215);
                    break;
                default:
                    this.SpellType = PetSpellType.Unknown;
                    break;
            }
        }

        public override string ToString()
        {
            switch (this.SpellType)
            {
                case PetSpellType.Unknown:
                    return "Unknown";
                case PetSpellType.Spell:
                    return this.Spell == null ? "Unknown" : this.Spell.Name;
                case PetSpellType.Action:
                    return this.Action == PetAction.MoveTo ? "Move To" : this.Action.ToString();
                case PetSpellType.Stance:
                    return this.Stance.ToString();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public enum PetSpellType
        {
            Unknown,
            Spell,
            Action,
            Stance,
        }

        public enum PetAction
        {
            None = -1,
            Wait = 0,
            Follow = 1,
            Attack = 2,
            Dismiss = 3,
            MoveTo = 4,
        }

        public enum PetStance
        {
            None = -1,
            Passive = 0,
            Defensive = 1,
            Aggressive = 2,
        }
    }
}
