#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Styx.Helpers
{
    /// <summary>
    /// Extension methods for Dictionary collections.
    /// </summary>
    public static class DictionaryExtensions
    {
        /// <summary>
        /// Removes all key-value pairs from the dictionary where the value matches the predicate.
        /// </summary>
        /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
        /// <param name="dictionary">The dictionary to remove items from.</param>
        /// <param name="predicate">The condition to match values for removal.</param>
        public static void RemoveAll<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, Func<TValue, bool> predicate)
        {
            var keysToRemove = dictionary.Keys
                .Where(key => predicate(dictionary[key]))
                .ToList();

            foreach (var key in keysToRemove)
            {
                dictionary.Remove(key);
            }
        }

        /// <summary>
        /// Removes all key-value pairs from the dictionary where the key-value pair matches the predicate.
        /// </summary>
        /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
        /// <param name="dictionary">The dictionary to remove items from.</param>
        /// <param name="predicate">The condition to match key-value pairs for removal.</param>
        public static void RemoveAll<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, Func<TKey, TValue, bool> predicate)
        {
            var keysToRemove = dictionary
                .Where(kvp => predicate(kvp.Key, kvp.Value))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                dictionary.Remove(key);
            }
        }

        /// <summary>
        /// Gets a value from the dictionary or adds a default value if the key doesn't exist.
        /// </summary>
        /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
        /// <param name="dictionary">The dictionary.</param>
        /// <param name="key">The key to look up.</param>
        /// <param name="defaultValueFactory">A function that creates the default value if the key doesn't exist.</param>
        /// <returns>The existing value or the newly added default value.</returns>
        public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, Func<TValue> defaultValueFactory)
        {
            if (!dictionary.TryGetValue(key, out TValue value))
            {
                value = defaultValueFactory();
                dictionary[key] = value;
            }
            return value;
        }
    }
}
