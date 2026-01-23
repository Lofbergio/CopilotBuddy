using System;
using System.Collections.Generic;

namespace Styx.Helpers
{
	/// <summary>
	/// Dictionary that organizes items by range.
	/// </summary>
	public class RangedDictionary<T> : Dictionary<Range, List<T>> where T : IRangeAble
	{
		/// <summary>
		/// Gets the total count of values across all ranges.
		/// </summary>
		public int ValueCount
		{
			get
			{
				int count = 0;
				foreach (var list in Values)
				{
					count += list.Count;
				}
				return count;
			}
		}

		/// <summary>
		/// Gets all values from all ranges.
		/// </summary>
		public List<T> GetAllValues()
		{
			var result = new List<T>();
			foreach (var list in Values)
			{
				result.AddRange(list);
			}
			return result;
		}

		/// <summary>
		/// Adds an item to the dictionary using its range.
		/// </summary>
		public void Add(T item)
		{
			var range = item.GetRange();
			if (ContainsKey(range))
			{
				this[range].Add(item);
			}
			else
			{
				var list = new List<T> { item };
				Add(range, list);
			}
		}

		/// <summary>
		/// Removes all items matching the predicate.
		/// </summary>
		public int RemoveWhereValue(Predicate<T> predicate)
		{
			int count = 0;
			foreach (var kvp in this)
			{
				count += kvp.Value.RemoveAll(predicate);
			}
			return count;
		}

		/// <summary>
		/// Removes a specific item from the dictionary.
		/// </summary>
		public bool Remove(T item)
		{
			var range = item.GetRange();
			if (ContainsKey(range))
			{
				return this[range].Remove(item);
			}
			return false;
		}

		/// <summary>
		/// Removes an item at a specific index from a range.
		/// </summary>
		public void RemoveIndexFromRange(Range range, int index)
		{
			if (ContainsKey(range))
			{
				this[range].RemoveAt(index);
			}
		}

		/// <summary>
		/// Checks if the dictionary contains a specific item.
		/// </summary>
		public bool Contains(T item)
		{
			var range = item.GetRange();
			if (ContainsKey(range))
			{
				return this[range].Contains(item);
			}
			return false;
		}

		/// <summary>
		/// Gets all items within a larger range (spanning multiple unit ranges).
		/// </summary>
		public List<T> GetByBigRange(Range range)
		{
			var result = new List<T>();
			for (int i = range.Lower; i < range.Higher; i++)
			{
				var unitRange = new Range(i, i + 1);
				if (ContainsKey(unitRange))
				{
					result.AddRange(this[unitRange]);
				}
			}
			return result;
		}
	}
}
