#nullable disable

using Styx.WoWInternals;

namespace Styx.Logic.Inventory.Frames
{
    /// <summary>
    /// Base class for WoW UI frames.
    /// </summary>
    public class Frame
    {
        public Frame(string frameName)
        {
            FrameName = frameName;
        }

        /// <summary>
        /// The name of the frame in the WoW UI.
        /// </summary>
        public string FrameName { get; private set; }

        /// <summary>
        /// Whether the frame is currently visible.
        /// </summary>
        public bool IsVisible
        {
            get
            {
                return Lua.GetReturnVal<int>(
                    string.Format("if {0} and {0}:IsVisible() then return 1 else return 0 end", FrameName), 0U) == 1;
            }
        }

        /// <summary>
        /// Hides the frame.
        /// </summary>
        public virtual void Hide()
        {
            Lua.DoString(string.Format("{0}:Hide()", FrameName), "CopilotBuddy.lua");
        }

        /// <summary>
        /// Shows the frame.
        /// </summary>
        public virtual void Show()
        {
            Lua.DoString(string.Format("{0}:Show()", FrameName), "CopilotBuddy.lua");
        }
    }
}
