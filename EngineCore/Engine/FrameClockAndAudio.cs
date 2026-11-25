#nullable enable
using System;
using System.Collections.Generic;

namespace ConsoleGraphicsEngine.Engine {
	/// <summary>Strict fixed-step frame clock (<see cref="FixedDeltaTime"/> = 1 / <see cref="TargetFps"/>).</summary>
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

		public void AdvanceFrame() => CurrentFrame++;
		public void Reset() => CurrentFrame = 0;
	}

	public interface IAudioBackend {
		void PlayCue(AudioEvent audioEvent);
	}

	public sealed class AudioSystem {
		readonly List<AudioEvent> _recentEvents = new List<AudioEvent>();
		public IAudioBackend? Backend { get; set; }
		public int TotalCuesPlayed { get; private set; }
		public AudioEvent? LastPlayedEvent { get; private set; }
		public IReadOnlyList<AudioEvent> RecentEvents => _recentEvents;

		public AudioSystem(EventQueue eventQueue) {
			eventQueue.SubscribeAudio(OnAudioEvent);
		}

		void OnAudioEvent(AudioEvent ev) {
			TotalCuesPlayed++;
			LastPlayedEvent = ev;
			_recentEvents.Add(ev);
			if (_recentEvents.Count > 32) _recentEvents.RemoveAt(0);
			Backend?.PlayCue(ev);
		}

		public void ClearHistory() {
			_recentEvents.Clear();
			LastPlayedEvent = null;
			TotalCuesPlayed = 0;
		}
	}
}
