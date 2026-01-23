using System;
using System.Runtime.Serialization;

namespace Styx.RemotableObjects
{
	/// <summary>
	/// Represents a message sent between bot instances or modules.
	/// </summary>
	[Serializable]
	public class BotMessage : MarshalByRefObject, ISerializable
	{
		/// <summary>
		/// Creates a new bot message.
		/// </summary>
		public BotMessage()
		{
		}

		/// <summary>
		/// Deserializes a bot message from serialization context.
		/// </summary>
		public BotMessage(SerializationInfo info, StreamingContext context)
		{
			Message = (string?)info.GetValue("Message", typeof(string)) ?? string.Empty;
		var timeStamp = info.GetValue("TimeStamp", typeof(DateTime));
		Timestamp = timeStamp is DateTime dt ? dt : DateTime.Now;
		var fromId = info.GetValue("FromId", typeof(uint));
		FromId = fromId is uint id ? id : 0U;
	}

	/// <summary>
	/// Gets or sets the message text.
	/// </summary>
	public string Message { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the message timestamp.
	/// </summary>
	public DateTime Timestamp { get; set; }

	/// <summary>
	/// Gets or sets the ID of the sender.
	/// </summary>
	public uint FromId { get; set; }
		/// </summary>
		public override string ToString()
		{
			return $"[{Timestamp:T}]: {Message} FROM:{FromId}";
		}

		/// <summary>
		/// Serializes the message to the provided serialization context.
		/// </summary>
		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("Message", Message);
			info.AddValue("TimeStamp", Timestamp);
			info.AddValue("FromId", FromId);
		}
	}
}
