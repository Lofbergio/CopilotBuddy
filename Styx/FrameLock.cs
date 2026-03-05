using System;
using System.Threading;
using GreenMagic;
using Styx.WoWInternals;

namespace Styx
{
	/// <summary>
	/// Acquires continuous execution mode on the EndScene executor for the
	/// duration of its lifetime.  While held, every <see cref="ExecutorRand.Execute"/>
	/// call reuses the same hooked frame instead of waiting for a new one.
	///
	/// Ported from HB 5.4.8/6.2.3 GreyMagic.FrameLock design:
	/// - Per-instance tracking via <c>_wasAcquired</c> (no static counter)
	/// - Exception-safe <see cref="Monitor.Enter(object, ref bool)"/>
	/// - Full IDisposable pattern with finalizer safety
	/// - <c>_disposed</c> guard prevents double-dispose
	///
	/// Keeps a parameterless constructor (all call sites use <c>new FrameLock()</c>)
	/// and resolves the executor via <see cref="ObjectManager.Executor"/>.
	/// </summary>
	public class FrameLock : IDisposable
	{
		private readonly ExecutorRand _executor;
		private bool _disposed;
		private bool _wasAcquired;

		public FrameLock()
		{
			_executor = ObjectManager.Executor;
			bool success = false;
			bool lockTaken = false;
			try
			{
				Monitor.Enter(_executor.AssemblyLock, ref lockTaken);
				if (!_executor.IsExecutingContinuously)
				{
					_executor.BeginExecute();
					_wasAcquired = true;
				}
				success = true;
			}
			finally
			{
				if (!success && lockTaken)
				{
					Monitor.Exit(_executor.AssemblyLock);
				}
			}
		}

		/// <summary>The executor this lock is bound to.</summary>
		public ExecutorRand Executor => _executor;

		private void ReleaseResources()
		{
			if (_disposed)
				return;

			try
			{
				if (_executor.IsExecutingContinuously && _wasAcquired)
				{
					_executor.EndExecute();
				}
			}
			catch
			{
				// Swallow — we must still release the monitor.
			}

			Monitor.Exit(_executor.AssemblyLock);
			_disposed = true;
		}

		protected virtual void Dispose(bool disposing)
		{
			// HB 6.2.3: only release from explicit Dispose, NEVER from finalizer.
			// Monitor.Exit must be called by the same thread that called Monitor.Enter.
			// The GC finalizer thread never held the lock — calling Exit there throws
			// SynchronizationLockException and can crash the process.
			if (disposing)
			{
				ReleaseResources();
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}
	}
}
