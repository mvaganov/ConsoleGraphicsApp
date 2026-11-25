#nullable enable
using System;

namespace ConsoleGraphicsEngine.MathCore;

/// <summary>
/// Fast 2D single-precision floating point vector used for rendering, UI, camera, and non-deterministic particles.
/// </summary>
public readonly struct Vec2 : IEquatable<Vec2> {
	public readonly float X, Y;
	public Vec2(float x, float y) { X = x; Y = y; }
	public static readonly Vec2 Zero = new Vec2(0, 0);

	public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
	public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
	public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
	public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
	public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
	public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);

	public float Dot(Vec2 o) => X * o.X + Y * o.Y;
	public float Cross(Vec2 o) => X * o.Y - Y * o.X;
	public float LengthSquared => X * X + Y * Y;
	public float Length => MathF.Sqrt(LengthSquared);
	/// <summary>This vector rotated +90 degrees (counter-clockwise when Y points up).</summary>
	public Vec2 Perp => new Vec2(-Y, X);

	public Vec2 Normalized() {
		float l = Length;
		return l > 1e-12f ? this / l : new Vec2(0, 0);
	}

	public Vec2 Rotated(float radians) {
		float c = MathF.Cos(radians), s = MathF.Sin(radians);
		return new Vec2(c * X - s * Y, s * X + c * Y);
	}

	public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
	public override bool Equals(object? obj) => obj is Vec2 o && Equals(o);
	public override int GetHashCode() => HashCode.Combine(X, Y);
}

/// <summary>
/// 2D vector backed by <see cref="FNum"/> for optionally deterministic physics calculations.
/// </summary>
public readonly struct FVec2 : IEquatable<FVec2> {
	public readonly FNum X, Y;
	public FVec2(FNum x, FNum y) { X = x; Y = y; }
	public FVec2(float x, float y) { X = FNum.FromFloat(x); Y = FNum.FromFloat(y); }
	public static readonly FVec2 Zero = new FVec2(FNum.Zero, FNum.Zero);

	public Vec2 ToVec2() => new Vec2(X.ToFloat(), Y.ToFloat());
	public static FVec2 FromVec2(Vec2 v) => new FVec2(v.X, v.Y);

	public static FVec2 operator +(FVec2 a, FVec2 b) => new FVec2(a.X + b.X, a.Y + b.Y);
	public static FVec2 operator -(FVec2 a, FVec2 b) => new FVec2(a.X - b.X, a.Y - b.Y);
	public static FVec2 operator -(FVec2 a) => new FVec2(-a.X, -a.Y);
	public static FVec2 operator *(FVec2 a, FNum s) => new FVec2(a.X * s, a.Y * s);
	public static FVec2 operator *(FNum s, FVec2 a) => new FVec2(a.X * s, a.Y * s);
	public static FVec2 operator /(FVec2 a, FNum s) => new FVec2(a.X / s, a.Y / s);

	public FNum Dot(FVec2 o) => X * o.X + Y * o.Y;
	public FNum Cross(FVec2 o) => X * o.Y - Y * o.X;
	public FNum LengthSquared => X * X + Y * Y;
	public FNum Length => FNum.Sqrt(LengthSquared);
	public FVec2 Perp => new FVec2(-Y, X);

	public FVec2 Normalized() {
		FNum l = Length;
		return l > FNum.Epsilon ? this / l : Zero;
	}

	public FVec2 Rotated(FNum radians) {
		FNum c = FNum.Cos(radians), s = FNum.Sin(radians);
		return new FVec2(c * X - s * Y, s * X + c * Y);
	}

	public bool Equals(FVec2 other) => X == other.X && Y == other.Y;
	public override bool Equals(object? obj) => obj is FVec2 o && Equals(o);
	public override int GetHashCode() => HashCode.Combine(X, Y);
}
