#nullable enable
using System;
using System.Collections.Generic;
using ConsoleGraphicsEngine.MathCore;

namespace ConsoleGraphicsEngine.Physics;

public abstract class Body {
	public string Name { get; }
	public FVec2 Position, Velocity;
	public FNum Angle, AngularVelocity;
	public FNum Mass = FNum.One, Inertia = FNum.One;
	public Rgb Color;
	public FNum InverseMass => FNum.One / Mass;
	public FNum InverseInertia => FNum.One / Inertia;

	protected Body(string name, FVec2 position, FVec2 velocity, Rgb color) {
		Name = name;
		Position = position;
		Velocity = velocity;
		Color = color;
	}
}

public sealed class CircleBody : Body {
	public readonly FNum Radius;
	public CircleBody(string name, float radius, Vec2 position, Vec2 velocity, Rgb color)
		: base(name, FVec2.FromVec2(position), FVec2.FromVec2(velocity), color) {
		Radius = FNum.FromFloat(radius);
		Mass = FNum.Pi * Radius * Radius;
		Inertia = FNum.Half * Mass * Radius * Radius;
	}
}

/// <summary>
/// A convex or concave simple polygon body.
/// Automatically computes center-of-mass, re-centers local vertices around the centroid,
/// calculates accurate area-weighted rotational inertia, and decomposes concave outlines into
/// convex sub-polygons (via ear-clipping triangulation) for exact collision detection.
/// </summary>
public sealed class PolygonBody : Body {
	public readonly FVec2[] Local;       // Outline vertices relative to center of mass (CCW)
	public readonly FVec2[] World;       // Outline vertices in world space
	public readonly Vec2[] RenderWorld;  // Cached float vertices for Canvas rasterizer
	public readonly List<FVec2[]> ConvexPartsLocal; // Convex sub-polygons in local space (1 part if already convex)
	public readonly List<FVec2[]> ConvexPartsWorld; // Convex sub-polygons transformed to world space
	public readonly bool IsConcave;
	public readonly FNum BoundingRadius;

	/// <summary>Create a regular convex polygon with <paramref name="sides"/> vertices.</summary>
	public PolygonBody(string name, int sides, float radius, Vec2 position, Vec2 velocity, float angularVelocity, Rgb color)
		: this(name, BuildRegularPolygonVertices(sides, radius), position, velocity, angularVelocity, color) {
	}

	/// <summary>Create a general (convex or concave) polygon from counter-clockwise local vertices.</summary>
	public PolygonBody(string name, IReadOnlyList<Vec2> rawVertices, Vec2 position, Vec2 velocity, float angularVelocity, Rgb color)
		: base(name, FVec2.FromVec2(position), FVec2.FromVec2(velocity), color) {
		AngularVelocity = FNum.FromFloat(angularVelocity);

		// Ensure CCW winding and compute centroid, area, and rotational inertia
		Vec2[] ccw = EnsureCounterClockwise(rawVertices);
		ComputeMassAndCentroid(ccw, out Vec2 centroid, out float area, out float inertia);

		int n = ccw.Length;
		Local = new FVec2[n];
		World = new FVec2[n];
		RenderWorld = new Vec2[n];
		float maxR2 = 0f;
		var centeredFloat = new Vec2[n];
		for (int i = 0; i < n; i++) {
			Vec2 c = ccw[i] - centroid;
			centeredFloat[i] = c;
			Local[i] = FVec2.FromVec2(c);
			float r2 = c.LengthSquared;
			if (r2 > maxR2) maxR2 = r2;
		}
		BoundingRadius = FNum.FromFloat(MathF.Sqrt(maxR2));
		Mass = FNum.FromFloat(MathF.Max(1f, area));
		Inertia = FNum.FromFloat(MathF.Max(1f, inertia));

		IsConcave = !CheckIsConvex(centeredFloat);
		ConvexPartsLocal = new List<FVec2[]>();
		ConvexPartsWorld = new List<FVec2[]>();

		if (!IsConcave) {
			ConvexPartsLocal.Add(Local);
			ConvexPartsWorld.Add(World);
		} else {
			List<Vec2[]> triangles = TriangulateEarClipping(centeredFloat);
			foreach (Vec2[] tri in triangles) {
				var localTri = new FVec2[3] {
					FVec2.FromVec2(tri[0]),
					FVec2.FromVec2(tri[1]),
					FVec2.FromVec2(tri[2])
				};
				ConvexPartsLocal.Add(localTri);
				ConvexPartsWorld.Add(new FVec2[3]);
			}
		}

		UpdateWorld();
	}

	/// <summary>Builds a symmetrical concave boomerang shape (8 vertices, CCW).</summary>
	public static Vec2[] CreateBoomerangVertices(float scale = 1.0f) {
		return new Vec2[] {
			new Vec2(0f, 9f) * scale,       // Outer elbow apex
			new Vec2(-13f, -5f) * scale,    // Left wing outer edge
			new Vec2(-15f, -10f) * scale,   // Left wing tip
			new Vec2(-9f, -11f) * scale,    // Left wing inner tip
			new Vec2(0f, 1.5f) * scale,     // Inner concave notch (reflex vertex!)
			new Vec2(9f, -11f) * scale,     // Right wing inner tip
			new Vec2(15f, -10f) * scale,    // Right wing tip
			new Vec2(13f, -5f) * scale      // Right wing outer edge
		};
	}

	static Vec2[] BuildRegularPolygonVertices(int sides, float radius) {
		var v = new Vec2[sides];
		for (int i = 0; i < sides; i++) {
			float a = 2 * MathF.PI * i / sides + MathF.PI / 2;
			v[i] = new Vec2(MathF.Cos(a) * radius, MathF.Sin(a) * radius);
		}
		return v;
	}

	static Vec2[] EnsureCounterClockwise(IReadOnlyList<Vec2> verts) {
		int n = verts.Count;
		var arr = new Vec2[n];
		float signedArea2 = 0f;
		for (int i = 0; i < n; i++) {
			arr[i] = verts[i];
			Vec2 next = verts[(i + 1) % n];
			signedArea2 += arr[i].Cross(next);
		}
		if (signedArea2 < 0f) {
			Array.Reverse(arr);
		}
		return arr;
	}

	static void ComputeMassAndCentroid(Vec2[] ccw, out Vec2 centroid, out float area, out float inertia) {
		int n = ccw.Length;
		float area2 = 0f;
		Vec2 cAccum = Vec2.Zero;
		for (int i = 0; i < n; i++) {
			Vec2 a = ccw[i];
			Vec2 b = ccw[(i + 1) % n];
			float cross = a.Cross(b);
			area2 += cross;
			cAccum += (a + b) * cross;
		}
		area = MathF.Abs(area2) * 0.5f;
		centroid = MathF.Abs(area2) > 1e-6f ? cAccum / (3f * area2) : Vec2.Zero;

		// Compute moment of inertia around centroid
		float iAccum = 0f;
		for (int i = 0; i < n; i++) {
			Vec2 a = ccw[i] - centroid;
			Vec2 b = ccw[(i + 1) % n] - centroid;
			float cross = a.Cross(b);
			float term = a.Dot(a) + a.Dot(b) + b.Dot(b);
			iAccum += cross * term;
		}
		inertia = MathF.Abs(iAccum) / 12f;
	}

	static bool CheckIsConvex(Vec2[] ccw) {
		int n = ccw.Length;
		for (int i = 0; i < n; i++) {
			Vec2 a = ccw[i];
			Vec2 b = ccw[(i + 1) % n];
			Vec2 c = ccw[(i + 2) % n];
			float cross = (b - a).Cross(c - b);
			if (cross < -1e-5f) return false;
		}
		return true;
	}

	/// <summary>Decomposes any simple CCW polygon into convex CCW triangles via ear clipping.</summary>
	public static List<Vec2[]> TriangulateEarClipping(Vec2[] ccw) {
		var triangles = new List<Vec2[]>();
		var indices = new List<int>(ccw.Length);
		for (int i = 0; i < ccw.Length; i++) indices.Add(i);

		int guard = ccw.Length * ccw.Length * 2 + 16;
		while (indices.Count > 3 && guard-- > 0) {
			bool earFound = false;
			int m = indices.Count;
			for (int i = 0; i < m; i++) {
				int iPrev = indices[(i + m - 1) % m];
				int iCurr = indices[i];
				int iNext = indices[(i + 1) % m];
				Vec2 a = ccw[iPrev], b = ccw[iCurr], c = ccw[iNext];

				// Must be a strictly convex corner (CCW turn > 0)
				if ((b - a).Cross(c - a) <= 1e-5f) continue;

				// Check no other remaining vertex lies inside triangle (a, b, c)
				bool containsOther = false;
				for (int j = 0; j < m; j++) {
					int idx = indices[j];
					if (idx == iPrev || idx == iCurr || idx == iNext) continue;
					if (PointInTriangle(ccw[idx], a, b, c)) {
						containsOther = true;
						break;
					}
				}
				if (!containsOther) {
					triangles.Add(new Vec2[] { a, b, c });
					indices.RemoveAt(i);
					earFound = true;
					break;
				}
			}
			if (!earFound) {
				// Fallback if numerical degeneracy encountered
				triangles.Add(new Vec2[] { ccw[indices[0]], ccw[indices[1]], ccw[indices[2]] });
				indices.RemoveAt(1);
			}
		}
		if (indices.Count == 3) {
			triangles.Add(new Vec2[] { ccw[indices[0]], ccw[indices[1]], ccw[indices[2]] });
		}
		return triangles;
	}

	static bool PointInTriangle(Vec2 p, Vec2 a, Vec2 b, Vec2 c) {
		float c1 = (b - a).Cross(p - a);
		float c2 = (c - b).Cross(p - b);
		float c3 = (a - c).Cross(p - c);
		return c1 >= -1e-5f && c2 >= -1e-5f && c3 >= -1e-5f;
	}

	public void UpdateWorld() {
		FNum cos = FNum.Cos(Angle), sin = FNum.Sin(Angle);
		for (int i = 0; i < Local.Length; i++) {
			FVec2 l = Local[i];
			FVec2 w = new FVec2(Position.X + (cos * l.X - sin * l.Y), Position.Y + (sin * l.X + cos * l.Y));
			World[i] = w;
			RenderWorld[i] = w.ToVec2();
		}
		if (IsConcave) {
			for (int p = 0; p < ConvexPartsLocal.Count; p++) {
				FVec2[] partLocal = ConvexPartsLocal[p];
				FVec2[] partWorld = ConvexPartsWorld[p];
				for (int i = 0; i < partLocal.Length; i++) {
					FVec2 l = partLocal[i];
					partWorld[i] = new FVec2(Position.X + (cos * l.X - sin * l.Y), Position.Y + (sin * l.X + cos * l.Y));
				}
			}
		}
	}
}
