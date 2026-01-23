#nullable disable
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Styx.Helpers;
using Styx.Plugins.PluginClass;

namespace Styx.Plugins
{
    /// <summary>
    /// Manages plugin loading, compilation, and execution.
    /// </summary>
    public static class PluginManager
    {
        /// <summary>
        /// Gets whether the plugin system is initialized.
        /// </summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>
        /// Gets all loaded plugins.
        /// </summary>
        public static List<PluginContainer> Plugins { get; private set; }

        static PluginManager()
        {
            Plugins = new List<PluginContainer>();
        }

        /// <summary>
        /// Pulses all enabled plugins.
        /// </summary>
        internal static void Pulse()
        {
            for (int i = 0; i < Plugins.Count; i++)
            {
                if (Plugins[i].Enabled)
                {
                    try
                    {
                        Plugins[i].Plugin.Pulse();
                    }
                    catch (Exception ex)
                    {
                        Logging.WriteException(ex);
                    }
                }
            }
        }

        /// <summary>
        /// Initializes the plugin system.
        /// </summary>
        /// <param name="defaultEnabled">Names of plugins to enable by default.</param>
        public static void Initialize(params string[] defaultEnabled)
        {
            if (!IsInitialized)
            {
                RefreshPlugins(defaultEnabled);
                IsInitialized = true;
            }
        }

        /// <summary>
        /// Refreshes the plugin list by reloading from the Plugins directory.
        /// </summary>
        /// <param name="defaultEnabled">Names of plugins to enable by default.</param>
        public static void RefreshPlugins(params string[] defaultEnabled)
        {
            Plugins.Clear();

            string pluginsPath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "Plugins");

            if (!Directory.Exists(pluginsPath))
            {
                Directory.CreateDirectory(pluginsPath);
                return;
            }

            var files = new List<string>();
            files.AddRange(Directory.GetFiles(pluginsPath, "*.cs", SearchOption.TopDirectoryOnly));
            files.AddRange(Directory.GetDirectories(pluginsPath, "*", SearchOption.TopDirectoryOnly));

            for (int i = 0; i < files.Count; i++)
            {
                try
                {
                    List<HBPlugin> loadedPlugins = CompileAndLoadFrom(files[i]);
                    foreach (HBPlugin plugin in loadedPlugins)
                    {
                        bool enableByDefault = defaultEnabled != null && defaultEnabled.Contains(plugin.Name);
                        var container = new PluginContainer(plugin, enableByDefault);
                        Plugins.Add(container);
                    }
                }
                catch (CompilerErrorsException ex)
                {
                    Logging.Write("Could not compile plugin: {0}", files[i]);
                    Logging.Write(ex.ToString());
                }
                catch (Exception ex)
                {
                    Logging.Write("Error loading plugin: {0}", files[i]);
                    Logging.WriteException(ex);
                }
            }

            Logging.Write("Plugin loading complete. {0} plugins loaded.", Plugins.Count);
        }

        /// <summary>
        /// Compiles and loads plugins from a path.
        /// </summary>
        /// <param name="path">The path to compile from (file or directory).</param>
        /// <returns>List of loaded plugins.</returns>
        public static List<HBPlugin> CompileAndLoadFrom(string path)
        {
            var classCollection = new ClassCollection<HBPlugin>();
            CompilerResults compilerResults;
            classCollection.CompileAndLoadFrom(path, out compilerResults);

            if (compilerResults != null && compilerResults.Errors.HasErrors)
            {
                throw new CompilerErrorsException(Utilities.FormatCompilerErrors(compilerResults));
            }

            return classCollection;
        }
    }
}
