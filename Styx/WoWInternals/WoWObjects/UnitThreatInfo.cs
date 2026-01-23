using System;
using System.Runtime.InteropServices;
using GreenMagic;

namespace Styx.WoWInternals.WoWObjects
{
	/// <summary>
	/// Threat information for a unit.
	/// </summary>
	public class UnitThreatInfo
	{
		private readonly ThreatEntry _entry;

		private UnitThreatInfo(ThreatEntry entry)
		{
			_entry = entry;
		}

		/// <summary>
		/// Raw threat percentage (0-100).
		/// </summary>
		public byte RawPercent => _entry.RawPercent;

		/// <summary>
		/// GUID of the threat target.
		/// </summary>
		public ulong TargetGuid => _entry.TargetGuid;

		/// <summary>
		/// Current threat status.
		/// </summary>
		public ThreatStatus ThreatStatus => (ThreatStatus)_entry.Status;

		/// <summary>
		/// Threat value.
		/// </summary>
		public uint ThreatValue => (uint)_entry.ThreatValue;

		public override string ToString()
		{
			return $"TargetGuid: {TargetGuid:X016}, RawPercent: {RawPercent}, ThreatStatus: {ThreatStatus}, ThreatValue: {ThreatValue}";
		}

		/// <summary>
		/// Gets threat info for a unit against a mob.
		/// </summary>
		internal static UnitThreatInfo GetThreatInfo(WoWUnit mob, WoWUnit unit)
		{
			var wow = ObjectManager.Wow;
			if (wow == null)
			{
				return new UnitThreatInfo(new ThreatEntry { TargetGuid = unit.Guid });
			}

			// Threat table offset in WoWUnit (3.3.5a)
			uint threatTableAddr = mob.BaseAddress + 4056;
			var threatTable = wow.Read<ThreatTable>(threatTableAddr);

			if (threatTable.TargetGuid != 0)
			{
				// Try to find unit in threat table
				if (TryFindInThreatTable(threatTable.HashTable, (uint)unit.Guid, unit.Guid, out uint entryAddr))
				{
					return new UnitThreatInfo(wow.Read<ThreatEntry>(entryAddr));
				}
			}

			// Not in threat table
			return new UnitThreatInfo(new ThreatEntry
			{
				Status = 0,
				RawPercent = 0,
				ThreatValue = 0,
				TargetGuid = unit.Guid,
				GuidHash = (uint)unit.Guid
			});
		}

		private static bool TryFindInThreatTable(HashTableInfo table, uint hash, ulong guid, out uint result)
		{
			result = 0;

			if (table.Mask == uint.MaxValue)
				return false;

			var wow = ObjectManager.Wow;
			if (wow == null)
				return false;

			uint index = hash & table.Mask;
			var bucket = wow.Read<HashBucket>(table.TablePtr + index * 12);

			uint ptr = bucket.NextPtr;
			while ((ptr & 1) == 0 && ptr != 0)
			{
				var entry = wow.Read<ThreatEntry>(ptr);
				if (entry.GuidHash == hash && entry.TargetGuid == guid)
				{
					result = ptr;
					return true;
				}
				ptr = wow.Read<uint>(ptr + 4 + bucket.EntrySize);
			}

			return false;
		}

		#region Structs

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		private struct ThreatEntry
		{
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
			private readonly byte[] _padding1;

			public uint GuidHash;

			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
			private readonly byte[] _padding2;

			public ulong TargetGuid;
			public byte Status;
			public byte RawPercent;
			private readonly ushort _padding3;
			public int ThreatValue;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		private struct ThreatTable
		{
			public readonly ulong TargetGuid;
			public readonly HashTableInfo HashTable;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		private struct HashTableInfo
		{
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
			private uint[] _reserved;
			public uint TablePtr;
			private uint _field;
			public uint Mask;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		private struct HashBucket
		{
			public uint EntrySize;
			private uint _field;
			public uint NextPtr;
		}

		#endregion
	}
}
