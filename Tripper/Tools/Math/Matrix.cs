#nullable disable
using System.Numerics;
using System.Runtime.InteropServices;

namespace Tripper.Tools.Math
{
    /// <summary>
    /// Tripper.Tools.Math.Matrix — thin wrapper around <see cref="Matrix4x4"/>.
    /// Matches HB 4.3.4's Matrix type with XNA-style M11–M44 field names.
    /// QBs use this via <c>Vector3.Transform(pos, transport.GetWorldMatrix())</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Matrix
    {
        public float M11, M12, M13, M14;
        public float M21, M22, M23, M24;
        public float M31, M32, M33, M34;
        public float M41, M42, M43, M44;

        public static readonly Matrix Identity = (Matrix)Matrix4x4.Identity;

        public Matrix(
            float m11, float m12, float m13, float m14,
            float m21, float m22, float m23, float m24,
            float m31, float m32, float m33, float m34,
            float m41, float m42, float m43, float m44)
        {
            M11 = m11; M12 = m12; M13 = m13; M14 = m14;
            M21 = m21; M22 = m22; M23 = m23; M24 = m24;
            M31 = m31; M32 = m32; M33 = m33; M34 = m34;
            M41 = m41; M42 = m42; M43 = m43; M44 = m44;
        }

        #region Implicit conversions Matrix <-> Matrix4x4

        public static implicit operator Matrix(Matrix4x4 m) => new Matrix(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44);

        public static implicit operator Matrix4x4(Matrix m) => new Matrix4x4(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44);

        #endregion

        #region Static factory methods (HB 4.3.4 API surface)

        public static Matrix CreatePerspectiveFieldOfView(float fov, float aspect, float nearZ, float farZ)
            => (Matrix)Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, nearZ, farZ);

        public static Matrix CreateLookAt(Vector3 cameraPosition, Vector3 cameraTarget, Vector3 cameraUpVector)
        {
            var pos = new System.Numerics.Vector3(cameraPosition.X, cameraPosition.Y, cameraPosition.Z);
            var target = new System.Numerics.Vector3(cameraTarget.X, cameraTarget.Y, cameraTarget.Z);
            var up = new System.Numerics.Vector3(cameraUpVector.X, cameraUpVector.Y, cameraUpVector.Z);
            return (Matrix)Matrix4x4.CreateLookAt(pos, target, up);
        }

        public static void Invert(ref Matrix source, out Matrix result)
        {
            Matrix4x4.Invert((Matrix4x4)source, out Matrix4x4 inv);
            result = (Matrix)inv;
        }

        #endregion
    }
}
