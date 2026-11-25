#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Particles;
using ConsoleGraphicsEngine.Physics;
using ConsoleGraphicsEngine.Rendering;
using ConsoleGraphicsEngine.Voronoi;

namespace ConsoleGraphicsEngine.Engine;

public enum AppViewMode {
	Physics,
	Voronoi
}

/// <summary>
/// Orchestrates the game engine subsystems: EventQueue, KeyInputSystem with state-based callback groups,
/// FrameClock (strict frame-based advancement), PhysicsWorld, ParticleSystem, VoronoiSystem, and rendering.
/// </summary>
public sealed class GameApplication {
	public const float ArenaHalfWidth = 80f;
	public const float ArenaHalfHeight = 40f;
	public const float MoveSpeed = 60f;
	public const float RotateSpeed = 1.5f;

	static readonly Rgb OutsideColor = new Rgb(8, 8, 16);
	static readonly Rgb FloorColor = new Rgb(22, 26, 46);
	static readonly Rgb GridColor = new Rgb(36, 42, 72);
	static readonly Rgb FrameColor = new Rgb(130, 140, 210);
	static readonly Rgb StatusTextColor = new Rgb(235, 238, 250);
	static readonly Rgb StatusBackdrop = new Rgb(6, 6, 14);
	static readonly Rgb PanelBackdrop = new Rgb(14, 18, 36);

	public FrameClock Clock { get; }
	public EventQueue Events { get; }
	public KeyInputSystem KeyInput { get; }
	public Camera Camera { get; }
	public PhysicsWorld World { get; }
	public ParticleSystem Particles { get; }
	public VoronoiSystem Voronoi { get; }

	public AppViewMode ViewMode { get; private set; } = AppViewMode.Physics;
	public bool IsRunning { get; set; } = true;
	public int SuperSampling { get; private set; } = 3;
	public int TotalCollisionsResolved { get; private set; }

	Canvas _canvas;
	int _termCols = -1, _termRows = -1;
	int _statFrames, _statCells;
	double _lastStatWallSeconds;
	double _measuredFps = 60.0;
	int _avgGlyphsPerFrame;

	public GameApplication(int initialWidth = 100, int initialHeight = 60, int targetFps = 60) {
		Clock = new FrameClock(targetFps);
		Events = new EventQueue();
		KeyInput = new KeyInputSystem(Events);
		Camera = new Camera();
		World = new PhysicsWorld(ArenaHalfWidth, ArenaHalfHeight, Events);
		Particles = new ParticleSystem();
		Voronoi = new VoronoiSystem();
		_canvas = new Canvas(initialWidth, initialHeight) { SuperSampling = SuperSampling };
		ResetCameraZoom();

		ConfigureEventsAndKeyGroups();
	}

	public Canvas Canvas => _canvas;

	void ConfigureEventsAndKeyGroups() {
		// Subscribe collision handler: emit particles whenever the circle and any polygon collide
		// (and smaller sparks on polygon-polygon collisions)
		Events.SubscribeCollision(ev => {
			TotalCollisionsResolved++;
			bool involvesCircle = ev.BodyAName == "Circle" || ev.BodyBName == "Circle";
			if (involvesCircle) {
				Rgb burstColor = ResolveBodyColor(ev.BodyAName == "Circle" ? ev.BodyBName : ev.BodyAName);
				Particles.EmitBurst(ev.ContactPoint, ev.Normal, burstColor, 22);
			} else {
				Rgb colA = ResolveBodyColor(ev.BodyAName);
				Rgb colB = ResolveBodyColor(ev.BodyBName);
				Particles.EmitBurst(ev.ContactPoint, ev.Normal, Rgb.Average(colA, colB), 10);
			}
		});

		// Schedule a recurring timed event every 60 frames (1 simulated second)
		Events.ScheduleTimed(Clock.CurrentFrame, 60, () => {
			// Heartbeat timed event hook (can be observed by tests or game rules)
		}, repeatIntervalFrames: 60, tag: "OneSecondHeartbeat");

		// 1. Global key group (always active)
		KeyCallbackGroup globalGroup = KeyInput.GetOrCreateGroup("Global", isEnabled: true);
		globalGroup.OnKey(ConsoleKey.Escape, () => {
			if (ViewMode == AppViewMode.Voronoi && Voronoi.ColorPicker.IsOpen) {
				CloseColorPicker();
			} else {
				IsRunning = false;
			}
		});
		globalGroup.OnKey(ConsoleKey.V, () => ToggleViewMode());
		globalGroup.OnKey(ConsoleKey.F, () => {
			FNum.Deterministic = !FNum.Deterministic;
		});

		// 2. Camera navigation group (active in Physics mode and Voronoi mode when ColorPicker is closed)
		KeyCallbackGroup cameraGroup = KeyInput.GetOrCreateGroup("CameraControls", isEnabled: true);
		cameraGroup.OnKey(ConsoleKey.R, () => {
			Camera.Position = new Vec2(0, 0);
			Camera.Angle = 0;
			ResetCameraZoom();
		});
		cameraGroup.OnAnyKey(key => {
			if (key.KeyChar >= '1' && key.KeyChar <= '9') {
				SuperSampling = key.KeyChar - '0';
				_canvas.SuperSampling = SuperSampling;
			} else if (key.Key is ConsoleKey.W or ConsoleKey.A or ConsoleKey.S or ConsoleKey.D
					   or ConsoleKey.Q or ConsoleKey.E or ConsoleKey.OemPlus or ConsoleKey.OemMinus
					   or ConsoleKey.Add or ConsoleKey.Subtract) {
				KeyInput.RecordKeyHeld(key.Key, Clock.CurrentFrame);
			}
		});

		// 3. Physics mode specific group
		KeyCallbackGroup physicsGroup = KeyInput.GetOrCreateGroup("PhysicsMode", isEnabled: true);
		physicsGroup.OnKey(ConsoleKey.Spacebar, () => {
			// Emit celebratory particle burst at origin on spacebar in physics mode
			Particles.EmitBurst(Camera.Position, new Vec2(0, 1), new Rgb(255, 210, 90), 20);
		});

		// 4. Voronoi mode group (enabled only when in Voronoi view and ColorPicker is closed)
		KeyCallbackGroup voronoiGroup = KeyInput.GetOrCreateGroup("VoronoiMode", isEnabled: false);
		voronoiGroup.OnKey(ConsoleKey.Spacebar, () => {
			Voronoi.AddSeedAt(Camera.Position);
		});
		voronoiGroup.OnKey(ConsoleKey.Enter, () => {
			OpenColorPicker();
		});

		// 5. ColorPicker UI group (enabled only when ColorPicker modal is open in Voronoi view)
		KeyCallbackGroup pickerGroup = KeyInput.GetOrCreateGroup("ColorPickerUI", isEnabled: false);
		pickerGroup.OnKey(ConsoleKey.Enter, () => CloseColorPicker());
		pickerGroup.OnKey(ConsoleKey.UpArrow, () => Voronoi.ColorPicker.PrevChannel());
		pickerGroup.OnKey(ConsoleKey.W, () => Voronoi.ColorPicker.PrevChannel());
		pickerGroup.OnKey(ConsoleKey.DownArrow, () => Voronoi.ColorPicker.NextChannel());
		pickerGroup.OnKey(ConsoleKey.S, () => Voronoi.ColorPicker.NextChannel());
		pickerGroup.OnKey(ConsoleKey.LeftArrow, () => Voronoi.ColorPicker.AdjustValue(Voronoi.GetSelectedSeed(Camera.Position), -10));
		pickerGroup.OnKey(ConsoleKey.A, () => Voronoi.ColorPicker.AdjustValue(Voronoi.GetSelectedSeed(Camera.Position), -10));
		pickerGroup.OnKey(ConsoleKey.RightArrow, () => Voronoi.ColorPicker.AdjustValue(Voronoi.GetSelectedSeed(Camera.Position), +10));
		pickerGroup.OnKey(ConsoleKey.D, () => Voronoi.ColorPicker.AdjustValue(Voronoi.GetSelectedSeed(Camera.Position), +10));
		pickerGroup.OnAnyKey(key => {
			if (key.KeyChar >= '1' && key.KeyChar <= '8') {
				Voronoi.ColorPicker.ApplyPreset(Voronoi.GetSelectedSeed(Camera.Position), key.KeyChar - '1');
			}
		});
	}

	Rgb ResolveBodyColor(string name) {
		if (name == World.Circle.Name) return World.Circle.Color;
		for (int i = 0; i < World.Polygons.Count; i++) {
			if (World.Polygons[i].Name == name) return World.Polygons[i].Color;
		}
		return new Rgb(255, 220, 120);
	}

	public void ToggleViewMode() {
		if (ViewMode == AppViewMode.Physics) {
			ViewMode = AppViewMode.Voronoi;
			Voronoi.ColorPicker.Close();
			KeyInput.SetGroupEnabled("PhysicsMode", false);
			KeyInput.SetGroupEnabled("VoronoiMode", true);
			KeyInput.SetGroupEnabled("ColorPickerUI", false);
			KeyInput.SetGroupEnabled("CameraControls", true);
		} else {
			ViewMode = AppViewMode.Physics;
			Voronoi.ColorPicker.Close();
			KeyInput.SetGroupEnabled("PhysicsMode", true);
			KeyInput.SetGroupEnabled("VoronoiMode", false);
			KeyInput.SetGroupEnabled("ColorPickerUI", false);
			KeyInput.SetGroupEnabled("CameraControls", true);
		}
	}

	public void OpenColorPicker() {
		if (ViewMode != AppViewMode.Voronoi) return;
		Voronoi.ColorPicker.Open();
		KeyInput.ClearHeldKeys();
		KeyInput.SetGroupEnabled("VoronoiMode", false);
		KeyInput.SetGroupEnabled("CameraControls", false);
		KeyInput.SetGroupEnabled("ColorPickerUI", true);
	}

	public void CloseColorPicker() {
		Voronoi.ColorPicker.Close();
		KeyInput.SetGroupEnabled("ColorPickerUI", false);
		if (ViewMode == AppViewMode.Voronoi) {
			KeyInput.SetGroupEnabled("VoronoiMode", true);
			KeyInput.SetGroupEnabled("CameraControls", true);
		}
	}

	public void ResetCameraZoom() {
		Camera.Zoom = Math.Min(_canvas.Width / (2f * ArenaHalfWidth), _canvas.Height / (2f * ArenaHalfHeight)) * 0.92f;
	}

	public void ResizeCanvas(int cols, int rows) {
		cols = Math.Max(1, cols);
		rows = Math.Max(1, rows);
		if (cols != _termCols || rows != _termRows) {
			_termCols = cols;
			_termRows = rows;
			_canvas = new Canvas(cols, rows * 2) { SuperSampling = SuperSampling };
			ResetCameraZoom();
		}
	}

	/// <summary>
	/// Executes exactly one deterministic frame step:
	/// 1. Resolve User Input events
	/// 2. Resolve Timed events
	/// 3. Apply continuous held-key camera motion
	/// 4. Step PhysicsWorld (which enqueues CollisionEvents)
	/// 5. Resolve Collision events (which emit particles)
	/// 6. Step non-deterministic ParticleSystem
	/// 7. Render current view mode and modular overlay UI rectangles
	/// 8. Advance FrameClock
	/// </summary>
	public void StepFrame() {
		float dt = Clock.FixedDeltaTime;
		long frame = Clock.CurrentFrame;

		// 1. User input resolution
		Events.ResolveInputEvents();

		// 2. Timed events resolution
		Events.ResolveTimedEvents(frame);

		// 3. Camera continuous movement if CameraControls group is enabled
		if (KeyInput.IsGroupEnabled("CameraControls")) {
			bool Held(ConsoleKey k) => KeyInput.IsKeyHeld(k, frame);
			Vec2 move = new Vec2(
				(Held(ConsoleKey.D) ? 1 : 0) - (Held(ConsoleKey.A) ? 1 : 0),
				(Held(ConsoleKey.W) ? 1 : 0) - (Held(ConsoleKey.S) ? 1 : 0));
			if (move.LengthSquared > 0) {
				Camera.Position += move.Normalized().Rotated(-Camera.Angle) * (MoveSpeed / Camera.Zoom * dt);
			}
			Camera.Angle += ((Held(ConsoleKey.Q) ? 1 : 0) - (Held(ConsoleKey.E) ? 1 : 0)) * (RotateSpeed * dt);
			bool zoomIn = Held(ConsoleKey.OemPlus) || Held(ConsoleKey.Add);
			bool zoomOut = Held(ConsoleKey.OemMinus) || Held(ConsoleKey.Subtract);
			Camera.Zoom *= (zoomIn ? 1.015f : 1f) * (zoomOut ? 0.985f : 1f);
			Camera.Zoom = Math.Clamp(Camera.Zoom, 0.15f, 50f);
		}

		// 4. Physics update
		World.Update(dt, frame);

		// 5. Collision event resolution
		Events.ResolveCollisionEvents();

		// 6. Fast non-deterministic particle simulation
		Particles.Update(dt);

		// 7. Render current view to Canvas
		RenderFrame();

		// 8. Advance frame counter
		Clock.AdvanceFrame();
	}

	void RenderFrame() {
		if (ViewMode == AppViewMode.Physics) {
			_canvas.Clear(OutsideColor);
			_canvas.Transform = Camera.GetViewTransform(_canvas.Width, _canvas.Height);
			DrawPhysicsScene(_canvas);
		} else {
			Voronoi.Draw(_canvas, Camera, Clock.CurrentFrame);
		}
		DrawModularHud(_canvas);
	}

	static string Fmt(double v) => (Math.Round(v, 2) == 0 ? 0.0 : v).ToString("F2", CultureInfo.InvariantCulture);
	static string FormatPoint(Vec2 p) => "(" + Fmt(p.X) + ", " + Fmt(p.Y) + ")";

	void DrawPhysicsScene(Canvas g) {
		float hw = World.HalfWidth.ToFloat(), hh = World.HalfHeight.ToFloat();
		g.FillRect(new Vec2(-hw, -hh), new Vec2(hw, hh), FloorColor);
		for (float x = -hw + 20; x < hw - 1; x += 20) g.DrawLine(new Vec2(x, -hh), new Vec2(x, hh), 0.5f, GridColor);
		for (float y = -hh + 20; y < hh - 1; y += 20) g.DrawLine(new Vec2(-hw, y), new Vec2(hw, y), 0.5f, GridColor);

		const float t = 1.5f;
		g.DrawLine(new Vec2(-hw - t / 2, -hh), new Vec2(hw + t / 2, -hh), t, FrameColor);
		g.DrawLine(new Vec2(-hw - t / 2, hh), new Vec2(hw + t / 2, hh), t, FrameColor);
		g.DrawLine(new Vec2(-hw, -hh), new Vec2(-hw, hh), t, FrameColor);
		g.DrawLine(new Vec2(hw, -hh), new Vec2(hw, hh), t, FrameColor);

		// Draw all polygons (Pentagon, Triangle, concave Boomerang)
		for (int i = 0; i < World.Polygons.Count; i++) {
			PolygonBody poly = World.Polygons[i];
			g.FillPolygon(poly.RenderWorld, poly.Color);
			Vec2 pos = poly.Position.ToVec2();
			g.DrawLine(pos, poly.RenderWorld[0], 1.1f, Rgb.Lerp(poly.Color, new Rgb(10, 15, 30), 0.65f));
		}

		// Bouncing circle + highlight
		CircleBody circle = World.Circle;
		Vec2 cPos = circle.Position.ToVec2();
		float cRad = circle.Radius.ToFloat();
		g.FillCircle(cPos, cRad, circle.Color);
		g.FillCircle(cPos + new Vec2(-cRad * 0.3f, cRad * 0.3f), cRad * 0.35f, new Rgb(255, 225, 150));

		// Particles
		Particles.Draw(g);

		// World-space labels using DrawText with optional backdrop blending
		g.DrawText(cPos, FormatPoint(cPos), TextAlign.Center, new Rgb(10, 12, 22), 0.45f);
		g.DrawText(new Vec2(0, 0), "+ origin", TextAlign.Left, new Rgb(10, 12, 22), 0.35f);
	}

	void DrawModularHud(Canvas g) {
		int cols = g.Width;
		int rows = g.Rows;
		if (rows < 3 || cols < 20) return;

		// Top-left modular info widget rectangle (demonstrating rectangular DrawOverlayText with WordWrap)
		int panelW = Math.Min(36, cols / 2);
		var topLeftRect = new ScreenRect(1, 0, panelW, 2);
		string modeName = ViewMode == AppViewMode.Physics ? "PHYSICS ARENA" : "VORONOI DIAGRAM";
		string mathMode = FNum.Deterministic ? "Det-Fixed(Q32.32)" : "Fast-Float";
		string topLeftInfo = $"[{modeName}] Frame #{Clock.CurrentFrame} | Math: {mathMode} | Hits: {TotalCollisionsResolved} | Particles: {Particles.ActiveCount}";
		g.DrawOverlayText(topLeftRect, topLeftInfo, new Rgb(210, 230, 255), TextAlign.Left, PanelBackdrop, 0.75f, TextWrapMode.WordWrap);

		// Top-right modular controls widget rectangle
		int rightW = Math.Min(42, cols - panelW - 3);
		if (rightW >= 18) {
			var topRightRect = new ScreenRect(cols - rightW - 1, 0, rightW, 2);
			string controlsText = ViewMode == AppViewMode.Physics
				? "V: Voronoi mode | F: Toggle Deterministic | WASD: Move | QE: Rotate | -/+: Zoom"
				: "V: Physics mode | Space: Place Seed | Enter: Color Picker | WASD/QE/-+: Camera";
			g.DrawOverlayText(topRightRect, controlsText, new Rgb(245, 225, 155), TextAlign.Right, PanelBackdrop, 0.75f, TextWrapMode.WordWrap);
		}

		// Bottom status bar rectangle
		var bottomRect = new ScreenRect(0, rows - 1, cols, 1);
		string status = $" AA {SuperSampling}x{SuperSampling} | {_measuredFps,2:0} fps | {_avgGlyphsPerFrame,3} glyphs | Cam {FormatPoint(Camera.Position)} | 1-9 AA, R reset, V mode, Esc quit";
		g.DrawOverlayText(bottomRect, status, StatusTextColor, TextAlign.Left, StatusBackdrop, 0.8f, TextWrapMode.None);
	}

	public void RunInteractiveTerminalLoop() {
		Console.CancelKeyPress += (s, e) => { e.Cancel = true; IsRunning = false; };

		using var terminal = new TerminalPresenter();
		var wallClock = Stopwatch.StartNew();
		double nextFrameTarget = wallClock.Elapsed.TotalSeconds;
		_lastStatWallSeconds = nextFrameTarget;

		while (IsRunning) {
			int cols = Math.Max(1, Console.WindowWidth);
			int rows = Math.Max(1, Console.WindowHeight);
			if (cols != _termCols || rows != _termRows) {
				ResizeCanvas(cols, rows);
				terminal.Invalidate();
			}

			// Poll raw terminal keys and enqueue UserInputEvents
			while (Console.KeyAvailable) {
				ConsoleKeyInfo key = Console.ReadKey(true);
				Events.EnqueueInput(new UserInputEvent(Clock.CurrentFrame, key));
			}

			// Execute one strict frame step
			StepFrame();

			// Present frame to terminal
			_statCells += terminal.Present(_canvas);
			_statFrames++;

			double now = wallClock.Elapsed.TotalSeconds;
			if (now - _lastStatWallSeconds >= 0.25) {
				_measuredFps = _statFrames / (now - _lastStatWallSeconds);
				_avgGlyphsPerFrame = _statCells / Math.Max(1, _statFrames);
				_lastStatWallSeconds = now;
				_statFrames = 0;
				_statCells = 0;
			}

			nextFrameTarget += Clock.FixedDeltaTime;
			double spare = nextFrameTarget - wallClock.Elapsed.TotalSeconds;
			if (spare > 0.002) {
				Thread.Sleep((int)(spare * 1000));
			} else if (spare < -0.25) {
				// Avoid spiral of death if window was paused/dragged
				nextFrameTarget = wallClock.Elapsed.TotalSeconds;
			}
		}
	}
}
