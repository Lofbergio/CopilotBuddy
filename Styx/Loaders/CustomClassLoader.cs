using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Styx.Helpers;

namespace Styx.Loaders
{
	/// <summary>
	/// Loader to compile C# source files into an in-memory assembly
	/// and instantiate types that subclass or implement a generic type T.
	/// Uses Roslyn compiler for .NET Core/.NET 5+ compatibility.
	/// </summary>
	public static class CustomClassLoader
	{
		/// <summary>
		/// Loads and compiles C# source files from a path, returning instances of type T.
		/// </summary>
		/// <typeparam name="T">The base class or interface to search for.</typeparam>
		/// <param name="path">Path to a .cs file or directory of .cs files.</param>
		/// <param name="compilerVersion">Ignored - kept for API compatibility.</param>
		/// <returns>List of instantiated objects of type T or its subclasses.</returns>
		public static IList<T> LoadFrom<T>(string path, string compilerVersion = "v4.0") where T : class
		{
			if (string.IsNullOrEmpty(path))
				throw new ArgumentNullException(nameof(path));

			var sources = new List<string>();

			if (Directory.Exists(path))
			{
				sources.AddRange(Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories));
			}
			else if (File.Exists(path))
			{
				sources.Add(path);
			}
			else
			{
				throw new FileNotFoundException("Source path not found", path);
			}

			if (sources.Count == 0)
				return new List<T>();

			// Parse all source files into syntax trees
			var syntaxTrees = new List<SyntaxTree>();
			foreach (var sourceFile in sources)
			{
				try
				{
					var code = File.ReadAllText(sourceFile);
					var syntaxTree = CSharpSyntaxTree.ParseText(code, 
						path: sourceFile,
						options: new CSharpParseOptions(LanguageVersion.Latest));
					syntaxTrees.Add(syntaxTree);
				}
				catch (Exception ex)
				{
					Logging.WriteDebug("[CustomClassLoader] Failed to parse {0}: {1}", 
						Path.GetFileName(sourceFile), ex.Message);
				}
			}

			if (syntaxTrees.Count == 0)
				return new List<T>();

			// Collect references from loaded assemblies
			var references = new List<MetadataReference>();
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				try
				{
					if (assembly.IsDynamic)
						continue;

					var location = assembly.Location;
					if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
						continue;

					references.Add(MetadataReference.CreateFromFile(location));
				}
				catch
				{
					// Skip assemblies we cannot reference
				}
			}

			// Add essential .NET runtime references
			var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
			if (runtimeDir != null)
			{
				var essentialRefs = new[] { 
					"System.Runtime.dll", 
					"System.Collections.dll", 
					"netstandard.dll",
					"System.Drawing.Primitives.dll",
					"System.Drawing.Common.dll",
					"System.Windows.Forms.dll",
					"System.ComponentModel.Primitives.dll",
					"System.Text.RegularExpressions.dll",
					"System.Linq.dll",
					"System.Linq.Expressions.dll"
				};
				foreach (var refName in essentialRefs)
				{
					var refPath = Path.Combine(runtimeDir, refName);
					if (File.Exists(refPath) && !references.Any(r => r.Display?.Contains(refName) == true))
					{
						references.Add(MetadataReference.CreateFromFile(refPath));
					}
				}
			}

			// Create compilation
			var compilation = CSharpCompilation.Create(
				assemblyName: "DynamicRoutines_" + Guid.NewGuid().ToString("N"),
				syntaxTrees: syntaxTrees,
				references: references,
				options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
					.WithOptimizationLevel(OptimizationLevel.Release)
					.WithPlatform(Platform.AnyCpu));

			// Emit to memory
			using (var ms = new MemoryStream())
			{
				EmitResult emitResult = compilation.Emit(ms);

				if (!emitResult.Success)
				{
					var errors = emitResult.Diagnostics
						.Where(d => d.Severity == DiagnosticSeverity.Error)
						.Select(d => d.ToString());
					
					throw new InvalidOperationException(
						"Compilation failed:\n" + string.Join(Environment.NewLine, errors));
				}

				ms.Seek(0, SeekOrigin.Begin);
				var compiledAssembly = Assembly.Load(ms.ToArray());

				var resultList = new List<T>();

				foreach (var type in compiledAssembly.GetTypes())
				{
					try
					{
						if (!type.IsClass || type.IsAbstract)
							continue;

						bool isImplementation = false;

						if (typeof(T).IsInterface)
						{
							if (type.GetInterfaces().Contains(typeof(T)))
								isImplementation = true;
						}
						else
						{
							if (type.IsSubclassOf(typeof(T)))
								isImplementation = true;
						}

						if (isImplementation)
						{
							var instance = Activator.CreateInstance(type);
							if (instance is T typedInstance)
								resultList.Add(typedInstance);
						}
					}
					catch (Exception ex)
					{
						Logging.WriteDebug("[CustomClassLoader] Failed to instantiate {0}: {1}", 
							type.FullName, ex.Message);
					}
				}

				return resultList;
			}
		}
	}
}
