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
		/// Buys every available + affordable trainer service.
		/// Returns the count bought and the count of available services skipped purely for cost
		/// (so the caller can decide whether to retry after earning money).
		/// </summary>
		public (int Bought, int UnaffordableRemaining) BuyAll()
		{
			// 3.3.5a BuyTrainerService takes a 1-based index — there is no "0 = buy all", so the old
			// BuyTrainerService(0) was a no-op. Filter to available services and buy each we can afford.
			const string lua =
@"SetTrainerServiceTypeFilter('available', 1)
SetTrainerServiceTypeFilter('unavailable', 0)
SetTrainerServiceTypeFilter('used', 0)
local bought, unaffordable = 0, 0
local money = GetMoney()
for i = GetNumTrainerServices(), 1, -1 do
  local _, _, category = GetTrainerServiceInfo(i)
  if category == 'available' then
    if GetTrainerServiceCost(i) <= money then
      BuyTrainerService(i)
      bought = bought + 1
    else
      unaffordable = unaffordable + 1
    end
  end
end
return bought, unaffordable";

			var vals = Lua.GetReturnValues(lua);
			int bought = 0, unaffordable = 0;
			if (vals != null && vals.Count >= 2)
			{
				int.TryParse(vals[0], out bought);
				int.TryParse(vals[1], out unaffordable);
			}
			return (bought, unaffordable);
		}
	}
}
