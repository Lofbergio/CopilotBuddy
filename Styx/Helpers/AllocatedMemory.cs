using System;
using System.Collections.Generic;
using GreenMagic;
using Styx.WoWInternals;

namespace Styx.Helpers
{
	/// <summary>
	/// Manages allocated memory chunks in the WoW process.
	/// Port from HB 3.3.5a Styx.Helpers.AllocatedMemory
	/// </summary>
	public class AllocatedMemory : IDisposable
	{
		private int _currentOffset;
		private readonly Dictionary<string, int> _allocatedChunks = new Dictionary<string, int>();
		private uint _address;

		public uint Address
		{
			get => _address;
			private set => _address = value;
		}

		public AllocatedMemory(int bytes)
		{
			Memory? memory = ObjectManager.Executor?.Memory;
			if (memory != null)
			{
				Address = memory.AllocateMemory(bytes);
			}
		}

		/// <summary>
		/// Gets the address of a named allocated chunk.
		/// </summary>
		public uint this[string allocatedName]
		{
			get
			{
				return (uint)(Address + (uint)_allocatedChunks[allocatedName]);
			}
		}

		/// <summary>
		/// Writes a value at a specific byte offset.
		/// </summary>
		public void Write<T>(int offsetInBytes, T value) where T : struct
		{
			Memory? memory = ObjectManager.Executor?.Memory;
			if (memory != null)
			{
				memory.Write(Address + (uint)offsetInBytes, value);
			}
		}

		/// <summary>
		/// Writes a value to a named allocated chunk.
		/// </summary>
		public void Write<T>(string allocatedName, T value) where T : struct
		{
			if (!_allocatedChunks.ContainsKey(allocatedName))
			{
				AllocateOfChunk<T>(allocatedName);
			}
			Memory? memory = ObjectManager.Executor?.Memory;
			if (memory != null)
			{
				memory.Write(this[allocatedName], value);
			}
		}

		/// <summary>
		/// Writes a value to a named allocated chunk at a specific offset.
		/// </summary>
		public void Write<T>(string allocatedName, int offsetInBytes, T value) where T : struct
		{
			Memory? memory = ObjectManager.Executor?.Memory;
			if (memory != null)
			{
				memory.Write(this[allocatedName] + (uint)offsetInBytes, value);
			}
		}

		/// <summary>
		/// Writes a single byte at a specific byte offset.
		/// </summary>
		public void WriteByte(int offsetInBytes, byte value)
		{
			Memory? memory = ObjectManager.Executor?.Memory;
			if (memory != null)
			{
				memory.Write(Address + (uint)offsetInBytes, value);
			}
		}

		/// <summary>
		/// Writes bytes at a specific byte offset.
		/// </summary>
		public void WriteBytes(int offsetInBytes, byte[] bytes)
		{
			Memory? memory = ObjectManager.Executor?.Memory;
			if (memory != null)
			{
				memory.WriteBytes(Address + (uint)offsetInBytes, bytes);
			}
		}

		/// <summary>
		/// Writes bytes to a named allocated chunk.
		/// </summary>
		public void WriteBytes(string allocatedName, byte[] bytes)
		{
			if (!_allocatedChunks.ContainsKey(allocatedName))
			{
				AllocateOfChunk(allocatedName, bytes.Length);
			}
			Memory? memory = ObjectManager.Executor?.Memory;
			if (memory != null)
			{
				memory.WriteBytes(this[allocatedName], bytes);
			}
		}

		/// <summary>
		/// Reads a value at a specific byte offset.
		/// </summary>
		public T Read<T>(int offsetInBytes) where T : struct
		{
			Memory? memory = ObjectManager.Executor?.Memory;
			if (memory == null)
				return default;
			return memory.Read<T>(Address + (uint)offsetInBytes);
		}

		/// <summary>
		/// Reads a value from a named allocated chunk.
		/// </summary>
		public T Read<T>(string allocatedName) where T : struct
		{
			Memory? memory = ObjectManager.Executor?.Memory;
			if (memory == null)
				return default;
			return memory.Read<T>(this[allocatedName]);
		}

		/// <summary>
		/// Gets address at a specific byte offset.
		/// </summary>
		public uint GetAddress(int offsetInBytes)
		{
			return Address + (uint)offsetInBytes;
		}

		/// <summary>
		/// Allocates a chunk of memory for a type with a given name.
		/// </summary>
		public void AllocateOfChunk<T>(string allocatedName) where T : struct
		{
			_allocatedChunks.Add(allocatedName, _currentOffset);
			_currentOffset += FastSize<T>.Size;
		}

		/// <summary>
		/// Allocates a chunk of memory with a given name and size.
		/// </summary>
		public void AllocateOfChunk(string allocatedName, int bytes)
		{
			_allocatedChunks.Add(allocatedName, _currentOffset);
			_currentOffset += bytes;
		}

		/// <summary>
		/// Gets the address of a named allocated chunk.
		/// </summary>
		public uint GetAllocatedChunk(string allocatedName)
		{
			return (uint)(Address + (uint)_allocatedChunks[allocatedName]);
		}

		public void Dispose()
		{
			if (Address != 0)
			{
				Memory? memory = ObjectManager.Executor?.Memory;
				if (memory != null)
				{
					memory.FreeMemory(Address);
				}
				Address = 0;
			}
		}
	}
}
