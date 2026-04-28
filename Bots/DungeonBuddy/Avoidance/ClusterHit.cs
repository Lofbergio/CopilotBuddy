using Tripper.Tools.Math;

namespace Bots.DungeonBuddy.Avoidance
{
    public class ClusterHit
    {
        public ClusterHit(AvoidCluster cluster, Vector3 point, float hitAngle)
        {
            Cluster = cluster;
            Point = point;
            HitAngle = hitAngle;
        }

        public AvoidCluster Cluster { get; private set; }
        public Vector3 Point { get; private set; }
        public float HitAngle { get; private set; }
    }
}