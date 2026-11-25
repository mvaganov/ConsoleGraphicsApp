#nullable enable
using System;
using System.Collections.Generic;
using ConsoleGraphicsEngine.MathCore;

namespace ConsoleGraphicsEngine.Engine;

public interface IGameEvent {
	long EnqueuedFrame { get; }
}

/// <summary>Event raised when a console key is pressed.</summary>
public sealed class UserInputEvent : IGameEvent {
	public long EnqueuedFrame { get; }
	public ConsoleKeyInfo KeyInfo { get; }
	public UserInputEvent(long frame, ConsoleKeyInfo keyInfo) {
		EnqueuedFrame = frame;
		KeyInfo = keyInfo;
	}
}

/// <summary>Event scheduled to execute at a specific target frame (or recurring every N frames).</summary>
public sealed class TimedEvent : IGameEvent {
	public long EnqueuedFrame { get; }
	public long TargetFrame { get; set; }
	public long RepeatIntervalFrames { get; }
	public Action Callback { get; }
	public string Tag { get; }

	public TimedEvent(long enqueuedFrame, long targetFrame, Action callback, long repeatIntervalFrames = 0, string tag = "") {
		EnqueuedFrame = enqueuedFrame;
		TargetFrame = targetFrame;
		Callback = callback;
		RepeatIntervalFrames = repeatIntervalFrames;
		Tag = tag;
	}
}

/// <summary>Event raised when two physics bodies collide.</summary>
public sealed class CollisionEvent : IGameEvent {
	public long EnqueuedFrame { get; }
	public string BodyAName { get; }
	public string BodyBName { get; }
	public Vec2 ContactPoint { get; }
	public Vec2 Normal { get; }
	public float Penetration { get; }
	public float ImpulseMagnitude { get; }

	public CollisionEvent(long frame, string bodyAName, string bodyBName, Vec2 contactPoint, Vec2 normal, float penetration, float impulseMagnitude) {
		EnqueuedFrame = frame;
		BodyAName = bodyAName;
		BodyBName = bodyBName;
		ContactPoint = contactPoint;
		Normal = normal;
		Penetration = penetration;
		ImpulseMagnitude = impulseMagnitude;
	}
}

/// <summary>
/// Central event queue that decouples and orders execution across three phases every frame:
/// 1. User input resolution
/// 2. Timed events resolution
/// 3. Collision resolution
/// </summary>
public sealed class EventQueue {
	readonly Queue<UserInputEvent> _inputQueue = new Queue<UserInputEvent>();
	readonly List<TimedEvent> _timedEvents = new List<TimedEvent>();
	readonly Queue<CollisionEvent> _collisionQueue = new Queue<CollisionEvent>();

	readonly List<Action<UserInputEvent>> _inputHandlers = new List<Action<UserInputEvent>>();
	readonly List<Action<CollisionEvent>> _collisionHandlers = new List<Action<CollisionEvent>>();

	public int PendingInputCount => _inputQueue.Count;
	public int PendingTimedCount => _timedEvents.Count;
	public int PendingCollisionCount => _collisionQueue.Count;

	public void SubscribeInput(Action<UserInputEvent> handler) => _inputHandlers.Add(handler);
	public void SubscribeCollision(Action<CollisionEvent> handler) => _collisionHandlers.Add(handler);

	public void EnqueueInput(UserInputEvent ev) => _inputQueue.Enqueue(ev);
	public void EnqueueCollision(CollisionEvent ev) => _collisionQueue.Enqueue(ev);

	public void ScheduleTimed(long currentFrame, long delayFrames, Action callback, long repeatIntervalFrames = 0, string tag = "") {
		_timedEvents.Add(new TimedEvent(currentFrame, currentFrame + Math.Max(0, delayFrames), callback, repeatIntervalFrames, tag));
	}

	/// <summary>Phase 1: Drain and resolve all queued user input events for the frame.</summary>
	public int ResolveInputEvents() {
		int count = 0;
		while (_inputQueue.Count > 0) {
			UserInputEvent ev = _inputQueue.Dequeue();
			count++;
			for (int i = 0; i < _inputHandlers.Count; i++) {
				_inputHandlers[i](ev);
			}
		}
		return count;
	}

	/// <summary>Phase 2: Resolve all timed events whose target frame has arrived (in deterministic registration order).</summary>
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

	/// <summary>Phase 3: Drain and resolve all collision events emitted during the physics step.</summary>
	public int ResolveCollisionEvents() {
		int count = 0;
		while (_collisionQueue.Count > 0) {
			CollisionEvent ev = _collisionQueue.Dequeue();
			count++;
			for (int i = 0; i < _collisionHandlers.Count; i++) {
				_collisionHandlers[i](ev);
			}
		}
		return count;
	}
}
