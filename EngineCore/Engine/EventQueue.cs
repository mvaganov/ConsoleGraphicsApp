#nullable enable
using System;
using System.Collections.Generic;
using ConsoleGraphicsEngine.MathCore;

namespace ConsoleGraphicsEngine.Engine {
	/// <summary>Platform-neutral key enum (decoupled from <c>System.ConsoleKey</c> and <c>UnityEngine.KeyCode</c>).</summary>
	public enum KeyCode {
		None = 0,
		Escape, Enter, Space, Tab, Backspace,
		UpArrow, DownArrow, LeftArrow, RightArrow,
		A, B, C, D, E, F, G, H, I, J, K, L, M,
		N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
		D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
		Plus, Minus
	}

	public readonly struct KeyEventData {
		public readonly KeyCode Key;
		public readonly char KeyChar;
		public readonly bool Shift, Ctrl, Alt;

		public KeyEventData(KeyCode key, char keyChar = '\0', bool shift = false, bool ctrl = false, bool alt = false) {
			Key = key; KeyChar = keyChar; Shift = shift; Ctrl = ctrl; Alt = alt;
		}
	}

	public enum PointerEventType { Move, Down, Drag, Up, Scroll }
	public enum PointerButton { None = -1, Left = 0, Middle = 1, Right = 2 }

	public interface IGameEvent {
		long EnqueuedFrame { get; }
	}

	public sealed class UserInputEvent : IGameEvent {
		public long EnqueuedFrame { get; }
		public KeyEventData KeyInfo { get; }
		public UserInputEvent(long frame, KeyEventData keyInfo) {
			EnqueuedFrame = frame; KeyInfo = keyInfo;
		}
	}

	public sealed class PointerInputEvent : IGameEvent {
		public long EnqueuedFrame { get; }
		public PointerEventType Type { get; }
		public PointerButton Button { get; }
		public int CellX { get; }
		public int CellY { get; }
		public Vec2 ScreenPixelPos { get; }
		public Vec2 WorldPos { get; }
		public float ScrollDelta { get; }

		public PointerInputEvent(long frame, PointerEventType type, PointerButton button,
								 int cellX, int cellY, Vec2 screenPixelPos, Vec2 worldPos, float scrollDelta = 0f) {
			EnqueuedFrame = frame; Type = type; Button = button;
			CellX = cellX; CellY = cellY; ScreenPixelPos = screenPixelPos;
			WorldPos = worldPos; ScrollDelta = scrollDelta;
		}
	}

	public sealed class TimedEvent : IGameEvent {
		public long EnqueuedFrame { get; }
		public long TargetFrame { get; set; }
		public long RepeatIntervalFrames { get; }
		public Action Callback { get; }
		public string Tag { get; }

		public TimedEvent(long enqueuedFrame, long targetFrame, Action callback, long repeatIntervalFrames = 0, string tag = "") {
			EnqueuedFrame = enqueuedFrame; TargetFrame = targetFrame;
			Callback = callback; RepeatIntervalFrames = repeatIntervalFrames; Tag = tag;
		}
	}

	public sealed class CollisionEvent : IGameEvent {
		public long EnqueuedFrame { get; }
		public string BodyAName { get; }
		public string BodyBName { get; }
		public Vec2 ContactPoint { get; }
		public Vec2 Normal { get; }
		public float Penetration { get; }
		public float ImpulseMagnitude { get; }

		public CollisionEvent(long frame, string bodyAName, string bodyBName, Vec2 contactPoint, Vec2 normal, float penetration, float impulseMagnitude) {
			EnqueuedFrame = frame; BodyAName = bodyAName; BodyBName = bodyBName;
			ContactPoint = contactPoint; Normal = normal; Penetration = penetration; ImpulseMagnitude = impulseMagnitude;
		}
	}

	/// <summary>Semantic audio cue + procedural synth fallback parameters.</summary>
	public sealed class AudioEvent : IGameEvent {
		public long EnqueuedFrame { get; }
		public string CueId { get; }
		public float Volume { get; }
		public float Pitch { get; }
		public Vec2 WorldPosition { get; }
		public float SynthFrequencyHz { get; }
		public float SynthDurationSeconds { get; }

		public AudioEvent(long frame, string cueId, float volume = 1.0f, float pitch = 1.0f,
						  Vec2 worldPosition = default, float synthFrequencyHz = 440f, float synthDurationSeconds = 0.08f) {
			EnqueuedFrame = frame; CueId = cueId;
			Volume = Math.Clamp(volume, 0f, 1f); Pitch = Math.Clamp(pitch, 0.25f, 3f);
			WorldPosition = worldPosition; SynthFrequencyHz = synthFrequencyHz; SynthDurationSeconds = synthDurationSeconds;
		}
	}

	/// <summary>4-phase frame event bus: 1. Input+Pointer, 2. Timed, 3. Collision, 4. Audio.</summary>
	public sealed class EventQueue {
		readonly Queue<UserInputEvent> _inputQueue = new Queue<UserInputEvent>();
		readonly Queue<PointerInputEvent> _pointerQueue = new Queue<PointerInputEvent>();
		readonly List<TimedEvent> _timedEvents = new List<TimedEvent>();
		readonly Queue<CollisionEvent> _collisionQueue = new Queue<CollisionEvent>();
		readonly Queue<AudioEvent> _audioQueue = new Queue<AudioEvent>();

		readonly List<Action<UserInputEvent>> _inputHandlers = new List<Action<UserInputEvent>>();
		readonly List<Action<PointerInputEvent>> _pointerHandlers = new List<Action<PointerInputEvent>>();
		readonly List<Action<CollisionEvent>> _collisionHandlers = new List<Action<CollisionEvent>>();
		readonly List<Action<AudioEvent>> _audioHandlers = new List<Action<AudioEvent>>();

		public int PendingInputCount => _inputQueue.Count;
		public int PendingPointerCount => _pointerQueue.Count;
		public int PendingTimedCount => _timedEvents.Count;
		public int PendingCollisionCount => _collisionQueue.Count;
		public int PendingAudioCount => _audioQueue.Count;

		public void SubscribeInput(Action<UserInputEvent> handler) => _inputHandlers.Add(handler);
		public void SubscribePointer(Action<PointerInputEvent> handler) => _pointerHandlers.Add(handler);
		public void SubscribeCollision(Action<CollisionEvent> handler) => _collisionHandlers.Add(handler);
		public void SubscribeAudio(Action<AudioEvent> handler) => _audioHandlers.Add(handler);

		public void EnqueueInput(UserInputEvent ev) => _inputQueue.Enqueue(ev);
		public void EnqueuePointer(PointerInputEvent ev) => _pointerQueue.Enqueue(ev);
		public void EnqueueCollision(CollisionEvent ev) => _collisionQueue.Enqueue(ev);
		public void EnqueueAudio(AudioEvent ev) => _audioQueue.Enqueue(ev);

		public void ScheduleTimed(long currentFrame, long delayFrames, Action callback, long repeatIntervalFrames = 0, string tag = "") =>
			_timedEvents.Add(new TimedEvent(currentFrame, currentFrame + Math.Max(0, delayFrames), callback, repeatIntervalFrames, tag));

		public int ResolveInputEvents() {
			int count = 0;
			while (_inputQueue.Count > 0) {
				UserInputEvent ev = _inputQueue.Dequeue();
				count++;
				for (int i = 0; i < _inputHandlers.Count; i++) _inputHandlers[i](ev);
			}
			while (_pointerQueue.Count > 0) {
				PointerInputEvent pev = _pointerQueue.Dequeue();
				count++;
				for (int i = 0; i < _pointerHandlers.Count; i++) _pointerHandlers[i](pev);
			}
			return count;
		}

		public int ResolveTimedEvents(long currentFrame) {
			int fired = 0;
			for (int i = 0; i < _timedEvents.Count; i++) {
				TimedEvent ev = _timedEvents[i];
				if (currentFrame >= ev.TargetFrame) {
					ev.Callback();
					fired++;
					if (ev.RepeatIntervalFrames > 0) {
						ev.TargetFrame = currentFrame + ev.RepeatIntervalFrames;
					} else {
						_timedEvents.RemoveAt(i);
						i--;
					}
				}
			}
			return fired;
		}

		public int ResolveCollisionEvents() {
			int count = 0;
			while (_collisionQueue.Count > 0) {
				CollisionEvent ev = _collisionQueue.Dequeue();
				count++;
				for (int i = 0; i < _collisionHandlers.Count; i++) _collisionHandlers[i](ev);
			}
			return count;
		}

		public int ResolveAudioEvents() {
			int count = 0;
			while (_audioQueue.Count > 0) {
				AudioEvent ev = _audioQueue.Dequeue();
				count++;
				for (int i = 0; i < _audioHandlers.Count; i++) _audioHandlers[i](ev);
			}
			return count;
		}
	}
}
