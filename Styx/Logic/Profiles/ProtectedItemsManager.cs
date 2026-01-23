#nullable disable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using Styx.Helpers;

namespace Styx.Logic.Profiles
{
    /// <summary>
    /// Manages protected items that should not be sold, mailed, or destroyed.
    /// </summary>
    public static class ProtectedItemsManager
    {
        // Items from XML files
        private static readonly DualHashSet<uint, string> _fileProtectedItems;
        // Items added at runtime
        private static readonly DualHashSet<uint, string> _runtimeProtectedItems;
        // Valid file names for protected items
        private static readonly HashSet<string> _validFileNames;

        static ProtectedItemsManager()
        {
            _fileProtectedItems = new DualHashSet<uint, string>();
            _runtimeProtectedItems = new DualHashSet<uint, string>();
            _validFileNames = new HashSet<string>
            {
                "protected items",
                "protecteditems",
                "protitems"
            };
            ReloadProtectedItems();
        }

        /// <summary>
        /// Reloads protected items from XML files.
        /// </summary>
        public static void ReloadProtectedItems()
        {
            _fileProtectedItems.HashSet1.Clear();
            _fileProtectedItems.HashSet2.Clear();

            string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (directoryName == null) return;

            string[] files = Directory.GetFiles(directoryName, "*.xml", SearchOption.AllDirectories);
            foreach (string filePath in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath)?.ToLower();
                if (_validFileNames.Contains(fileName))
                {
                    LoadProtectedItemsFile(filePath);
                }
            }
        }

        /// <summary>
        /// Loads protected items from an XML file.
        /// </summary>
        private static void LoadProtectedItemsFile(string filePath)
        {
            if (Path.GetExtension(filePath)?.ToLower() != ".xml" || !File.Exists(filePath))
                return;

            XElement root;
            try
            {
                root = XElement.Load(filePath);
            }
            catch (XmlException ex)
            {
                Logging.Write($"Error in ProtectedItems XML file at {filePath}: {ex.Message}");
                return;
            }

            foreach (var element in root.Elements())
            {
                if (element.Name.ToString().ToLower() != "item")
                    continue;

                uint id = 0;
                string name = "";

                foreach (var attr in element.Attributes())
                {
                    string attrName = attr.Name.ToString().ToLower();
                    switch (attrName)
                    {
                        case "id":
                        case "entry":
                            uint.TryParse(attr.Value, out id);
                            break;
                        case "name":
                            name = attr.Value.ToLower();
                            break;
                    }
                }

                // Try to get id from element value if not in attributes
                if (id == 0 && !string.IsNullOrEmpty(element.Value))
                {
                    uint.TryParse(element.Value, out id);
                }

                if (id == 0 && string.IsNullOrEmpty(name))
                    continue;

                if (!_fileProtectedItems.Contains(id))
                    _fileProtectedItems.Add(id);
                if (!_fileProtectedItems.Contains(name))
                    _fileProtectedItems.Add(name);
            }
        }

        /// <summary>
        /// Checks if an item id is protected.
        /// </summary>
        public static bool Contains(uint item)
        {
            if (_fileProtectedItems.Contains(item) || _runtimeProtectedItems.Contains(item))
                return true;

            return CheckProfileProtectedItems(item);
        }

        /// <summary>
        /// Checks if an item name is protected.
        /// </summary>
        public static bool Contains(string item)
        {
            item = item.ToLower();

            if (_fileProtectedItems.Contains(item) || _runtimeProtectedItems.Contains(item))
                return true;

            return CheckProfileProtectedItems(item);
        }

        /// <summary>
        /// Checks if item is in current profile's protected items.
        /// </summary>
        private static bool CheckProfileProtectedItems(uint item)
        {
            return ProfileManager.CurrentProfile?.ProtectedItems?.Contains(item) ?? false;
        }

        /// <summary>
        /// Checks if item is in current profile's protected items.
        /// </summary>
        private static bool CheckProfileProtectedItems(string item)
        {
            return ProfileManager.CurrentProfile?.ProtectedItems?.Contains(item.ToLower()) ?? false;
        }

        /// <summary>
        /// Adds an item id to the runtime protected list.
        /// </summary>
        public static bool Add(uint item) => _runtimeProtectedItems.Add(item);

        /// <summary>
        /// Adds an item name to the runtime protected list.
        /// </summary>
        public static bool Add(string item) => _runtimeProtectedItems.Add(item.ToLower());

        /// <summary>
        /// Removes an item id from the runtime protected list.
        /// </summary>
        public static bool Remove(uint item) => _runtimeProtectedItems.Remove(item);

        /// <summary>
        /// Removes an item name from the runtime protected list.
        /// </summary>
        public static bool Remove(string item) => _runtimeProtectedItems.Remove(item.ToLower());

        /// <summary>
        /// Gets all protected item names.
        /// </summary>
        public static List<string> GetAllItemNames()
        {
            var list = new List<string>();

            foreach (string name in _fileProtectedItems.HashSet2)
                list.Add(name);

            foreach (string name in _runtimeProtectedItems.HashSet2)
                list.Add(name);

            if (ProfileManager.CurrentProfile?.ProtectedItems != null)
            {
                foreach (string name in ProfileManager.CurrentProfile.ProtectedItems.HashSet2)
                    list.Add(name);
            }

            return list;
        }

        /// <summary>
        /// Gets all protected item ids.
        /// </summary>
        public static List<uint> GetAllItemIds()
        {
            var list = new List<uint>();

            foreach (uint id in _fileProtectedItems.HashSet1)
                list.Add(id);

            foreach (uint id in _runtimeProtectedItems.HashSet1)
                list.Add(id);

            if (ProfileManager.CurrentProfile?.ProtectedItems != null)
            {
                foreach (uint id in ProfileManager.CurrentProfile.ProtectedItems.HashSet1)
                    list.Add(id);
            }

            return list;
        }
    }
}
