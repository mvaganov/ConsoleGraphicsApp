#nullable enable
using System;
using System.Collections.Generic;
using ConsoleGraphicsEngine.Engine;
using ConsoleGraphicsEngine.MathCore;

namespace ConsoleGraphicsEngine.Physics {
	/// <summary>Sub-stepped impulse-based rigid-body solver using <see cref="FNum"/>/<see cref="FVec2"/>.</summary>
	public sealed class PhysicsWorld {
		static readonly FNum Restitution = FNum.One;
		static readonly FNum MaxSpeed = FNum.FromFloat(220f);
		static readonly FNum MaxSpin = FNum.FromFloat(8f);
		const int SubSteps = 4;

		public readonly FNum HalfWidth, HalfHeight;
		public readonly CircleBody Circle;
		public readonly PolygonBody Pentagon;
		public readonly PolygonBody Triangle;
		public readonly PolygonBody Boomerang;
		public readonly List<PolygonBody> Polygons = new List<PolygonBody>();

		readonly EventQueue? _eventQueue;
		readonly HashSet<(string, string)> _emittedThisFrame = new HashSet<(string, string)>();

		public PhysicsWorld(float halfWidth, float halfHeight, EventQueue? eventQueue = null) {
			HalfWidth = FNum.FromFloat(halfWidth);
			HalfHeight = FNum.FromFloat(halfHeight);
			_eventQueue = eventQueue;

			Circle = new CircleBody("Circle", 7f, new Vec2(-45, 14), new Vec2(58, 37), new Rgb(255, 170, 40));
			Pentagon = new PolygonBody("Pentagon", 5, 12.5f, new Vec2(28, -8), new Vec2(-26, 18), 1.6f, new Rgb(70, 190, 255));
			Triangle = new PolygonBody("Triangle", 3, 11.5f, new Vec2(-10, -18), new Vec2(34, -28), -2.1f, new Rgb(120, 235, 115));
			Boomerang = new PolygonBody("Boomerang", PolygonBody.CreateBoomerangVertices(0.95f), new Vec2(32, 20), new Vec2(-32, -24), 2.4f, new Rgb(235, 100, 220));

			Polygons.Add(Pentagon);
			Polygons.Add(Triangle);
			Polygons.Add(Boomerang);
		}

		public void Update(float dt, long currentFrame = 0) {
			_emittedThisFrame.Clear();
			FNum h = FNum.FromFloat(dt) / FNum.FromFloat(SubSteps);
			for (int i = 0; i < SubSteps; i++) SubStep(h, currentFrame);
		}

		void SubStep(FNum h, long currentFrame) {
			Integrate(Circle, h);
			for (int i = 0; i < Polygons.Count; i++) {
				Integrate(Polygons[i], h);
				Polygons[i].UpdateWorld();
			}

			CollideCircleWithWalls();
			for (int i = 0; i < Polygons.Count; i++) CollidePolygonWithWalls(Polygons[i]);
			for (int i = 0; i < Polygons.Count; i++) CollideCircleWithPolygon(Circle, Polygons[i], currentFrame);

			for (int i = 0; i < Polygons.Count; i++) {
				for (int j = i + 1; j < Polygons.Count; j++) {
					CollidePolygonWithPolygon(Polygons[i], Polygons[j], currentFrame);
				}
			}
		}

		static void Integrate(Body b, FNum h) {
			FNum speed = b.Velocity.Length;
			if (speed > MaxSpeed) b.Velocity = b.Velocity * (MaxSpeed / speed);
			b.AngularVelocity = FNum.Clamp(b.AngularVelocity, -MaxSpin, MaxSpin);
			b.Position += b.Velocity * h;
			b.Angle += b.AngularVelocity * h;
		}

		void CollideCircleWithWalls() {
			CircleBody c = Circle;
			FVec2 p = c.Position, v = c.Velocity;
			FNum r = c.Radius;
			if (p.X - r < -HalfWidth) { p = new FVec2(-HalfWidth + r, p.Y); v = new FVec2(FNum.Abs(v.X), v.Y); }
			if (p.X + r > HalfWidth) { p = new FVec2(HalfWidth - r, p.Y); v = new FVec2(-FNum.Abs(v.X), v.Y); }
			if (p.Y - r < -HalfHeight) { p = new FVec2(p.X, -HalfHeight + r); v = new FVec2(v.X, FNum.Abs(v.Y)); }
			if (p.Y + r > HalfHeight) { p = new FVec2(p.X, HalfHeight - r); v = new FVec2(v.X, -FNum.Abs(v.Y)); }
			c.Position = p; c.Velocity = v;
		}

		void CollidePolygonWithWalls(PolygonBody b) {
			CollidePolygonWithWall(b, new FVec2(FNum.One, FNum.Zero), -HalfWidth);
			CollidePolygonWithWall(b, new FVec2(-FNum.One, FNum.Zero), -HalfWidth);
			CollidePolygonWithWall(b, new FVec2(FNum.Zero, FNum.One), -HalfHeight);
			CollidePolygonWithWall(b, new FVec2(FNum.Zero, -FNum.One), -HalfHeight);
		}

		static void CollidePolygonWithWall(PolygonBody b, FVec2 n, FNum d) {
			FNum deepest = FNum.Zero;
			int index = -1;
			for (int i = 0; i < b.World.Length; i++) {
				FNum s = n.Dot(b.World[i]) - d;
				if (s < deepest) { deepest = s; index = i; }
			}
			if (index < 0) return;
			FVec2 contact = b.World[index] + n * (-deepest);
			FVec2 r = contact - b.Position;
			b.Position += n * (-deepest);
			b.UpdateWorld();
			FVec2 contactVelocity = b.Velocity + r.Perp * b.AngularVelocity;
			FNum vn = contactVelocity.Dot(n);
			if (vn >= FNum.Zero) return;
			FNum rn = r.Cross(n);
			FNum j = -(FNum.One + Restitution) * vn / (b.InverseMass + rn * rn * b.InverseInertia);
			b.Velocity += n * (j * b.InverseMass);
			b.AngularVelocity += rn * j * b.InverseInertia;
		}

		void CollideCircleWithPolygon(CircleBody c, PolygonBody p, long currentFrame) {
			FNum maxDist = c.Radius + p.BoundingRadius;
			if ((c.Position - p.Position).LengthSquared > maxDist * maxDist) return;

			FNum bestPenetration = FNum.Zero;
			FVec2 bestNormal = FVec2.Zero, bestContact = FVec2.Zero;
			bool hasHit = false;

			for (int partIdx = 0; partIdx < p.ConvexPartsWorld.Count; partIdx++) {
				FVec2[] v = p.ConvexPartsWorld[partIdx];
				if (TestCircleVsConvexPart(c.Position, c.Radius, v, out FVec2 normal, out FVec2 contact, out FNum penetration)) {
					if (!hasHit || penetration > bestPenetration) {
						hasHit = true;
						bestPenetration = penetration;
						bestNormal = normal;
						bestContact = contact;
					}
				}
			}
			if (!hasHit) return;

			FNum invSum = c.InverseMass + p.InverseMass;
			FVec2 lever = bestContact - p.Position;
			c.Position += bestNormal * (bestPenetration * c.InverseMass / invSum);
			p.Position -= bestNormal * (bestPenetration * p.InverseMass / invSum);
			p.UpdateWorld();

			FVec2 polygonPointVelocity = p.Velocity + lever.Perp * p.AngularVelocity;
			FNum vn = (c.Velocity - polygonPointVelocity).Dot(bestNormal);
			FNum j = FNum.Zero;
			if (vn < FNum.Zero) {
				FNum rn = lever.Cross(bestNormal);
				j = -(FNum.One + Restitution) * vn / (invSum + rn * rn * p.InverseInertia);
				c.Velocity += bestNormal * (j * c.InverseMass);
				p.Velocity -= bestNormal * (j * p.InverseMass);
				p.AngularVelocity -= rn * j * p.InverseInertia;
			}
			EmitCollisionOnce(currentFrame, c.Name, p.Name, bestContact.ToVec2(), bestNormal.ToVec2(), bestPenetration.ToFloat(), j.ToFloat());
		}

		static bool TestCircleVsConvexPart(FVec2 circlePos, FNum radius, FVec2[] v,
										   out FVec2 normal, out FVec2 contact, out FNum penetration) {
			normal = FVec2.Zero; contact = FVec2.Zero; penetration = FNum.Zero;
			int count = v.Length;
			FNum best = FNum.MinValue;
			int bi = 0;
			for (int i = 0; i < count; i++) {
				FVec2 e = v[(i + 1) % count] - v[i];
				FVec2 outward = new FVec2(e.Y, -e.X).Normalized();
				FNum s = outward.Dot(circlePos - v[i]);
				if (s > best) { best = s; bi = i; }
			}
			if (best > radius) return false;

			FVec2 a = v[bi], b = v[(bi + 1) % count];
			FVec2 edge = b - a;
			FVec2 faceNormal = new FVec2(edge.Y, -edge.X).Normalized();

			if (best < FNum.Epsilon) {
				normal = faceNormal;
				contact = circlePos - faceNormal * best;
				penetration = radius - best;
				return true;
			}

			FVec2 closest;
			if ((circlePos - a).Dot(edge) <= FNum.Zero) closest = a;
			else if ((circlePos - b).Dot(-edge) <= FNum.Zero) closest = b;
			else closest = circlePos - faceNormal * best;

			FVec2 d = circlePos - closest;
			FNum dist = d.Length;
			if (dist > radius) return false;

			normal = dist > FNum.Epsilon ? d / dist : faceNormal;
			contact = closest;
			penetration = radius - dist;
			return true;
		}

		void CollidePolygonWithPolygon(PolygonBody a, PolygonBody b, long currentFrame) {
			FNum maxDist = a.BoundingRadius + b.BoundingRadius;
			if ((a.Position - b.Position).LengthSquared > maxDist * maxDist) return;

			FNum bestPenetration = FNum.Zero;
			FVec2 bestNormal = FVec2.Zero, bestContact = FVec2.Zero;
			bool hasHit = false;

			for (int ia = 0; ia < a.ConvexPartsWorld.Count; ia++) {
				FVec2[] partA = a.ConvexPartsWorld[ia];
				for (int ib = 0; ib < b.ConvexPartsWorld.Count; ib++) {
					FVec2[] partB = b.ConvexPartsWorld[ib];
					if (TestConvexVsConvexSAT(partA, partB, out FVec2 normalAB, out FVec2 contact, out FNum penetration)) {
						if (!hasHit || penetration > bestPenetration) {
							hasHit = true;
							bestPenetration = penetration;
							bestNormal = normalAB;
							bestContact = contact;
						}
					}
				}
			}
			if (!hasHit) return;

			FNum invMassSum = a.InverseMass + b.InverseMass;
			FVec2 ra = bestContact - a.Position, rb = bestContact - b.Position;

			a.Position -= bestNormal * (bestPenetration * (a.InverseMass / invMassSum));
			b.Position += bestNormal * (bestPenetration * (b.InverseMass / invMassSum));
			a.UpdateWorld();
			b.UpdateWorld();

			FVec2 va = a.Velocity + ra.Perp * a.AngularVelocity;
			FVec2 vb = b.Velocity + rb.Perp * b.AngularVelocity;
			FNum relVn = (vb - va).Dot(bestNormal);
			FNum j = FNum.Zero;
			if (relVn < FNum.Zero) {
				FNum raCrossN = ra.Cross(bestNormal), rbCrossN = rb.Cross(bestNormal);
				FNum denom = invMassSum + raCrossN * raCrossN * a.InverseInertia + rbCrossN * rbCrossN * b.InverseInertia;
				j = -(FNum.One + Restitution) * relVn / denom;

				FVec2 impulse = bestNormal * j;
				a.Velocity -= impulse * a.InverseMass;
				a.AngularVelocity -= raCrossN * j * a.InverseInertia;
				b.Velocity += impulse * b.InverseMass;
				b.AngularVelocity += rbCrossN * j * b.InverseInertia;
			}
			EmitCollisionOnce(currentFrame, a.Name, b.Name, bestContact.ToVec2(), bestNormal.ToVec2(), bestPenetration.ToFloat(), j.ToFloat());
		}

		/// <summary>SAT overlap test between two CCW convex polygons; <paramref name="normalAB"/> points from A to B.</summary>
		public static bool TestConvexVsConvexSAT(FVec2[] polyA, FVec2[] polyB,
												 out FVec2 normalAB, out FVec2 contactPoint, out FNum penetration) {
			normalAB = FVec2.Zero; contactPoint = FVec2.Zero; penetration = FNum.MaxValue;
			if (!FindMinimumPenetration(polyA, polyB, out FNum penA, out FVec2 normA, out FVec2 supportB)) return false;
			if (!FindMinimumPenetration(polyB, polyA, out FNum penB, out FVec2 normB, out FVec2 supportA)) return false;

			if (penA <= penB) {
				penetration = penA;
				normalAB = normA;
				contactPoint = supportB;
			} else {
				penetration = penB;
				normalAB = -normB;
				contactPoint = supportA;
			}
			return true;
		}

		static bool FindMinimumPenetration(FVec2[] refPoly, FVec2[] incPoly,
										   out FNum minPenetration, out FVec2 bestNormal, out FVec2 bestContact) {
			minPenetration = FNum.MaxValue; bestNormal = FVec2.Zero; bestContact = FVec2.Zero;
			int n = refPoly.Length;
			for (int i = 0; i < n; i++) {
				FVec2 a = refPoly[i], b = refPoly[(i + 1) % n];
				FVec2 edge = b - a;
				FVec2 outward = new FVec2(edge.Y, -edge.X).Normalized();

				FNum deepestDist = FNum.MaxValue;
				FVec2 support = incPoly[0];
				for (int j = 0; j < incPoly.Length; j++) {
					FNum d = outward.Dot(incPoly[j] - a);
					if (d < deepestDist) { deepestDist = d; support = incPoly[j]; }
				}
				if (deepestDist > FNum.Zero) return false;

				FNum pen = -deepestDist;
				if (pen < minPenetration) {
					minPenetration = pen;
					bestNormal = outward;
					bestContact = support;
				}
			}
			return true;
		}

		void EmitCollisionOnce(long currentFrame, string nameA, string nameB, Vec2 contact, Vec2 normal, float penetration, float impulse) {
			if (_eventQueue == null) return;
			var key = string.CompareOrdinal(nameA, nameB) <= 0 ? (nameA, nameB) : (nameB, nameA);
			if (_emittedThisFrame.Add(key)) {
				_eventQueue.EnqueueCollision(new CollisionEvent(currentFrame, nameA, nameB, contact, normal, penetration, impulse));
			}
		}
	}
}
