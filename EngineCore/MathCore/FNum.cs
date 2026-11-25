#nullable enable
using System;
using System.Globalization;

namespace ConsoleGraphicsEngine.MathCore {
	/// <summary>
	/// Dual-mode scalar: Q32.32 deterministic fixed-point (<see cref="Deterministic"/>=true) or IEEE-754 float (false).
	/// Pure 64-bit math (no Int128) for netstandard2.1 / Unity IL2CPP compatibility.
	/// </summary>
	public readonly struct FNum : IComparable<FNum>, IEquatable<FNum> {
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
			if (float.IsNaN(v) || float.IsInfinity(v)) return Zero;
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
		public static readonly FNum Pi = FromRaw(13493037705L);       // pi * 2^32
		public static readonly FNum TwoPi = FromRaw(26986075409L);    // 2*pi * 2^32
		public static readonly FNum HalfPi = FromRaw(6746518852L);    // pi/2 * 2^32
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
				long r = MulQ32_32(a.FixedRaw, b.FixedRaw);
				return new FNum(r, (float)(r * InvOneRaw));
			}
			float f = a.FloatVal * b.FloatVal;
			return new FNum((long)(f * OneRaw), f);
		}

		public static FNum operator /(FNum a, FNum b) {
			if (Deterministic) {
				if (b.FixedRaw == 0) return Zero;
				long r = DivQ32_32(a.FixedRaw, b.FixedRaw);
				return new FNum(r, (float)(r * InvOneRaw));
			}
			if (MathF.Abs(b.FloatVal) < 1e-12f) return Zero;
			float f = a.FloatVal / b.FloatVal;
			return new FNum((long)(f * OneRaw), f);
		}

		// 64x64 -> Q32.32 via 32-bit limb partial products
		private static long MulQ32_32(long a, long b) {
			bool neg = (a < 0) ^ (b < 0);
			ulong ua = (ulong)(a < 0 ? -a : a);
			ulong ub = (ulong)(b < 0 ? -b : b);

			ulong aLo = ua & 0xFFFFFFFFUL, aHi = ua >> 32;
			ulong bLo = ub & 0xFFFFFFFFUL, bHi = ub >> 32;

			ulong p0 = aLo * bLo;
			ulong p1 = aLo * bHi;
			ulong p2 = aHi * bLo;
			ulong p3 = aHi * bHi;

			ulong mid = (p0 >> 32) + (p1 & 0xFFFFFFFFUL) + (p2 & 0xFFFFFFFFUL);
			ulong hi = p3 + (p1 >> 32) + (p2 >> 32) + (mid >> 32);
			long signedResult = (long)((hi << 32) | (mid & 0xFFFFFFFFUL));
			return neg ? -signedResult : signedResult;
		}

		// (a << 32) / b via 96-by-64 bit restoring division
		private static long DivQ32_32(long a, long b) {
			if (a == 0 || b == 0) return 0;
			bool neg = (a < 0) ^ (b < 0);
			ulong ua = (ulong)(a < 0 ? -a : a);
			ulong ub = (ulong)(b < 0 ? -b : b);

			ulong rem = ua >> 32;
			ulong low = ua << 32;
			ulong quot = 0;

			for (int i = 0; i < 64; i++) {
				rem = (rem << 1) | (low >> 63);
				low <<= 1;
				quot <<= 1;
				if (rem >= ub) {
					rem -= ub;
					quot |= 1UL;
				}
			}
			long signedQuot = (long)quot;
			return neg ? -signedQuot : signedQuot;
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

		// Digit-by-digit Q32.32 sqrt (48 iterations = 64 input bits + 32 fractional bits)
		public static FNum Sqrt(FNum a) {
			if (Deterministic) {
				if (a.FixedRaw <= 0) return Zero;
				ulong val = (ulong)a.FixedRaw;
				ulong rem = 0, root = 0;
				for (int i = 0; i < 48; i++) {
					root <<= 1;
					ulong top2 = i < 32 ? (val >> (62 - 2 * i)) & 3UL : 0UL;
					rem = (rem << 2) | top2;
					ulong testDiv = (root << 1) | 1UL;
					if (rem >= testDiv) {
						rem -= testDiv;
						root |= 1UL;
					}
				}
				long r = (long)root;
				return new FNum(r, (float)(r * InvOneRaw));
			}
			return FromFloat(MathF.Sqrt(MathF.Max(0, a.FloatVal)));
		}

		// 7th-order Taylor polynomial sine on folded [-pi/2, pi/2] domain
		public static FNum Sin(FNum rad) {
			if (!Deterministic) return FromFloat(MathF.Sin(rad.FloatVal));
			long twoPi = TwoPi.FixedRaw, pi = Pi.FixedRaw, halfPi = HalfPi.FixedRaw;
			long x = rad.FixedRaw % twoPi;
			if (x > pi) x -= twoPi;
			else if (x < -pi) x += twoPi;

			if (x > halfPi) x = pi - x;
			else if (x < -halfPi) x = -pi - x;

			long x2 = MulQ32_32(x, x);
			long x3 = MulQ32_32(x2, x);
			long x5 = MulQ32_32(x3, x2);
			long x7 = MulQ32_32(x5, x2);
			long res = Math.Clamp(x - (x3 / 6) + (x5 / 120) - (x7 / 5040), -OneRaw, OneRaw);
			return new FNum(res, (float)(res * InvOneRaw));
		}

		public static FNum Cos(FNum rad) =>
			!Deterministic ? FromFloat(MathF.Cos(rad.FloatVal)) : Sin(FromRaw(rad.FixedRaw + HalfPi.FixedRaw));

		public int CompareTo(FNum other) => Deterministic ? FixedRaw.CompareTo(other.FixedRaw) : FloatVal.CompareTo(other.FloatVal);
		public bool Equals(FNum other) => this == other;
		public override bool Equals(object? obj) => obj is FNum o && Equals(o);
		public override int GetHashCode() => Deterministic ? FixedRaw.GetHashCode() : FloatVal.GetHashCode();
		public override string ToString() => ToDouble().ToString("F3", CultureInfo.InvariantCulture);
	}
}
