#nullable disable
using System.Collections.Generic;

namespace Styx.Helpers
{
    /// <summary>
    /// A queue with a fixed capacity that automatically dequeues old items when full.
    /// </summary>
    /// <typeparam name="T">The type of elements in the queue.</typeparam>
    public class CapacityQueue<T> : Queue<T>
    {
        /// <summary>
        /// Gets the maximum capacity of the queue.
        /// </summary>
        public int Capacity { get; private set; }

        /// <summary>
        /// Initializes a new instance of the CapacityQueue class with the specified capacity.
        /// </summary>
        /// <param name="capacity">The maximum number of elements the queue can hold.</param>
        public CapacityQueue(int capacity) : base(capacity)
        {
            Capacity = capacity;
        }

        /// <summary>
        /// Adds an item to the queue. If the queue is at capacity, the oldest item is removed.
        /// </summary>
        /// <param name="item">The item to add.</param>
        public new void Enqueue(T item)
        {
            if (Count + 1 >= Capacity)
            {
                Dequeue();
            }
            base.Enqueue(item);
        }
    }
}
