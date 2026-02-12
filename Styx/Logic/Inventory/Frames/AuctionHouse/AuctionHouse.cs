using System;
using System.Collections.Generic;
using Styx.WoWInternals;

namespace Styx.Logic.Inventory.Frames.AuctionHouse
{
    /// <summary>
    /// FEAT-34: Auction house manager. Provides search, browse, bid, buyout,
    /// and listing operations via Lua (WotLK AH API).
    /// Ported from HB 4.3.4 AuctionHouse.
    /// </summary>
    public static class AuctionHouse
    {
        /// <summary>
        /// The auction frame instance.
        /// </summary>
        public static AuctionFrame Frame => AuctionFrame.Instance;

        /// <summary>
        /// Whether the AH frame is open and we can interact.
        /// </summary>
        public static bool IsOpen => Frame != null && Frame.IsOpen;

        /// <summary>
        /// Auction list types for query.
        /// </summary>
        public enum AuctionListType
        {
            List = 0,
            Bidder = 1,
            Owner = 2
        }

        #region Query

        /// <summary>
        /// Performs an auction search query via Lua QueryAuctionItems.
        /// </summary>
        /// <param name="searchText">Item name to search for (empty = all)</param>
        /// <param name="page">Page number (0-based)</param>
        /// <param name="minLevel">Minimum item level filter (0 = no filter)</param>
        /// <param name="maxLevel">Maximum item level filter (0 = no filter)</param>
        /// <param name="usable">Only show usable items</param>
        /// <param name="quality">Minimum quality filter (-1 = no filter)</param>
        /// <param name="isExact">Exact name match</param>
        public static void PerformSearch(string searchText = "", int page = 0,
            int minLevel = 0, int maxLevel = 0, bool usable = false,
            int quality = -1, bool isExact = false)
        {
            string q = quality >= 0 ? quality.ToString() : "nil";
            string u = usable ? "1" : "nil";
            string e = isExact ? "1" : "nil";
            Lua.DoString(
                $"QueryAuctionItems(\"{searchText}\",{minLevel},{maxLevel},nil,nil,nil,{page},{u},{q},{e})");
        }

        /// <summary>
        /// Gets the number of auction items returned by the last query.
        /// Returns (batchCount, totalCount).
        /// </summary>
        public static (int batch, int total) GetNumAuctionItems(AuctionListType listType = AuctionListType.List)
        {
            try
            {
                string type = listType switch
                {
                    AuctionListType.Bidder => "bidder",
                    AuctionListType.Owner => "owner",
                    _ => "list"
                };
                var results = Lua.GetReturnValues(
                    $"local b,t = GetNumAuctionItems(\"{type}\"); return b,t");
                if (results != null && results.Count >= 2)
                {
                    return (Lua.ParseLuaValue<int>(results[0]),
                            Lua.ParseLuaValue<int>(results[1]));
                }
            }
            catch { }
            return (0, 0);
        }

        /// <summary>
        /// Gets the number of list auctions (from last search).
        /// </summary>
        public static int NumListAuctions
        {
            get
            {
                var (batch, _) = GetNumAuctionItems(AuctionListType.List);
                return batch;
            }
        }

        /// <summary>
        /// Gets the total number of list auctions matching last search.
        /// </summary>
        public static int FullNumListAuctions
        {
            get
            {
                var (_, total) = GetNumAuctionItems(AuctionListType.List);
                return total;
            }
        }

        /// <summary>
        /// Number of auctions the player is bidding on.
        /// </summary>
        public static int NumBidderAuctions
        {
            get
            {
                var (batch, _) = GetNumAuctionItems(AuctionListType.Bidder);
                return batch;
            }
        }

        /// <summary>
        /// Number of auctions owned by the player.
        /// </summary>
        public static int NumOwnerAuctions
        {
            get
            {
                var (batch, _) = GetNumAuctionItems(AuctionListType.Owner);
                return batch;
            }
        }

        #endregion

        #region Get Auction Data

        /// <summary>
        /// Gets auction data at the specified index (1-based) for the given list type.
        /// Uses Lua GetAuctionItemInfo.
        /// </summary>
        public static WoWAuction? GetAuction(int index, AuctionListType listType = AuctionListType.List)
        {
            try
            {
                string type = listType switch
                {
                    AuctionListType.Bidder => "bidder",
                    AuctionListType.Owner => "owner",
                    _ => "list"
                };
                var results = Lua.GetReturnValues(
                    $"local name,tex,cnt,q,canUse,lvl,minBid,inc,buyout,curBid,highBid,owner = " +
                    $"GetAuctionItemInfo(\"{type}\",{index}); " +
                    $"return name or '',tex or 0,cnt or 0,q or 0,canUse and 1 or 0,lvl or 0," +
                    $"minBid or 0,inc or 0,buyout or 0,curBid or 0,highBid and 1 or 0,owner or ''");

                if (results == null || results.Count < 12 || string.IsNullOrEmpty(results[0]))
                    return null;

                return new WoWAuction
                {
                    Name = results[0],
                    Texture = Lua.ParseLuaValue<int>(results[1]),
                    Count = Lua.ParseLuaValue<int>(results[2]),
                    Quality = Lua.ParseLuaValue<int>(results[3]),
                    CanUse = Lua.ParseLuaValue<int>(results[4]) != 0,
                    Level = Lua.ParseLuaValue<int>(results[5]),
                    MinBid = Lua.ParseLuaValue<int>(results[6]),
                    MinIncrement = Lua.ParseLuaValue<int>(results[7]),
                    BuyoutPrice = Lua.ParseLuaValue<int>(results[8]),
                    CurrentBid = Lua.ParseLuaValue<int>(results[9]),
                    IsHighBidder = Lua.ParseLuaValue<int>(results[10]) != 0,
                    Owner = results[11]
                };
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Gets all auctions from the last search batch.
        /// </summary>
        public static List<WoWAuction> GetListAuctions()
        {
            var list = new List<WoWAuction>();
            int count = NumListAuctions;
            for (int i = 1; i <= count; i++)
            {
                var auction = GetAuction(i, AuctionListType.List);
                if (auction != null)
                    list.Add(auction);
            }
            return list;
        }

        /// <summary>
        /// Gets all auctions the player is bidding on.
        /// </summary>
        public static List<WoWAuction> GetBidderAuctions()
        {
            var list = new List<WoWAuction>();
            int count = NumBidderAuctions;
            for (int i = 1; i <= count; i++)
            {
                var auction = GetAuction(i, AuctionListType.Bidder);
                if (auction != null)
                    list.Add(auction);
            }
            return list;
        }

        /// <summary>
        /// Gets all auctions owned by the player.
        /// </summary>
        public static List<WoWAuction> GetOwnedAuctions()
        {
            var list = new List<WoWAuction>();
            int count = NumOwnerAuctions;
            for (int i = 1; i <= count; i++)
            {
                var auction = GetAuction(i, AuctionListType.Owner);
                if (auction != null)
                    list.Add(auction);
            }
            return list;
        }

        #endregion

        #region Actions

        /// <summary>
        /// Places a bid on an auction at the given index.
        /// </summary>
        public static void PlaceBid(int index, int bid, AuctionListType listType = AuctionListType.List)
        {
            string type = listType switch
            {
                AuctionListType.Bidder => "bidder",
                AuctionListType.Owner => "owner",
                _ => "list"
            };
            Lua.DoString($"PlaceAuctionBid(\"{type}\",{index},{bid})");
        }

        /// <summary>
        /// Buys out an auction at the given index (1-based).
        /// </summary>
        public static void Buyout(int index, AuctionListType listType = AuctionListType.List)
        {
            var auction = GetAuction(index, listType);
            if (auction != null && auction.HasBuyout)
            {
                PlaceBid(index, auction.BuyoutPrice, listType);
            }
        }

        /// <summary>
        /// Cancels an owned auction at the given index (1-based).
        /// </summary>
        public static void CancelAuction(int index)
        {
            Lua.DoString($"CancelAuction({index})");
        }

        /// <summary>
        /// Posts an item from bags to the AH.
        /// Uses ClickAuctionSellItemButton + StartAuction Lua.
        /// </summary>
        /// <param name="bag">Bag index (0-4)</param>
        /// <param name="slot">Slot in bag (1-based)</param>
        /// <param name="startingBid">Minimum bid in copper</param>
        /// <param name="buyoutPrice">Buyout price in copper (0 = no buyout)</param>
        /// <param name="duration">Duration index: 1=12h, 2=24h, 3=48h</param>
        /// <param name="stackSize">Number of items per stack</param>
        public static void PostAuction(int bag, int slot, int startingBid,
            int buyoutPrice, int duration = 2, int stackSize = 1)
        {
            // Pick up item
            Lua.DoString($"PickupContainerItem({bag},{slot})");
            // Place on sell slot
            Lua.DoString("ClickAuctionSellItemButton()");
            // Start auction
            Lua.DoString($"StartAuction({startingBid},{buyoutPrice},{duration},{stackSize})");
        }

        /// <summary>
        /// Gets the auction deposit cost for the currently selected sell item.
        /// </summary>
        public static int GetAuctionDeposit(int duration = 2)
        {
            try
            {
                return Lua.GetReturnVal<int>(
                    $"return CalculateAuctionDeposit({duration})", 0);
            }
            catch { return 0; }
        }

        /// <summary>
        /// Queries the player's own auctions.
        /// </summary>
        public static void GetOwnerAuctionItems()
        {
            Lua.DoString("GetOwnerAuctionItems()");
        }

        /// <summary>
        /// Queries auctions the player has bid on.
        /// </summary>
        public static void GetBidderAuctionItems()
        {
            Lua.DoString("GetBidderAuctionItems()");
        }

        #endregion
    }
}
