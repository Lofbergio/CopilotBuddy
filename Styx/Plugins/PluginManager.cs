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
        /// Gets whether plugins are currently being built/compiled.
        /// </summary>
        public static bool IsBuildingPlugins { get; private set; }

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
                // Note: Plugin.Initialize() is already called by PluginContainer.Enabled setter
                // No need to call it again here
                IsInitialized = true;
            }
        }

        /// <summary>
        /// Updates the EnabledPlugins property in CharacterSettings.
        /// Note: Does NOT save immediately - save is done at app close or bot start/stop (HB 4.3.4 pattern).
        /// </summary>
        public static void UpdateEnabledPlugins()
        {
            try
            {
                var enabledPluginNames = Plugins
                    .Where(p => p.Enabled)
                    .Select(p => p.Name)
                    .ToArray();
                
                Helpers.CharacterSettings.Instance.EnabledPlugins = enabledPluginNames;
                // Note: No Save() here - HB 4.3.4 saves at window close and bot start/stop
            }
            catch (Exception ex)
            {
                Helpers.Logging.WriteException(ex);
            }
        }

        /// <summary>
        /// Saves the list of enabled plugins to CharacterSettings (legacy compatibility).
        /// </summary>
        [Obsolete("Use UpdateEnabledPlugins() instead. Saving is handled globally.")]
        public static void SaveEnabledPlugins()
        {
            UpdateEnabledPlugins();
        }

        /// <summary>
        /// Refreshes the plugin list by reloading from the Plugins directory.
        /// </summary>
        /// <param name="defaultEnabled">Names of plugins to enable by default.</param>
        public static void RefreshPlugins(params string[] defaultEnabled)
        {
            if (IsBuildingPlugins)
                return;

            try
            {
                IsBuildingPlugins = true;
                Plugins.Clear();

                // Force garbage collection to release old plugin assemblies
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // Scan the main assembly for built-in HBPlugin subclasses (e.g. LeaderPlugin)
                try
                {
                    foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
                    {
                        if (!type.IsAbstract && typeof(HBPlugin).IsAssignableFrom(type))
                        {
                            HBPlugin plugin = (HBPlugin)Activator.CreateInstance(type);
                            bool enableByDefault = defaultEnabled != null && defaultEnabled.Contains(plugin.Name);
                            Plugins.Add(new PluginContainer(plugin, enableByDefault));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.WriteException(ex);
                }

                string pluginsPath = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "Plugins");

                if (!Directory.Exists(pluginsPath))
                {
                    Directory.CreateDirectory(pluginsPath);
                    Logging.Write("No plugins found. Place plugins in the Plugins directory.");
                    return;
                }

                var files = new List<string>();
                files.AddRange(Directory.GetFiles(pluginsPath, "*.cs", SearchOption.TopDirectoryOnly));
                files.AddRange(Directory.GetDirectories(pluginsPath, "*", SearchOption.TopDirectoryOnly));

                if (files.Count == 0)
                {
                    Logging.Write("No plugins found. Place plugins in the Plugins directory.");
                    return;
                }

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
                        Logging.Write("Plugin from {0} could not be compiled. Compiler errors:", files[i]);
                        Logging.Write(ex.ToString());
                    }
                    catch (Exception ex)
                    {
                        Logging.Write("Error loading plugin: {0}", files[i]);
                        Logging.WriteException(ex);
                    }
                }

                Logging.Write("Plugin loading complete. {0} plugins loaded.", Plugins.Count);
                
                if (Plugins.Count == 0)
                {
                    Logging.Write("No plugins found. Place plugins in the Plugins directory.");
                }
                else
                {
                    Logging.Write("Plugins refreshed successfully.");
                }
            }
            finally
            {
                IsBuildingPlugins = false;
            }
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
