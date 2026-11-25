#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using ConsoleGraphicsEngine.Engine;
using ConsoleGraphicsEngine.Logging;
using ConsoleGraphicsEngine.MathCore;

namespace ConsoleGraphicsEngine.Scripting {
	/// <summary>
	/// Frame-deterministic input scripting engine for batched agent verification and regression tests.
	/// Syntax (comma/semicolon/newline separated tokens):
	/// <list type="bullet">
	/// <item><c>v</c>, <c>space</c>, <c>enter</c>, <c>esc</c>, <c>r</c>, <c>f</c>, <c>w</c>/<c>a</c>/<c>s</c>/<c>d</c>, <c>q</c>/<c>e</c>, <c>up</c>/<c>down</c>/<c>left</c>/<c>right</c>, <c>1</c>..<c>9</c>, <c>+</c>/<c>-</c> (key press + 1 frame step unless grouped)</item>
	/// <item><c>wait:N</c> or <c>w:N</c> (advance N frames)</item>
	/// <item><c>hold:KEY:N</c> (press KEY every frame for N frames)</item>
	/// <item><c>click:X:Y</c>, <c>rclick:X:Y</c>, <c>move:X:Y</c>, <c>drag:X0:Y0:X1:Y1</c>, <c>scroll:DELTA</c> (pointer input + 1 frame step)</item>
	/// <item><c>assert_mode:Physics|Voronoi</c>, <c>assert_picker:open|closed</c>, <c>assert_seeds:N</c> (deterministic invariant check)</item>
	/// </list>
	/// </summary>
	public sealed class InputScriptEngine {
		readonly struct ScriptCommand {
			public readonly string Raw;
			public readonly string Verb;
			public readonly string[] Args;
			public ScriptCommand(string raw, string verb, string[] args) {
				Raw = raw; Verb = verb; Args = args;
			}
		}

		readonly Queue<ScriptCommand> _commands = new Queue<ScriptCommand>();
		int _waitFramesRemaining;
		KeyEventData _heldKey;
		int _holdFramesRemaining;

		public int PendingCommandCount => _commands.Count;
		public bool IsFinished => _commands.Count == 0 && _waitFramesRemaining <= 0 && _holdFramesRemaining <= 0;

		public void LoadScript(string script) {
			if (string.IsNullOrWhiteSpace(script)) return;
			string[] tokens = script.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < tokens.Length; i++) {
				string raw = tokens[i].Trim();
				if (raw.Length == 0 || raw.StartsWith("#", StringComparison.Ordinal)) continue;
				string[] parts = raw.Split(':');
				string verb = parts[0].Trim().ToLowerInvariant();
				string[] args = new string[parts.Length - 1];
				for (int j = 1; j < parts.Length; j++) args[j - 1] = parts[j].Trim();
				_commands.Enqueue(new ScriptCommand(raw, verb, args));
			}
		}

		/// <summary>Feeds scripted inputs for the current frame prior to <see cref="GameApplication.StepFrame"/>.</summary>
		public void TickBeforeFrame(GameApplication app) {
			if (_holdFramesRemaining > 0) {
				app.Events.EnqueueInput(new UserInputEvent(app.Clock.CurrentFrame, _heldKey));
				_holdFramesRemaining--;
				return;
			}
			if (_waitFramesRemaining > 0) {
				_waitFramesRemaining--;
				return;
			}

			while (_commands.Count > 0) {
				ScriptCommand cmd = _commands.Dequeue();
				if (ExecuteImmediateAssertion(cmd, app)) {
					// Assertions evaluate on current state without consuming a frame
					continue;
				}
				ExecuteInputCommand(cmd, app);
				break;
			}
		}

		/// <summary>Runs the entire loaded script to completion, advancing frames as needed.</summary>
		public int RunToCompletion(GameApplication app, int maxSafetyFrames = 10000) {
			int framesRun = 0;
			while (!IsFinished && framesRun < maxSafetyFrames) {
				TickBeforeFrame(app);
				if (IsFinished && app.Events.PendingInputCount == 0 && app.Events.PendingPointerCount == 0) break;
				app.StepFrame();
				framesRun++;
			}
			// Drain any trailing assertion commands at the end of the script
			while (_commands.Count > 0) {
				ScriptCommand cmd = _commands.Dequeue();
				if (!ExecuteImmediateAssertion(cmd, app)) {
					ExecuteInputCommand(cmd, app);
					app.StepFrame();
					framesRun++;
				}
			}
			return framesRun;
		}

		bool ExecuteImmediateAssertion(ScriptCommand cmd, GameApplication app) {
			if (cmd.Verb == "assert_mode" && cmd.Args.Length >= 1) {
				string expected = cmd.Args[0];
				string actual = app.ViewMode.ToString();
				if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)) {
					throw new InvalidOperationException($"Script assert_mode failed at frame {app.Clock.CurrentFrame}: expected {expected}, got {actual}");
				}
				return true;
			}
			if (cmd.Verb == "assert_picker" && cmd.Args.Length >= 1) {
				bool expectOpen = string.Equals(cmd.Args[0], "open", StringComparison.OrdinalIgnoreCase) || cmd.Args[0] == "true";
				if (app.Voronoi.ColorPicker.IsOpen != expectOpen) {
					throw new InvalidOperationException($"Script assert_picker failed at frame {app.Clock.CurrentFrame}: expected open={expectOpen}, got {app.Voronoi.ColorPicker.IsOpen}");
				}
				return true;
			}
			if (cmd.Verb == "assert_seeds" && cmd.Args.Length >= 1 && int.TryParse(cmd.Args[0], out int expectedSeeds)) {
				if (app.Voronoi.Seeds.Count != expectedSeeds) {
					throw new InvalidOperationException($"Script assert_seeds failed at frame {app.Clock.CurrentFrame}: expected {expectedSeeds}, got {app.Voronoi.Seeds.Count}");
				}
				return true;
			}
#if DEBUG
			if (cmd.Verb == "assert_checksum" && cmd.Args.Length >= 1) {
				string actualHex = StateChecksum.ComputeHex(app);
				if (!string.Equals(cmd.Args[0], actualHex, StringComparison.OrdinalIgnoreCase)) {
					throw new InvalidOperationException($"Script assert_checksum failed at frame {app.Clock.CurrentFrame}: expected {cmd.Args[0]}, got {actualHex}");
				}
				return true;
			}
#endif
			return false;
		}

		void ExecuteInputCommand(ScriptCommand cmd, GameApplication app) {
			long frame = app.Clock.CurrentFrame;
			if ((cmd.Verb == "wait" || cmd.Verb == "w") && cmd.Args.Length >= 1 && int.TryParse(cmd.Args[0], out int waitFrames)) {
				_waitFramesRemaining = Math.Max(0, waitFrames - 1);
				return;
			}
			if (cmd.Verb == "hold" && cmd.Args.Length >= 2 && int.TryParse(cmd.Args[1], out int holdFrames)) {
				_heldKey = ParseKeyToken(cmd.Args[0]);
				app.Events.EnqueueInput(new UserInputEvent(frame, _heldKey));
				_holdFramesRemaining = Math.Max(0, holdFrames - 1);
				return;
			}
			if ((cmd.Verb == "click" || cmd.Verb == "rclick" || cmd.Verb == "move") && cmd.Args.Length >= 2 &&
				int.TryParse(cmd.Args[0], out int cx) && int.TryParse(cmd.Args[1], out int cy)) {
				PointerEventType type = cmd.Verb == "move" ? PointerEventType.Move : PointerEventType.Down;
				PointerButton btn = cmd.Verb == "rclick" ? PointerButton.Right : (cmd.Verb == "click" ? PointerButton.Left : PointerButton.None);
				EnqueueCellPointer(app, frame, type, btn, cx, cy);
				return;
			}
			if (cmd.Verb == "drag" && cmd.Args.Length >= 4 &&
				int.TryParse(cmd.Args[0], out int x0) && int.TryParse(cmd.Args[1], out int y0) &&
				int.TryParse(cmd.Args[2], out int x1) && int.TryParse(cmd.Args[3], out int y1)) {
				EnqueueCellPointer(app, frame, PointerEventType.Down, PointerButton.Left, x0, y0);
				EnqueueCellPointer(app, frame, PointerEventType.Drag, PointerButton.Left, x1, y1);
				EnqueueCellPointer(app, frame, PointerEventType.Up, PointerButton.Left, x1, y1);
				return;
			}
			if (cmd.Verb == "scroll" && cmd.Args.Length >= 1 && float.TryParse(cmd.Args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float delta)) {
				int scx = app.RenderBackend.CellColumns / 2, scy = app.RenderBackend.CellRows / 2;
				Vec2 px = new Vec2(scx + 0.5f, scy * 2f + 1f);
				app.Events.EnqueuePointer(new PointerInputEvent(frame, PointerEventType.Scroll, PointerButton.None, scx, scy, px, app.Camera.Position, delta));
				return;
			}

			KeyEventData key = ParseKeyToken(cmd.Verb);
			if (key.Key != KeyCode.None || key.KeyChar != '\0') {
				app.Events.EnqueueInput(new UserInputEvent(frame, key));
			} else {
				Log.w($"Unknown input script token: '{cmd.Raw}'");
			}
		}

		static void EnqueueCellPointer(GameApplication app, long frame, PointerEventType type, PointerButton btn, int cellX, int cellY) {
			Vec2 px = new Vec2(cellX + 0.5f, cellY * 2f + 1.0f);
			Vec2 world = app.Camera.ScreenCellToWorld(cellX, cellY, app.RenderBackend.PixelWidth, app.RenderBackend.PixelHeight);
			app.Events.EnqueuePointer(new PointerInputEvent(frame, type, btn, cellX, cellY, px, world));
		}

		public static KeyEventData ParseKeyToken(string token) => token.ToLowerInvariant() switch {
			"esc" or "escape" => new KeyEventData(KeyCode.Escape, '\u001b'),
			"enter" or "return" => new KeyEventData(KeyCode.Enter, '\r'),
			"space" or "spacebar" => new KeyEventData(KeyCode.Space, ' '),
			"tab" => new KeyEventData(KeyCode.Tab, '\t'),
			"up" or "uparrow" => new KeyEventData(KeyCode.UpArrow),
			"down" or "downarrow" => new KeyEventData(KeyCode.DownArrow),
			"left" or "leftarrow" => new KeyEventData(KeyCode.LeftArrow),
			"right" or "rightarrow" => new KeyEventData(KeyCode.RightArrow),
			"+" or "plus" => new KeyEventData(KeyCode.Plus, '+'),
			"-" or "minus" => new KeyEventData(KeyCode.Minus, '-'),
			"v" => new KeyEventData(KeyCode.V, 'v'),
			"f" => new KeyEventData(KeyCode.F, 'f'),
			"r" => new KeyEventData(KeyCode.R, 'r'),
			"w" => new KeyEventData(KeyCode.W, 'w'),
			"a" => new KeyEventData(KeyCode.A, 'a'),
			"s" => new KeyEventData(KeyCode.S, 's'),
			"d" => new KeyEventData(KeyCode.D, 'd'),
			"q" => new KeyEventData(KeyCode.Q, 'q'),
			"e" => new KeyEventData(KeyCode.E, 'e'),
			"1" => new KeyEventData(KeyCode.D1, '1'),
			"2" => new KeyEventData(KeyCode.D2, '2'),
			"3" => new KeyEventData(KeyCode.D3, '3'),
			"4" => new KeyEventData(KeyCode.D4, '4'),
			"5" => new KeyEventData(KeyCode.D5, '5'),
			"6" => new KeyEventData(KeyCode.D6, '6'),
			"7" => new KeyEventData(KeyCode.D7, '7'),
			"8" => new KeyEventData(KeyCode.D8, '8'),
			"9" => new KeyEventData(KeyCode.D9, '9'),
			_ => new KeyEventData(KeyCode.None)
		};
	}
}
