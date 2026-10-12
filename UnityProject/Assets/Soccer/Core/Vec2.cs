using System;

namespace Soccer.Core
{
    /// <summary>逻辑平面坐标：X 向右，Y 向上，单位 u。与 Unity 相机、像素无关。</summary>
    [Serializable]
    public struct Vec2
    {
        public float X;
        public float Y;
        public Vec2(float x, float y) { X = x; Y = y; }
        public float LengthSquared => X * X + Y * Y;
        public float Length => (float)Math.Sqrt(LengthSquared);
        public Vec2 Normalized => Length > 0.00001f ? this / Length : new Vec2(1, 0);
        public static Vec2 Zero => new Vec2(0, 0);
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float n) => new Vec2(a.X * n, a.Y * n);
        public static Vec2 operator /(Vec2 a, float n) => new Vec2(a.X / n, a.Y / n);
        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static Vec2 Rotate(Vec2 v, float radians)
        {
            float c = (float)Math.Cos(radians), s = (float)Math.Sin(radians);
            return new Vec2(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }
        public override string ToString() => $"({X:0.00}, {Y:0.00})";
    }

    public static class Geometry
    {
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static float Clamp(float v, float min, float max) => Math.Max(min, Math.Min(max, v));

        /// <summary>线段扫掠圆，返回首次接触比例；起点重叠时只有继续靠近才命中。</summary>
        public static bool SweepCircle(Vec2 start, Vec2 delta, Vec2 center, float radius, out float t)
        {
            t = 0;
            Vec2 relative = start - center;
            float a = delta.LengthSquared, b = Vec2.Dot(relative, delta);
            float c = relative.LengthSquared - radius * radius;
            if (c <= 0) return b < -0.000001f;
            float discriminant = b * b - a * c;
            if (a < 0.0000001f || b >= 0 || discriminant < 0) return false;
            t = (-b - (float)Math.Sqrt(discriminant)) / a;
            return t >= 0 && t <= 1;
        }
    }
}
