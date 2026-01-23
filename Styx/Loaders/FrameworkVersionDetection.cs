using System;
using System.Globalization;
using Microsoft.Win32;

namespace Styx.Loaders
{
	/// <summary>
	/// Detects the installed .NET Framework version.
	/// </summary>
	public static class FrameworkVersionDetection
	{
		/// <summary>
		/// Gets a value indicating whether .NET Framework 4.x is installed.
		/// </summary>
		public static bool DotNet4Installed
		{
			get
			{
				try
				{
					return ReadRegistryValue(
						RegistryHive.LocalMachine,
						"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full",
						"Install",
						RegistryValueKind.DWord,
						out uint installed) && installed == 1U;
				}
				catch
				{
					return false;
				}
			}
		}

		/// <summary>
		/// Reads a value from the Windows registry.
		/// </summary>
		private static bool ReadRegistryValue<T>(
			RegistryHive hive,
			string keyPath,
			string valueName,
			RegistryValueKind expectedKind,
			out T? data)
		{
			data = default;

			try
			{
				using (var baseKey = RegistryKey.OpenRemoteBaseKey(hive, string.Empty))
				using (var subKey = baseKey?.OpenSubKey(keyPath, RegistryKeyPermissionCheck.ReadSubTree))
				{
					if (subKey == null)
						return false;

					// Check if the value kind matches
					try
					{
						if (subKey.GetValueKind(valueName) != expectedKind)
							return false;
					}
					catch
					{
						return false;
					}

					// Read the value
					var value = subKey.GetValue(valueName);
					if (value == null)
						return false;

					// Convert to the requested type
					data = (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
					return true;
				}
			}
			catch
			{
				return false;
			}
		}
	}
}
