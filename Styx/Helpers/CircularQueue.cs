using System;
using System.Collections.Generic;
using System.Linq;
using Styx.Helpers;

namespace Styx.Helpers
{
    /// <summary>
    /// Path traversal mode for CircularQueue.
    /// Circle: indefinitely loops  1→2→3→1→2→3→...
    /// Bounce: reverses at each end 1→2→3→2→1→2→3→...
    /// </summary>
    public enum QueueMode { Circle, Bounce }

    public class CircularQueue<T> : Queue<T>
	{
		public event EndOfQueue? OnEndOfQueue;
		public event StartOfQueue? OnStartOfQueue;

		public T? First { get; private set; }

        // ── Bounce state ──────────────────────────────────────────────
        /// <summary>
        /// Controls whether the queue circles or bounces. Set before first Dequeue.
        /// </summary>
        public QueueMode Mode { get; set; } = QueueMode.Circle;

        private List<T>? _bounceList;   // snapshot of items in insertion order
        private int      _bounceIndex;  // current position in _bounceList
        private bool     _bounceForward = true;
        // ──────────────────────────────────────────────────────────────

        public new T Dequeue()
		{
            if (Mode == QueueMode.Bounce)
                return BounceDequeue();

            try
			{
				T item = base.Dequeue();
				Enqueue(item);

				// Fire OnStartOfQueue when we've cycled back to the first item
				if (item != null && item.Equals(First) && OnStartOfQueue != null)
				{
					OnStartOfQueue(new object(), EventArgs.Empty);
				}

				// Fire OnEndOfQueue when First is at the end (equals Peek, which is next)
				T? first = First;
				if (first != null && first.Equals(Peek()) && OnEndOfQueue != null)
				{
					OnEndOfQueue(new object(), EventArgs.Empty);
				}

				return item;
			}
			catch (Exception ex)
			{
				Logging.Write("CircularQueue Dequeue error: {0}", ex.Message);
				throw;
			}
		}

        private T BounceDequeue()
        {
            // Lazy-initialise snapshot from current queue contents on first bounce call.
            if (_bounceList == null || _bounceList.Count == 0)
            {
                _bounceList   = new List<T>(this.ToArray());
                _bounceIndex  = 0;
                _bounceForward = true;
            }

            if (_bounceList.Count == 0)
                throw new InvalidOperationException("CircularQueue is empty.");

            T item = _bounceList[_bounceIndex];

            // Advance index, reversing direction at each end.
            if (_bounceList.Count == 1)
            {
                // Nothing to advance — single-element list.
            }
            else if (_bounceForward)
            {
                if (_bounceIndex < _bounceList.Count - 1)
                    _bounceIndex++;
                else
                {
                    // Hit the end — reverse and step back one.
                    _bounceForward = false;
                    _bounceIndex--;
                }
            }
            else
            {
                if (_bounceIndex > 0)
                    _bounceIndex--;
                else
                {
                    // Hit the start — reverse and step forward one.
                    _bounceForward = true;
                    _bounceIndex++;
                }
            }

            return item;
        }

        /// <summary>
        /// Returns the next item without advancing. Bounce-aware.
        /// </summary>
        public new T Peek()
        {
            if (Mode == QueueMode.Bounce && _bounceList != null && _bounceList.Count > 0)
                return _bounceList[_bounceIndex];
            return base.Peek();
        }

        /// <summary>
        /// Total item count. In bounce mode driven by the snapshot list.
        /// </summary>
        public new int Count =>
            (Mode == QueueMode.Bounce && _bounceList != null) ? _bounceList.Count : base.Count;

        public new void Enqueue(T item)
		{
			// Check if First is default/null (works for both value and reference types)
			object? obj = First;
			T? defaultValue = default;
			if (obj?.Equals(defaultValue) ?? true)
			{
				First = item;
			}
			base.Enqueue(item);

            // Keep bounce snapshot in sync when items are added before first Dequeue.
            if (Mode == QueueMode.Bounce && _bounceList != null)
                _bounceList.Add(item);
		}

		public void Add(T item)
		{
			Enqueue(item);
		}

		public void CycleTo(T item)
		{
            if (Mode == QueueMode.Bounce)
            {
                // In bounce mode, set the bounce index to point at the requested item.
                if (_bounceList == null || _bounceList.Count == 0)
                    _bounceList = new List<T>(this.ToArray());
                int idx = _bounceList.IndexOf(item);
                if (idx >= 0) { _bounceIndex = idx; _bounceForward = true; }
                return;
            }

            T current = Peek();
			for (int i = 0; i < Count; i++)
			{
				if (current != null && current.Equals(item))
				{
					return;
				}
				Dequeue();
				current = Peek();
			}
			Dequeue();
		}

        /// <summary>
        /// Resets the bounce traversal to the beginning of the list.
        /// </summary>
        public void ResetBounce()
        {
            _bounceIndex  = 0;
            _bounceForward = true;
        }

		public CircularQueue()
		{
		}

		public delegate void EndOfQueue(object sender, EventArgs e);
		public delegate void StartOfQueue(object sender, EventArgs e);
	}
}
