using Styx.Logic.Pathing;
using Styx.WoWInternals.WoWObjects;

namespace Bots.DungeonBuddy.Avoidance
{
    /// <summary>
    /// Zone d'évitement attachée à un WoWObject
    /// </summary>
    public class AvoidObject : Avoid
    {
        private readonly WoWObject _object;

        public AvoidObject(AvoidInfo info, WoWObject obj) : base(info)
        {
            _object = obj;
        }

        public override WoWPoint Location => _object?.Location ?? WoWPoint.Empty;
        
        public override bool IsValid => 
            _object != null && 
            _object.IsValid && 
            Info.CanRun(_object);

        public override void Update()
        {
            // La position est mise à jour automatiquement via _object.Location
        }
    }
}