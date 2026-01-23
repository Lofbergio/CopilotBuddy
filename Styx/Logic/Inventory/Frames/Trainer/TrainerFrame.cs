using Styx.WoWInternals;

namespace Styx.Logic.Inventory.Frames.Trainer
{
	/// <summary>
	/// Represents the class trainer frame in WoW.
	/// </summary>
	public class TrainerFrame : Frame
	{
		public static readonly TrainerFrame Instance = new TrainerFrame();

		public TrainerFrame() : base("ClassTrainerFrame")
		{
		}

		/// <summary>
		/// Gets whether the trainer frame is visible.
		/// </summary>
		public new bool IsVisible => Lua.GetReturnVal<bool>("return ClassTrainerFrame and ClassTrainerFrame:IsVisible()", 0);

		/// <summary>
		/// Gets the number of trainer services available.
		/// </summary>
		public int NumTrainerServices => Lua.GetReturnVal<int>("return GetNumTrainerServices()", 0);

		/// <summary>
		/// Gets whether this is a tradeskill trainer.
		/// </summary>
		public bool IsTradeskillTrainer => Lua.GetReturnVal<bool>("return IsTradeskillTrainer()", 0);

		/// <summary>
		/// Gets the currently selected trainer service index.
		/// </summary>
		public int Selected => Lua.GetReturnVal<int>("return GetTrainerSelectionIndex()", 0);

		/// <summary>
		/// Sets a trainer service filter.
		/// </summary>
		public void SetServiceFilter(TrainerServiceFilter filter, bool show)
		{
			Lua.DoString($"SetTrainerServiceTypeFilter('{filter.ToString().ToLower()}',{(show ? 1 : 0)})");
		}

		/// <summary>
		/// Gets the cost of a trainer service.
		/// </summary>
		public int GetServiceCost(int index)
		{
			return Lua.GetReturnVal<int>($"return GetTrainerServiceCost({index + 1})", 0);
		}

		/// <summary>
		/// Closes the trainer frame.
		/// </summary>
		public void Close()
		{
			Lua.DoString("CloseTrainer()");
		}

		/// <summary>
		/// Buys a specific trainer service by index.
		/// </summary>
		public void Buy(int index)
		{
			Lua.DoString($"BuyTrainerService({index + 1})");
		}

		/// <summary>
		/// Buys all available trainer services.
		/// </summary>
		public bool BuyAll()
		{
			return Lua.GetReturnVal<bool>("BuyTrainerService(0); return GetNumTrainerServices();", 0);
		}
	}
}
