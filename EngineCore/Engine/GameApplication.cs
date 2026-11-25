#nullable enable
using System;
using System.Globalization;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Particles;
using ConsoleGraphicsEngine.Physics;
using ConsoleGraphicsEngine.Rendering;
using ConsoleGraphicsEngine.SceneGraph;
using ConsoleGraphicsEngine.Voronoi;

namespace ConsoleGraphicsEngine.Engine {
	public enum AppViewMode { Physics, Voronoi }

	/// <summary>Host-agnostic simulation &amp; rendering coordinator (zero <c>System.Console</c> or <c>UnityEngine</c> refs).</summary>
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
		public AudioSystem Audio { get; }
		public Camera Camera { get; }
		public PhysicsWorld World { get; }
		public Scene ActiveScene { get; }
		public ParticleSystem Particles { get; }
		public VoronoiSystem Voronoi { get; }
		public IRenderBackend RenderBackend { get; private set; }

		public AppViewMode ViewMode { get; private set; } = AppViewMode.Physics;
		public bool IsRunning { get; set; } = true;
		public int SuperSampling { get; private set; } = 3;
		public int TotalCollisionsResolved { get; private set; }
		public double MeasuredFps { get; set; } = 60.0;
		public int AvgGlyphsPerFrame { get; set; }

		public Canvas Canvas => RenderBackend.SoftwareCanvas
			?? throw new InvalidOperationException("Active IRenderBackend has no software Canvas.");

		public GameApplication(int initialPixelWidth = 100, int initialPixelHeight = 60, int targetFps = 60, IRenderBackend? customBackend = null) {
			Clock = new FrameClock(targetFps);
			Events = new EventQueue();
			KeyInput = new KeyInputSystem(Events);
			Audio = new AudioSystem(Events);
			Camera = new Camera();
			World = new PhysicsWorld(ArenaHalfWidth, ArenaHalfHeight, Events);
			ActiveScene = new Scene("PhysicsArenaScene");
			Particles = new ParticleSystem();
			Voronoi = new VoronoiSystem();
			RenderBackend = customBackend ?? new CanvasRenderBackend(initialPixelWidth, initialPixelHeight) { SuperSampling = SuperSampling };

			Voronoi.ColorPicker.OnCloseRequested = CloseColorPicker;
			BuildPhysicsEntities();
			ResetCameraZoom();
			ConfigureEventsAndKeyGroups();
		}

		public void SetRenderBackend(IRenderBackend backend) {
			RenderBackend = backend;
			RenderBackend.SuperSampling = SuperSampling;
			ResetCameraZoom();
		}

		void BuildPhysicsEntities() {
			for (int i = 0; i < World.Polygons.Count; i++) {
				PolygonBody poly = World.Polygons[i];
				Entity polyEntity = ActiveScene.CreateEntity(poly.Name, poly.Position.ToVec2(), poly.Angle.ToFloat());
				polyEntity.Tag = poly.IsConcave ? "ConcavePolygon" : "ConvexPolygon";
				polyEntity.AddComponent(new RigidBodyComponent(poly));
				polyEntity.AddComponent(new ShapeRendererComponent(poly) { DrawSpoke = true });
			}

			Entity circleEntity = ActiveScene.CreateEntity(World.Circle.Name, World.Circle.Position.ToVec2(), 0f);
			circleEntity.Tag = "PlayerCircle";
			circleEntity.AddComponent(new RigidBodyComponent(World.Circle));
			circleEntity.AddComponent(new ShapeRendererComponent(World.Circle) { DrawHighlight = true, DrawPositionLabel = true });
		}

		void ConfigureEventsAndKeyGroups() {
			Events.SubscribeCollision(ev => {
				TotalCollisionsResolved++;
				bool involvesCircle = ev.BodyAName == "Circle" || ev.BodyBName == "Circle";
				if (involvesCircle) {
					Rgb burstColor = ResolveBodyColor(ev.BodyAName == "Circle" ? ev.BodyBName : ev.BodyAName);
					Particles.EmitBurst(ev.ContactPoint, ev.Normal, burstColor, 22);
					Events.EnqueueAudio(new AudioEvent(Clock.CurrentFrame, "collision_circle_polygon",
						volume: Math.Clamp(ev.ImpulseMagnitude / 120f, 0.25f, 1.0f),
						pitch: 1.15f, worldPosition: ev.ContactPoint, synthFrequencyHz: 520f, synthDurationSeconds: 0.09f));
				} else {
					Rgb colA = ResolveBodyColor(ev.BodyAName), colB = ResolveBodyColor(ev.BodyBName);
					Particles.EmitBurst(ev.ContactPoint, ev.Normal, Rgb.Average(colA, colB), 10);
					Events.EnqueueAudio(new AudioEvent(Clock.CurrentFrame, "collision_polygon_polygon",
						volume: Math.Clamp(ev.ImpulseMagnitude / 140f, 0.2f, 0.85f),
						pitch: 0.9f, worldPosition: ev.ContactPoint, synthFrequencyHz: 340f, synthDurationSeconds: 0.07f));
				}
			});

			Events.ScheduleTimed(Clock.CurrentFrame, 60, () => { }, repeatIntervalFrames: 60, tag: "OneSecondHeartbeat");

			// 1. Global group
			KeyCallbackGroup globalGroup = KeyInput.GetOrCreateGroup("Global", isEnabled: true);
			globalGroup.OnKey(KeyCode.Escape, () => {
				if (ViewMode == AppViewMode.Voronoi && Voronoi.ColorPicker.IsOpen) CloseColorPicker();
				else IsRunning = false;
			});
			globalGroup.OnKey(KeyCode.V, ToggleViewMode);
			globalGroup.OnKey(KeyCode.F, () => FNum.Deterministic = !FNum.Deterministic);

			// 2. CameraControls group
			KeyCallbackGroup cameraGroup = KeyInput.GetOrCreateGroup("CameraControls", isEnabled: true);
			cameraGroup.OnKey(KeyCode.R, () => {
				Camera.Position = new Vec2(0, 0);
				Camera.Angle = 0;
				ResetCameraZoom();
			});
			cameraGroup.OnAnyKey(key => {
				if (key.KeyChar >= '1' && key.KeyChar <= '9') {
					SuperSampling = key.KeyChar - '0';
					RenderBackend.SuperSampling = SuperSampling;
				} else if (key.Key is KeyCode.W or KeyCode.A or KeyCode.S or KeyCode.D
						 or KeyCode.Q or KeyCode.E or KeyCode.Plus or KeyCode.Minus) {
					KeyInput.RecordKeyHeld(key.Key, Clock.CurrentFrame);
				}
			});
			cameraGroup.OnPointer(pev => {
				if (pev.Type == PointerEventType.Scroll && MathF.Abs(pev.ScrollDelta) > 0.01f) {
					Camera.Zoom = Math.Clamp(Camera.Zoom * (pev.ScrollDelta > 0 ? 1.12f : 0.89f), 0.15f, 50f);
				}
			});

			// 3. PhysicsMode group
			KeyCallbackGroup physicsGroup = KeyInput.GetOrCreateGroup("PhysicsMode", isEnabled: true);
			physicsGroup.OnKey(KeyCode.Space, () => {
				Particles.EmitBurst(Camera.Position, new Vec2(0, 1), new Rgb(255, 210, 90), 20);
				Events.EnqueueAudio(new AudioEvent(Clock.CurrentFrame, "particle_burst", 0.7f, 1.2f, Camera.Position, 660f, 0.06f));
			});
			physicsGroup.OnPointer(pev => {
				if (pev.Type == PointerEventType.Down && pev.Button == PointerButton.Left) {
					Particles.EmitBurst(pev.WorldPos, new Vec2(0, 1), new Rgb(255, 210, 90), 20);
					Events.EnqueueAudio(new AudioEvent(Clock.CurrentFrame, "pointer_tap_burst", 0.75f, 1.25f, pev.WorldPos, 680f, 0.06f));
				}
			});

			// 4. VoronoiMode group
			KeyCallbackGroup voronoiGroup = KeyInput.GetOrCreateGroup("VoronoiMode", isEnabled: false);
			voronoiGroup.OnKey(KeyCode.Space, () => {
				Voronoi.AddSeedAt(Camera.Position);
				Events.EnqueueAudio(new AudioEvent(Clock.CurrentFrame, "voronoi_seed_placed", 0.8f, 1.0f, Camera.Position, 580f, 0.08f));
			});
			voronoiGroup.OnKey(KeyCode.Enter, OpenColorPicker);
			voronoiGroup.OnPointer(pev => {
				if (pev.Type == PointerEventType.Down && pev.Button == PointerButton.Left) {
					Camera.Position = pev.WorldPos;
					Voronoi.AddSeedAt(pev.WorldPos);
					Events.EnqueueAudio(new AudioEvent(Clock.CurrentFrame, "voronoi_seed_placed", 0.8f, 1.05f, pev.WorldPos, 600f, 0.08f));
				} else if (pev.Type == PointerEventType.Down && pev.Button == PointerButton.Right) {
					Camera.Position = pev.WorldPos;
					OpenColorPicker();
				}
			});

			// 5. ColorPickerUI group
			KeyCallbackGroup pickerGroup = KeyInput.GetOrCreateGroup("ColorPickerUI", isEnabled: false);
			pickerGroup.OnKey(KeyCode.Enter, CloseColorPicker);
			pickerGroup.OnKey(KeyCode.UpArrow, () => Voronoi.ColorPicker.PrevChannel());
			pickerGroup.OnKey(KeyCode.W, () => Voronoi.ColorPicker.PrevChannel());
			pickerGroup.OnKey(KeyCode.DownArrow, () => Voronoi.ColorPicker.NextChannel());
			pickerGroup.OnKey(KeyCode.S, () => Voronoi.ColorPicker.NextChannel());
			pickerGroup.OnKey(KeyCode.LeftArrow, () => Voronoi.ColorPicker.AdjustValue(Voronoi.GetSelectedSeed(Camera.Position), -10));
			pickerGroup.OnKey(KeyCode.A, () => Voronoi.ColorPicker.AdjustValue(Voronoi.GetSelectedSeed(Camera.Position), -10));
			pickerGroup.OnKey(KeyCode.RightArrow, () => Voronoi.ColorPicker.AdjustValue(Voronoi.GetSelectedSeed(Camera.Position), +10));
			pickerGroup.OnKey(KeyCode.D, () => Voronoi.ColorPicker.AdjustValue(Voronoi.GetSelectedSeed(Camera.Position), +10));
			pickerGroup.OnAnyKey(key => {
				if (key.KeyChar >= '1' && key.KeyChar <= '8') {
					Voronoi.ColorPicker.ApplyPreset(Voronoi.GetSelectedSeed(Camera.Position), key.KeyChar - '1');
					Events.EnqueueAudio(new AudioEvent(Clock.CurrentFrame, "ui_color_preset", 0.6f, 1.1f, Camera.Position, 720f, 0.05f));
				}
			});
			pickerGroup.OnPointer(pev => {
				VoronoiSeed? selected = Voronoi.GetSelectedSeed(Camera.Position);
				if (Voronoi.ColorPicker.HandlePointer(pev, selected, RenderBackend.CellColumns, RenderBackend.CellRows) && pev.Type == PointerEventType.Down) {
					Events.EnqueueAudio(new AudioEvent(Clock.CurrentFrame, "ui_slider_adjust", 0.5f, 1.0f, Camera.Position, 500f, 0.04f));
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
			bool toVoronoi = ViewMode == AppViewMode.Physics;
			ViewMode = toVoronoi ? AppViewMode.Voronoi : AppViewMode.Physics;
			Voronoi.ColorPicker.Close();
			KeyInput.SetGroupEnabled("PhysicsMode", !toVoronoi);
			KeyInput.SetGroupEnabled("VoronoiMode", toVoronoi);
			KeyInput.SetGroupEnabled("ColorPickerUI", false);
			KeyInput.SetGroupEnabled("CameraControls", true);
			Events.EnqueueAudio(new AudioEvent(Clock.CurrentFrame, "mode_switch", 0.7f, 1.0f, Camera.Position, 480f, 0.07f));
		}

		public void OpenColorPicker() {
			if (ViewMode != AppViewMode.Voronoi) return;
			Voronoi.ColorPicker.Open();
			KeyInput.ClearHeldKeys();
			KeyInput.SetGroupEnabled("VoronoiMode", false);
			KeyInput.SetGroupEnabled("CameraControls", false);
			KeyInput.SetGroupEnabled("ColorPickerUI", true);
			Events.EnqueueAudio(new AudioEvent(Clock.CurrentFrame, "ui_open", 0.65f, 1.15f, Camera.Position, 620f, 0.06f));
		}

		public void CloseColorPicker() {
			Voronoi.ColorPicker.Close();
			KeyInput.SetGroupEnabled("ColorPickerUI", false);
			if (ViewMode == AppViewMode.Voronoi) {
				KeyInput.SetGroupEnabled("VoronoiMode", true);
				KeyInput.SetGroupEnabled("CameraControls", true);
			}
			Events.EnqueueAudio(new AudioEvent(Clock.CurrentFrame, "ui_close", 0.65f, 0.9f, Camera.Position, 420f, 0.06f));
		}

		public void ResetCameraZoom() =>
			Camera.Zoom = Math.Min(RenderBackend.PixelWidth / (2f * ArenaHalfWidth), RenderBackend.PixelHeight / (2f * ArenaHalfHeight)) * 0.92f;

		public void ResizeViewport(int cellCols, int cellRows) {
			cellCols = Math.Max(1, cellCols);
			cellRows = Math.Max(1, cellRows);
			if (RenderBackend is CanvasRenderBackend crb && (crb.CellColumns != cellCols || crb.CellRows != cellRows)) {
				crb.Resize(cellCols, cellRows * 2);
				ResetCameraZoom();
			}
		}

		public void StepFrame() {
			float dt = Clock.FixedDeltaTime;
			long frame = Clock.CurrentFrame;

			KeyInput.UpdateVirtualCursorFallback(RenderBackend.CellColumns / 2, RenderBackend.CellRows / 2, Camera.Position);
			Events.ResolveInputEvents();
			Events.ResolveTimedEvents(frame);

			if (KeyInput.IsGroupEnabled("CameraControls")) {
				bool Held(KeyCode k) => KeyInput.IsKeyHeld(k, frame);
				Vec2 move = new Vec2(
					(Held(KeyCode.D) ? 1 : 0) - (Held(KeyCode.A) ? 1 : 0),
					(Held(KeyCode.W) ? 1 : 0) - (Held(KeyCode.S) ? 1 : 0));
				if (move.LengthSquared > 0) {
					Camera.Position += move.Normalized().Rotated(-Camera.Angle) * (MoveSpeed / Camera.Zoom * dt);
				}
				Camera.Angle += ((Held(KeyCode.Q) ? 1 : 0) - (Held(KeyCode.E) ? 1 : 0)) * (RotateSpeed * dt);
				Camera.Zoom *= (Held(KeyCode.Plus) ? 1.015f : 1f) * (Held(KeyCode.Minus) ? 0.985f : 1f);
				Camera.Zoom = Math.Clamp(Camera.Zoom, 0.15f, 50f);
			}

			World.Update(dt, frame);
			ActiveScene.SyncTransformsFromPhysics();
			ActiveScene.Update(dt, frame);
			Events.ResolveCollisionEvents();
			Events.ResolveAudioEvents();
			Particles.Update(dt);
			RenderFrame();
			Clock.AdvanceFrame();
		}

		void RenderFrame() {
			if (ViewMode == AppViewMode.Physics) {
				RenderBackend.Clear(OutsideColor);
				RenderBackend.ViewTransform = Camera.GetViewTransform(RenderBackend.PixelWidth, RenderBackend.PixelHeight);
				DrawPhysicsScene(RenderBackend);
			} else {
				Voronoi.Draw(RenderBackend, Camera, Clock.CurrentFrame);
			}
			DrawModularHud(RenderBackend);
		}

		static string Fmt(double v) => (Math.Round(v, 2) == 0 ? 0.0 : v).ToString("F2", CultureInfo.InvariantCulture);
		static string FormatPoint(Vec2 p) => "(" + Fmt(p.X) + ", " + Fmt(p.Y) + ")";

		void DrawPhysicsScene(IRenderBackend g) {
			float hw = World.HalfWidth.ToFloat(), hh = World.HalfHeight.ToFloat();
			g.FillRect(new Vec2(-hw, -hh), new Vec2(hw, hh), FloorColor);
			for (float x = -hw + 20; x < hw - 1; x += 20) g.DrawLine(new Vec2(x, -hh), new Vec2(x, hh), 0.5f, GridColor);
			for (float y = -hh + 20; y < hh - 1; y += 20) g.DrawLine(new Vec2(-hw, y), new Vec2(hw, y), 0.5f, GridColor);

			const float t = 1.5f;
			g.DrawLine(new Vec2(-hw - t / 2, -hh), new Vec2(hw + t / 2, -hh), t, FrameColor);
			g.DrawLine(new Vec2(-hw - t / 2, hh), new Vec2(hw + t / 2, hh), t, FrameColor);
			g.DrawLine(new Vec2(-hw, -hh), new Vec2(-hw, hh), t, FrameColor);
			g.DrawLine(new Vec2(hw, -hh), new Vec2(hw, hh), t, FrameColor);

			ActiveScene.Draw(g);
			Particles.Draw(g);
			g.DrawText(new Vec2(0, 0), "+ origin", TextAlign.Left, new Rgb(10, 12, 22), 0.35f);
		}

		void DrawModularHud(IRenderBackend g) {
			int cols = g.CellColumns, rows = g.CellRows;
			if (rows < 3 || cols < 20) return;

			int panelW = Math.Min(42, cols / 2);
			string modeName = ViewMode == AppViewMode.Physics ? "PHYSICS ARENA" : "VORONOI DIAGRAM";
			string mathMode = FNum.Deterministic ? "Det-Fixed(Q32.32)" : "Fast-Float";
			string lastCue = Audio.LastPlayedEvent != null ? Audio.LastPlayedEvent.CueId : "none";
			string topLeftInfo = $"[{modeName}] F#{Clock.CurrentFrame} | {mathMode} | Hits:{TotalCollisionsResolved} | Part:{Particles.ActiveCount} | SFX:{lastCue}";
			g.DrawOverlayText(new ScreenRect(1, 0, panelW, 2), topLeftInfo, new Rgb(210, 230, 255), TextAlign.Left, PanelBackdrop, 0.75f, TextWrapMode.WordWrap);

			int rightW = Math.Min(44, cols - panelW - 3);
			if (rightW >= 18) {
				string controlsText = ViewMode == AppViewMode.Physics
					? "V: Voronoi | F: Det/Float | Click/Space: Burst | WASD/QE/Wheel: Cam"
					: "V: Physics | Click/Space: Seed | RClick/Enter: Picker | WASD/QE: Cam";
				g.DrawOverlayText(new ScreenRect(cols - rightW - 1, 0, rightW, 2), controlsText, new Rgb(245, 225, 155), TextAlign.Right, PanelBackdrop, 0.75f, TextWrapMode.WordWrap);
			}

			string ptrInfo = KeyInput.HasActivePointer ? $"Ptr({KeyInput.PointerCellX},{KeyInput.PointerCellY})" : "Ptr(Virtual)";
			string status = $" AA {SuperSampling}x{SuperSampling} | {MeasuredFps,2:0} fps | {AvgGlyphsPerFrame,3} glyphs | Cam {FormatPoint(Camera.Position)} | {ptrInfo} | 1-9 AA, R reset, Esc quit";
			g.DrawOverlayText(new ScreenRect(0, rows - 1, cols, 1), status, StatusTextColor, TextAlign.Left, StatusBackdrop, 0.8f, TextWrapMode.None);
		}
	}
}
