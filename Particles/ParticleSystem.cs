#nullable enable
using System;
using System.Collections.Generic;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Rendering;

namespace ConsoleGraphicsEngine.Particles;

public struct Particle {
	public Vec2 Position;
	public Vec2 Velocity;
	public Rgb StartColor;
	public Rgb EndColor;
	public float Lifetime;
	public float MaxLifetime;
	public float Size;
}

/// <summary>
/// Fast non-deterministic floating-point particle system.
/// Uses hardware single-precision floats (<see cref="Vec2"/>) for high throughput independent of
/// whether physics is running in deterministic fixed-point mode.
/// </summary>
public sealed class ParticleSystem {
	readonly List<Particle> _particles = new List<Particle>(256);
	readonly Random _rng = new Random();
	public float Drag { get; set; } = 1.8f;

	public int ActiveCount => _particles.Count;
	public IReadOnlyList<Particle> Particles => _particles;

	/// <summary>
	/// Emits a burst of particles at a world-space collision contact point.
	/// </summary>
	public void EmitBurst(Vec2 position, Vec2 normal, Rgb baseColor, int count = 18) {
		for (int i = 0; i < count; i++) {
			float angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
			float speed = 12f + (float)_rng.NextDouble() * 48f;
			Vec2 dir = new Vec2(MathF.Cos(angle), MathF.Sin(angle));
			// Slightly bias burst outward along collision normal
			Vec2 vel = dir * speed + normal * (8f + (float)_rng.NextDouble() * 15f);
			float life = 0.35f + (float)_rng.NextDouble() * 0.45f;
			float size = 1.1f + (float)_rng.NextDouble() * 1.4f;

			Rgb bright = Rgb.Lerp(baseColor, new Rgb(255, 250, 210), 0.45f);
			Rgb dark = Rgb.Lerp(baseColor, new Rgb(20, 20, 35), 0.75f);

			_particles.Add(new Particle {
				Position = position,
				Velocity = vel,
				StartColor = bright,
				EndColor = dark,
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

	public void Draw(Canvas canvas) {
		for (int i = 0; i < _particles.Count; i++) {
			Particle p = _particles[i];
			float t = p.Lifetime / p.MaxLifetime;
			Rgb color = Rgb.Lerp(p.StartColor, p.EndColor, t);
			float radius = p.Size * (1f - 0.65f * t);
			if (radius > 0.1f) {
				canvas.FillCircle(p.Position, radius, color);
			}
		}
	}

	public void Clear() => _particles.Clear();
}
