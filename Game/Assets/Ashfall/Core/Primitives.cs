using System;

namespace Ashfall.Core
{
    /// <summary>Deterministic PCG32 generator. World generation and simulation never use System.Random or Unity's RNG.</summary>
    public sealed class Rng
    {
        ulong state, inc;
        public Rng(ulong seed, ulong stream = 54) { state = 0; inc = (stream << 1) | 1; Next(); state += seed; Next(); }
        public ulong State { get { return state; } set { state = value; } }
        public uint Next()
        {
            ulong old = state; state = old * 6364136223846793005UL + inc;
            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27); int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }
        public float Float() { return (Next() >> 8) * (1f / 16777216f); }
        public float Range(float min, float max) { return min + (max - min) * Float(); }
        /// <summary>Integer in [min, max).</summary>
        public int Range(int min, int max) { return max <= min ? min : min + (int)(Next() % (uint)(max - min)); }
        public bool Chance(float p) { return Float() < p; }
        public T Pick<T>(T[] items) { return items[Range(0, items.Length)]; }
    }

    [Serializable]
    public struct Vec2
    {
        public float x, y;
        public Vec2(float x, float y) { this.x = x; this.y = y; }
        public static readonly Vec2 Zero = new Vec2(0, 0);
        public float Length { get { return (float)Math.Sqrt(x * x + y * y); } }
        public float SqrLength { get { return x * x + y * y; } }
        public Vec2 Normalized { get { float l = Length; return l < 1e-5f ? Zero : new Vec2(x / l, y / l); } }
        public static Vec2 operator +(Vec2 a, Vec2 b) { return new Vec2(a.x + b.x, a.y + b.y); }
        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.x - b.x, a.y - b.y); }
        public static Vec2 operator *(Vec2 a, float s) { return new Vec2(a.x * s, a.y * s); }
        public static float Dot(Vec2 a, Vec2 b) { return a.x * b.x + a.y * b.y; }
        public static float Distance(Vec2 a, Vec2 b) { return (a - b).Length; }
        public static Vec2 FromAngle(float radians) { return new Vec2((float)Math.Cos(radians), (float)Math.Sin(radians)); }
        public float Angle { get { return (float)Math.Atan2(y, x); } }
        public override string ToString() { return "(" + x.ToString("0.00") + "," + y.ToString("0.00") + ")"; }
    }

    public struct Int2 : IEquatable<Int2>
    {
        public int x, y;
        public Int2(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Int2 o) { return x == o.x && y == o.y; }
        public override bool Equals(object o) { return o is Int2 && Equals((Int2)o); }
        public override int GetHashCode() { return x * 73856093 ^ y * 19349663; }
        public static Int2 operator +(Int2 a, Int2 b) { return new Int2(a.x + b.x, a.y + b.y); }
    }

    public struct Rect
    {
        public int x, y, w, h;
        public Rect(int x, int y, int w, int h) { this.x = x; this.y = y; this.w = w; this.h = h; }
        public int MaxX { get { return x + w; } }
        public int MaxY { get { return y + h; } }
        public bool Contains(int px, int py) { return px >= x && py >= y && px < x + w && py < y + h; }
        public bool Contains(Vec2 p) { return p.x >= x && p.y >= y && p.x < x + w && p.y < y + h; }
        public bool Overlaps(Rect o, int margin = 0) { return x - margin < o.MaxX && o.x - margin < MaxX && y - margin < o.MaxY && o.y - margin < MaxY; }
        public Vec2 Center { get { return new Vec2(x + w * .5f, y + h * .5f); } }
        public Rect Expand(int m) { return new Rect(x - m, y - m, w + 2 * m, h + 2 * m); }
        /// <summary>Distance from p to the nearest point of the rectangle (0 inside).</summary>
        public float DistanceTo(Vec2 p)
        {
            float dx = Math.Max(0, Math.Max(x - p.x, p.x - MaxX)), dy = Math.Max(0, Math.Max(y - p.y, p.y - MaxY));
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public static class MathUtil
    {
        public static float Clamp(float v, float min, float max) { return v < min ? min : v > max ? max : v; }
        public static float Clamp01(float v) { return Clamp(v, 0, 1); }
        public static int Clamp(int v, int min, int max) { return v < min ? min : v > max ? max : v; }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float MoveTowards(float v, float target, float delta) { return Math.Abs(target - v) <= delta ? target : v + Math.Sign(target - v) * delta; }
        public static float WrapAngle(float a) { while (a > Math.PI) a -= (float)(Math.PI * 2); while (a < -Math.PI) a += (float)(Math.PI * 2); return a; }
    }
}
