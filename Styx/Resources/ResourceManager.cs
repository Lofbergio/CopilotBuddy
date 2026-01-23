#nullable disable
using System.IO;

namespace Styx.Resources
{
    /// <summary>
    /// Stub for Styx.Resources namespace (HB 4.3.4 compatibility).
    /// Used by Singular for resource loading.
    /// </summary>
    public static class ResourceManager
    {
        /// <summary>
        /// Gets the resource path.
        /// </summary>
        public static string ResourcePath
        {
            get
            {
                string appPath = System.AppDomain.CurrentDomain.BaseDirectory;
                string resourcePath = Path.Combine(appPath, "Resources");
                
                if (!Directory.Exists(resourcePath))
                    Directory.CreateDirectory(resourcePath);
                
                return resourcePath;
            }
        }

        /// <summary>
        /// Gets a resource file path.
        /// </summary>
        public static string GetResourcePath(string filename)
        {
            return Path.Combine(ResourcePath, filename);
        }
    }
}
