#nullable disable
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Styx.Helpers;

namespace Styx.Loaders
{
    /// <summary>
    /// Loads classes of type T from assemblies.
    /// </summary>
    /// <typeparam name="T">The type to load.</typeparam>
    public class DllLoader<T> : List<T>
    {
        public DllLoader(string path, params object[] constructorArgs)
        {
            bool isInterface = typeof(T).IsInterface;

            string[] files;
            if (!Directory.Exists(path))
            {
                files = new string[] { path };
            }
            else
            {
                files = Directory.GetFiles(path, "*.dll", SearchOption.AllDirectories);
            }

            foreach (string file in files)
            {
                try
                {
                    Assembly assembly = Assembly.LoadFrom(file);
                    LoadFromAssembly(assembly, new object[0]);
                }
                catch (BadImageFormatException)
                {
                    // Not a .NET assembly, skip
                }
            }
        }

        internal DllLoader(Assembly asm, params object[] constructorArgs)
        {
            LoadFromAssembly(asm, constructorArgs);
        }

        internal void LoadFromAssembly(Assembly asm, params object[] constructorArgs)
        {
            bool isInterface = typeof(T).IsInterface;

            foreach (Type type in asm.GetTypes())
            {
                try
                {
                    if (type.IsClass)
                    {
                        if (isInterface && type.GetInterfaces().Contains(typeof(T)))
                        {
                            Add((T)Activator.CreateInstance(type, constructorArgs));
                        }
                        else if (type.IsSubclassOf(typeof(T)))
                        {
                            Add((T)Activator.CreateInstance(type, constructorArgs));
                        }
                    }
                }
                catch (TargetInvocationException ex)
                {
                    Logging.Write("Could not construct instance of {0}. Exception was thrown: Exception:", type.Name);
                    Logging.Write(ex.InnerException == null ? "Unknown" : ex.InnerException.Message);
                }
                catch (ReflectionTypeLoadException ex)
                {
                    foreach (Exception loaderEx in ex.LoaderExceptions)
                    {
                        Logging.WriteException(loaderEx);
                    }
                }
            }
        }
    }
}
