using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using PartyBot.IPC;
using Styx.Helpers;

namespace VibeParty
{
	// Role-transparent pub/sub over the duplex PartyHub (leader hosts the server; follower hosts the client).
	// Generic on purpose — it routes on Type + TargetGuid and tracks liveness, and knows NOTHING about quests
	// or loot. Every coordination feature (Command / Progress / future Claim/Grant/Lease) is a bus client.
	//
	// Threading: Received fires on hub threads (server client-reader threads, or the client Run thread), so
	// SUBSCRIBERS MUST BE PURE DATA — store the message, do NOT touch the object manager / Lua (bot-thread
	// only). Act on the stored data from Pulse (the bot thread).
	public sealed class PartyBus
	{
		public const int Port = 1338;   // VibeParty's own hub — DiscoBot's legacy RemotingServer keeps :1337

		public PartyBus(bool isLeader, ulong selfGuid, string selfName)
		{
			_isLeader = isLeader;
			_selfGuid = selfGuid;
			_selfName = selfName ?? "";
			if (isLeader)
			{
				_server = new PartyHubServer(Port);
				_server.Received += OnInbound;
			}
			else
			{
				_client = new PartyHubClient(Port, () => (_selfGuid, _selfName));
				_client.Received += OnInbound;
			}
		}

		public bool IsLeader => _isLeader;

		/// <summary>Leader is always "connected" (it's the hub); follower reflects its live socket. Used to fall
		/// back to un-coordinated (degraded) behavior when the hub is unreachable — never fail closed.</summary>
		public bool Connected => _isLeader || (_client?.IsConnected ?? false);

		/// <summary>Leader: target 0 broadcasts, else targets one follower. Follower: always goes to the leader.</summary>
		public void Publish(string type, string payload, ulong target = 0UL)
		{
			PartyMessage msg = new PartyMessage(type, _selfGuid, _selfName, payload, target);
			if (_isLeader)
			{
				if (target == 0) _server!.Broadcast(msg);
				else _server!.SendTo(target, msg);
			}
			else _client!.Send(msg);
		}

		/// <summary>One handler per type (last registration wins — sufficient for our features).</summary>
		public void Subscribe(string type, Action<PartyMessage> handler) => _subs[type] = handler;

		/// <summary>Members (excluding self) whose last message is within the window — the liveness set.</summary>
		public List<ulong> LiveMembers(TimeSpan within)
		{
			long cutoff = DateTime.UtcNow.Ticks - within.Ticks;
			List<ulong> live = new List<ulong>();
			foreach (KeyValuePair<ulong, long> kv in _lastSeen)
				if (kv.Key != _selfGuid && kv.Value >= cutoff) live.Add(kv.Key);
			return live;
		}

		public bool IsLive(ulong guid, TimeSpan within)
			=> _lastSeen.TryGetValue(guid, out long t) && t >= DateTime.UtcNow.Ticks - within.Ticks;

		public void Stop()
		{
			_server?.Stop();
			_client?.Stop();
		}

		private void OnInbound(PartyMessage msg)
		{
			if (msg.SenderGuid != 0) _lastSeen[msg.SenderGuid] = DateTime.UtcNow.Ticks;
			// Leader relays follower→follower: a message targeted at another member is forwarded to it, not
			// handled here (star topology — the hub is the only path between followers, e.g. a mage's WaterOffer).
			if (_isLeader && msg.TargetGuid != 0 && msg.TargetGuid != _selfGuid)
			{
				_server!.SendTo(msg.TargetGuid, msg);
				return;
			}
			if (msg.TargetGuid != 0 && msg.TargetGuid != _selfGuid) return;   // not for us (belt-and-suspenders)
			if (_subs.TryGetValue(msg.Type, out Action<PartyMessage>? h))
			{
				try { h(msg); }
				catch (Exception ex) { Logging.Write(System.Drawing.Color.Red, "[PartyBus] handler '{0}' threw: {1}", msg.Type, ex.Message); }
			}
		}

		private readonly bool _isLeader;
		private readonly ulong _selfGuid;
		private readonly string _selfName;
		private readonly PartyHubServer? _server;
		private readonly PartyHubClient? _client;
		private readonly ConcurrentDictionary<string, Action<PartyMessage>> _subs = new ConcurrentDictionary<string, Action<PartyMessage>>();
		private readonly ConcurrentDictionary<ulong, long> _lastSeen = new ConcurrentDictionary<ulong, long>();
	}
}
