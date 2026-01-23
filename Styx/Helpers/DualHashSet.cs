#nullable disable
using System.Collections;
using System.Collections.Generic;

namespace Styx.Helpers
{
    /// <summary>
    /// A collection that contains two HashSets of different types.
    /// Allows checking membership against either type.
    /// </summary>
    public class DualHashSet<T1, T2> : IEnumerable<object>
    {
        /// <summary>
        /// Gets the first HashSet.
        /// </summary>
        public HashSet<T1> HashSet1 { get; private set; }

        /// <summary>
        /// Gets the second HashSet.
        /// </summary>
        public HashSet<T2> HashSet2 { get; private set; }

        /// <summary>
        /// Gets the total count of items in both sets.
        /// </summary>
        public int Count => HashSet1.Count + HashSet2.Count;

        /// <summary>
        /// Creates a new DualHashSet.
        /// </summary>
        public DualHashSet()
        {
            HashSet1 = new HashSet<T1>();
            HashSet2 = new HashSet<T2>();
        }

        /// <summary>
        /// Adds an item of type T1.
        /// </summary>
        public bool Add(T1 item) => HashSet1.Add(item);

        /// <summary>
        /// Adds an item of type T2.
        /// </summary>
        public bool Add(T2 item) => HashSet2.Add(item);

        /// <summary>
        /// Adds a pair of items.
        /// </summary>
        public void Add(ValuePair<T1, T2> item)
        {
            HashSet1.Add(item.Value1);
            HashSet2.Add(item.Value2);
        }

        /// <summary>
        /// Removes an item of type T1.
        /// </summary>
        public bool Remove(T1 item) => HashSet1.Remove(item);

        /// <summary>
        /// Removes an item of type T2.
        /// </summary>
        public bool Remove(T2 item) => HashSet2.Remove(item);

        /// <summary>
        /// Checks if an item of type T1 exists.
        /// </summary>
        public bool Contains(T1 item) => HashSet1.Contains(item);

        /// <summary>
        /// Checks if an item of type T2 exists.
        /// </summary>
        public bool Contains(T2 item) => HashSet2.Contains(item);

        /// <summary>
        /// Checks if either value in the pair exists.
        /// </summary>
        public bool Contains(ValuePair<T1, T2> vp)
        {
            return Contains(vp.Value1) || Contains(vp.Value2);
        }

        /// <summary>
        /// Clears both hash sets.
        /// </summary>
        public void Clear()
        {
            HashSet1.Clear();
            HashSet2.Clear();
        }

        public IEnumerator<object> GetEnumerator()
        {
            foreach (var item in HashSet1)
                yield return item;
            foreach (var item in HashSet2)
                yield return item;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
