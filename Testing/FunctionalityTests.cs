#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ConsoleGraphicsEngine.CliHost;
using ConsoleGraphicsEngine.Engine;
using ConsoleGraphicsEngine.Logging;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Particles;
using ConsoleGraphicsEngine.Physics;
using ConsoleGraphicsEngine.Rendering;
using ConsoleGraphicsEngine.SceneGraph;
using ConsoleGraphicsEngine.Scripting;
using ConsoleGraphicsEngine.UI;
using ConsoleGraphicsEngine.UnityBridge;
using ConsoleGraphicsEngine.Voronoi;

namespace ConsoleGraphicsEngine.Testing {
	/// <summary>16-part automated CLI test suite invoked via <c>dotnet run -- --test</c>.</summary>
	public static class FunctionalityTests {
		public static int RunAllTests() {
			int passed = 0, failed = 0;

			void Run(string name, Action test) {
				try {
					test();
					Console.WriteLine($"[PASS] {name}");
					passed++;
				} catch (Exception ex) {
					Console.Error.WriteLine($"[FAIL] {name}: {ex.Message}");
					failed++;
				}
			}

			Console.WriteLine("=== Running ConsoleGraphicsEngine Functionality & Unity-Portability Tests ===");

			Run("1. DrawOverlayText rectangular bounds, word wrapping, and backdrop blending", TestDrawOverlayTextRectAndWrap);
			Run("2. DrawText world-space text backdrop blending", TestDrawTextBackdrop);
			Run("3. EventQueue ordering: Input, Pointer, Timed, Collision, and Audio resolution", TestEventQueue);
			Run("4. KeyInputSystem platform-neutral KeyCode callback groups and state toggling", TestKeyCallbackGroups);
			Run("5. Portable 64-bit Q32.32 deterministic FNum physics reproducibility (no Int128)", TestDeterministicPhysics);
			Run("6. ParticleSystem & AudioEvent emission on Circle-Polygon collision", TestParticleAndAudioEmissionOnCollision);
			Run("7. Polygon-Polygon collision (Pentagon, Triangle) and Concave Boomerang collision", TestPolygonAndConcaveCollisions);
			Run("8. Voronoi mode switching ('v'), seed placement (Space), flashing selection, and ColorPicker (Enter)", TestVoronoiAndColorPickerFlow);
			Run("9. Scene, Entity, RigidBodyComponent & ShapeRendererComponent lifecycle synchronization", TestSceneAndEntityComponents);
			Run("10. Pointer / Touch / ANSI SGR-1006 Mouse input & interactive UISlider/UIButton hit-testing", TestPointerAndInteractiveUIWidgets);
			Run("11. Dual-mode IRenderBackend: CanvasRenderBackend vs UnityNativeVectorBackend", TestDualRenderBackends);
			Run("12. Portable Log system (EngineCore/Logging/Log.cs) visibility, color escapes, and file sink", TestLoggingSystem);
			Run("13. Deterministic 64-bit FNV-1a StateChecksum (#if DEBUG) reproducibility & perturbation", TestStateChecksum);
			Run("14. InputScriptEngine batched deterministic scripting & inline invariant assertions", TestInputScriptingEngine);
			Run("15. FrameExporter ASCII/ANSI snapshots and BMP + Animated GIF89a export to local/", TestFrameExporterBmpAndGif);
			Run("16. Roslyn static analysis (EngineCore isolation, K&R braces, LF, CLI Help) & Preprocessor Symbol Docs", TestRoslynAnalyzerAndPreprocessorDocs);

			Console.WriteLine($"=== Results: {passed} passed, {failed} failed ===");
			return failed == 0 ? 0 : 1;
		}

		static void Assert(bool condition, string message) {
			if (!condition) throw new InvalidOperationException(message);
		}

		static void TestDrawOverlayTextRectAndWrap() {
			var canvas = new Canvas(40, 20);
			var bg = new Rgb(10, 10, 10);
			canvas.Clear(bg);

			var rect = new ScreenRect(5, 2, 12, 3);
			canvas.DrawOverlayText(rect, "Hello modular world engine UI", new Rgb(250, 200, 100), TextAlign.Left, new Rgb(110, 110, 110), 0.5f, TextWrapMode.WordWrap);

			Assert(canvas.Pixels[0] == bg, "Outside pixel must remain untouched.");
			Rgb insidePixel = canvas.Pixels[(2 * 2) * canvas.Width + 5];
			Assert(insidePixel != bg && insidePixel.R == 60, $"Inside pixel should blend toward backdrop (got R={insidePixel.R}).");

			List<string> wrapped = Canvas.WrapText("Hello modular world engine UI", 12, TextWrapMode.WordWrap);
			Assert(wrapped.Count >= 3 && wrapped[0] == "Hello" && wrapped[1] == "modular", "WordWrap lines mismatch.");
			Assert(canvas.TextChars[2 * canvas.Width + 5] == 'H' && canvas.TextChars[3 * canvas.Width + 5] == 'm', "Wrapped glyph cell placement mismatch.");
		}

		static void TestDrawTextBackdrop() {
			var canvas = new Canvas(40, 20);
			canvas.Clear(new Rgb(0, 0, 0));
			canvas.DrawText(new Vec2(10, 6), "TEST", new Rgb(255, 255, 255), TextAlign.Left, new Rgb(200, 100, 50), 0.5f);

			Rgb blended = canvas.Pixels[(2 * 3) * canvas.Width + 10];
			Assert(blended.R == 100 && blended.G == 50 && blended.B == 25, $"DrawText backdrop blend mismatch ({blended.R},{blended.G},{blended.B})");
			Assert(canvas.TextChars[3 * canvas.Width + 10] == 'T', "Expected 'T' at row 3 col 10.");
		}

		static void TestEventQueue() {
			var queue = new EventQueue();
			var log = new List<string>();

			queue.SubscribeInput(ev => log.Add($"Input:{ev.KeyInfo.Key}"));
			queue.SubscribePointer(pev => log.Add($"Pointer:{pev.Type}@({pev.CellX},{pev.CellY})"));
			queue.ScheduleTimed(0, 2, () => log.Add("Timed:Frame2"));
			queue.SubscribeCollision(ev => log.Add($"Collision:{ev.BodyAName}-{ev.BodyBName}"));
			queue.SubscribeAudio(aev => log.Add($"Audio:{aev.CueId}"));

			queue.EnqueueInput(new UserInputEvent(0, new KeyEventData(KeyCode.W, 'w')));
			queue.EnqueuePointer(new PointerInputEvent(0, PointerEventType.Down, PointerButton.Left, 10, 5, new Vec2(10, 10), new Vec2(0, 0)));
			queue.EnqueueCollision(new CollisionEvent(0, "Circle", "Pentagon", new Vec2(1, 2), new Vec2(0, 1), 0.5f, 10f));
			queue.EnqueueAudio(new AudioEvent(0, "sfx_hit", 0.8f, 1.0f));

			queue.ResolveInputEvents();
			queue.ResolveTimedEvents(1);
			queue.ResolveTimedEvents(2);
			queue.ResolveCollisionEvents();
			queue.ResolveAudioEvents();

			Assert(log.Count == 5, $"Expected 5 resolved events, got {log.Count}");
			Assert(log[0] == "Input:W" && log[1] == "Pointer:Down@(10,5)" && log[2] == "Timed:Frame2" &&
				   log[3] == "Collision:Circle-Pentagon" && log[4] == "Audio:sfx_hit", "Event phase resolution order mismatch.");
		}

		static void TestKeyCallbackGroups() {
			var queue = new EventQueue();
			var keys = new KeyInputSystem(queue);
			int gameplayCalls = 0, uiCalls = 0;

			keys.GetOrCreateGroup("Gameplay", isEnabled: true).OnKey(KeyCode.Enter, () => gameplayCalls++);
			keys.GetOrCreateGroup("ModalUI", isEnabled: false).OnKey(KeyCode.Enter, () => uiCalls++);

			queue.EnqueueInput(new UserInputEvent(1, new KeyEventData(KeyCode.Enter, '\r')));
			queue.ResolveInputEvents();
			Assert(gameplayCalls == 1 && uiCalls == 0, "Only enabled Gameplay group should trigger.");

			keys.SetGroupEnabled("Gameplay", false);
			keys.SetGroupEnabled("ModalUI", true);

			queue.EnqueueInput(new UserInputEvent(2, new KeyEventData(KeyCode.Enter, '\r')));
			queue.ResolveInputEvents();
			Assert(gameplayCalls == 1 && uiCalls == 1, "Only enabled ModalUI group should trigger after toggle.");
		}

		static void TestDeterministicPhysics() {
			bool prev = FNum.Deterministic;
			try {
				FNum.Deterministic = true;
				FNum three = FNum.Sqrt(FNum.FromFloat(9f));
				Assert(MathF.Abs(three.ToFloat() - 3f) < 1e-4f, $"Portable Q32.32 Sqrt(9) expected 3, got {three.ToFloat()}");

				var worldA = new PhysicsWorld(80, 40);
				var worldB = new PhysicsWorld(80, 40);
				for (int f = 0; f < 180; f++) {
					worldA.Update(1f / 60f, f);
					worldB.Update(1f / 60f, f);
				}

				Assert(worldA.Circle.Position.X.FixedRaw == worldB.Circle.Position.X.FixedRaw &&
					   worldA.Circle.Position.Y.FixedRaw == worldB.Circle.Position.Y.FixedRaw &&
					   worldA.Boomerang.Angle.FixedRaw == worldB.Boomerang.Angle.FixedRaw,
					"Deterministic Q32.32 trajectory must be bit-identical across runs.");

				FNum.Deterministic = false;
				var worldFast = new PhysicsWorld(80, 40);
				for (int f = 0; f < 60; f++) worldFast.Update(1f / 60f, f);
				Assert(!float.IsNaN(worldFast.Circle.Position.X.ToFloat()), "Fast float physics position must be finite.");
			} finally {
				FNum.Deterministic = prev;
			}
		}

		static void TestParticleAndAudioEmissionOnCollision() {
			var app = new GameApplication(80, 40, 60);
			app.World.Circle.Position = FVec2.FromVec2(new Vec2(20f, -8f));
			app.World.Circle.Velocity = FVec2.FromVec2(new Vec2(60f, 0f));
			app.World.Pentagon.Position = FVec2.FromVec2(new Vec2(28f, -8f));
			app.World.Pentagon.Velocity = FVec2.FromVec2(new Vec2(-40f, 0f));
			app.World.Pentagon.UpdateWorld();

			app.StepFrame();
			Assert(app.TotalCollisionsResolved > 0 && app.Particles.ActiveCount > 0, "Expected collision + particle burst.");
			Assert(app.Audio.TotalCuesPlayed > 0 && app.Audio.LastPlayedEvent?.CueId == "collision_circle_polygon", "Expected collision audio cue.");
		}

		static void TestPolygonAndConcaveCollisions() {
			var queue = new EventQueue();
			int polyCollisions = 0;
			queue.SubscribeCollision(ev => {
				if (ev.BodyAName != "Circle" && ev.BodyBName != "Circle") polyCollisions++;
			});

			var world = new PhysicsWorld(80, 40, queue);
			Assert(world.Polygons.Count == 3 && world.Boomerang.IsConcave && world.Boomerang.ConvexPartsLocal.Count >= 3,
				"Boomerang must be ear-clipped into >= 3 convex triangles.");

			world.Triangle.Position = FVec2.FromVec2(new Vec2(0f, 0f));
			world.Triangle.Velocity = FVec2.FromVec2(new Vec2(50f, 0f));
			world.Triangle.UpdateWorld();

			world.Boomerang.Position = FVec2.FromVec2(new Vec2(9f, 0f));
			world.Boomerang.Velocity = FVec2.FromVec2(new Vec2(-50f, 0f));
			world.Boomerang.UpdateWorld();

			world.Update(1f / 60f, 1);
			queue.ResolveCollisionEvents();

			Assert(polyCollisions > 0 && world.Triangle.Velocity.X.ToFloat() < 50f && world.Boomerang.Velocity.X.ToFloat() > -50f,
				"SAT collision must resolve impulse between Triangle and concave Boomerang.");
		}

		static void TestVoronoiAndColorPickerFlow() {
			var app = new GameApplication(80, 40, 60);
			app.Events.EnqueueInput(new UserInputEvent(app.Clock.CurrentFrame, new KeyEventData(KeyCode.V, 'v')));
			app.StepFrame();
			Assert(app.ViewMode == AppViewMode.Voronoi, "Pressing 'v' must switch to Voronoi mode.");

			app.Camera.Position = new Vec2(12.5f, -7.25f);
			int initialSeeds = app.Voronoi.Seeds.Count;
			app.Events.EnqueueInput(new UserInputEvent(app.Clock.CurrentFrame, new KeyEventData(KeyCode.Space, ' ')));
			app.StepFrame();
			Assert(app.Voronoi.Seeds.Count == initialSeeds + 1, "Spacebar must add Voronoi seed at camera center.");

			VoronoiSeed? selected = app.Voronoi.GetSelectedSeed(app.Camera.Position);
			Assert(selected != null && (selected.Position - app.Camera.Position).Length < 1e-3f, "Placed seed must be selected.");

			app.Events.EnqueueInput(new UserInputEvent(app.Clock.CurrentFrame, new KeyEventData(KeyCode.Enter, '\r')));
			app.StepFrame();
			Assert(app.Voronoi.ColorPicker.IsOpen, "Enter must open ColorPickerUI.");

			app.Events.EnqueueInput(new UserInputEvent(app.Clock.CurrentFrame, new KeyEventData(KeyCode.D3, '3')));
			app.StepFrame();
			Assert(selected!.Color == ColorPickerUI.Presets[2], "Preset key must update selected Voronoi seed color.");

			app.Events.EnqueueInput(new UserInputEvent(app.Clock.CurrentFrame, new KeyEventData(KeyCode.Enter, '\r')));
			app.StepFrame();
			Assert(!app.Voronoi.ColorPicker.IsOpen, "Enter must close ColorPickerUI.");
		}

		static void TestSceneAndEntityComponents() {
			var app = new GameApplication(80, 40, 60);
			Assert(app.ActiveScene.Entities.Count == 4, "Expected 4 Entities in ActiveScene.");

			Entity? boomerangEntity = app.ActiveScene.FindEntity("Boomerang");
			Assert(boomerangEntity != null && boomerangEntity.Tag == "ConcavePolygon", "Boomerang entity missing.");
			Assert(boomerangEntity!.GetComponent<RigidBodyComponent>() != null &&
				   boomerangEntity.GetComponent<ShapeRendererComponent>() != null, "Missing RigidBody/ShapeRenderer components.");

			FVec2 initialPos = boomerangEntity.Transform.Position;
			app.StepFrame();
			Assert(boomerangEntity.Transform.Position != initialPos &&
				   boomerangEntity.Transform.Position == app.World.Boomerang.Position, "Entity Transform must sync with physics Body.");
		}

		static void TestPointerAndInteractiveUIWidgets() {
			var app = new GameApplication(80, 40, 60);
			bool parsed = ConsoleInputAdapter.TryParseSgrMouse("0;25;10", 'M', app, out PointerInputEvent? sgrEvent);
			Assert(parsed && sgrEvent != null && sgrEvent.Type == PointerEventType.Down &&
				   sgrEvent.Button == PointerButton.Left && sgrEvent.CellX == 24 && sgrEvent.CellY == 9, "SGR-1006 parse mismatch.");

			app.Particles.Clear();
			app.Events.EnqueuePointer(sgrEvent!);
			app.StepFrame();
			Assert(app.Particles.ActiveCount > 0, "Pointer click in PhysicsMode must emit particle burst.");

			app.ToggleViewMode();
			app.OpenColorPicker();
			app.StepFrame();

			VoronoiSeed? seed = app.Voronoi.GetSelectedSeed(app.Camera.Position);
			app.Events.EnqueuePointer(new PointerInputEvent(app.Clock.CurrentFrame, PointerEventType.Down, PointerButton.Left, 50, 5, new Vec2(50, 10), Vec2.Zero));
			app.StepFrame();
			Assert(seed != null && seed.Color.R > 200, $"Pointer click on SliderR should set R > 200 (got {seed?.Color.R}).");
		}

		static void TestDualRenderBackends() {
			var nativeVectorBackend = new UnityNativeVectorBackend(160, 96);
			var app = new GameApplication(160, 96, 60, nativeVectorBackend);
			app.StepFrame();

			bool hasPoly = false, hasCircle = false, hasOverlay = false;
			for (int i = 0; i < nativeVectorBackend.Commands.Count; i++) {
				var cmd = nativeVectorBackend.Commands[i];
				if (cmd.Type == VectorCommandType.Polygon) hasPoly = true;
				else if (cmd.Type == VectorCommandType.Circle) hasCircle = true;
				else if (cmd.Type == VectorCommandType.OverlayText) hasOverlay = true;
			}
			Assert(hasPoly && hasCircle && hasOverlay, "UnityNativeVectorBackend must record Polygon, Circle, and OverlayText commands.");

			app.SetRenderBackend(new CanvasRenderBackend(100, 60));
			app.StepFrame();
			Assert(app.RenderBackend.SoftwareCanvas != null && app.Canvas.Width == 100, "Runtime switch to CanvasRenderBackend failed.");
		}

		sealed class MemoryLogSink : ILogSink {
			public LogColorPair CurrentColor { get; set; } = LogColorPair.Default;
			public readonly StringBuilder Buffer = new StringBuilder();
			public void Write(string text) => Buffer.Append(text);
		}

		static void TestLoggingSystem() {
			ILogSink? prevSink = Log.Sink;
			Log.Code prevVis = Log.visibility;
			string prevFile = Log.LogFile;
			bool prevSource = Log.IdentifySourceCode;
			try {
				var memSink = new MemoryLogSink();
				Log.Sink = memSink;
				Log.visibility = Log.Code.Verbose;
				Log.IdentifySourceCode = false;
				Log.LogFile = "local/test_log.txt";
				Log.InitLogFile();

				Log.i("Engine_Info_Marker");
				Log.Assert(2 + 2 == 4, "Math_Check_Pass");

				string output = memSink.Buffer.ToString();
#if DEBUG || KEEP_LOGS
				Assert(output.Contains("Engine_Info_Marker", StringComparison.Ordinal), "Expected Log.i output in sink under DEBUG/KEEP_LOGS.");
				Assert(File.Exists("local/test_log.txt"), "Expected local/test_log.txt to be created.");
#endif
				string stackPos = Log.StackPosition(1);
				Assert(stackPos.Contains("FunctionalityTests", StringComparison.Ordinal), $"StackPosition should identify caller file, got '{stackPos}'.");
			} finally {
				Log.Sink = prevSink;
				Log.visibility = prevVis;
				Log.LogFile = prevFile;
				Log.IdentifySourceCode = prevSource;
			}
		}

		static void TestStateChecksum() {
#if DEBUG
			bool prev = FNum.Deterministic;
			try {
				FNum.Deterministic = true;
				var app1 = new GameApplication(80, 40, 60);
				var app2 = new GameApplication(80, 40, 60);
				for (int i = 0; i < 45; i++) {
					app1.StepFrame();
					app2.StepFrame();
				}
				ulong h1 = StateChecksum.Compute(app1);
				ulong h2 = StateChecksum.Compute(app2);
				Assert(h1 == h2 && h1 != 0, $"Deterministic StateChecksum mismatch: 0x{h1:X16} vs 0x{h2:X16}");

				// Perturb state by 1 raw fixed-point unit (2^-32) and verify checksum changes
				app2.World.Circle.Position = new FVec2(FNum.FromRaw(app2.World.Circle.Position.X.FixedRaw + 1), app2.World.Circle.Position.Y);
				ulong hPerturbed = StateChecksum.Compute(app2);
				Assert(h1 != hPerturbed, "StateChecksum must detect single-bit fixed-point position change.");
			} finally {
				FNum.Deterministic = prev;
			}
#endif
		}

		static void TestInputScriptingEngine() {
			bool prev = FNum.Deterministic;
			try {
				FNum.Deterministic = true;
				const string script = "assert_mode:Physics, v, assert_mode:Voronoi, hold:d:5, space, assert_seeds:6, enter, assert_picker:open, 3, enter, assert_picker:closed, wait:4";

				var appA = new GameApplication(80, 40, 60);
				var engineA = new InputScriptEngine();
				engineA.LoadScript(script);
				int framesA = engineA.RunToCompletion(appA);

				var appB = new GameApplication(80, 40, 60);
				var engineB = new InputScriptEngine();
				engineB.LoadScript(script);
				int framesB = engineB.RunToCompletion(appB);

				Assert(framesA == framesB && framesA > 10, $"Script frame count mismatch: {framesA} vs {framesB}");
				Assert(appA.ViewMode == AppViewMode.Voronoi && appA.Voronoi.Seeds.Count == 6 && !appA.Voronoi.ColorPicker.IsOpen,
					"Scripted Voronoi workflow final state mismatch.");
#if DEBUG
				Assert(StateChecksum.Compute(appA) == StateChecksum.Compute(appB), "Scripted runs must yield identical StateChecksum.");
#endif
			} finally {
				FNum.Deterministic = prev;
			}
		}

		static void TestFrameExporterBmpAndGif() {
			var app = new GameApplication(60, 30, 60);
			app.StepFrame();

			string ascii = FrameExporter.DumpAsciiSnapshot(app.Canvas, app.Clock.CurrentFrame);
			Assert(ascii.Contains("PHYSICS ARENA", StringComparison.Ordinal) && ascii.Contains("+ origin", StringComparison.Ordinal),
				"ASCII frame snapshot must contain overlaid HUD and world text.");

			string ansi = FrameExporter.DumpAnsiSnapshot(app.Canvas);
			Assert(ansi.Contains("\u001b[38;2;", StringComparison.Ordinal) && ansi.Contains("\u2580", StringComparison.Ordinal),
				"ANSI frame snapshot must contain 24-bit escape codes and upper-half-block glyphs.");

			string bmpPath = FrameExporter.SaveBmp(app.Canvas, "local/test_frame.bmp", pixelScale: 4);
			Assert(File.Exists(bmpPath), "BMP file was not created in local/.");
			byte[] bmpBytes = File.ReadAllBytes(bmpPath);
			Assert(bmpBytes.Length > 54 && bmpBytes[0] == (byte)'B' && bmpBytes[1] == (byte)'M', "Invalid BMP header.");

			// Verify 4x8 cell layout: col 3 & row 7 blank buffer, standard glyph 'H' empty on row 5, descenders 'j','y','p','q' use row 5
			var fontCanvas = new Canvas(6, 2);
			var bgCol = new Rgb(10, 20, 30);
			var fgCol = new Rgb(250, 240, 230);
			fontCanvas.Clear(bgCol);
			fontCanvas.DrawOverlayText(new ScreenRect(0, 0, 6, 1), "Hjypq", fgCol, TextAlign.Left, null, 0f, TextWrapMode.None);
			string fontBmpPath = FrameExporter.SaveBmp(fontCanvas, "local/test_font.bmp", pixelScale: 4);
			byte[] fb = File.ReadAllBytes(fontBmpPath);
			int outW = 6 * 4, outH = 2 * 4, stride = (outW * 3 + 3) & ~3;
			Rgb ReadBmpPixel(int x, int y) {
				int off = 54 + (outH - 1 - y) * stride + x * 3;
				return new Rgb(fb[off + 2], fb[off + 1], fb[off]);
			}
			for (int cell = 0; cell < 5; cell++) {
				for (int y = 0; y < 8; y++) Assert(ReadBmpPixel(cell * 4 + 3, y) == bgCol, $"Cell {cell} col 3 must be empty buffer.");
				for (int x = 0; x < 4; x++) Assert(ReadBmpPixel(cell * 4 + x, 7) == bgCol, $"Cell {cell} row 7 must be empty buffer.");
			}
			// Cell 0 ('H'): row 4 lit on cols 0 & 2, row 5 completely empty (not copied from row 4)
			Assert(ReadBmpPixel(0, 4) == fgCol && ReadBmpPixel(2, 4) == fgCol, "'H' row 4 should be lit.");
			Assert(ReadBmpPixel(0, 5) == bgCol && ReadBmpPixel(1, 5) == bgCol && ReadBmpPixel(2, 5) == bgCol, "'H' row 5 must be empty.");
			// Cells 1..4 ('j','y','p','q'): must have at least one lit descender pixel on row 5
			for (int cell = 1; cell <= 4; cell++) {
				bool hasDescent = ReadBmpPixel(cell * 4 + 0, 5) == fgCol || ReadBmpPixel(cell * 4 + 1, 5) == fgCol || ReadBmpPixel(cell * 4 + 2, 5) == fgCol;
				Assert(hasDescent, $"Descender glyph at cell {cell} must light row 5.");
			}

			var gif = new GifRecorder(pixelScale: 4);
			gif.CaptureFrame(app.Canvas);
			app.ToggleViewMode();
			app.StepFrame();
			gif.CaptureFrame(app.Canvas);
			string gifPath = gif.Save("local/test_capture.gif", delayCentiseconds: 4);
			Assert(File.Exists(gifPath), "GIF file was not created in local/.");
			byte[] gifBytes = File.ReadAllBytes(gifPath);
			Assert(gifBytes.Length > 800 && Encoding.ASCII.GetString(gifBytes, 0, 6) == "GIF89a" && gifBytes[^1] == 0x3B,
				"Invalid GIF89a header or trailer.");
		}

		static void TestRoslynAnalyzerAndPreprocessorDocs() {
			RoslynAnalysisReport report = RoslynProjectAnalyzer.AnalyzeProject();
			Assert(report.FilesAnalyzed >= 15, $"Expected Roslyn to analyze >= 15 .cs files, got {report.FilesAnalyzed}.");
			if (!report.IsClean) {
				throw new InvalidOperationException("Roslyn static analysis violations:\n  " + string.Join("\n  ", report.Violations));
			}

			// Verify Roslyn discovered our preprocessor symbols (#if DEBUG, KEEP_LOGS, UNITY_5_3_OR_NEWER, UNITY_2017_1_OR_NEWER)
			var symbols = new HashSet<string>(StringComparer.Ordinal);
			foreach (var occ in report.SymbolOccurrences) symbols.Add(occ.Symbol);
			Assert(symbols.Contains("DEBUG") && symbols.Contains("KEEP_LOGS") && symbols.Contains("UNITY_5_3_OR_NEWER"),
				"Roslyn analyzer failed to discover expected preprocessor directives.");

			string docPath = RoslynProjectAnalyzer.WritePreprocessorMarkdownFile();
			Assert(File.Exists(docPath), "PREPROCESSOR_SYMBOLS.md was not generated.");
		}
	}
}
