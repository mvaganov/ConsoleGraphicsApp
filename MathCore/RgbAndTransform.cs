#nullable enable
using System;

namespace ConsoleGraphicsEngine.MathCore;

public readonly struct Rgb : IEquatable<Rgb> {
	public readonly byte R, G, B;
	public Rgb(int r, int g, int b) {
		R = (byte)Math.Clamp(r, 0, 255);
		G = (byte)Math.Clamp(g, 0, 255);
		B = (byte)Math.Clamp(b, 0, 255);
	}

	public static Rgb Lerp(Rgb a, Rgb b, float t) {
		if (t <= 0) return a;
		if (t >= 1) return b;
		return new Rgb(
			(int)(a.R + (b.R - a.R) * t + 0.5f),
			(int)(a.G + (b.G - a.G) * t + 0.5f),
			(int)(a.B + (b.B - a.B) * t + 0.5f));
	}

	public static Rgb Average(Rgb a, Rgb b) =>
		new Rgb((a.R + b.R + 1) / 2, (a.G + b.G + 1) / 2, (a.B + b.B + 1) / 2);

	/// <summary>Convert HSV (H in [0, 360), S in [0, 1], V in [0, 1]) to RGB.</summary>
	public static Rgb FromHsv(float h, float s, float v) {
		h = ((h % 360f) + 360f) % 360f;
		s = Math.Clamp(s, 0f, 1f);
		v = Math.Clamp(v, 0f, 1f);
		float c = v * s;
		float x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
		float m = v - c;
		float r1, g1, b1;
		if (h < 60f) { r1 = c; g1 = x; b1 = 0; }
		else if (h < 120f) { r1 = x; g1 = c; b1 = 0; }
		else if (h < 180f) { r1 = 0; g1 = c; b1 = x; }
		else if (h < 240f) { r1 = 0; g1 = x; b1 = c; }
		else if (h < 300f) { r1 = x; g1 = 0; b1 = c; }
		else { r1 = c; g1 = 0; b1 = x; }
		return new Rgb(
			(int)((r1 + m) * 255f + 0.5f),
			(int)((g1 + m) * 255f + 0.5f),
			(int)((b1 + m) * 255f + 0.5f));
	}

	/// <summary>Perceived brightness, 0 (black) to 255 (white).</summary>
	public float Luminance => 0.2126f * R + 0.7152f * G + 0.0722f * B;
	public bool Equals(Rgb o) => R == o.R && G == o.G && B == o.B;
	public override bool Equals(object? obj) => obj is Rgb o && Equals(o);
	public override int GetHashCode() => (R << 16) | (G << 8) | B;
	public static bool operator ==(Rgb a, Rgb b) => a.Equals(b);
	public static bool operator !=(Rgb a, Rgb b) => !a.Equals(b);
}

/// <summary>
/// A 2D affine transform (rotate / scale / translate / shear) stored as a 2x3 matrix:
///     x' = M11*x + M12*y + Tx
///     y' = M21*x + M22*y + Ty
/// Multiplying transforms composes them: (a * b).Apply(p) == a.Apply(b.Apply(p)).
/// </summary>
public readonly struct Transform2D {
	public readonly float M11, M12, M21, M22, Tx, Ty;
	public Transform2D(float m11, float m12, float m21, float m22, float tx, float ty) {
		M11 = m11; M12 = m12; M21 = m21; M22 = m22; Tx = tx; Ty = ty;
	}
	public static readonly Transform2D Identity = new Transform2D(1, 0, 0, 1, 0, 0);
	public static Transform2D Translation(float x, float y) => new Transform2D(1, 0, 0, 1, x, y);
	public static Transform2D Translation(Vec2 v) => Translation(v.X, v.Y);
	public static Transform2D Scale(float sx, float sy) => new Transform2D(sx, 0, 0, sy, 0, 0);
	public static Transform2D Scale(float s) => Scale(s, s);
	/// <summary>Rotation by an angle in radians (counter-clockwise when Y points up).</summary>
	public static Transform2D Rotation(float radians) {
		float c = MathF.Cos(radians), s = MathF.Sin(radians);
		return new Transform2D(c, -s, s, c, 0, 0);
	}
	public Vec2 Apply(Vec2 p) => new Vec2(M11 * p.X + M12 * p.Y + Tx, M21 * p.X + M22 * p.Y + Ty);

	/// <summary>Inverse transform (maps pixel space back to world space).</summary>
	public Transform2D Inverse() {
		float det = Determinant;
		if (MathF.Abs(det) < 1e-12f) return Identity;
		float invDet = 1.0f / det;
		float i11 = M22 * invDet;
		float i12 = -M12 * invDet;
		float i21 = -M21 * invDet;
		float i22 = M11 * invDet;
		float itx = -(i11 * Tx + i12 * Ty);
		float ity = -(i21 * Tx + i22 * Ty);
		return new Transform2D(i11, i12, i21, i22, itx, ity);
	}

	public static Transform2D operator *(Transform2D a, Transform2D b) => new Transform2D(
		a.M11 * b.M11 + a.M12 * b.M21, a.M11 * b.M12 + a.M12 * b.M22,
		a.M21 * b.M11 + a.M22 * b.M21, a.M21 * b.M12 + a.M22 * b.M22,
		a.M11 * b.Tx + a.M12 * b.Ty + a.Tx,
		a.M21 * b.Tx + a.M22 * b.Ty + a.Ty);

	public float Determinant => M11 * M22 - M12 * M21;
}

/// <summary>
/// Screen-space rectangle measured in terminal character cells (X = column, Y = row).
/// </summary>
public readonly struct ScreenRect {
	public readonly int X, Y, Width, Height;
	public ScreenRect(int x, int y, int width, int height) {
		X = x; Y = y; Width = Math.Max(0, width); Height = Math.Max(0, height);
	}
	public int Right => X + Width;
	public int Bottom => Y + Height;
}
