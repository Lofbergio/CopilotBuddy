using System.Diagnostics;
using Styx.Logic.Inventory;
using Styx.Logic.Profiles;

namespace Bots.Vibes.Shared
{
    /// <summary>
    /// Keeps the character's current best food/drink in the runtime protected-items list so the
    /// engine's mail/sell behaviour (e.g. MailWhite, which makes white consumables eligible) never
    /// strips what the bot rests with. Call Sync() each Pulse while mailing/selling is active and
    /// Clear() on Stop. Tracks the best tier and refreshes as it changes (level-up, restock).
    /// State is per-instance — one per bot run.
    /// </summary>
    public class ConsumableProtection
    {
        private uint _foodId, _drinkId;
        private readonly Stopwatch _throttle = new();

        public void Sync()
        {
            if (_throttle.IsRunning && _throttle.Elapsed.TotalSeconds < 10) return;
            _throttle.Restart();

            uint food = Consumable.GetBestFood(false)?.Entry ?? 0;
            if (food != _foodId)
            {
                if (_foodId != 0) ProtectedItemsManager.Remove(_foodId);
                if (food != 0) ProtectedItemsManager.Add(food);
                _foodId = food;
            }

            uint drink = Consumable.GetBestDrink(false)?.Entry ?? 0;
            if (drink != _drinkId)
            {
                if (_drinkId != 0) ProtectedItemsManager.Remove(_drinkId);
                if (drink != 0) ProtectedItemsManager.Add(drink);
                _drinkId = drink;
            }
        }

        public void Clear()
        {
            if (_foodId != 0) { ProtectedItemsManager.Remove(_foodId); _foodId = 0; }
            if (_drinkId != 0) { ProtectedItemsManager.Remove(_drinkId); _drinkId = 0; }
            _throttle.Reset();
        }
    }
}
