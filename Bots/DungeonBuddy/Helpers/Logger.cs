using Styx.Helpers;

namespace Bots.DungeonBuddy.Helpers
{
    public static class Logger
    {
        public static void Write(string message) => Logging.Write(message);
        public static void Write(string format, params object[] args) => Logging.Write(format, args);
        public static void WriteDiagnostic(string message) => Logging.WriteDiagnostic(message);
        public static void WriteDiagnostic(string format, params object[] args) => Logging.WriteDiagnostic(format, args);
    }
}
