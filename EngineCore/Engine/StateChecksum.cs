#nullable enable
#if DEBUG
using System;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Physics;
using ConsoleGraphicsEngine.SceneGraph;
using ConsoleGraphicsEngine.Voronoi;

namespace ConsoleGraphicsEngine.Engine {
	/// <summary>
	/// Fast 64-bit FNV-1a deterministic state checksum over physics, scene graph, camera, and Voronoi state.
	/// Compiled only under <c>#if DEBUG</c> so Release builds strip all hashing overhead.
	/// </summary>
	public static class StateChecksum {
		const ulong FnvOffsetBasis = 14695981039346656037UL;
		const ulong FnvPrime = 1099511628211UL;

		static ulong Mix(ulong hash, long val) => (hash ^ (ulong)val) * FnvPrime;
		static ulong MixFloat(ulong hash, float val) => Mix(hash, BitConverter.SingleToInt32Bits(val));

		static ulong MixBody(ulong hash, Body b) {
			hash = Mix(hash, b.Position.X.FixedRaw);
			hash = Mix(hash, b.Position.Y.FixedRaw);
			hash = Mix(hash, b.Velocity.X.FixedRaw);
			hash = Mix(hash, b.Velocity.Y.FixedRaw);
			hash = Mix(hash, b.Angle.FixedRaw);
			hash = Mix(hash, b.AngularVelocity.FixedRaw);
			return hash;
		}

		public static ulong Compute(GameApplication app) {
			ulong h = FnvOffsetBasis;
			h = Mix(h, app.Clock.CurrentFrame);
			h = Mix(h, (long)app.ViewMode);
			h = Mix(h, FNum.Deterministic ? 1L : 0L);
			h = Mix(h, app.TotalCollisionsResolved);

			h = MixBody(h, app.World.Circle);
			for (int i = 0; i < app.World.Polygons.Count; i++) {
				h = MixBody(h, app.World.Polygons[i]);
			}

			var entities = app.ActiveScene.Entities;
			for (int i = 0; i < entities.Count; i++) {
				Entity e = entities[i];
				h = Mix(h, e.IsActive ? 1L : 0L);
				h = Mix(h, e.Transform.Position.X.FixedRaw);
				h = Mix(h, e.Transform.Position.Y.FixedRaw);
				h = Mix(h, e.Transform.Angle.FixedRaw);
			}

			h = MixFloat(h, app.Camera.Position.X);
			h = MixFloat(h, app.Camera.Position.Y);
			h = MixFloat(h, app.Camera.Angle);
			h = MixFloat(h, app.Camera.Zoom);

			h = Mix(h, app.Voronoi.ColorPicker.IsOpen ? 1L : 0L);
			h = Mix(h, app.Voronoi.ColorPicker.ActiveChannel);
			var seeds = app.Voronoi.Seeds;
			for (int i = 0; i < seeds.Count; i++) {
				VoronoiSeed s = seeds[i];
				h = MixFloat(h, s.Position.X);
				h = MixFloat(h, s.Position.Y);
				h = Mix(h, (s.Color.R << 16) | (s.Color.G << 8) | s.Color.B);
			}
			return h;
		}

		public static string ComputeHex(GameApplication app) => $"0x{Compute(app):X16}";
	}
}
#endif
