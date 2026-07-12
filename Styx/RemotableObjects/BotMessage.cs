using System;
using System.Runtime.Serialization;

namespace Styx.RemotableObjects
{
	[Serializable]
	public class BotMessage : MarshalByRefObject, ISerializable
	{
		public BotMessage()
		{
		}

		public BotMessage(SerializationInfo info, StreamingContext context)
		{
			Message = (string)info.GetValue("Message", typeof(string));
			Timestamp = (DateTime)info.GetValue("TimeStamp", typeof(DateTime));
			FromId = (uint)info.GetValue("FromId", typeof(uint));
			LeaderX = (float)info.GetValue("LeaderX", typeof(float));
			LeaderY = (float)info.GetValue("LeaderY", typeof(float));
			LeaderZ = (float)info.GetValue("LeaderZ", typeof(float));
			TargetGuid = (ulong)info.GetValue("TargetGuid", typeof(ulong));
			LeaderGuid = (ulong)info.GetValue("LeaderGuid", typeof(ulong));
			LeaderName = info.GetString("LeaderName");
			LeaderTargetGuid = (ulong)info.GetValue("LeaderTargetGuid", typeof(ulong));
			LeaderInCombat = (bool)info.GetValue("LeaderInCombat", typeof(bool));
			LeaderInInstance = (bool)info.GetValue("LeaderInInstance", typeof(bool));
			LeaderGhost = (bool)info.GetValue("LeaderGhost", typeof(bool));
			LeaderBagsOpen = (bool)info.GetValue("LeaderBagsOpen", typeof(bool));
		}

		public override string ToString()
		{
			return string.Format("[{0}]: {1} FROM:{2}", Timestamp.ToLongTimeString(), Message, FromId);
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("Message", Message);
			info.AddValue("TimeStamp", Timestamp);
			info.AddValue("FromId", FromId);
			info.AddValue("LeaderX", LeaderX);
			info.AddValue("LeaderY", LeaderY);
			info.AddValue("LeaderZ", LeaderZ);
			info.AddValue("TargetGuid", TargetGuid);
			info.AddValue("LeaderGuid", LeaderGuid);
			info.AddValue("LeaderName", LeaderName);
			info.AddValue("LeaderTargetGuid", LeaderTargetGuid);
			info.AddValue("LeaderInCombat", LeaderInCombat);
			info.AddValue("LeaderInInstance", LeaderInInstance);
			info.AddValue("LeaderGhost", LeaderGhost);
			info.AddValue("LeaderBagsOpen", LeaderBagsOpen);
		}

		public void SetMessage(BotMessage message)
		{
			Cache.Instance.Message = message;
		}

		public BotMessage? GetMessage()
		{
			return Cache.Instance.Message;
		}

		public string Message;
		public DateTime Timestamp;
		public uint FromId;
		public float LeaderX;
		public float LeaderY;
		public float LeaderZ;
		public ulong TargetGuid;
		public ulong LeaderGuid;
		public string LeaderName;
		// Phase 2: leader's current target (the quest-giver it's working) so followers turn in there.
		public ulong LeaderTargetGuid;
		// Assist: leader is actually in combat (pull committed) — followers focus-fire only then.
		public bool LeaderInCombat;
		// LFG mirror: followers match the leader's inside/outside-the-dungeon state…
		public bool LeaderInInstance;
		// …EXCEPT while the leader is dead/ghost (a corpse run reads as "left the instance" — it isn't).
		public bool LeaderGhost;
		// Bag-visibility sync: followers mirror the leader's open/closed bag UI.
		public bool LeaderBagsOpen;
	}
}
