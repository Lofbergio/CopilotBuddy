using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Styx.Helpers;

namespace Styx.Loaders
{
	/// <summary>
	/// Verifies loaded assemblies for compatibility and security.
	/// </summary>
	public static class AssemblyVerifier
	{
		// Trusted assemblies (dynamically compiled)
		internal static readonly HashSet<string> TrustedAssemblies;
		// Known public key tokens
		internal static readonly HashSet<string> KnownPublicKeyTokens;
		private static bool _initialized;

		static AssemblyVerifier()
		{
			TrustedAssemblies = new HashSet<string>();
			
			KnownPublicKeyTokens = new HashSet<string>
			{
				"0a69764484db0660",
				"53d73c680b668dc5",
				"b03f5f7f11d50a3a",
				"cd3409ee69028647",
				"e462d016e0e48151",
				"629e3702a350074b"
			};
		}

		/// <summary>
		/// Initializes the assembly verifier, registering event handlers.
		/// </summary>
		public static void Initialize()
		{
			if (_initialized)
				return;

			_initialized = true;

			AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
			AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += OnAssemblyResolve;
			AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;

			// Start background thread for token updates
			var thread = new Thread(UpdateTokens)
			{
				IsBackground = true
			};
			thread.Start();
		}

		/// <summary>
		/// Handles assembly resolution events.
		/// </summary>
		private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
		{
			if (args.Name.Contains("CopilotBuddy"))
				return Assembly.GetEntryAssembly();

			var name = args.Name.Split(',')[0];
			var appPath = AppDomain.CurrentDomain.BaseDirectory ?? "./";
			var searchPath = Path.Combine(appPath, name + ".dll");

			if (File.Exists(searchPath))
				return Assembly.LoadFrom(searchPath);

			var files = Directory.GetFiles(appPath, name + ".dll", SearchOption.AllDirectories);
			if (files.Length > 0)
				return Assembly.LoadFrom(files[0]);

			return null;
		}

		/// <summary>
		/// Handles assembly load events, checking for unauthorized DLLs.
		/// </summary>
		private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs e)
		{
			try
			{
				var assembly = e.LoadedAssembly;
				var escapedCodeBase = assembly.EscapedCodeBase;
				var name = assembly.GetName().Name;
				var fullName = assembly.FullName;

				if (string.IsNullOrEmpty(fullName))
					return;

				// Skip known trusted assemblies
				if (fullName.StartsWith("Tripper") || fullName.StartsWith("Styx") || 
				    name.Contains("RecastManaged") || escapedCodeBase.Contains("GAC_MSIL") ||
				    escapedCodeBase.EndsWith("System.dll") || assembly.GlobalAssemblyCache)
					return;

				// Check if in trusted assemblies list
				if (TrustedAssemblies.Any(prefix => fullName.StartsWith(prefix)))
					return;

				// Check public key token
				var publicKeyToken = assembly.GetName().GetPublicKeyToken();
				var tokenStr = publicKeyToken != null 
					? publicKeyToken.Aggregate("", (current, b) => current + b.ToString("x2"))
					: "";

				if (!KnownPublicKeyTokens.Contains(tokenStr))
				{
					// Skip Microsoft.Xml.Serialization.GeneratedAssembly
					try
					{
						var types = assembly.GetTypes();
						if (types.Length > 0 && types[0].Namespace == "Microsoft.Xml.Serialization.GeneratedAssembly")
						{
							Logging.Write("Unknown DLL Loaded: " + fullName);
							Logging.WriteDebug("PKT: {0}", tokenStr);
						}
					}
					catch
					{
						// Ignore type loading errors
					}
				}
			}
			catch (Exception ex)
			{
				Logging.WriteException(ex);
			}
		}

		/// <summary>
		/// Updates the set of known public key tokens (background thread).
		/// </summary>
		private static void UpdateTokens()
		{
			try
			{
				// Dans HB original, télécharge les tokens depuis buddynav.de
				// Pour CopilotBuddy, on collecte juste les tokens des assemblies chargées
				foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
				{
					var publicKey = assembly.GetName().GetPublicKey();
					if (publicKey != null && publicKey.Length > 0)
					{
						string keyStr = publicKey.Aggregate("", (current, b) => current + b.ToString("x2"));
						KnownPublicKeyTokens.Add(keyStr);
					}
				}
			}
			catch
			{
				// Ignore errors in background thread
			}
		}
	}
}
