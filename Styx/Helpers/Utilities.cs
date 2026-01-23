using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;

namespace Styx.Helpers
{
    /// <summary>
    /// General utility methods for HB 3.3.5a
    /// </summary>
    public static class Utilities
    {
        /// <summary>
        /// Gets object string representation or returns nullRet if null
        /// </summary>
        public static string GetObjectString(object? obj, string nullRet)
        {
            return obj?.ToString() ?? nullRet;
        }

        /// <summary>
        /// Formats a string with culture-aware formatting
        /// </summary>
        public static string FormatString([Localizable(true)] string format, params object[] args)
        {
            try
            {
                return string.Format(Thread.CurrentThread.CurrentUICulture, format, args);
            }
            catch (Exception ex)
            {
                return "FS_EMPTY " + ex.GetType().Name + " " + format.ToString();
            }
        }

        /// <summary>
        /// Formats compiler errors into readable string.
        /// Original HB: internal static string \u0019(CompilerResults results)
        /// </summary>
        internal static string FormatCompilerErrors(CompilerResults results)
        {
            var sb = new StringBuilder();
            
            foreach (CompilerError error in results.Errors)
            {
                if (!error.IsWarning)
                {
                    sb.AppendLine(string.Format("File: {0} Line: {1} {3}: {2}",
                        Path.GetFileName(error.FileName),
                        error.Line,
                        error.ErrorText,
                        error.IsWarning ? "Warning" : "Error"));
                }
            }
            
            return sb.ToString();
        }
    }
}
