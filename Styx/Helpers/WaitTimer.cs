using System;
using System.Threading;

namespace Styx.Helpers
{
	public class WaitTimer
	{
		private TimeSpan _waitTime;
		private DateTime _startTime;
		private WaitTimerFinishedHandler? _finishedHandler;

		public static WaitTimer OneSecond
		{
			get { return new WaitTimer(new TimeSpan(0, 0, 1)); }
		}

		public static WaitTimer FiveSeconds
		{
			get { return new WaitTimer(new TimeSpan(0, 0, 5)); }
		}

		public static WaitTimer TenSeconds
		{
			get { return new WaitTimer(new TimeSpan(0, 0, 10)); }
		}

		public static WaitTimer ThirtySeconds
		{
			get { return new WaitTimer(new TimeSpan(0, 0, 30)); }
		}

		public static WaitTimer OneMinute
		{
			get { return new WaitTimer(new TimeSpan(0, 1, 0)); }
		}

		public TimeSpan WaitTime
		{
			get { return _waitTime; }
			set { _waitTime = value; }
		}

		public DateTime StartTime
		{
			get { return _startTime; }
			private set { _startTime = value; }
		}

		public DateTime EndTime
		{
			get { return StartTime.Add(WaitTime); }
		}

		public bool IsFinished
		{
			get { return DateTime.Now >= EndTime; }
		}

		public TimeSpan TimeLeft
		{
			get
			{
				TimeSpan left = EndTime - DateTime.Now;
				return left > TimeSpan.Zero ? left : TimeSpan.Zero;
			}
		}

		public WaitTimer(TimeSpan waitTime)
		{
			WaitTime = waitTime;
			Stop();
		}

		public void Reset()
		{
			StartTime = DateTime.Now;
		}

		public void Stop()
		{
			StartTime = DateTime.Now.AddDays(-5.0);
		}

		public void Update()
		{
			if (IsFinished && _finishedHandler != null)
			{
				WaitTimerEventArgs args = new WaitTimerEventArgs
				{
					TimeFinished = DateTime.Now,
					TimeStarted = StartTime,
					WaitTime = WaitTime
				};
				_finishedHandler(this, args);
			}
		}

		public void Wait()
		{
			while (!IsFinished)
			{
				Thread.Sleep(10);
			}
		}

		public event WaitTimerFinishedHandler Finished
		{
			add
			{
				WaitTimerFinishedHandler handler = _finishedHandler;
				WaitTimerFinishedHandler compare;
				do
				{
					compare = handler;
					WaitTimerFinishedHandler combined = (WaitTimerFinishedHandler)Delegate.Combine(compare, value);
					handler = Interlocked.CompareExchange(ref _finishedHandler, combined, compare);
				}
				while (handler != compare);
			}
			remove
			{
				WaitTimerFinishedHandler handler = _finishedHandler;
				WaitTimerFinishedHandler compare;
				do
				{
					compare = handler;
					WaitTimerFinishedHandler removed = (WaitTimerFinishedHandler)Delegate.Remove(compare, value);
					handler = Interlocked.CompareExchange(ref _finishedHandler, removed, compare);
				}
				while (handler != compare);
			}
		}

		public class WaitTimerEventArgs : EventArgs
		{
			public DateTime TimeFinished { get; set; }
			public DateTime TimeStarted { get; set; }
			public TimeSpan WaitTime { get; set; }
		}

		public delegate void WaitTimerFinishedHandler(object sender, WaitTimerEventArgs args);
	}
}
