using Styx.Logic.Inventory.Frames;
using Styx.WoWInternals;

namespace Styx.Logic.Inventory.Frames.AuctionHouse
{
    /// <summary>
    /// FEAT-34: Represents the Auction House UI frame.
    /// Ported from HB 4.3.4 AuctionFrame + AuctionHouse.
    /// </summary>
    public class AuctionFrame : Frame
    {
        public AuctionFrame() : base("AuctionFrame")
        {
        }

        static AuctionFrame()
        {
            Instance = new AuctionFrame();
        }

        /// <summary>
        /// Closes the auction house.
        /// </summary>
        public void Close()
        {
            Lua.DoString("CloseAuctionHouse()");
        }

        /// <summary>
        /// Hides the auction frame.
        /// </summary>
        public override void Hide()
        {
            Close();
        }

        /// <summary>
        /// Whether the auction frame is currently open.
        /// </summary>
        public bool IsOpen => IsVisible;

        /// <summary>
        /// Singleton instance.
        /// </summary>
        public static readonly AuctionFrame Instance;
    }
}
