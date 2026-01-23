using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.IO;
using System.Reflection;
using Microsoft.CSharp;

namespace Styx.Helpers
{
	/// <summary>
	/// Factory for compiling assemblies from source files.
	/// </summary>
	public class AssemblyFactory
	{
		private CompilerErrorCollection? _compilerErrors;

		/// <summary>
		/// Gets the compilation errors from the last assembly creation.
		/// </summary>
		public CompilerErrorCollection? CompilerErrors => _compilerErrors;

		/// <summary>
		/// Creates an assembly from a single source file.
		/// </summary>
		public Assembly? CreateAssembly(string filename)
		{
			return CreateAssembly(filename, new ArrayList());
		}

		/// <summary>
		/// Creates an assembly from a single source file with references.
		/// </summary>
		public Assembly? CreateAssembly(string filename, IList references)
		{
			_compilerErrors = null;
			string extension = Path.GetExtension(filename);

			CodeDomProvider? provider = extension switch
			{
				".cs" => new CSharpCodeProvider(),
				".vb" => new CSharpCodeProvider(), // VB not supported, fallback
				_ => throw new InvalidOperationException("Script files must have a .cs or .vb extension.")
			};

#pragma warning disable CS0618 // ICodeCompiler is obsolete but matches original API
			ICodeCompiler compiler = provider.CreateCompiler();
#pragma warning restore CS0618

			var parameters = new CompilerParameters
			{
				CompilerOptions = "/target:library /optimize",
				GenerateExecutable = false,
				GenerateInMemory = true,
				IncludeDebugInformation = false
			};

			parameters.ReferencedAssemblies.Add("mscorlib.dll");
			parameters.ReferencedAssemblies.Add("System.dll");

			foreach (string reference in references)
			{
				if (!parameters.ReferencedAssemblies.Contains(reference))
				{
					parameters.ReferencedAssemblies.Add(reference);
				}
			}

			CompilerResults results = compiler.CompileAssemblyFromFile(parameters, filename);

			if (results.Errors.Count > 0)
			{
				_compilerErrors = results.Errors;
				throw new Exception("Compiler error(s) encountered and saved to AssemblyFactory.CompilerErrors");
			}

			return results.CompiledAssembly;
		}

		/// <summary>
		/// Creates an assembly from multiple source files.
		/// </summary>
		public Assembly? CreateAssembly(IList filenames)
		{
			return CreateAssembly(filenames, new ArrayList());
		}

		/// <summary>
		/// Creates an assembly from multiple source files with references.
		/// </summary>
		public Assembly? CreateAssembly(IList filenames, IList references)
		{
			string? commonExtension = null;

			foreach (string filename in filenames)
			{
				string ext = Path.GetExtension(filename);
				if (commonExtension == null)
				{
					commonExtension = ext;
				}
				else if (commonExtension != ext)
				{
					throw new ArgumentException("All files in the file list must be of the same type.");
				}
			}

			_compilerErrors = null;

			CodeDomProvider? provider = commonExtension switch
			{
				".cs" => new CSharpCodeProvider(),
				".vb" => new CSharpCodeProvider(), // VB not supported, fallback
				_ => throw new InvalidOperationException("Script files must have a .cs or .vb extension.")
			};

#pragma warning disable CS0618
			ICodeCompiler compiler = provider.CreateCompiler();
#pragma warning restore CS0618

			var parameters = new CompilerParameters
			{
				CompilerOptions = "/target:library /optimize",
				GenerateExecutable = false,
				GenerateInMemory = true,
				IncludeDebugInformation = false
			};

			parameters.ReferencedAssemblies.Add("mscorlib.dll");
			parameters.ReferencedAssemblies.Add("System.dll");

			foreach (string reference in references)
			{
				if (!parameters.ReferencedAssemblies.Contains(reference))
				{
					parameters.ReferencedAssemblies.Add(reference);
				}
			}

			string[] fileArray = (string[])ArrayList.Adapter(filenames).ToArray(typeof(string));
			CompilerResults results = compiler.CompileAssemblyFromFileBatch(parameters, fileArray);

			if (results.Errors.Count > 0)
			{
				_compilerErrors = results.Errors;
				throw new Exception("Compiler error(s) encountered and saved to AssemblyFactory.CompilerErrors");
			}

			return results.CompiledAssembly;
		}
	}
}
