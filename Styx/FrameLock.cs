using System;
using Styx.WoWInternals;

namespace Styx
{
	public class FrameLock : IDisposable
	{
		public FrameLock()
		{
			if (_lockCount == 0)
			{
				ObjectManager.Executor?.BeginExecute();
			}
			_lockCount++;
		}

		public void Dispose()
		{
			_lockCount--;
			if (_lockCount == 0)
			{
				ObjectManager.Executor?.EndExecute();
			}
		}

		private static int _lockCount;
	}
}
