#nullable disable
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Styx.Loaders
{
    /// <summary>
    /// Loads and compiles classes dynamically from source code or assemblies.
    /// </summary>
    /// <typeparam name="T">The type to load.</typeparam>
    public class DynamicLoader<T> : List<T>
    {
        public DynamicLoader(string path, bool compileSource, params object[] constructorArgs)
        {
            Path = path;

            if (Directory.Exists(path))
            {
                IsDirectory = true;
            }
            else if (!File.Exists(path))
            {
                throw new FileNotFoundException("The specified path was not found.", path);
            }

            if (!compileSource)
            {
                AddRange(IsDirectory
                    ? new DllLoader<T>(path, constructorArgs)
                    : new DllLoader<T>(System.IO.Path.GetDirectoryName(path), constructorArgs));
            }
            else
            {
                var compiler = new SourceCompiler(path);
                CompilerResults = compiler.Compile();
                
                if (CompilerResults != null)
                {
                    // Check if there are any actual errors (not warnings)
                    if (!CompilerResults.Errors.Cast<CompilerError>().Any(e => !e.IsWarning))
                    {
                        AddRange(new DllLoader<T>(compiler.CompiledAssembly, new object[0]));
                    }
                }
            }
        }

        /// <summary>
        /// Gets the path being loaded from.
        /// </summary>
        public string Path { get; private set; }

        /// <summary>
        /// Gets whether the path is a directory.
        /// </summary>
        public bool IsDirectory { get; private set; }

        /// <summary>
        /// Gets the compiler results if source was compiled.
        /// </summary>
        public CompilerResults CompilerResults { get; private set; }
    }
}
