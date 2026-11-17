using System;

namespace TheArtifacts.Core.Combat
{
    /// <summary>A point or direction on the arena floor (meters). X/Y map to Unity's X/Z.</summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y) { X = x; Y = y; }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);

        public float LengthSquared => X * X + Y * Y;
        public float Length => (float)Math.Sqrt(LengthSquared);

        public Vec2 Normalized()
        {
            var len = Length;
            return len < 1e-6f ? Zero : new Vec2(X / len, Y / len);
        }

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;

        /// <summary>Moves from <paramref name="from"/> toward <paramref name="to"/> by at most <paramref name="maxStep"/>.</summary>
        public static Vec2 MoveTowards(Vec2 from, Vec2 to, float maxStep)
        {
            var delta = to - from;
            var dist = delta.Length;
            return dist <= maxStep || dist < 1e-6f ? to : from + delta * (maxStep / dist);
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
        public override string ToString() => $"({X:0.##}, {Y:0.##})";
    }

    /// <summary>A telegraphed area on the floor. Fixed when the tell starts (Combat Spec 2).</summary>
    public abstract class Area
    {
        public abstract bool Contains(Vec2 point);
    }

    public sealed class CircleArea : Area
    {
        public Vec2 Center { get; }
        public float Radius { get; }
        public CircleArea(Vec2 center, float radius) { Center = center; Radius = radius; }
        public override bool Contains(Vec2 p) => (p - Center).LengthSquared <= Radius * Radius;
    }

    /// <summary>Damages between the inner and outer radius; safe inside the inner radius.</summary>
    public sealed class RingArea : Area
    {
        public Vec2 Center { get; }
        public float InnerRadius { get; }
        public float OuterRadius { get; }
        public RingArea(Vec2 center, float inner, float outer) { Center = center; InnerRadius = inner; OuterRadius = outer; }

        public override bool Contains(Vec2 p)
        {
            var d2 = (p - Center).LengthSquared;
            return d2 >= InnerRadius * InnerRadius && d2 <= OuterRadius * OuterRadius;
        }
    }

    public sealed class ConeArea : Area
    {
        public Vec2 Origin { get; }
        public Vec2 Facing { get; }
        public float AngleDegrees { get; }
        public float Range { get; }
        readonly float _cosHalf;

        public ConeArea(Vec2 origin, Vec2 facing, float angleDegrees, float range)
        {
            Origin = origin;
            Facing = facing.Normalized();
            AngleDegrees = angleDegrees;
            Range = range;
            _cosHalf = (float)Math.Cos(angleDegrees * 0.5 * Math.PI / 180.0);
        }

        public override bool Contains(Vec2 p)
        {
            var d = p - Origin;
            var len = d.Length;
            if (len > Range) return false;
            if (len < 1e-4f) return true;
            return Vec2.Dot(d * (1f / len), Facing) >= _cosHalf - 1e-6f;
        }
    }

    /// <summary>A rectangle starting at the origin, extending along the direction.</summary>
    public sealed class LineArea : Area
    {
        public Vec2 Origin { get; }
        public Vec2 Direction { get; }
        public float Length { get; }
        public float Width { get; }

        public LineArea(Vec2 origin, Vec2 direction, float length, float width)
        {
            Origin = origin;
            Direction = direction.Normalized();
            Length = length;
            Width = width;
        }

        public Vec2 End => Origin + Direction * Length;

        public override bool Contains(Vec2 p)
        {
            var d = p - Origin;
            var along = Vec2.Dot(d, Direction);
            if (along < 0f || along > Length) return false;
            var side = Vec2.Dot(d, new Vec2(-Direction.Y, Direction.X));
            return Math.Abs(side) <= Width * 0.5f;
        }
    }
}
