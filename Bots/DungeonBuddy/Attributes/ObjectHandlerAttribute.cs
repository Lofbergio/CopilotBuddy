using System;

namespace Bots.DungeonBuddy.Attributes
{
    /// <summary>
    /// Marque une méthode comme handler pour un GameObject spécifique.
    /// Utilisé pour interagir avec des objets du donjon (leviers, portes, etc.)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
    public sealed class ObjectHandlerAttribute : Attribute
    {
        public ObjectHandlerAttribute(int objectEntryId)
            : this(objectEntryId, "", 40)
        {
        }

        public ObjectHandlerAttribute(int objectEntryId, string objectDisplayName)
            : this(objectEntryId, objectDisplayName, 40)
        {
        }

        public ObjectHandlerAttribute(int objectEntryId, string objectDisplayName, int range)
        {
            ObjectEntry = objectEntryId;
            ObjectName = objectDisplayName;
            ObjectRange = range;
        }

        public int ObjectEntry { get; set; }
        public string ObjectName { get; set; }
        public int ObjectRange { get; set; }
        public int ObjectRangeSqr => ObjectRange * ObjectRange;
    }
}