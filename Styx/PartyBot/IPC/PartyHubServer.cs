using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Styx.Helpers;

namespace PartyBot.IPC
{
	/// <summary>
	/// Duplex party hub — the LEADER side. Star topology: every follower connects here; the leader is the
	/// sole hub (matches the coordination invariant "followers announce, leader arbitrates"). Replaces the
	/// old write-only RemotingServer for VibeParty (DiscoBot keeps the legacy one). Each connection gets ONE
	/// reader thread (inbound → Received) and ONE writer thread draining a per-client outbound queue — never
	/// two threads on the same socket direction, which is the safe way to run a NetworkStream full-duplex.
	/// </summary>
	public class PartyHubServer
	{
		public event Action<PartyMessage>? Received;

		public PartyHubServer(int port)
		{
			_listener = new TcpListener(IPAddress.Loopback, port);
			_listener.Start();   // let a bind failure throw to the caller (leader already up elsewhere)
			_acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "PartyHubAccept" };
			_acceptThread.Start();
			Logging.Write("[PartyHub] server listening on 127.0.0.1:{0}", port);
		}

		/// <summary>Send to every connected follower.</summary>
		public void Broadcast(PartyMessage msg)
		{
			string line = JsonSerializer.Serialize(msg, _json);
			lock (_clientsLock)
				foreach (ClientConn c in _clients)
					c.Enqueue(line);
		}

		/// <summary>Send to the one follower that announced this GUID (no-op if not connected).</summary>
		public void SendTo(ulong guid, PartyMessage msg)
		{
			string line = JsonSerializer.Serialize(msg, _json);
			lock (_clientsLock)
				foreach (ClientConn c in _clients)
					if (c.Guid == guid) { c.Enqueue(line); break; }
		}

		public void Stop()
		{
			if (_stopped) return;
			_stopped = true;
			try { _listener.Stop(); } catch { }
			lock (_clientsLock)
			{
				foreach (ClientConn c in _clients) c.Close();
				_clients.Clear();
			}
			Logging.Write("[PartyHub] server stopped");
		}

		private void AcceptLoop()
		{
			while (!_stopped)
			{
				try
				{
					TcpClient tcp = _listener.AcceptTcpClient();
					ClientConn conn = new ClientConn(tcp);
					lock (_clientsLock) _clients.Add(conn);
					conn.Start(OnClientMessage, () => Drop(conn));
				}
				catch (Exception)
				{
					if (_stopped) break;   // Stop() unblocks AcceptTcpClient — normal shutdown
					Thread.Sleep(500);
				}
			}
		}

		private void OnClientMessage(ClientConn conn, PartyMessage msg)
		{
			if (conn.Guid == 0 && msg.SenderGuid != 0) conn.Guid = msg.SenderGuid;   // first message binds the conn
			try { Received?.Invoke(msg); } catch (Exception ex) { Logging.WriteException(ex); }
		}

		private void Drop(ClientConn conn)
		{
			lock (_clientsLock) _clients.Remove(conn);
			conn.Close();
		}

		private readonly TcpListener _listener;
		private readonly Thread _acceptThread;
		private readonly List<ClientConn> _clients = new List<ClientConn>();
		private readonly object _clientsLock = new object();
		private volatile bool _stopped;
		private static readonly JsonSerializerOptions _json = new JsonSerializerOptions { IncludeFields = true };

		// One connected follower: reader thread + outbound-queue writer thread.
		private sealed class ClientConn
		{
			public ulong Guid;   // announced by the follower's first message; used for targeted SendTo

			public ClientConn(TcpClient tcp) { _tcp = tcp; }

			public void Start(Action<ClientConn, PartyMessage> onMessage, Action onClosed)
			{
				NetworkStream stream = _tcp.GetStream();
				_writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
				_reader = new StreamReader(stream, Encoding.UTF8);

				new Thread(() => WriteLoop()) { IsBackground = true, Name = "PartyHubClientWriter" }.Start();
				new Thread(() => ReadLoop(onMessage, onClosed)) { IsBackground = true, Name = "PartyHubClientReader" }.Start();
			}

			public void Enqueue(string line)
			{
				try { if (!_outbound.IsAddingCompleted) _outbound.Add(line); } catch { }
			}

			public void Close()
			{
				try { _outbound.CompleteAdding(); } catch { }   // unblocks the writer
				try { _tcp.Close(); } catch { }                 // unblocks the reader
			}

			private void WriteLoop()
			{
				try { foreach (string line in _outbound.GetConsumingEnumerable()) _writer!.WriteLine(line); }
				catch (Exception) { /* connection gone */ }
			}

			private void ReadLoop(Action<ClientConn, PartyMessage> onMessage, Action onClosed)
			{
				try
				{
					while (true)
					{
						string? line = _reader!.ReadLine();
						if (line == null) break;
						PartyMessage? msg;
						try { msg = JsonSerializer.Deserialize<PartyMessage>(line, _json); }
						catch { continue; }   // tolerate a garbled/partial line
						if (msg != null) onMessage(this, msg);
					}
				}
				catch (Exception) { /* disconnect */ }
				finally { onClosed(); }
			}

			private readonly TcpClient _tcp;
			private StreamWriter? _writer;
			private StreamReader? _reader;
			private readonly BlockingCollection<string> _outbound = new BlockingCollection<string>();
		}
	}
}
