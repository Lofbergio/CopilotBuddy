using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Styx.Helpers;
using Styx.RemotableObjects;

namespace PartyBot.IPC
{
	/// <summary>
	/// TCP client that connects to the LeaderPlugin RemotingServer on port 1337
	/// and polls for BotMessage updates every 76ms.
	/// Replaces the original HB System.Runtime.Remoting.Channels.Tcp channel
	/// which is not available in .NET 5+.
	/// </summary>
	public class RemotingClient
	{
		public event ClientBotMessageRecievedEventArgs? ClientRecievedBotMessage
		{
			add
			{
				ClientBotMessageRecievedEventArgs? current = _handler;
				ClientBotMessageRecievedEventArgs? exchanged;
				do
				{
					exchanged = current;
					ClientBotMessageRecievedEventArgs? combined = (ClientBotMessageRecievedEventArgs?)Delegate.Combine(exchanged, value);
					current = Interlocked.CompareExchange(ref _handler, combined, exchanged);
				}
				while (current != exchanged);
			}
			remove
			{
				ClientBotMessageRecievedEventArgs? current = _handler;
				ClientBotMessageRecievedEventArgs? exchanged;
				do
				{
					exchanged = current;
					ClientBotMessageRecievedEventArgs? removed = (ClientBotMessageRecievedEventArgs?)Delegate.Remove(exchanged, value);
					current = Interlocked.CompareExchange(ref _handler, removed, exchanged);
				}
				while (current != exchanged);
			}
		}

		public RemotingClient()
		{
			// Verify server is reachable before starting polling thread
			try
			{
				using TcpClient probe = new TcpClient();
				probe.Connect("127.0.0.1", 1337);
			}
			catch (Exception)
			{
				throw new InvalidOperationException(
					"Remoting server has not been started yet. Please make sure your leader is started and running the LeaderPlugin.");
			}

			_thread = new Thread(Poll)
			{
				IsBackground = true,
				Name = "RemotingClientEventChecker"
			};
			_thread.Start();
			if (_thread.IsAlive)
				Logging.WriteDebug("Remoting client started");
		}

		private void Poll()
		{
			while (true)
			{
				try
				{
					using TcpClient tcp = new TcpClient();
					tcp.Connect("127.0.0.1", 1337);
					using NetworkStream stream = tcp.GetStream();
					using StreamReader reader = new StreamReader(stream, Encoding.UTF8);

					while (true)
					{
						string? line = reader.ReadLine();
						if (line == null) break;

						BotMessage? message = JsonSerializer.Deserialize<BotMessage>(line);
						if (message == null) continue;

						if (message.Timestamp > _lastTimestamp || _lastTimestamp == DateTime.MinValue)
						{
							_lastTimestamp = message.Timestamp;
							_handler?.Invoke(message);
						}

						Thread.Sleep(76);
					}
				}
				catch (Exception ex)
				{
					Logging.WriteException(ex);
					Thread.Sleep(500);
				}
			}
		}

		public void SetMessage(BotMessage message)
		{
			// Send message to server (used by leader side; not used from DiscoBot)
			try
			{
				using TcpClient tcp = new TcpClient();
				tcp.Connect("127.0.0.1", 1337);
				using NetworkStream stream = tcp.GetStream();
				using StreamWriter writer = new StreamWriter(stream, Encoding.UTF8);
				writer.WriteLine(JsonSerializer.Serialize(message));
				writer.Flush();
			}
			catch (Exception ex)
			{
				Logging.WriteException(ex);
			}
		}

		private ClientBotMessageRecievedEventArgs? _handler;
		private readonly Thread _thread;
		private DateTime _lastTimestamp;
	}
}

