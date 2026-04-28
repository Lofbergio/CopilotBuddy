using Tripper.Tools.Math;

namespace Bots.DungeonBuddy.Avoidance
{
    public class LineCircleTangentPoints
    {
        public readonly Vector3 RightPoint;
        public readonly Vector3 LeftPoint;

        public LineCircleTangentPoints(Vector3 rightPoint, Vector3 leftPoint)
        {
            RightPoint = rightPoint;
            LeftPoint = leftPoint;
        }
    }
}