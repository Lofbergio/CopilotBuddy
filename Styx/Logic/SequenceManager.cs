using System;
using System.Collections.Generic;
using Styx.Helpers;

namespace Styx.Logic
{
    /// <summary>
    /// Manages bot action sequences with override support
    /// </summary>
    public static class SequenceManager
    {
        private static readonly Dictionary<BotSequence, Action> DefaultExecutors = new Dictionary<BotSequence, Action>();
        private static readonly Dictionary<BotSequence, Action> OverrideExecutors = new Dictionary<BotSequence, Action>();

        /// <summary>
        /// Calls the sequence executor (override if present, otherwise default)
        /// </summary>
        public static void CallSequenceExecutor(BotSequence seq)
        {
            if (OverrideExecutors.ContainsKey(seq))
            {
                OverrideExecutors[seq]();
            }
            else if (DefaultExecutors.ContainsKey(seq))
            {
                DefaultExecutors[seq]();
            }
        }

        /// <summary>
        /// Calls only the default sequence executor (ignores overrides)
        /// </summary>
        public static void CallDefaultSequenceExecutor(BotSequence seq)
        {
            if (DefaultExecutors.ContainsKey(seq))
            {
                DefaultExecutors[seq]();
            }
        }

        /// <summary>
        /// Gets the override executor for a sequence (null if none)
        /// </summary>
        public static Action? GetSequenceExecutorOverride(BotSequence seq)
        {
            return OverrideExecutors.ContainsKey(seq) ? OverrideExecutors[seq] : null;
        }

        /// <summary>
        /// Adds or updates an override executor for a sequence
        /// </summary>
        public static void AddSequenceExecutorOverride(BotSequence seq, Action executor)
        {
            if (OverrideExecutors.ContainsKey(seq))
            {
                OverrideExecutors[seq] = executor;
            }
            else
            {
                OverrideExecutors.Add(seq, executor);
            }
        }

        /// <summary>
        /// Adds a default sequence executor (warns if duplicate)
        /// </summary>
        public static void AddDefaultSequenceExecutor(BotSequence seq, Action executor)
        {
            if (DefaultExecutors.ContainsKey(seq))
            {
                if (executor != DefaultExecutors[seq])
                {
                    Logging.Write("Duplicate default sequence handler for {0} added! Method: {1}", 
                        seq.ToString(), 
                        executor.Method?.ToString() ?? "Unknown");
                    DefaultExecutors[seq] = executor;
                }
            }
            else
            {
                DefaultExecutors.Add(seq, executor);
            }
        }

        /// <summary>
        /// Clears all default executors (internal use)
        /// </summary>
        internal static void ClearDefaultExecutors()
        {
            DefaultExecutors.Clear();
        }
    }
}
