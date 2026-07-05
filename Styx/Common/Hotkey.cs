using System;
using System.Windows.Forms;

namespace Styx.Common
{
    public class Hotkey : IEquatable<Hotkey>
    {
        internal Hotkey(string name, Keys key, ModifierKeys modifierKeys, int id, Action<Hotkey> callback)
        {
            Name = name;
            Key = key;
            ModifierKeys = modifierKeys;
            Id = id;
            Callback = callback;
        }

        public Action<Hotkey> Callback { get; set; }
        public int Id { get; private set; }
        public Keys Key { get; private set; }
        public ModifierKeys ModifierKeys { get; private set; }
        public string Name { get; private set; }
        internal bool IsRegistered { get; set; }

        // ReferenceEquals, NOT ==: the overloaded operator routes through object.Equals → virtual
        // Equals → back here — infinite recursion that stack-overflowed the whole app the moment
        // List<Hotkey>.Remove compared two distinct instances (HotkeysManager.Unregister on bot stop).
        public bool Equals(Hotkey other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Id == other.Id && string.Equals(Name, other.Name);
        }

        public override string ToString()
        {
            return Name + " [" + FormatKeyCombo() + "]";
        }

        private string FormatKeyCombo()
        {
            string text = "";
            if (ModifierKeys.HasFlag(ModifierKeys.Control))
                text += "Ctrl + ";
            if (ModifierKeys.HasFlag(ModifierKeys.Alt))
                text += "Alt + ";
            if (ModifierKeys.HasFlag(ModifierKeys.Shift))
                text += "Shift + ";
            return text + Key;
        }

        public override bool Equals(object obj)
        {
            return obj is Hotkey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Id * 397) ^ ((Name != null) ? Name.GetHashCode() : 0);
        }

        public static bool operator ==(Hotkey left, Hotkey right)
        {
            return ReferenceEquals(left, right) || (left is not null && left.Equals(right));
        }

        public static bool operator !=(Hotkey left, Hotkey right)
        {
            return !(left == right);
        }
    }
}
