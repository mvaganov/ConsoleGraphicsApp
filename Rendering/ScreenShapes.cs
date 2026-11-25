#nullable enable
using System;
using System.Collections.Generic;
using ConsoleGraphicsEngine.MathCore;

namespace ConsoleGraphicsEngine.Rendering;

/// <summary>Shapes in pixel space (what the rasterizer actually draws).</summary>
public abstract class ScreenShape {
	/// <summary>True if the shape is convex. For a convex shape, a pixel whose four corners are all
	/// inside is completely inside, so the rasterizer can skip its super-sampling.</summary>
	public virtual bool IsConvex => false;
	/// <summary>Which pixel rows could this shape touch?</summary>
	public abstract bool GetYRange(out float minY, out float maxY);
	/// <summary>Within one pixel row, which columns could it touch?</summary>
	public abstract bool GetXSpan(float rowTop, float rowBottom, out float minX, out float maxX);
	/// <summary>Is this exact sample point inside the shape?</summary>
	public abstract bool Contains(float x, float y);
}

/// <summary>Any simple polygon (convex or concave), filled with the even-odd rule.</summary>
public sealed class ScreenPolygon : ScreenShape {
	readonly float[] _x, _y;
	readonly float[] _dxdy; // for edge i -> i+1: how far x moves per unit of y
	readonly float _minY, _maxY;
	readonly bool _valid;
	readonly bool _convex;
	public override bool IsConvex => _convex;

	public ScreenPolygon(IReadOnlyList<Vec2> worldVertices, Transform2D toPixels) {
		int count = worldVertices.Count;
		_x = new float[count];
		_y = new float[count];
		_dxdy = new float[count];
		_minY = float.PositiveInfinity;
		_maxY = float.NegativeInfinity;
		_valid = count >= 3;
		for (int i = 0; i < count; i++) {
			Vec2 p = toPixels.Apply(worldVertices[i]);
			_x[i] = p.X;
			_y[i] = p.Y;
			if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) _valid = false;
			if (p.Y < _minY) _minY = p.Y;
			if (p.Y > _maxY) _maxY = p.Y;
		}
		for (int i = 0; i < count; i++) {
			int j = (i + 1) % count;
			float dy = _y[j] - _y[i];
			_dxdy[i] = dy == 0 ? 0 : (_x[j] - _x[i]) / dy;
		}
		_convex = _valid && ComputeIsConvex();
	}

	bool ComputeIsConvex() {
		int sign = 0;
		float totalTurn = 0;
		int count = _x.Length;
		for (int i = 0; i < count; i++) {
			int j = (i + 1) % count, k = (i + 2) % count;
			float e1x = _x[j] - _x[i], e1y = _y[j] - _y[i];
			float e2x = _x[k] - _x[j], e2y = _y[k] - _y[j];
			float cross = e1x * e2y - e1y * e2x;
			float dot = e1x * e2x + e1y * e2y;
			if (cross != 0) {
				int s = cross > 0 ? 1 : -1;
				if (sign == 0) sign = s;
				else if (s != sign) return false;
			}
			totalTurn += MathF.Atan2(cross, dot);
		}
		return sign != 0 && Math.Abs(Math.Abs(totalTurn) - 2 * Math.PI) < 1e-4;
	}

	public override bool GetYRange(out float minY, out float maxY) {
		minY = _minY; maxY = _maxY;
		return _valid;
	}

	public override bool GetXSpan(float rowTop, float rowBottom, out float minX, out float maxX) {
		minX = float.PositiveInfinity;
		maxX = float.NegativeInfinity;
		int count = _x.Length;
		for (int i = 0; i < count; i++) {
			int j = (i + 1) % count;
			float x0 = _x[i], y0 = _y[i], x1 = _x[j], y1 = _y[j];
			if (Math.Max(y0, y1) < rowTop || Math.Min(y0, y1) > rowBottom) continue;
			if (y0 == y1) {
				minX = Math.Min(minX, Math.Min(x0, x1));
				maxX = Math.Max(maxX, Math.Max(x0, x1));
				continue;
			}
			float ta = (rowTop - y0) / (y1 - y0);
			float tb = (rowBottom - y0) / (y1 - y0);
			float tLo = MathF.Max(0.0f, MathF.Min(ta, tb));
			float tHi = MathF.Min(1.0f, MathF.Max(ta, tb));
			if (tLo > tHi) continue;

			float xa = x0 + (x1 - x0) * tLo;
			float xb = x0 + (x1 - x0) * tHi;
			minX = Math.Min(minX, Math.Min(xa, xb));
			maxX = Math.Max(maxX, Math.Max(xa, xb));
		}
		return minX <= maxX;
	}

	public override bool Contains(float x, float y) {
		bool inside = false;
		int count = _x.Length;
		for (int i = 0; i < count; i++) {
			int j = i + 1 == count ? 0 : i + 1;
			float yi = _y[i], yj = _y[j];
			if ((yi > y) != (yj > y)) {
				float xCross = _x[i] + (y - yi) * _dxdy[i];
				if (x < xCross) inside = !inside;
			}
		}
		return inside;
	}
}

/// <summary>
/// A circle after the view transform. If the transform scales X and Y differently
/// the circle becomes an ellipse; we handle that by mapping each sample point back
/// into the circle's own (world) space and checking the plain circle equation there.
/// </summary>
public sealed class ScreenCircle : ScreenShape {
	readonly bool _valid;
	readonly float _cx, _cy;                 // center in pixels
	readonly float _i11, _i12, _i21, _i22;   // inverse of the transform's 2x2 part
	readonly float _r2;                      // world radius squared
	readonly float _halfW, _halfH;           // half extents of the pixel-space bounding box

	public ScreenCircle(Vec2 worldCenter, float radius, Transform2D t) {
		Vec2 c = t.Apply(worldCenter);
		_cx = c.X; _cy = c.Y;
		_r2 = radius * radius;
		float det = t.Determinant;
		_valid = Math.Abs(det) > 1e-12 && radius > 0 && float.IsFinite(_cx) && float.IsFinite(_cy);
		if (!_valid) return;
		_i11 = t.M22 / det; _i12 = -t.M12 / det;
		_i21 = -t.M21 / det; _i22 = t.M11 / det;
		_halfW = radius * MathF.Sqrt(t.M11 * t.M11 + t.M12 * t.M12);
		_halfH = radius * MathF.Sqrt(t.M21 * t.M21 + t.M22 * t.M22);
	}

	public override bool IsConvex => true;
	public override bool GetYRange(out float minY, out float maxY) {
		minY = _cy - _halfH; maxY = _cy + _halfH;
		return _valid;
	}
	public override bool GetXSpan(float rowTop, float rowBottom, out float minX, out float maxX) {
		minX = _cx - _halfW; maxX = _cx + _halfW;
		return _valid;
	}
	public override bool Contains(float x, float y) {
		float dx = x - _cx, dy = y - _cy;
		float lx = _i11 * dx + _i12 * dy;
		float ly = _i21 * dx + _i22 * dy;
		return lx * lx + ly * ly <= _r2;
	}
}
