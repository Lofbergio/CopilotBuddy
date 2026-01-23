using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Styx.Helpers
{
    /// <summary>
    /// Extension methods for HB 3.3.5a
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Converts IntPtr to uint
        /// </summary>
        public static uint ToUInt32(this IntPtr val)
        {
            return (uint)val.ToInt32();
        }

        /// <summary>
        /// Converts IntPtr to ulong
        /// </summary>
        public static ulong ToUInt64(this IntPtr val)
        {
            return (ulong)val.ToInt64();
        }

        /// <summary>
        /// Converts string to int (returns 0 on failure)
        /// </summary>
        public static int ToInt32(this string value)
        {
            int result;
            int.TryParse(value, out result);
            return result;
        }

        /// <summary>
        /// Converts string to uint (returns 0 on failure)
        /// </summary>
        public static uint ToUInt32(this string value)
        {
            uint result;
            uint.TryParse(value, out result);
            return result;
        }

        /// <summary>
        /// Converts string to bool (returns false on failure)
        /// </summary>
        public static bool ToBoolean(this string value)
        {
            bool result;
            bool.TryParse(value, out result);
            return result;
        }

        /// <summary>
        /// Converts string to float (returns 0f on failure)
        /// </summary>
        public static float ToFloat(this string value)
        {
            float result;
            float.TryParse(value, out result);
            return result;
        }

        /// <summary>
        /// Converts float to invariant culture string
        /// </summary>
        public static string ToStringInvariant(this float value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Converts double to invariant culture string
        /// </summary>
        public static string ToStringInvariant(this double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Converts byte array to UTF8 string (null-terminated)
        /// </summary>
        public static string ToUTF8String(this byte[] arr)
        {
            return arr.ToUTF8String(0);
        }

        /// <summary>
        /// Converts byte array to UTF8 string from startIndex
        /// </summary>
        public static string ToUTF8String(this byte[] arr, int startIndex)
        {
            return arr.ToUTF8String(startIndex, arr.Length - startIndex);
        }

        /// <summary>
        /// Converts byte array to UTF8 string with max count (null-terminated)
        /// </summary>
        public static string ToUTF8String(this byte[] arr, int startIndex, int maxCount)
        {
            int length = maxCount;
            int index = startIndex;
            
            for (int i = 0; i < maxCount; i++)
            {
                if (arr[index] == 0)
                {
                    length = i;
                    break;
                }
                index++;
            }
            
            return Encoding.UTF8.GetString(arr, startIndex, length);
        }

        /// <summary>
        /// Finds index of value in array
        /// </summary>
        public static int IndexOf<T>(this T[] arr, T val)
        {
            return arr.IndexOf(val, 0);
        }

        /// <summary>
        /// Finds index of value in array from startIndex
        /// </summary>
        public static int IndexOf<T>(this T[] arr, T val, int startIndex)
        {
            return arr.IndexOf(val, startIndex, arr.Length - startIndex);
        }

        /// <summary>
        /// Finds index of value in array with max count
        /// </summary>
        public static int IndexOf<T>(this T[] arr, T val, int startIndex, int maxCount)
        {
            for (int i = 0; i < maxCount; i++)
            {
                if (EqualityComparer<T>.Default.Equals(arr[startIndex + i], val))
                {
                    return startIndex + i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Finds index of value in array with max count and fail return value
        /// </summary>
        public static int IndexOf<T>(this T[] arr, T val, int startIndex, int count, int failReturn)
        {
            int index = startIndex;
            for (int i = 0; i < count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(arr[index], val))
                {
                    return index;
                }
                index++;
            }
            return failReturn;
        }

        /// <summary>
        /// Checks if enumerable contains any of the strings in list
        /// </summary>
        public static bool ContainsAny(this IEnumerable<string> enumer, IEnumerable<string> list)
        {
            foreach (string item in enumer)
            {
                if (item.ContainsAny(list))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if string contains any of the strings in list
        /// </summary>
        public static bool ContainsAny(this string str, IEnumerable<string> list)
        {
            foreach (string item in list)
            {
                if (str.Contains(item))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Converts IList to comma-separated string
        /// </summary>
        public static string ToRealString(this IList lst)
        {
            if (lst == null || lst.Count == 0)
            {
                return "NULL";
            }

            var strings = new List<string>();
            foreach (object obj in lst)
            {
                try
                {
                    if (obj == null || obj.Equals(null))
                    {
                        strings.Add("NULL");
                    }
                    else
                    {
                        strings.Add(obj.ToString() ?? "NULL");
                    }
                }
                catch
                {
                    strings.Add("NULL");
                }
            }

            return string.Join(",", strings.ToArray());
        }

        /// <summary>
        /// Converts array to comma-separated string
        /// </summary>
        public static string ToRealString<T>(this T[] lst)
        {
            if (lst == null)
            {
                return "NULL";
            }

            var list = new List<T>(lst);
            return list.ToRealString();
        }

        /// <summary>
        /// Converts List to comma-separated string
        /// </summary>
        public static string ToRealString<T>(this List<T> list)
        {
            if (list == null || list.Count == 0)
            {
                return "NULL";
            }

            var strings = new List<string>();
            foreach (var item in list)
            {
                try
                {
                    if (item == null)
                    {
                        strings.Add("NULL");
                    }
                    else
                    {
                        strings.Add(item.ToString() ?? "NULL");
                    }
                }
                catch
                {
                    strings.Add("NULL");
                }
            }

            return string.Join(",", strings.ToArray());
        }
    }
}
