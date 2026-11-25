#nullable enable
using System;
using System.Globalization;

namespace ConsoleGraphicsEngine.MathCore;

/// <summary>
/// Configurable number type supporting both fast hardware floats and deterministic Q32.32 fixed-point arithmetic.
/// Toggle <see cref="Deterministic"/> to switch calculation behavior at runtime or per frame.
/// </summary>
public readonly struct FNum : IComparable<FNum>, IEquatable<FNum> {
	/// <summary>
	/// When true, all FNum operations and math functions execute using deterministic Q32.32 fixed-point integer math.
	/// When false, hardware IEEE-754 single-precision float arithmetic is used for maximum speed.
	/// </summary>
	public static bool Deterministic { get; set; } = true;

	public const long OneRaw = 1L << 32;
	private const double InvOneRaw = 1.0 / (1L << 32);

	public readonly long FixedRaw;
	public readonly float FloatVal;

	private FNum(long fixedRaw, float floatVal) {
		FixedRaw = fixedRaw;
		FloatVal = floatVal;
	}

	public static FNum FromFloat(float v) {
		if (!float.IsFinite(v)) return Zero;
		double clamped = Math.Clamp((double)v, -2147483640.0, 2147483640.0);
		long raw = (long)(clamped * OneRaw);
		return new FNum(raw, (float)(raw * InvOneRaw));
	}

	public static FNum FromRaw(long raw) => new FNum(raw, (float)(raw * InvOneRaw));

	public float ToFloat() => Deterministic ? (float)(FixedRaw * InvOneRaw) : FloatVal;
	public double ToDouble() => Deterministic ? FixedRaw * InvOneRaw : FloatVal;

	public static readonly FNum Zero = FromRaw(0);
	public static readonly FNum One = FromRaw(OneRaw);
	public static readonly FNum Half = FromRaw(OneRaw >> 1);
	public static readonly FNum Two = FromRaw(OneRaw * 2);
	public static readonly FNum Pi = FromRaw(13493037705L);       // 3.141592653589793 * 2^32
	public static readonly FNum TwoPi = FromRaw(26986075409L);    // 2 * Pi * 2^32
	public static readonly FNum HalfPi = FromRaw(6746518852L);    // Pi / 2 * 2^32
	public static readonly FNum Epsilon = FromRaw(4295L);         // ~1e-6 * 2^32
	public static readonly FNum MaxValue = FromRaw(long.MaxValue >> 2);
	public static readonly FNum MinValue = FromRaw(long.MinValue >> 2);

	public static implicit operator FNum(int v) => new FNum((long)v << 32, v);
	public static implicit operator FNum(float v) => FromFloat(v);
	public static explicit operator float(FNum v) => v.ToFloat();

	public static FNum operator +(FNum a, FNum b) {
		if (Deterministic) {
			long r = a.FixedRaw + b.FixedRaw;
			return new FNum(r, (float)(r * InvOneRaw));
		}
		float f = a.FloatVal + b.FloatVal;
		return new FNum((long)(f * OneRaw), f);
	}

	public static FNum operator -(FNum a, FNum b) {
		if (Deterministic) {
			long r = a.FixedRaw - b.FixedRaw;
			return new FNum(r, (float)(r * InvOneRaw));
		}
		float f = a.FloatVal - b.FloatVal;
		return new FNum((long)(f * OneRaw), f);
	}

	public static FNum operator -(FNum a) =>
		Deterministic ? new FNum(-a.FixedRaw, -a.ToFloat()) : new FNum(-a.FixedRaw, -a.FloatVal);

	public static FNum operator *(FNum a, FNum b) {
		if (Deterministic) {
			Int128 prod = (Int128)a.FixedRaw * (Int128)b.FixedRaw;
			long r = (long)(prod >> 32);
			return new FNum(r, (float)(r * InvOneRaw));
		}
		float f = a.FloatVal * b.FloatVal;
		return new FNum((long)(f * OneRaw), f);
	}

	public static FNum operator /(FNum a, FNum b) {
		if (Deterministic) {
			if (b.FixedRaw == 0) return Zero;
			Int128 num = (Int128)a.FixedRaw << 32;
			long r = (long)(num / b.FixedRaw);
			return new FNum(r, (float)(r * InvOneRaw));
		}
		if (MathF.Abs(b.FloatVal) < 1e-12f) return Zero;
		float f = a.FloatVal / b.FloatVal;
		return new FNum((long)(f * OneRaw), f);
	}

	public static bool operator <(FNum a, FNum b) => Deterministic ? a.FixedRaw < b.FixedRaw : a.FloatVal < b.FloatVal;
	public static bool operator >(FNum a, FNum b) => Deterministic ? a.FixedRaw > b.FixedRaw : a.FloatVal > b.FloatVal;
	public static bool operator <=(FNum a, FNum b) => Deterministic ? a.FixedRaw <= b.FixedRaw : a.FloatVal <= b.FloatVal;
	public static bool operator >=(FNum a, FNum b) => Deterministic ? a.FixedRaw >= b.FixedRaw : a.FloatVal >= b.FloatVal;
	public static bool operator ==(FNum a, FNum b) => Deterministic ? a.FixedRaw == b.FixedRaw : a.FloatVal == b.FloatVal;
	public static bool operator !=(FNum a, FNum b) => !(a == b);

	public static FNum Abs(FNum a) => a < Zero ? -a : a;
	public static FNum Min(FNum a, FNum b) => a < b ? a : b;
	public static FNum Max(FNum a, FNum b) => a > b ? a : b;
	public static FNum Clamp(FNum v, FNum min, FNum max) => v < min ? min : (v > max ? max : v);

	/// <summary>Deterministic integer bit-by-bit square root in Q32.32 when Deterministic is true.</summary>
	public static FNum Sqrt(FNum a) {
		if (Deterministic) {
			if (a.FixedRaw <= 0) return Zero;
			UInt128 num = (UInt128)a.FixedRaw << 32;
			UInt128 res = 0;
			UInt128 bit = (UInt128)1 << 94;
			while (bit > num) bit >>= 2;
			while (bit != 0) {
				if (num >= res + bit) {
					num -= res + bit;
					res = (res >> 1) + bit;
				} else {
					res >>= 1;
				}
				bit >>= 2;
			}
			long r = (long)res;
			return new FNum(r, (float)(r * InvOneRaw));
		}
		return FromFloat(MathF.Sqrt(MathF.Max(0, a.FloatVal)));
	}

	/// <summary>Deterministic 7th-order minimax polynomial sine when Deterministic is true.</summary>
	public static FNum Sin(FNum rad) {
		if (!Deterministic) return FromFloat(MathF.Sin(rad.FloatVal));
		// Wrap angle into [-Pi, Pi] deterministically
		long twoPi = TwoPi.FixedRaw;
		long pi = Pi.FixedRaw;
		long halfPi = HalfPi.FixedRaw;
		long x = rad.FixedRaw % twoPi;
		if (x > pi) x -= twoPi;
		else if (x < -pi) x += twoPi;

		// Fold into [-Pi/2, Pi/2]
		if (x > halfPi) x = pi - x;
		else if (x < -halfPi) x = -pi - x;

		// Taylor series: x - x^3/6 + x^5/120 - x^7/5040 in Q32.32
		Int128 x1 = x;
		Int128 x2 = (x1 * x1) >> 32;
		Int128 x3 = (x2 * x1) >> 32;
		Int128 x5 = (x3 * x2) >> 32;
		Int128 x7 = (x5 * x2) >> 32;
		Int128 res = x1 - (x3 / 6) + (x5 / 120) - (x7 / 5040);
		long clamped = (long)Int128.Clamp(res, -OneRaw, OneRaw);
		return new FNum(clamped, (float)(clamped * InvOneRaw));
	}

	public static FNum Cos(FNum rad) {
		if (!Deterministic) return FromFloat(MathF.Cos(rad.FloatVal));
		return Sin(FromRaw(rad.FixedRaw + HalfPi.FixedRaw));
	}

	public int CompareTo(FNum other) => Deterministic ? FixedRaw.CompareTo(other.FixedRaw) : FloatVal.CompareTo(other.FloatVal);
	public bool Equals(FNum other) => this == other;
	public override bool Equals(object? obj) => obj is FNum o && Equals(o);
	public override int GetHashCode() => Deterministic ? FixedRaw.GetHashCode() : FloatVal.GetHashCode();
	public override string ToString() => ToDouble().ToString("F3", CultureInfo.InvariantCulture);
}
