namespace Styx.RemotableObjects
{
	/// <summary>
	/// Observer pattern interface for receiving bot messages.
	/// </summary>
	public interface IObserver
	{
		/// <summary>
		/// Notifies the observer of a new bot message.
		/// </summary>
		void Notify(BotMessage message);
	}
}
