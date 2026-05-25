using Styx.Helpers;
using Styx.Logic.Questing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;

namespace Styx.Logic.Profiles.Quest
{
    /// <summary>
    /// Manages the per-profile CompileBatch for quest behaviors that use
    /// [CompileString] / [CompileExpression] (e.g. RunCode).
    ///
    /// Flow (mirrors HB WoD Class1208 semantics):
    ///   1. QuestBot.Start()        → ProfileBatchManager.Reset()
    ///   2. ForcedCodeBehavior ctor → ProfileBatchManager.Register(qbInstance)
    ///   3. ForcedCodeBehavior.OnStart() → ProfileBatchManager.EnsureCompiled()
    ///
    /// Because CB executes nodes sequentially, Definition QBs are registered before
    /// any Statement QB tries to compile, so cross-node variable sharing works.
    /// </summary>
    public static class ProfileBatchManager
    {
        private static CompileBatch _currentBatch = new CompileBatch();

        /// <summary>
        /// The active batch for the current profile session.
        /// </summary>
        public static CompileBatch CurrentBatch => _currentBatch;

        /// <summary>
        /// Resets the batch. Call when a new profile is loaded.
        /// </summary>
        public static void Reset()
        {
            _currentBatch = new CompileBatch();
            Logging.WriteDebug("[ProfileBatchManager] Batch reset for new profile session.");
        }

        /// <summary>
        /// Scans the QB instance for [CompileString] and [CompileExpression] properties
        /// and registers them with the current batch.
        /// </summary>
        public static void Register(CustomForcedBehavior behavior)
        {
            if (behavior == null) return;
            if (_currentBatch.IsCompiled)
            {
                // Carry forward Definition (CompileString) code so methods like void Log(...)
                // defined in an earlier batch remain available to expressions in the new batch.
                var priorDefs = _currentBatch.GetDefinitionCode().ToList();
                Logging.WriteDebug("[ProfileBatchManager] Batch already compiled, starting new batch (carrying {0} definition(s)).", priorDefs.Count);
                _currentBatch = new CompileBatch();
                foreach (string def in priorDefs)
                    _currentBatch.Add(def);
            }

            Type type = behavior.GetType();
            foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (prop.GetCustomAttribute<CompileStringAttribute>() != null)
                {
                    string code = prop.GetValue(behavior) as string;
                    if (!string.IsNullOrEmpty(code))
                    {
                        _currentBatch.Add(code, behavior);
                        Logging.WriteDebug("[ProfileBatchManager] Registered CompileString from {0}.{1}", type.Name, prop.Name);
                    }
                }
                else if (prop.GetCustomAttribute<CompileExpressionAttribute>() != null)
                {
                    DelayCompiledExpression expr = prop.GetValue(behavior) as DelayCompiledExpression;
                    if (expr != null)
                    {
                        _currentBatch.AddExpression(expr, behavior);
                        Logging.WriteDebug("[ProfileBatchManager] Registered CompileExpression from {0}.{1}", type.Name, prop.Name);
                    }
                }
            }
        }

        /// <summary>
        /// Compiles the current batch if not already compiled.
        /// Returns true if compiled successfully (or nothing to compile).
        /// </summary>
        public static bool EnsureCompiled()
        {
            if (_currentBatch.IsCompiled)
                return !_currentBatch.HasErrors;

            // Don't compile until at least one Statement expression is registered.
            // Definition nodes call EnsureCompiled() before any Statement has registered;
            // compiling too early puts Definition code in a separate batch from the Statements.
            if (!_currentBatch.HasPendingExpressions)
                return true;

            bool result = _currentBatch.Compile();
            if (!result)
                Logging.Write(Color.Red, "[ProfileBatchManager] Compilation failed with {0} error(s).", _currentBatch.Errors.Length);
            return result;
        }
    }
}
