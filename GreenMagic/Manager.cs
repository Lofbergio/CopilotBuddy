using System.Collections.Generic;

namespace GreenMagic
{
    /// <summary>
    /// Base manager class for memory operations.
    /// </summary>
    public abstract class Manager<T> where T : IMemoryOperation
    {
        protected internal Memory Win32 { get; set; }
        protected internal Dictionary<string, T> Applications = new Dictionary<string, T>();

        internal Manager(Memory memory)
        {
            Win32 = memory;
        }

        public virtual T this[string name] => Applications[name];

        public virtual void ApplyAll()
        {
            foreach (var kvp in Applications)
            {
                kvp.Value.Apply();
            }
        }

        public virtual void RemoveAll()
        {
            foreach (var kvp in Applications)
            {
                kvp.Value.Remove();
            }
        }

        public virtual void DeleteAll()
        {
            foreach (var kvp in Applications)
            {
                kvp.Value.Dispose();
            }
            Applications.Clear();
        }

        public virtual void Delete(string name)
        {
            if (Applications.TryGetValue(name, out var operation))
            {
                operation.Dispose();
                Applications.Remove(name);
            }
        }

        public bool Contains(string name) => Applications.ContainsKey(name);
    }
}
