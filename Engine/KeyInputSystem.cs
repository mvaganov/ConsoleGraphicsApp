#nullable enable
using System;
using System.Collections.Generic;

namespace ConsoleGraphicsEngine.Engine;

/// <summary>
/// A named group of key callbacks that can be toggled on or off as a unit when game state changes
/// (e.g., Global, CameraControls, PhysicsMode, VoronoiMode, ColorPickerUI).
/// </summary>
public sealed class KeyCallbackGroup {
	public string Name { get; }
	public bool IsEnabled { get; set; }
	readonly Dictionary<ConsoleKey, List<Action<ConsoleKeyInfo>>> _keyPressCallbacks = new();
	readonly Dictionary<char, List<Action<ConsoleKeyInfo>>> _charCallbacks = new();
	readonly List<Action<ConsoleKeyInfo>> _anyKeyCallbacks = new();

	public KeyCallbackGroup(string name, bool isEnabled = true) {
		Name = name;
		IsEnabled = isEnabled;
	}

	public KeyCallbackGroup OnKey(ConsoleKey key, Action<ConsoleKeyInfo> callback) {
		if (!_keyPressCallbacks.TryGetValue(key, out var list)) {
			list = new List<Action<ConsoleKeyInfo>>();
			_keyPressCallbacks[key] = list;
		}
		list.Add(callback);
		return this;
	}

	public KeyCallbackGroup OnKey(ConsoleKey key, Action callback) =>
		OnKey(key, _ => callback());

	public KeyCallbackGroup OnChar(char ch, Action<ConsoleKeyInfo> callback) {
		if (!_charCallbacks.TryGetValue(ch, out var list)) {
			list = new List<Action<ConsoleKeyInfo>>();
			_charCallbacks[ch] = list;
		}
		list.Add(callback);
		return this;
	}

	public KeyCallbackGroup OnAnyKey(Action<ConsoleKeyInfo> callback) {
		_anyKeyCallbacks.Add(callback);
		return this;
	}

	public bool Dispatch(ConsoleKeyInfo keyInfo) {
		if (!IsEnabled) return false;
		bool handled = false;
		if (_keyPressCallbacks.TryGetValue(keyInfo.Key, out var keyList)) {
			for (int i = 0; i < keyList.Count; i++) {
				keyList[i](keyInfo);
				handled = true;
			}
		}
		if (keyInfo.KeyChar != '\0' && _charCallbacks.TryGetValue(keyInfo.KeyChar, out var charList)) {
			for (int i = 0; i < charList.Count; i++) {
				charList[i](keyInfo);
				handled = true;
			}
		}
		for (int i = 0; i < _anyKeyCallbacks.Count; i++) {
			_anyKeyCallbacks[i](keyInfo);
			handled = true;
		}
		return handled;
	}
}

/// <summary>
/// Manages named <see cref="KeyCallbackGroup"/>s and frame-based held-key tracking.
/// Integrates directly with <see cref="EventQueue"/> to resolve <see cref="UserInputEvent"/>s.
/// </summary>
public sealed class KeyInputSystem {
	readonly List<KeyCallbackGroup> _groups = new();
	readonly Dictionary<string, KeyCallbackGroup> _groupsByName = new(StringComparer.OrdinalIgnoreCase);
	readonly Dictionary<ConsoleKey, long> _keyHeldUntilFrame = new();

	/// <summary>Number of frames a key press counts as held for continuous movement controls.</summary>
	public int KeyHoldFrames { get; set; } = 9; // ~0.15s at 60 FPS

	public KeyInputSystem(EventQueue eventQueue) {
		eventQueue.SubscribeInput(OnInputEvent);
	}

	public KeyCallbackGroup GetOrCreateGroup(string name, bool isEnabled = true) {
		if (_groupsByName.TryGetValue(name, out var existing)) return existing;
		var group = new KeyCallbackGroup(name, isEnabled);
		_groups.Add(group);
		_groupsByName[name] = group;
		return group;
	}

	public void SetGroupEnabled(string name, bool enabled) {
		if (_groupsByName.TryGetValue(name, out var group)) {
			group.IsEnabled = enabled;
		}
	}

	public bool IsGroupEnabled(string name) =>
		_groupsByName.TryGetValue(name, out var group) && group.IsEnabled;

	public void RecordKeyHeld(ConsoleKey key, long currentFrame) {
		_keyHeldUntilFrame[key] = currentFrame + KeyHoldFrames;
	}

	public bool IsKeyHeld(ConsoleKey key, long currentFrame) =>
		_keyHeldUntilFrame.TryGetValue(key, out long until) && currentFrame < until;

	public void ClearHeldKeys() => _keyHeldUntilFrame.Clear();

	void OnInputEvent(UserInputEvent ev) {
		// Snapshot active enabled states so a callback toggling groups mid-dispatch doesn't double-fire on the same key press
		Span<bool> activeSnapshot = _groups.Count <= 32 ? stackalloc bool[_groups.Count] : new bool[_groups.Count];
		for (int i = 0; i < _groups.Count; i++) activeSnapshot[i] = _groups[i].IsEnabled;

		for (int i = 0; i < _groups.Count; i++) {
			if (activeSnapshot[i]) {
				_groups[i].Dispatch(ev.KeyInfo);
			}
		}
	}
}
