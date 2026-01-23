using System;
using System.Threading;

namespace Styx.WoWInternals
{
	/// <summary>
	/// Helper class to wait for a Lua event.
	/// </summary>
	public class LuaEventWait : IDisposable
	{
		private const int WaitInterval = 30;
		private readonly AutoResetEvent autoResetEvent_0 = new AutoResetEvent(false);

		public string EventName { get; private set; }

		public LuaEventWait(string eventName)
		{
			EventName = eventName;
			Lua.Events.AttachEvent(eventName, EventHandler);
		}

		private void EventHandler(object sender, LuaEventArgs e)
		{
			autoResetEvent_0.Set();
		}

		public bool Wait()
		{
			return Wait(-1);
		}

		public bool Wait(int millisecondsTimeout)
		{
			return Wait(new TimeSpan(0, 0, 0, 0, millisecondsTimeout));
		}

		public bool Wait(TimeSpan timeout)
		{
			if (timeout.TotalMilliseconds < 0.0)
			{
				for (;;)
				{
					LuaEvents.ProcessPendingEvents();
					if (autoResetEvent_0.WaitOne(0))
					{
						break;
					}
					Thread.Sleep(WaitInterval);
				}
				return true;
			}
			else
			{
				DateTime now = DateTime.Now;
				DateTime dateTime = now.Add(timeout);
				for (;;)
				{
					LuaEvents.ProcessPendingEvents();
					if (autoResetEvent_0.WaitOne(0))
					{
						break;
					}
					Thread.Sleep(WaitInterval);
					if (DateTime.Now >= dateTime)
					{
						return false;
					}
				}
				return true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}

		~LuaEventWait()
		{
			Dispose(false);
		}

		protected virtual void Dispose(bool disposing)
		{
			Lua.Events.DetachEvent(EventName, EventHandler);
			if (disposing)
			{
				autoResetEvent_0.Close();
				GC.SuppressFinalize(this);
			}
		}
	}
}
