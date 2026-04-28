using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Styx.Helpers;
using Tripper.Tools.Math;

namespace Bots.DungeonBuddy.Avoidance
{
    public class AvoidCluster : List<Avoid>
    {
        public AvoidCluster(params Avoid[] avoids)
        {
            AddRange(avoids);
        }

        public LineClusterTangentPoints GetLineTangentPoints(Vector3 externalPoint)
        {
            Class108 state = new Class108();
            state.externalPoint = externalPoint;

            var withTangents = this
                .Select(a => new { avoid = a, tangents = Helpers.GetLineCircleTangentPoints(a.Location, a.Radius + 0.25f, externalPoint) })
                .Where(x => x.tangents != null)
                .Select(x => new { avoid = x.avoid, tangents = x.tangents });

            var tangentPairs = withTangents.ToArray();

            LineClusterTangentPoints result;
            if (tangentPairs.Length > 0)
            {
                Class109 state2 = new Class109();
                state2.state108 = state;
                Vector3 toCenter = Center - state.externalPoint;
                state2.centerAngle = (float)Math.Atan2(toCenter.Y, toCenter.X);

                var leftAngles = tangentPairs
                    .Select(x => new { pair = x, relLeft = x.tangents.LeftPoint - state.externalPoint })
                    .Select(x => new { x.pair, relLeft = x.relLeft, angle = WoWMathHelper.NormalizeRadian((float)Math.Atan2(x.relLeft.Y, x.relLeft.X) - state2.centerAngle) })
                    .Where(x => x.angle < Math.PI)
                    .OrderByDescending(x => x.angle);
                var bestLeft = leftAngles.Select(x => x.pair).FirstOrDefault();

                var rightAngles = tangentPairs
                    .Select(x => new { pair = x, relRight = x.tangents.RightPoint - state.externalPoint })
                    .Select(x => new { x.pair, relRight = x.relRight, angle = WoWMathHelper.NormalizeRadian(state2.centerAngle - (float)Math.Atan2(x.relRight.Y, x.relRight.X)) })
                    .Where(x => x.angle < Math.PI)
                    .OrderByDescending(x => x.angle);
                var bestRight = rightAngles.Select(x => x.pair).FirstOrDefault();

                if (bestRight != null && bestLeft != null)
                    result = new LineClusterTangentPoints(bestRight.tangents.RightPoint, bestLeft.tangents.LeftPoint, bestRight.avoid, bestLeft.avoid);
                else
                    result = null;
            }
            else
            {
                result = null;
            }

            return result;
        }

        public Vector3 Center
        {
            get
            {
                float centerX = (this.Max(a => a.Location.X + a.Radius) + this.Min(a => a.Location.X - a.Radius)) / 2f;
                float centerY = (this.Max(a => a.Location.Y + a.Radius) + this.Min(a => a.Location.Y - a.Radius)) / 2f;
                float centerZ = this.Average(a => a.Location.Z);
                return new Vector3(centerX, centerY, centerZ);
            }
        }

        private static bool TangentResultNotNull(Class246<Avoid, LineCircleTangentPoints> x) => x.result != null;
        private static Class247<Avoid, LineCircleTangentPoints> ToPair(Class246<Avoid, LineCircleTangentPoints> x) => new Class247<Avoid, LineCircleTangentPoints>(x.avoid, x.result);
        private static bool AngleLessThanPi(Class249<Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3>, float> x) => x.Angle < Math.PI;
        private static float AngleSelector(Class249<Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3>, float> x) => x.Angle;
        private static Class247<Avoid, LineCircleTangentPoints> ToLeftPair(Class249<Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3>, float> x) => x.Result.result;
        private static bool RightAngleLessThanPi(Class250<Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3>, float> x) => x.Angle < Math.PI;
        private static float RightAngleSelector(Class250<Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3>, float> x) => x.Angle;
        private static Class247<Avoid, LineCircleTangentPoints> ToRightPair(Class250<Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3>, float> x) => x.Result.result;

        private static float MaxX(Avoid a) => a.Location.X + a.Radius;
        private static float MinX(Avoid a) => a.Location.X - a.Radius;
        private static float MaxY(Avoid a) => a.Location.Y + a.Radius;
        private static float MinY(Avoid a) => a.Location.Y - a.Radius;
        private static float ZSelector(Avoid a) => a.Location.Z;

        private sealed class Class108
        {
            public Vector3 externalPoint;
            public Class246<Avoid, LineCircleTangentPoints> method_0(Avoid avoid)
            {
                return new Class246<Avoid, LineCircleTangentPoints>(avoid, Helpers.GetLineCircleTangentPoints(avoid.Location, avoid.Radius + 0.25f, this.externalPoint));
            }
            public Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3> method_1(Class247<Avoid, LineCircleTangentPoints> pair)
            {
                return new Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3>(pair, pair.Tangents.LeftPoint - this.externalPoint);
            }
            public Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3> method_2(Class247<Avoid, LineCircleTangentPoints> pair)
            {
                return new Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3>(pair, pair.Tangents.RightPoint - this.externalPoint);
            }
        }

        private sealed class Class109
        {
            public Class108 state108;
            public float centerAngle;
            public Class249<Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3>, float> method_0(Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3> pair)
            {
                return new Class249<Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3>, float>(pair, WoWMathHelper.NormalizeRadian((float)Math.Atan2(pair.relativePoint.Y, pair.relativePoint.X) - this.centerAngle));
            }
            public Class250<Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3>, float> method_1(Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3> pair)
            {
                return new Class250<Class248<Class247<Avoid, LineCircleTangentPoints>, Vector3>, float>(pair, WoWMathHelper.NormalizeRadian(this.centerAngle - (float)Math.Atan2(pair.relativePoint.Y, pair.relativePoint.X)));
            }
        }
    }

    internal class Class246<T1, T2>
    {
        public T1 avoid;
        public T2 result;
        public Class246(T1 avoid, T2 result) { this.avoid = avoid; this.result = result; }
    }

    internal class Class247<T1, T2>
    {
        public T1 avoid;
        public T2 Tangents;
        public Class247(T1 avoid, T2 tangents) { this.avoid = avoid; this.Tangents = tangents; }
    }

    internal class Class248<T1, T2>
    {
        public T1 result;
        public T2 relativePoint;
        public Class248(T1 result, T2 relativePoint) { this.result = result; this.relativePoint = relativePoint; }
    }

    internal class Class249<T1, T2>
    {
        public T1 Result;
        public T2 Angle;
        public Class249(T1 result, T2 angle) { this.Result = result; this.Angle = angle; }
    }

    internal class Class250<T1, T2>
    {
        public T1 Result;
        public T2 Angle;
        public Class250(T1 result, T2 angle) { this.Result = result; this.Angle = angle; }
    }
}