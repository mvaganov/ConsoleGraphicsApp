#nullable enable
using System;
using System.Collections.Generic;
using ConsoleGraphicsEngine.MathCore;

namespace ConsoleGraphicsEngine.Engine {
	/// <summary>State-togglable input context (e.g. Global, CameraControls, PhysicsMode, VoronoiMode, ColorPickerUI).</summary>
	public sealed class KeyCallbackGroup {
		public string Name { get; }
		public bool IsEnabled { get; set; }
		readonly Dictionary<KeyCode, List<Action<KeyEventData>>> _keyPressCallbacks = new Dictionary<KeyCode, List<Action<KeyEventData>>>();
		readonly Dictionary<char, List<Action<KeyEventData>>> _charCallbacks = new Dictionary<char, List<Action<KeyEventData>>>();
		readonly List<Action<KeyEventData>> _anyKeyCallbacks = new List<Action<KeyEventData>>();
		readonly List<Action<PointerInputEvent>> _pointerCallbacks = new List<Action<PointerInputEvent>>();

		public KeyCallbackGroup(string name, bool isEnabled = true) {
			Name = name;
			IsEnabled = isEnabled;
		}

		public KeyCallbackGroup OnKey(KeyCode key, Action<KeyEventData> callback) {
			if (!_keyPressCallbacks.TryGetValue(key, out var list)) {
				list = new List<Action<KeyEventData>>();
				_keyPressCallbacks[key] = list;
			}
			list.Add(callback);
			return this;
		}

		public KeyCallbackGroup OnKey(KeyCode key, Action callback) => OnKey(key, _ => callback());

		public KeyCallbackGroup OnChar(char ch, Action<KeyEventData> callback) {
			if (!_charCallbacks.TryGetValue(ch, out var list)) {
				list = new List<Action<KeyEventData>>();
				_charCallbacks[ch] = list;
			}
			list.Add(callback);
			return this;
		}

		public KeyCallbackGroup OnAnyKey(Action<KeyEventData> callback) {
			_anyKeyCallbacks.Add(callback);
			return this;
		}

		public KeyCallbackGroup OnPointer(Action<PointerInputEvent> callback) {
			_pointerCallbacks.Add(callback);
			return this;
		}

		public bool Dispatch(KeyEventData keyInfo) {
			if (!IsEnabled) return false;
			bool handled = false;
			if (_keyPressCallbacks.TryGetValue(keyInfo.Key, out var keyList)) {
				for (int i = 0; i < keyList.Count; i++) { keyList[i](keyInfo); handled = true; }
			}
			if (keyInfo.KeyChar != '\0' && _charCallbacks.TryGetValue(keyInfo.KeyChar, out var charList)) {
				for (int i = 0; i < charList.Count; i++) { charList[i](keyInfo); handled = true; }
			}
			for (int i = 0; i < _anyKeyCallbacks.Count; i++) { _anyKeyCallbacks[i](keyInfo); handled = true; }
			return handled;
		}

		public bool DispatchPointer(PointerInputEvent pointerEvent) {
			if (!IsEnabled) return false;
			bool handled = false;
			for (int i = 0; i < _pointerCallbacks.Count; i++) { _pointerCallbacks[i](pointerEvent); handled = true; }
			return handled;
		}
	}

	public sealed class KeyInputSystem {
		readonly List<KeyCallbackGroup> _groups = new List<KeyCallbackGroup>();
		readonly Dictionary<string, KeyCallbackGroup> _groupsByName = new Dictionary<string, KeyCallbackGroup>(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<KeyCode, long> _keyHeldUntilFrame = new Dictionary<KeyCode, long>();

		/// <summary>Frames a discrete key press counts as held (~0.15s at 60 FPS) since terminals lack key-up events.</summary>
		public int KeyHoldFrames { get; set; } = 9;

		public int PointerCellX { get; private set; }
		public int PointerCellY { get; private set; }
		public Vec2 PointerWorldPos { get; private set; }
		public bool IsPointerDown { get; private set; }
		public bool HasActivePointer { get; private set; }

		public KeyInputSystem(EventQueue eventQueue) {
			eventQueue.SubscribeInput(OnInputEvent);
			eventQueue.SubscribePointer(OnPointerEvent);
		}

		public KeyCallbackGroup GetOrCreateGroup(string name, bool isEnabled = true) {
			if (_groupsByName.TryGetValue(name, out var existing)) return existing;
			var group = new KeyCallbackGroup(name, isEnabled);
			_groups.Add(group);
			_groupsByName[name] = group;
			return group;
		}

		public void SetGroupEnabled(string name, bool enabled) {
			if (_groupsByName.TryGetValue(name, out var group)) group.IsEnabled = enabled;
		}

		public bool IsGroupEnabled(string name) =>
			_groupsByName.TryGetValue(name, out var group) && group.IsEnabled;

		public void RecordKeyHeld(KeyCode key, long currentFrame) =>
			_keyHeldUntilFrame[key] = currentFrame + KeyHoldFrames;

		public bool IsKeyHeld(KeyCode key, long currentFrame) =>
			_keyHeldUntilFrame.TryGetValue(key, out long until) && currentFrame < until;

		public void ClearHeldKeys() => _keyHeldUntilFrame.Clear();

		public void UpdateVirtualCursorFallback(int centerCellX, int centerCellY, Vec2 cameraWorldCenter) {
			if (!HasActivePointer) {
				PointerCellX = centerCellX;
				PointerCellY = centerCellY;
				PointerWorldPos = cameraWorldCenter;
			}
		}

		void OnInputEvent(UserInputEvent ev) {
			// Snapshot active flags so a callback toggling groups mid-dispatch won't double-fire on the same keystroke
			Span<bool> activeSnapshot = _groups.Count <= 32 ? stackalloc bool[_groups.Count] : new bool[_groups.Count];
			for (int i = 0; i < _groups.Count; i++) activeSnapshot[i] = _groups[i].IsEnabled;
			for (int i = 0; i < _groups.Count; i++) {
				if (activeSnapshot[i]) _groups[i].Dispatch(ev.KeyInfo);
			}
		}

		void OnPointerEvent(PointerInputEvent ev) {
			HasActivePointer = true;
			PointerCellX = ev.CellX;
			PointerCellY = ev.CellY;
			PointerWorldPos = ev.WorldPos;
			if (ev.Type == PointerEventType.Down) IsPointerDown = true;
			else if (ev.Type == PointerEventType.Up) IsPointerDown = false;

			Span<bool> activeSnapshot = _groups.Count <= 32 ? stackalloc bool[_groups.Count] : new bool[_groups.Count];
			for (int i = 0; i < _groups.Count; i++) activeSnapshot[i] = _groups[i].IsEnabled;
			for (int i = 0; i < _groups.Count; i++) {
				if (activeSnapshot[i]) _groups[i].DispatchPointer(ev);
			}
		}
	}
}
