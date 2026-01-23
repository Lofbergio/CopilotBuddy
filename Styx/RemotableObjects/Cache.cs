namespace Styx.RemotableObjects
{
	/// <summary>
	/// Cache for remotable objects and bot messages.
	/// Implements the singleton pattern for global access.
	/// </summary>
	public class Cache
	{
		/// <summary>
		/// Gets the singleton instance of the cache.
		/// </summary>
		public static Cache Instance { get; } = new Cache();

		private IObserver? _observer;
		private BotMessage? _message;

		/// <summary>
		/// Gets or sets the current bot message.
		/// Setting notifies any registered observer.
		/// </summary>
		public BotMessage? Message
		{
			get => _message;
			set
			{
				_message = value;
				if (value != null && _observer != null)
					_observer.Notify(value);
			}
		}

		/// <summary>
		/// Sets an observer to be notified of message changes.
		/// </summary>
		public void SetObserver(IObserver? observer)
		{
			_observer = observer;
		}
	}
}
