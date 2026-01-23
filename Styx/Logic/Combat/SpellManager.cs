using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using GreenMagic;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Combat
{
	public static class SpellManager
	{
		#region Constants - Offsets 3.3.5a (12340)

		// Cooldown list base
		private const uint CooldownListBase = 0xD3EDB4;  // 13890996 decimal

		#endregion

		private static int _lastKnownSpellCount;
		private static readonly Dictionary<string, WoWSpell> _knownSpells = new Dictionary<string, WoWSpell>(StringComparer.OrdinalIgnoreCase);

		public static Dictionary<string, WoWSpell> KnownSpells => _knownSpells;

		// HB 4.3.4 compatibility aliases
		public static Dictionary<string, WoWSpell> Spells => _knownSpells;
		public static Dictionary<string, WoWSpell> RawSpells => _knownSpells;

		public static int NumKnownSpells
		{
			get
			{
				LocalPlayer? me = StyxWoW.Me;
				return me?.KnownSpells.Count ?? 0;
			}
		}

		public static void Refresh()
		{
			if (_lastKnownSpellCount == 0 || NumKnownSpells != _lastKnownSpellCount)
			{
				Logging.Write("[SpellManager] Refreshing known spells...");
				_knownSpells.Clear();

				LocalPlayer? me = StyxWoW.Me;
				if (me == null) return;

				foreach (WoWSpell spell in me.KnownSpells)
				{
					if (!_knownSpells.ContainsKey(spell.Name))
					{
						_knownSpells.Add(spell.Name, spell);
						Logging.WriteDebug("[SpellManager] Added spell: {0}", spell.Name);
					}
				}

				_lastKnownSpellCount = NumKnownSpells;
				Logging.Write("[SpellManager] Spell refresh complete.");
			}
		}

		public static bool CastBarVisible
		{
			get
			{
				try
				{
					LocalPlayer? me = StyxWoW.Me;
					return me != null && me.Casting > 0;
				}
				catch
				{
					return false;
				}
			}
		}

		public static bool GlobalCooldown
		{
			get
			{
				try
				{
					Memory? memory = ObjectManager.Wow;
					if (memory == null) return false;

					long frequency;
					long counter;
					QueryPerformanceFrequency(out frequency);
					QueryPerformanceCounter(out counter);
					long currentTime = counter * 1000L / frequency;

					uint cooldownPtr = memory.Read<uint>(CooldownListBase);

					while (cooldownPtr != 0U && (cooldownPtr & 1U) == 0U)
					{
						uint startTime = memory.Read<uint>(cooldownPtr + 16U);
						uint duration = memory.Read<uint>(cooldownPtr + 44U);

						if ((ulong)(startTime + duration) > (ulong)currentTime)
						{
							return true;
						}

						cooldownPtr = memory.Read<uint>(cooldownPtr + 4U);
					}

					return false;
				}
				catch
				{
					return false;
				}
			}
		}

		public static TimeSpan GlobalCooldownLeft
		{
			get
			{
				try
				{
					Memory? memory = ObjectManager.Wow;
					if (memory == null) return TimeSpan.Zero;

					long frequency;
					long counter;
					QueryPerformanceFrequency(out frequency);
					QueryPerformanceCounter(out counter);
					long currentTime = counter * 1000L / frequency;

					uint cooldownPtr = memory.Read<uint>(CooldownListBase);

					while (cooldownPtr != 0U && (cooldownPtr & 1U) == 0U)
					{
						uint startTime = memory.Read<uint>(cooldownPtr + 16U);
						uint duration = memory.Read<uint>(cooldownPtr + 44U);

						long endTime = startTime + duration;
						if (endTime > currentTime)
						{
							return TimeSpan.FromMilliseconds(endTime - currentTime);
						}

						cooldownPtr = memory.Read<uint>(cooldownPtr + 4U);
					}

					return TimeSpan.Zero;
				}
				catch
				{
					return TimeSpan.Zero;
				}
			}
		}

		public static bool HasSpell(string name)
		{
			return _knownSpells.ContainsKey(name);
		}

		public static bool HasSpell(int id)
		{
			foreach (var spell in _knownSpells.Values)
			{
				if (spell.Id == id)
					return true;
			}
			return false;
		}

		public static WoWSpell? GetSpellByName(string name)
		{
			if (_knownSpells.TryGetValue(name, out WoWSpell? spell))
			{
				return spell;
			}
			return null;
		}

		public static bool CanCastSpell(string name)
		{
			if (!HasSpell(name))
				return false;

			if (GlobalCooldown)
				return false;

			WoWSpell? spell = GetSpellByName(name);
			if (spell == null)
				return false;

			// Check if spell is on cooldown
			if (spell.Cooldown)
				return false;

			return true;
		}

		// HB 4.3.4 compatibility wrappers
		public static bool CanCast(string spellName) => CanCastSpell(spellName);

		public static bool CanCast(int spellId, WoWUnit target, bool checkRange = true, bool checkMovement = false)
		{
			// Convert spellId to name and use existing CanCast
			WoWSpell? spell = Spells.Values.FirstOrDefault(s => s.Id == spellId);
			if (spell == null)
				return false;
			return CanCast(spell.Name, target, checkRange, checkMovement);
		}

		public static bool CanCast(string spellName, WoWUnit target, bool checkRange = true, bool checkMovement = false)
		{
			if (!CanCast(spellName))
		{
			Logging.WriteDebug("[SpellManager] CanCast({0}) = false", spellName);
			return false;
		}
		if (target == null || !target.IsValid)
		{
			Logging.WriteDebug("[SpellManager] CanCast({0}) target invalid", spellName);
			return false;
		}
		Logging.WriteDebug("[SpellManager] CanCast({0}) = true", spellName);
		// Additional checks could be added here for range/movement
		return true;
	}

	public static bool Cast(string spellName) => CastSpell(spellName);

	public static bool Cast(string spellName, WoWUnit target)
	{
		Logging.WriteDebug("[SpellManager] Cast({0}, {1}) called", spellName, target?.Name ?? "null");
		if (target == null)
			return Cast(spellName);
				return Cast(spellName);
			
			// Target the unit and cast
			StyxWoW.ResetAfk();
			target.Target();
			return CastSpell(spellName);
		}

		public static bool Cast(WoWSpell spell)
		{
			if (spell == null)
				return false;
			return CastSpell(spell.Name);
		}

		public static void CastSpellById(uint spellId)
		{
			CastSpellById((int)spellId);
		}

		public static void CastSpellById(int spellId)
		{
			CastSpellById(spellId, 0UL);
		}

		public static void CastSpellById(int spellId, ulong targetGuid)
		{
			StyxWoW.ResetAfk();
			// Use Lua to cast spell for now
			if (targetGuid == 0UL || targetGuid == StyxWoW.Me?.Guid)
			{
				Lua.DoString(string.Format("CastSpellByID({0})", spellId));
			}
			else
			{
				// Target the unit first, then cast
				Lua.DoString(string.Format("CastSpellByID({0})", spellId));
			}
		}

		public static void CastSpellById(int spellId, WoWUnit target)
		{
			CastSpellById(spellId, target.Guid);
		}

		public static bool CastSpell(string name)
		{
			return CastSpell(name, 0UL, true);
		}

		public static bool CastableSpell(WoWSpell spell)
		{
			try
			{
				if (spell.Id == 0)
					return false;

				LocalPlayer? me = StyxWoW.Me;
				if (me == null)
					return false;

				if (me.IsCasting)
					return false;

				if (!_knownSpells.ContainsKey(spell.Name))
					return false;

				// Check if spell is on cooldown
				if (spell.Cooldown)
					return false;

				// Check aura effects
				for (int i = 0; i < 3; i++)
				{
					var effect = spell.GetSpellEffect(i);
					if (effect == null) continue;

					WoWApplyAuraType auraType = effect.AuraType;
					if (auraType == WoWApplyAuraType.ModStat ||
						auraType == WoWApplyAuraType.ModStealth ||
						auraType == WoWApplyAuraType.ModSpeedAlways ||
						auraType == WoWApplyAuraType.ModSpeedFlight ||
						auraType == WoWApplyAuraType.PeriodicHeal ||
						auraType == WoWApplyAuraType.ModResistance ||
						auraType == WoWApplyAuraType.FeignDeath)
					{
						return true;
					}
				}

				// Check power type and cost
				WoWPowerType powerType = spell.PowerType;
				if (powerType == WoWPowerType.Runes)
					return true;

				int powerCost = spell.PowerCost;
				if (powerCost == 0)
					return true;

				// Check if moving (for cast time spells)
				if (spell.CastTime != 0U && me.IsMoving)
					return false;

				// Check available power
				if (me.GetCurrentPower(powerType) >= powerCost)
					return true;

				return false;
			}
			catch (Exception ex)
			{
				Logging.WriteException(ex);
				return false;
			}
		}

		public static void StopCasting()
		{
			try
			{
				LocalPlayer? me = StyxWoW.Me;
				if (me != null && me.IsCasting)
				{
					Lua.DoString("SpellStopCasting()");
				}
			}
			catch (Exception ex)
			{
				Logging.WriteException(ex);
			}
		}

		public static bool CastSpell(string name, WoWUnit target)
		{
			return CastSpell(name, target.Guid, true);
		}

		public static bool CastSpell(string name, ulong targetGuid, bool returnImmediately)
		{
			if (!CanCastSpell(name))
			{
				Logging.WriteDebug("[SpellManager] Cannot cast spell: {0}", name);
				return false;
			}

			WoWSpell? spell = GetSpellByName(name);
			if (spell == null)
				return false;

			CastSpellById(spell.Id, targetGuid);

			if (!returnImmediately)
			{
				// Wait for cast to complete
				System.Threading.Thread.Sleep((int)spell.CastTime);

				// Wait for GCD
				while (GlobalCooldown)
				{
					System.Threading.Thread.Sleep(10);
				}
			}

			Logging.WriteDebug("[SpellManager] Cast spell: {0}", name);
			return true;
		}

		// Struct for Spell_C__HandleTerrainClick
		[Flags]
		public enum MouseButton : uint
		{
			None = 0U,
			Left = 1U,
			Middle = 2U,
			Right = 4U,
			XButton1 = 8U,
			XButton2 = 16U
		}

		private enum MouseButtonByte : byte { Left = 0, Right = 1 }
		
		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
		private struct TerrainClickInfo
		{
			public WoWPoint Location;
			public ulong TargetGuid;
			public MouseButtonByte Button;
		}
		
		// Spell_C__HandleTerrainClick function address (WoW 3.3.5a)
		private const uint Spell_C__HandleTerrainClick = 0x80B740; // 8438592U

		public static bool ClickRemoteLocation(WoWPoint location)
		{
			StyxWoW.ResetAfk();
			
			ExecutorRand? executor = ObjectManager.Executor;
			if (executor == null)
			{
				Logging.WriteDebug("[SpellManager] Invalid executor for ClickRemoteLocation");
				return false;
			}

			var click = new TerrainClickInfo
			{
				Location = location,
				TargetGuid = 0UL,
				Button = MouseButtonByte.Left
			};

			try
			{
				lock (executor.AssemblyLock)
				{
					// Allocate memory for the click struct
					uint structPtr = executor.Memory.AllocateMemory(System.Runtime.InteropServices.Marshal.SizeOf(click));
					if (structPtr == 0U)
					{
						Logging.WriteDebug("[SpellManager] Could not allocate memory for ClickRemoteLocation");
						return false;
					}

					try
					{
						// Write struct to allocated memory
						executor.Memory.Write(structPtr, click);
						
						// Call Spell_C__HandleTerrainClick
						executor.Clear();
						executor.AddLine("push {0}", structPtr);
						executor.AddLine("call {0}", Spell_C__HandleTerrainClick);
						executor.AddLine("add esp, 4");  // Clean up stack (cdecl)
						executor.AddLine("retn");
						executor.Execute();
						
						int result = executor.Memory.Read<int>(executor.ReturnPointer);
						return result != 0;
					}
					finally
					{
						executor.Memory.FreeMemory(structPtr);
					}
				}
			}
			catch (Exception ex)
			{
				Logging.WriteException(ex);
				return false;
			}
		}

		#region Native Methods

		[DllImport("kernel32.dll")]
		private static extern bool QueryPerformanceFrequency(out long frequency);

		[DllImport("kernel32.dll")]
		private static extern bool QueryPerformanceCounter(out long counter);

		#endregion
	}
}
