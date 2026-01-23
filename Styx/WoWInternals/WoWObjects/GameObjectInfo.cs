using System;
using System.Collections.Generic;

namespace Styx.WoWInternals.WoWObjects
{
	/// <summary>
	/// Cached information about a game object.
	/// </summary>
	public class GameObjectInfo
	{
		private readonly WoWCache.WoWCache.InfoBlock? _infoBlock;
		private KeyValuePair<DateTime, WoWCache.WoWCache.GameObjectCacheEntry>? _cachedEntry;
		private List<ItemInfo>? _questItems;

		private GameObjectInfo(WoWCache.WoWCache.InfoBlock block)
		{
			_infoBlock = block;
		}

		/// <summary>
		/// Base address of the cache entry.
		/// </summary>
		public uint BaseAddress => _infoBlock?.Address ?? 0;

		/// <summary>
		/// Game object ID.
		/// </summary>
		public uint Id => _infoBlock?.Id ?? 0;

		/// <summary>
		/// Whether this game object is cached.
		/// </summary>
		public bool IsCached => _infoBlock != null;

		/// <summary>
		/// Quest items associated with this game object.
		/// </summary>
		public List<ItemInfo> QuestItems => GetQuestItems();

		/// <summary>
		/// Internal cache entry.
		/// </summary>
		public WoWCache.WoWCache.GameObjectCacheEntry InternalInfo => GetGameObjectInfo();

		/// <summary>
		/// Gets the game object cache entry with caching.
		/// </summary>
		public WoWCache.WoWCache.GameObjectCacheEntry GetGameObjectInfo()
		{
			var now = DateTime.Now;

			if (_cachedEntry != null)
			{
				var cached = _cachedEntry.Value;
				var elapsed = now.Subtract(cached.Key);
				if (elapsed.TotalMilliseconds <= 3000.0)
				{
					return cached.Value;
				}
			}

			var entry = _infoBlock?.GameObject ?? default;
			_cachedEntry = new KeyValuePair<DateTime, WoWCache.WoWCache.GameObjectCacheEntry>(now, entry);
			return entry;
		}

		/// <summary>
		/// Gets the quest items for this game object.
		/// </summary>
		public List<ItemInfo> GetQuestItems()
		{
			if (_questItems == null)
			{
				_questItems = new List<ItemInfo>();
				int[] questItemIds = InternalInfo.QuestItems;

				for (int i = 0; i < questItemIds.Length; i++)
				{
					if (questItemIds[i] > 0)
					{
						var itemInfo = ItemInfo.FromId((uint)questItemIds[i]);
						if (itemInfo != null)
						{
							_questItems.Add(itemInfo);
						}
					}
				}
			}
			return _questItems;
		}

		/// <summary>
		/// Creates a GameObjectInfo from an ID.
		/// </summary>
		public static GameObjectInfo? FromId(uint gameObjectId)
		{
			var infoBlock = StyxWoW.Cache[WoWCache.CacheDb.GameObject].GetInfoBlockById(gameObjectId);
			if (infoBlock == null)
			{
				return null;
			}
			return new GameObjectInfo(infoBlock);
		}
	}
}
