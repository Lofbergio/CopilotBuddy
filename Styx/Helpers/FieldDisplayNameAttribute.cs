using System;
using System.ComponentModel;

namespace Styx.Helpers
{
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class FieldDisplayNameAttribute : DisplayNameAttribute
    {
        public FieldDisplayNameAttribute(string displayName)
            : base(displayName)
        {
        }
    }
}
