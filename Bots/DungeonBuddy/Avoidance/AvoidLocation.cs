using Styx.Logic.Pathing;

namespace Bots.DungeonBuddy.Avoidance
{
    /// <summary>
    /// Zone d'évitement à position fixe
    /// </summary>
    public class AvoidLocation : Avoid
    {
        private WoWPoint _location;

        public AvoidLocation(AvoidInfo info) : base(info)
        {
            _location = info.LocationSelector?.Invoke() ?? WoWPoint.Empty;
        }

        public override WoWPoint Location => _location;
        
        public override bool IsValid => 
            _location != WoWPoint.Empty && 
            Info.CanRun(null);

        public override void Update()
        {
            if (Info.LocationSelector != null)
                _location = Info.LocationSelector();
        }
    }
}