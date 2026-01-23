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
    /// Uses encrypted Data.bin file from HB.
    /// </summary>
    public static class Connection
    {
        private static SQLiteConnection _connection;
        private static readonly Regex ParamRegex = new Regex(@"@[\w_]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Gets the SQLite connection instance.
        /// </summary>
        public static SQLiteConnection Instance
        {
            get
            {
                if (_connection == null)
                {
                    var builder = new SQLiteConnectionStringBuilder
                    {
                        Password = "JkejXP5_fG2vN-jlFVME",
                        DataSource = System.IO.Path.Combine(Logging.ApplicationPath, "Data.bin")
                    };
                    _connection = new SQLiteConnection(builder.ConnectionString);
                    _connection.StateChange += OnStateChange;
                    _connection.Open();
                    SQLiteFunction.RegisterFunction(typeof(DistanceFunction));
                    SQLiteFunction.RegisterFunction(typeof(PathDistanceFunction));
                }
                return _connection;
            }
        }

        private static void OnStateChange(object sender, StateChangeEventArgs e)
        {
            if (e.CurrentState == ConnectionState.Closed || e.CurrentState == ConnectionState.Broken)
            {
                _connection.Close();
                _connection.Open();
            }
        }

        /// <summary>
        /// Creates a SQLite command with parameter placeholders.
        /// </summary>
        internal static SQLiteCommand CreateCommand(string commandText)
        {
            var command = new SQLiteCommand(commandText, Instance);
            var matches = ParamRegex.Matches(commandText);
            foreach (Match match in matches)
            {
                command.Parameters.Add(new SQLiteParameter(match.Value));
            }
            return command;
        }

        /// <summary>
        /// Executes a command with parameters and returns a reader.
        /// </summary>
        internal static SQLiteDataReader ExecuteReader(SQLiteCommand command, params object[] args)
        {
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
            for (int i = 0; i < args.Length; i++)
            {
                command.Parameters[i].Value = args[i];
            }
            return command.ExecuteNonQuery();
        }

        /// <summary>
        /// SQLite function for vector distance calculation.
        /// </summary>
        [SQLiteFunction(Arguments = 6, FuncType = FunctionType.Scalar, Name = "VECTORDISTANCE")]
        public sealed class DistanceFunction : SQLiteFunction
        {
            public override object Invoke(object[] args)
            {
                var p1 = new WoWPoint(Convert.ToSingle(args[0]), Convert.ToSingle(args[1]), Convert.ToSingle(args[2]));
                var p2 = new WoWPoint(Convert.ToSingle(args[3]), Convert.ToSingle(args[4]), Convert.ToSingle(args[5]));
                return p1.DistanceSqr(p2);
            }
        }

        /// <summary>
        /// SQLite function for path distance calculation.
        /// </summary>
        [SQLiteFunction(Arguments = 6, FuncType = FunctionType.Scalar, Name = "PATHDISTANCE")]
        public sealed class PathDistanceFunction : SQLiteFunction
        {
            public override object Invoke(object[] args)
            {
                var p1 = new WoWPoint(Convert.ToSingle(args[0]), Convert.ToSingle(args[1]), Convert.ToSingle(args[2]));
                var p2 = new WoWPoint(Convert.ToSingle(args[3]), Convert.ToSingle(args[4]), Convert.ToSingle(args[5]));
                // TODO: Use actual path distance via Navigator when available
                return p1.DistanceSqr(p2);
            }
        }
    }
}
