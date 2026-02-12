#nullable disable
using System;
using System.Data;
using System.Data.SQLite;
using System.Text.RegularExpressions;
using Styx.Helpers;
using Styx.Logic.Pathing;

namespace Styx.Database
{
    /// <summary>
    /// Provides SQLite database connection for NPC data.
    /// Uses encrypted Data.bin file from HB (SEE encryption).
    /// </summary>
    public static class Connection
    {
        private static SQLiteConnection _connection;
        private static readonly Regex ParamRegex = new Regex(@"@[\w_]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static bool _isAvailable = true;
        private static bool _initialized = false;

        /// <summary>
        /// Indicates if the database is available.
        /// </summary>
        public static bool IsAvailable => _isAvailable && _connection != null;

        /// <summary>
        /// Gets the SQLite connection instance.
        /// </summary>
        public static SQLiteConnection Instance
        {
            get
            {
                if (!_initialized)
                {
                    _initialized = true;
                    TryInitialize();
                }
                return _connection;
            }
        }

        private static void TryInitialize()
        {
            try
            {
                var dbPath = System.IO.Path.Combine(Logging.ApplicationPath, "Data.bin");
                if (!System.IO.File.Exists(dbPath))
                {
                    Logging.Write(LogLevel.Normal, "[Database] Data.bin not found at: {0}", dbPath);
                    _isAvailable = false;
                    return;
                }

                // Use SQLiteConnectionStringBuilder like HB does
                var builder = new SQLiteConnectionStringBuilder();
                builder.Password = "JkejXP5_fG2vN-jlFVME";
                builder.DataSource = dbPath;
                builder.ReadOnly = true;

                _connection = new SQLiteConnection(builder.ConnectionString);
                _connection.StateChange += OnConnectionStateChange;
                _connection.Open();

                // Register custom functions for distance calculations
                SQLiteFunction.RegisterFunction(typeof(VectorDistanceFunction));
                SQLiteFunction.RegisterFunction(typeof(PathDistanceFunction));

                Logging.Write(LogLevel.Normal, "[Database] Data.bin loaded successfully");
            }
            catch (Exception ex)
            {
                Logging.Write(LogLevel.Normal, "[Database] Failed to load Data.bin: {0}", ex.Message);
                Logging.WriteDebug("[Database] Exception: {0}", ex);
                _isAvailable = false;
                _connection = null;
            }
        }

        private static void OnConnectionStateChange(object sender, StateChangeEventArgs e)
        {
            if (e.CurrentState == ConnectionState.Broken)
            {
                try
                {
                    _connection?.Close();
                    _connection?.Open();
                }
                catch (Exception ex)
                {
                    Logging.WriteDebug("[Database] Failed to reconnect: {0}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Creates a SQLite command with parameter placeholders.
        /// </summary>
        internal static SQLiteCommand CreateCommand(string commandText)
        {
            if (!IsAvailable)
                return null;

            var command = new SQLiteCommand(commandText, Instance);
            var matches = ParamRegex.Matches(commandText);
            foreach (Match match in matches)
            {
                command.Parameters.Add(new SQLiteParameter(match.Value, null));
            }
            return command;
        }

        /// <summary>
        /// Executes a command with parameters and returns a reader.
        /// </summary>
        internal static SQLiteDataReader ExecuteReader(SQLiteCommand command, params object[] args)
        {
            if (command == null)
                return null;

            for (int i = 0; i < args.Length; i++)
            {
                command.Parameters[i].Value = args[i];
            }
            return command.ExecuteReader();
        }

        /// <summary>
        /// Executes a non-query command with parameters.
        /// </summary>
        internal static int ExecuteNonQuery(SQLiteCommand command, params object[] args)
        {
            if (command == null)
                return 0;

            for (int i = 0; i < args.Length; i++)
            {
                command.Parameters[i].Value = args[i];
            }
            return command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Custom SQL function for vector distance calculation (squared).
    /// </summary>
    [SQLiteFunction(Name = "VECTORDISTANCE", Arguments = 6, FuncType = FunctionType.Scalar)]
    public class VectorDistanceFunction : SQLiteFunction
    {
        public override object Invoke(object[] args)
        {
            double x1 = Convert.ToDouble(args[0]);
            double y1 = Convert.ToDouble(args[1]);
            double z1 = Convert.ToDouble(args[2]);
            double x2 = Convert.ToDouble(args[3]);
            double y2 = Convert.ToDouble(args[4]);
            double z2 = Convert.ToDouble(args[5]);

            double dx = x2 - x1;
            double dy = y2 - y1;
            double dz = z2 - z1;
            return dx * dx + dy * dy + dz * dz;
        }
    }

    /// <summary>
    /// Custom SQL function for path distance calculation.
    /// Uses pathfinding when available, falls back to Euclidean distance.
    /// </summary>
    [SQLiteFunction(Name = "PATHDISTANCE", Arguments = 6, FuncType = FunctionType.Scalar)]
    public class PathDistanceFunction : SQLiteFunction
    {
        public override object Invoke(object[] args)
        {
            double x1 = Convert.ToDouble(args[0]);
            double y1 = Convert.ToDouble(args[1]);
            double z1 = Convert.ToDouble(args[2]);
            double x2 = Convert.ToDouble(args[3]);
            double y2 = Convert.ToDouble(args[4]);
            double z2 = Convert.ToDouble(args[5]);

            // FEAT-41: Try mesh-based path distance first; fall back to Euclidean
            try
            {
                var from = new Styx.Logic.Pathing.WoWPoint((float)x1, (float)y1, (float)z1);
                var to = new Styx.Logic.Pathing.WoWPoint((float)x2, (float)y2, (float)z2);
                var path = Styx.Logic.Pathing.Navigator.GeneratePath(from, to);
                if (path != null && path.Length > 1)
                {
                    double totalDist = 0;
                    for (int i = 1; i < path.Length; i++)
                    {
                        double sdx = path[i].X - path[i - 1].X;
                        double sdy = path[i].Y - path[i - 1].Y;
                        double sdz = path[i].Z - path[i - 1].Z;
                        totalDist += Math.Sqrt(sdx * sdx + sdy * sdy + sdz * sdz);
                    }
                    if (totalDist > 0)
                        return totalDist * totalDist; // Return squared for consistency
                }
            }
            catch
            {
                // Navigator not initialized or mesh not loaded — fall back
            }

            // Euclidean distance squared fallback
            double dx = x2 - x1;
            double dy = y2 - y1;
            double dz = z2 - z1;
            return dx * dx + dy * dy + dz * dz;
        }
    }
}
