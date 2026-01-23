using System;
using System.Collections.Generic;

namespace Styx.Helpers
{
	public class CircularQueue<T> : Queue<T>
	{
		public event EndOfQueueHandler? OnEndOfQueue;
		public event StartOfQueueHandler? OnStartOfQueue;

		public T? First { get; private set; }

		public new T Dequeue()
		{
			T item = base.Dequeue();
			Enqueue(item);

			if (item != null && item.Equals(First) && OnEndOfQueue != null)
			{
				OnEndOfQueue(new object(), EventArgs.Empty);
			}

			T? first = First;
			if (first != null && first.Equals(Peek()) && OnStartOfQueue != null)
			{
				OnStartOfQueue(new object(), EventArgs.Empty);
			}

			return item;
		}

		public new void Enqueue(T item)
		{
			if (First == null)
			{
				First = item;
			}
			base.Enqueue(item);
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

		public delegate void EndOfQueueHandler(object sender, EventArgs e);
		public delegate void StartOfQueueHandler(object sender, EventArgs e);
	}
}
