using System;
using System.Diagnostics;
using System.IO;
using Styx.Helpers;
using Styx.WoWInternals;

namespace Styx
{
    public static class Guard
    {
        internal static void CheckExecutor()
        {
            if (ObjectManager.Executor == null)
            {
                Logging.WriteDebug("Invalid Executor! StackTrace:");
                StackTrace stackTrace = new StackTrace(true);
                StackFrame[] frames = stackTrace.GetFrames();
                if (frames != null)
                {
                    for (int i = 1; i < Math.Min(frames.Length, 10); i++)
                    {
                        StackFrame frame = frames[i];
                        Logging.WriteDebug("\tCaller {0}: {1} in {2} line {3}",
                            i,
                            frame.GetMethod()?.Name,
                            Path.GetFileName(frame.GetFileName()),
                            frame.GetFileLineNumber());
                    }
                }
                throw new InvalidExecutorException();
            }
        }
    }
}
