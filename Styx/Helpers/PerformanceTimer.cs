using System;
using System.Diagnostics;

#nullable disable
namespace Styx.Helpers
{
    /// <summary>
    /// Simple performance timer for measuring execution time of code blocks.
    /// Use in a using statement or call Start()/StopAndPrint() manually.
    /// </summary>
    /// <example>
    /// using (var timer = new PerformanceTimer("My operation"))
    /// {
    ///     timer.Start();
    ///     // ... code to measure ...
    /// }
    /// </example>
    public class PerformanceTimer : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private readonly string _debugText;
        private bool _dontPrint;
        private bool _disposed;

        /// <summary>
        /// Creates a new performance timer with the specified debug text.
        /// </summary>
        /// <param name="debugText">Text to display when printing results.</param>
        public PerformanceTimer(string debugText)
        {
            _stopwatch = new Stopwatch();
            _debugText = debugText;
        }

        /// <summary>
        /// Gets the elapsed time in milliseconds.
        /// </summary>
        public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;

        /// <summary>
        /// Gets the elapsed time in ticks.
        /// </summary>
        public long ElapsedTicks => _stopwatch.ElapsedTicks;

        /// <summary>
        /// Gets the elapsed time as a TimeSpan.
        /// </summary>
        public TimeSpan Elapsed => _stopwatch.Elapsed;

        /// <summary>
        /// Gets whether the timer is currently running.
        /// </summary>
        public bool IsRunning => _stopwatch.IsRunning;

        /// <summary>
        /// Starts or resumes the timer.
        /// </summary>
        public void Start()
        {
            _stopwatch.Start();
        }

        /// <summary>
        /// Starts the timer only in debug builds with TIMERS defined.
        /// </summary>
        [Conditional("TIMERS")]
        public void StartConditional()
        {
            Start();
        }

        /// <summary>
        /// Stops the timer without printing.
        /// </summary>
        public void Stop()
        {
            _stopwatch.Stop();
        }

        /// <summary>
        /// Resets the timer to zero.
        /// </summary>
        public void Reset()
        {
            _stopwatch.Reset();
        }

        /// <summary>
        /// Resets and starts the timer.
        /// </summary>
        public void Restart()
        {
            _stopwatch.Restart();
        }

        /// <summary>
        /// Prevents the timer from printing when stopped or disposed.
        /// </summary>
        public void DontPrint()
        {
            _dontPrint = true;
        }

        /// <summary>
        /// Stops the timer and prints the elapsed time.
        /// </summary>
        public void StopAndPrint()
        {
            _stopwatch.Stop();
            if (!_dontPrint)
            {
                Logging.Write($"[{_stopwatch.Elapsed}] {_debugText}");
            }
        }

        /// <summary>
        /// Stops and prints only in debug builds with TIMERS defined.
        /// </summary>
        [Conditional("TIMERS")]
        public void StopAndPrintConditional()
        {
            StopAndPrint();
        }

        /// <summary>
        /// Disposes the timer. Stops and prints if not already done.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_stopwatch.IsRunning)
            {
                StopAndPrint();
            }
        }

        /// <summary>
        /// Creates a PerformanceTimer that automatically starts.
        /// </summary>
        public static PerformanceTimer StartNew(string debugText)
        {
            var timer = new PerformanceTimer(debugText);
            timer.Start();
            return timer;
        }
    }
}
