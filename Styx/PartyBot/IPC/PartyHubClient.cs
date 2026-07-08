using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Styx.Helpers;

namespace PartyBot.IPC
{
	/// <summary>
	/// Duplex party hub — the FOLLOWER side. Connects to the leader's PartyHubServer, reads downstream
	/// messages on its own thread (→ Received), and sends upstream via Send() (guarded, direct-write — the
	/// follower's send volume is low: a progress report every few seconds, the odd claim). Reconnects with
	/// backoff; when disconnected, Send() is a no-op (fail degraded — never block the bot waiting on the hub).
	/// On connect it announces a "Hello" so the server binds this connection to our GUID for targeted sends.
	/// </summary>
	public class PartyHubClient
	{
		public event Action<PartyMessage>? Received;

		public PartyHubClient(int port, Func<(ulong guid, string name)> identity)
		{
			_port = port;
			_identity = identity;
			_thread = new Thread(Run) { IsBackground = true, Name = "PartyHubClient" };
			_thread.Start();
		}

		public bool IsConnected { get { lock (_writeLock) return _writer != null; } }

		public void Send(PartyMessage msg)
		{
			lock (_writeLock)
			{
				if (_writer == null) return;   // not connected — degraded, drop it
				try { _writer.WriteLine(JsonSerializer.Serialize(msg, _json)); }
				catch { _writer = null; }
			}
		}

		public void Stop()
		{
			_stopped = true;
			try { _tcp?.Close(); } catch { }
		}

		private void Run()
		{
			while (!_stopped)
			{
				try
				{
					using TcpClient tcp = new TcpClient();
					tcp.Connect("127.0.0.1", _port);
					_tcp = tcp;
					using NetworkStream stream = tcp.GetStream();
					using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
					StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
					lock (_writeLock) _writer = writer;

					(ulong guid, string name) = _identity();
					Send(new PartyMessage("Hello", guid, name, ""));

					try
					{
						while (!_stopped)
						{
							string? line = reader.ReadLine();
							if (line == null) break;
							PartyMessage? msg;
							try { msg = JsonSerializer.Deserialize<PartyMessage>(line, _json); }
							catch { continue; }
							if (msg != null)
							{
								try { Received?.Invoke(msg); }
								catch (Exception ex) { Logging.WriteException(ex); }
							}
						}
					}
					finally { lock (_writeLock) _writer = null; }
				}
				catch (Exception)
				{
					lock (_writeLock) _writer = null;
					if (_stopped) break;
					Thread.Sleep(500);   // reconnect backoff
				}
			}
		}

		private readonly int _port;
		private readonly Func<(ulong, string)> _identity;
		private readonly Thread _thread;
		private volatile bool _stopped;
		private TcpClient? _tcp;
		private StreamWriter? _writer;
		private readonly object _writeLock = new object();
		private static readonly JsonSerializerOptions _json = new JsonSerializerOptions { IncludeFields = true };
	}
}
