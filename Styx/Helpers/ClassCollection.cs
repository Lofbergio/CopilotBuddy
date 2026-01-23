#nullable disable
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using Styx.Loaders;

namespace Styx.Helpers
{
    /// <summary>
    /// A collection of dynamically loaded classes that compiles and instantiates
    /// types inheriting from T.
    /// Ported exactly from HB 3.3.5a.
    /// </summary>
    /// <typeparam name="T">The base type to load.</typeparam>
    public class ClassCollection<T> : List<T>, IDisposable where T : class
    {
        private readonly List<string> _tempFiles = new List<string>();

        public ClassCollection()
        {
        }

        public void Dispose()
        {
            Clear();
            foreach (string file in _tempFiles)
            {
                File.Delete(file);
            }
            GC.SuppressFinalize(this);
        }

        ~ClassCollection()
        {
            Dispose();
        }

        /// <summary>
        /// Compiles and loads classes from a path (file or directory).
        /// </summary>
        /// <param name="path">The path to compile from.</param>
        /// <param name="results">The compiler results.</param>
        /// <returns>The number of classes loaded.</returns>
        public int CompileAndLoadFrom(string path, out CompilerResults results)
        {
            DynamicLoader<T> dynamicLoader = new DynamicLoader<T>(path, true, new object[0]);
            AddRange(dynamicLoader);
            results = dynamicLoader.CompilerResults;
            return Count;
        }
    }
}
