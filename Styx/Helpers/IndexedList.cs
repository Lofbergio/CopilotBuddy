using System;
using System.Collections.Generic;

namespace Styx.Helpers
{
    public class IndexedList<T> : List<T>
    {
        private int _index;

        public IndexedList(bool isCyclic = false)
        {
            IsCyclic = isCyclic;
        }

        public IndexedList(int capacity, bool isCyclic = false)
            : base(capacity)
        {
            IsCyclic = isCyclic;
        }

        public IndexedList(IEnumerable<T> collection, bool isCyclic = false)
            : base(collection)
        {
            IsCyclic = isCyclic;
        }

        public bool IsCyclic { get; set; }

        public int Index
        {
            get
            {
                Clamp();
                return _index;
            }
            set
            {
                _index = value;
                Clamp();
            }
        }

        public T? CurrentOrDefault
        {
            get
            {
                int i = Index;
                return i >= Count ? default : this[i];
            }
        }

        public T Current => this[Index];

        public void Next() { Index++; }
        public void Previous() { Index--; }

        private void Clamp()
        {
            if (_index == 0 || (_index >= 0 && _index < Count))
                return;

            if (Count == 0)
            {
                _index = 0;
                return;
            }

            if (IsCyclic)
            {
                if (_index < 0)
                {
                    int wrap = -_index - 1;
                    wrap %= Count;
                    _index = Count - 1 - wrap;
                }
                else
                {
                    _index %= Count;
                }
            }
            else
            {
                _index = Math.Max(0, Math.Min(Count - 1, _index));
            }
        }
    }
}
