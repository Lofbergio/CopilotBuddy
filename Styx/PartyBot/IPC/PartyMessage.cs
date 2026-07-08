using System;

namespace PartyBot.IPC
{
	// One framed message on the party bus (JSON line on the wire; System.Text.Json with IncludeFields).
	// Deliberately transport-agnostic and payload-opaque: the bus routes on Type + TargetGuid and never
	// interprets Payload — features (Command / Progress / future Claim/Grant/Lease) own their own payload
	// encoding. Public fields (not properties) to match the JsonSerializerOptions{IncludeFields=true} the
	// rest of PartyBot.IPC already uses.
	public class PartyMessage
	{
		public string Type = "";        // "Hello" | "Command" | "Progress" | future broker types
		public ulong SenderGuid;        // the sending member's player GUID (set on every message)
		public string SenderName = "";  // for human-readable logs
		public ulong TargetGuid;        // 0 = broadcast to all; else deliver ONLY to this member
		public long TimestampUtc;       // DateTime.UtcNow.Ticks — liveness + ordering
		public string Payload = "";     // type-specific JSON (e.g. a serialized BotMessage for "Command")

		public PartyMessage() { }

		public PartyMessage(string type, ulong senderGuid, string senderName, string payload, ulong targetGuid = 0UL)
		{
			Type = type;
			SenderGuid = senderGuid;
			SenderName = senderName ?? "";
			TargetGuid = targetGuid;
			Payload = payload ?? "";
			TimestampUtc = DateTime.UtcNow.Ticks;
		}
	}
}
