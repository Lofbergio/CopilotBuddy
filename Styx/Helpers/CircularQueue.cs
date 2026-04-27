using System;
using System.Collections.Generic;
using Styx.Helpers;

namespace Styx.Helpers
{
	public class CircularQueue<T> : Queue<T>
	{
		public event EndOfQueue? OnEndOfQueue;
		public event StartOfQueue? OnStartOfQueue;

		public T? First { get; private set; }

		public new T Dequeue()
		{
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
		}

		public void Add(T item)
		{
			Enqueue(item);
		}

		public void CycleTo(T item)
		{
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

		public CircularQueue()
		{
		}

		public delegate void EndOfQueue(object sender, EventArgs e);
		public delegate void StartOfQueue(object sender, EventArgs e);
	}
}
