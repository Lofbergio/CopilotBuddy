using System;
using Styx.Logic.BehaviorTree;

namespace Styx.Helpers
{
    public class ActivitySetter : IDisposable
    {
        private readonly string _previousText;

        public ActivitySetter(string text)
        {
            _previousText = TreeRoot.StatusText;
            TreeRoot.StatusText = text;
        }

        public void Dispose()
        {
            if (!string.IsNullOrEmpty(_previousText))
            {
                TreeRoot.StatusText = _previousText;
            }
        }
    }
}
