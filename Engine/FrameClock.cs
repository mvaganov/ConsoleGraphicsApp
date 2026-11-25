#nullable enable
using System;

namespace ConsoleGraphicsEngine.Engine;

/// <summary>
/// Manages strict frame-based advancement. Every simulation tick advances by an exact fixed frame delta
/// (<see cref="FixedDeltaTime"/> = 1 / <see cref="TargetFps"/>), guaranteeing frame-accurate reproducibility
/// regardless of wall-clock jitter.
/// </summary>
public sealed class FrameClock {
	public int TargetFps { get; }
	public float FixedDeltaTime { get; }
	public long CurrentFrame { get; private set; }
	public double ElapsedSimulatedSeconds => CurrentFrame * (double)FixedDeltaTime;

	public FrameClock(int targetFps = 60) {
		TargetFps = Math.Max(1, targetFps);
		FixedDeltaTime = 1.0f / TargetFps;
		CurrentFrame = 0;
	}

	public void AdvanceFrame() {
		CurrentFrame++;
	}

	public void Reset() {
		CurrentFrame = 0;
	}
}
