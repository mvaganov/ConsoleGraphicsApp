#nullable enable
using System;
using System.Collections.Generic;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Rendering;

namespace ConsoleGraphicsEngine.Particles {
	public struct Particle {
		public Vec2 Position, Velocity;
		public Rgb StartColor, EndColor;
		public float Lifetime, MaxLifetime, Size;
	}

	/// <summary>Non-deterministic hardware-float particle emitter &amp; updater.</summary>
	public sealed class ParticleSystem {
		readonly List<Particle> _particles = new List<Particle>(256);
		readonly Random _rng = new Random();
		public float Drag { get; set; } = 1.8f;

		public int ActiveCount => _particles.Count;
		public IReadOnlyList<Particle> Particles => _particles;

		public void EmitBurst(Vec2 position, Vec2 normal, Rgb baseColor, int count = 18) {
			for (int i = 0; i < count; i++) {
				float angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
				float speed = 12f + (float)_rng.NextDouble() * 48f;
				Vec2 dir = new Vec2(MathF.Cos(angle), MathF.Sin(angle));
				Vec2 vel = dir * speed + normal * (8f + (float)_rng.NextDouble() * 15f);
				float life = 0.35f + (float)_rng.NextDouble() * 0.45f;
				float size = 1.1f + (float)_rng.NextDouble() * 1.4f;

				_particles.Add(new Particle {
					Position = position,
					Velocity = vel,
					StartColor = Rgb.Lerp(baseColor, new Rgb(255, 250, 210), 0.45f),
					EndColor = Rgb.Lerp(baseColor, new Rgb(20, 20, 35), 0.75f),
					Lifetime = 0f,
					MaxLifetime = life,
					Size = size
				});
			}
		}

		public void Update(float dt) {
			float damping = MathF.Max(0f, 1f - Drag * dt);
			for (int i = _particles.Count - 1; i >= 0; i--) {
				Particle p = _particles[i];
				p.Lifetime += dt;
				if (p.Lifetime >= p.MaxLifetime) {
					_particles.RemoveAt(i);
					continue;
				}
				p.Position += p.Velocity * dt;
				p.Velocity *= damping;
				_particles[i] = p;
			}
		}

		public void Draw(IRenderBackend backend) {
			for (int i = 0; i < _particles.Count; i++) {
				Particle p = _particles[i];
				float t = p.Lifetime / p.MaxLifetime;
				float radius = p.Size * (1f - 0.65f * t);
				if (radius > 0.1f) {
					backend.FillCircle(p.Position, radius, Rgb.Lerp(p.StartColor, p.EndColor, t));
				}
			}
		}

		public void Clear() => _particles.Clear();
	}
}
