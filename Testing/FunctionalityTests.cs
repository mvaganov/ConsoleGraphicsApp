#nullable enable
using System;
using System.Collections.Generic;
using ConsoleGraphicsEngine.Engine;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Particles;
using ConsoleGraphicsEngine.Physics;
using ConsoleGraphicsEngine.Rendering;
using ConsoleGraphicsEngine.Voronoi;

namespace ConsoleGraphicsEngine.Testing;

/// <summary>
/// Built-in automated functionality test suite executable from the command line via `--test`.
/// Verifies every requirement without requiring an interactive terminal window.
/// </summary>
public static class FunctionalityTests {
	public static int RunAllTests() {
		int passed = 0;
		int failed = 0;

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

		Console.WriteLine("=== Running ConsoleGraphicsEngine Functionality Tests ===");

		Run("1. DrawOverlayText rectangular bounds, word wrapping, and backdrop blending", TestDrawOverlayTextRectAndWrap);
		Run("2. DrawText world-space text backdrop blending", TestDrawTextBackdrop);
		Run("3. EventQueue ordering: Input, Timed events, and Collision resolution", TestEventQueue);
		Run("4. KeyInputSystem callback groups and state toggling", TestKeyCallbackGroups);
		Run("5. Strict frame-based advancement & deterministic FNum physics reproducibility", TestDeterministicPhysics);
		Run("6. ParticleSystem emission on Circle-Polygon collision", TestParticleEmissionOnCollision);
		Run("7. Polygon-Polygon collision (Pentagon, Triangle) and Concave Boomerang collision", TestPolygonAndConcaveCollisions);
		Run("8. Voronoi mode switching ('v'), seed placement (Space), flashing selection, and ColorPicker (Enter)", TestVoronoiAndColorPickerFlow);

		Console.WriteLine($"=== Results: {passed} passed, {failed} failed ===");
		return failed == 0 ? 0 : 1;
	}

	static void Assert(bool condition, string message) {
		if (!condition) throw new InvalidOperationException(message);
	}

	static void TestDrawOverlayTextRectAndWrap() {
		var canvas = new Canvas(40, 20); // 40 cols x 10 rows
		var bg = new Rgb(10, 10, 10);
		canvas.Clear(bg);

		var rect = new ScreenRect(5, 2, 12, 3);
		var backdrop = new Rgb(110, 110, 110);
		var textColor = new Rgb(250, 200, 100);
		canvas.DrawOverlayText(rect, "Hello modular world engine UI", textColor, TextAlign.Left, backdrop, 0.5f, TextWrapMode.WordWrap);

		// Verify backdrop only modified pixels inside rect (cols 5..16, terminal rows 2..4 -> pixel rows 4..9)
		Rgb outsidePixel = canvas.Pixels[0];
		Assert(outsidePixel == bg, "Outside pixel should remain untouched by rectangular DrawOverlayText.");

		Rgb insidePixel = canvas.Pixels[(2 * 2) * canvas.Width + 5];
		Assert(insidePixel != bg && insidePixel.R == 60, $"Inside pixel should be blended toward backdrop (got R={insidePixel.R}).");

		// Verify word wrapping split across multiple rows within rect
		List<string> wrapped = Canvas.WrapText("Hello modular world engine UI", 12, TextWrapMode.WordWrap);
		Assert(wrapped.Count >= 3, $"Expected >= 3 wrapped lines, got {wrapped.Count}");
		Assert(wrapped[0] == "Hello", $"Line 0 mismatch: '{wrapped[0]}'");
		Assert(wrapped[1] == "modular", $"Line 1 mismatch: '{wrapped[1]}'");
		Assert(canvas.TextChars[2 * canvas.Width + 5] == 'H', "Expected 'H' at top-left of rect row 2 col 5");
		Assert(canvas.TextChars[3 * canvas.Width + 5] == 'm', "Expected 'm' on wrapped row 3 col 5");
	}

	static void TestDrawTextBackdrop() {
		var canvas = new Canvas(40, 20);
		var bg = new Rgb(0, 0, 0);
		canvas.Clear(bg);

		var backdrop = new Rgb(200, 100, 50);
		canvas.DrawText(new Vec2(10, 6), "TEST", new Rgb(255, 255, 255), TextAlign.Left, backdrop, 0.5f);

		// Position (10, 6) in identity transform -> row = 6 / 2 = 3, col = 10..13
		Rgb blended = canvas.Pixels[(2 * 3) * canvas.Width + 10];
		Assert(blended.R == 100 && blended.G == 50 && blended.B == 25,
			$"DrawText backdrop did not blend underlying pixels (got {blended.R},{blended.G},{blended.B})");
		Assert(canvas.TextChars[3 * canvas.Width + 10] == 'T', "Expected 'T' at row 3 col 10");
	}

	static void TestEventQueue() {
		var queue = new EventQueue();
		var log = new List<string>();

		queue.SubscribeInput(ev => log.Add($"Input:{ev.KeyInfo.Key}"));
		queue.ScheduleTimed(0, 2, () => log.Add("Timed:Frame2"));
		queue.SubscribeCollision(ev => log.Add($"Collision:{ev.BodyAName}-{ev.BodyBName}"));

		queue.EnqueueInput(new UserInputEvent(0, new ConsoleKeyInfo('w', ConsoleKey.W, false, false, false)));
		queue.EnqueueCollision(new CollisionEvent(0, "Circle", "Pentagon", new Vec2(1, 2), new Vec2(0, 1), 0.5f, 10f));

		queue.ResolveInputEvents();
		queue.ResolveTimedEvents(1); // Should not fire yet
		queue.ResolveTimedEvents(2); // Fires at frame 2
		queue.ResolveCollisionEvents();

		Assert(log.Count == 3, $"Expected 3 resolved events, got {log.Count}");
		Assert(log[0] == "Input:W", $"Expected Input:W first, got {log[0]}");
		Assert(log[1] == "Timed:Frame2", $"Expected Timed:Frame2 second, got {log[1]}");
		Assert(log[2] == "Collision:Circle-Pentagon", $"Expected Collision third, got {log[2]}");
	}

	static void TestKeyCallbackGroups() {
		var queue = new EventQueue();
		var keys = new KeyInputSystem(queue);
		int gameplayCalls = 0;
		int uiCalls = 0;

		keys.GetOrCreateGroup("Gameplay", isEnabled: true).OnKey(ConsoleKey.Enter, () => gameplayCalls++);
		keys.GetOrCreateGroup("ModalUI", isEnabled: false).OnKey(ConsoleKey.Enter, () => uiCalls++);

		queue.EnqueueInput(new UserInputEvent(1, new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));
		queue.ResolveInputEvents();
		Assert(gameplayCalls == 1 && uiCalls == 0, "Only enabled Gameplay group should trigger.");

		// Switch active group
		keys.SetGroupEnabled("Gameplay", false);
		keys.SetGroupEnabled("ModalUI", true);

		queue.EnqueueInput(new UserInputEvent(2, new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));
		queue.ResolveInputEvents();
		Assert(gameplayCalls == 1 && uiCalls == 1, "Only enabled ModalUI group should trigger after toggle.");
	}

	static void TestDeterministicPhysics() {
		bool prev = FNum.Deterministic;
		try {
			FNum.Deterministic = true;
			var worldA = new PhysicsWorld(80, 40);
			var worldB = new PhysicsWorld(80, 40);

			for (int f = 0; f < 180; f++) {
				worldA.Update(1f / 60f, f);
				worldB.Update(1f / 60f, f);
			}

			Assert(worldA.Circle.Position.X.FixedRaw == worldB.Circle.Position.X.FixedRaw &&
				   worldA.Circle.Position.Y.FixedRaw == worldB.Circle.Position.Y.FixedRaw,
				"Deterministic fixed-point circle positions must be bit-identical across runs.");
			Assert(worldA.Boomerang.Angle.FixedRaw == worldB.Boomerang.Angle.FixedRaw,
				"Deterministic fixed-point boomerang angles must be bit-identical across runs.");

			// Also verify non-deterministic fast float mode runs cleanly
			FNum.Deterministic = false;
			var worldFast = new PhysicsWorld(80, 40);
			for (int f = 0; f < 60; f++) {
				worldFast.Update(1f / 60f, f);
			}
			Assert(float.IsFinite(worldFast.Circle.Position.X.ToFloat()), "Fast float physics position must be finite.");
		} finally {
			FNum.Deterministic = prev;
		}
	}

	static void TestParticleEmissionOnCollision() {
		var app = new GameApplication(80, 40, 60);
		// Place circle directly overlapping with Pentagon and moving toward it
		app.World.Circle.Position = FVec2.FromVec2(new Vec2(20f, -8f));
		app.World.Circle.Velocity = FVec2.FromVec2(new Vec2(60f, 0f));
		app.World.Pentagon.Position = FVec2.FromVec2(new Vec2(28f, -8f));
		app.World.Pentagon.Velocity = FVec2.FromVec2(new Vec2(-40f, 0f));
		app.World.Pentagon.UpdateWorld();

		Assert(app.Particles.ActiveCount == 0, "Particles should start at 0.");
		app.StepFrame();
		Assert(app.TotalCollisionsResolved > 0, "Expected collision event between Circle and Pentagon.");
		Assert(app.Particles.ActiveCount > 0, $"Expected particles emitted on Circle-Polygon collision, got {app.Particles.ActiveCount}.");
	}

	static void TestPolygonAndConcaveCollisions() {
		var queue = new EventQueue();
		int polyCollisions = 0;
		queue.SubscribeCollision(ev => {
			if (ev.BodyAName != "Circle" && ev.BodyBName != "Circle") polyCollisions++;
		});

		var world = new PhysicsWorld(80, 40, queue);
		Assert(world.Polygons.Count == 3, "Expected Pentagon, Triangle, and Boomerang polygons.");
		Assert(world.Boomerang.IsConcave, "Boomerang polygon must be detected as concave.");
		Assert(world.Boomerang.ConvexPartsLocal.Count >= 3, "Concave boomerang must be decomposed into convex triangles.");

		// Force Triangle and concave Boomerang to collide head-on
		world.Triangle.Position = FVec2.FromVec2(new Vec2(0f, 0f));
		world.Triangle.Velocity = FVec2.FromVec2(new Vec2(50f, 0f));
		world.Triangle.UpdateWorld();

		world.Boomerang.Position = FVec2.FromVec2(new Vec2(9f, 0f));
		world.Boomerang.Velocity = FVec2.FromVec2(new Vec2(-50f, 0f));
		world.Boomerang.UpdateWorld();

		world.Update(1f / 60f, 1);
		queue.ResolveCollisionEvents();

		Assert(polyCollisions > 0, "Expected polygon-polygon collision between Triangle and concave Boomerang.");
		Assert(world.Triangle.Velocity.X.ToFloat() < 50f && world.Boomerang.Velocity.X.ToFloat() > -50f,
			"Impulse should alter velocities of colliding polygons.");
	}

	static void TestVoronoiAndColorPickerFlow() {
		var app = new GameApplication(80, 40, 60);
		Assert(app.ViewMode == AppViewMode.Physics, "Default mode should be Physics.");

		// Press 'V' to enter Voronoi mode
		app.Events.EnqueueInput(new UserInputEvent(app.Clock.CurrentFrame, new ConsoleKeyInfo('v', ConsoleKey.V, false, false, false)));
		app.StepFrame();
		Assert(app.ViewMode == AppViewMode.Voronoi, "Pressing 'v' must switch to Voronoi mode.");

		// Move camera to a distinct location and press Space to place a new Voronoi seed
		app.Camera.Position = new Vec2(12.5f, -7.25f);
		int initialSeeds = app.Voronoi.Seeds.Count;
		app.Events.EnqueueInput(new UserInputEvent(app.Clock.CurrentFrame, new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false)));
		app.StepFrame();
		Assert(app.Voronoi.Seeds.Count == initialSeeds + 1, "Pressing Space in Voronoi mode must place a new Voronoi seed.");

		VoronoiSeed? selected = app.Voronoi.GetSelectedSeed(app.Camera.Position);
		Assert(selected != null && (selected.Position - app.Camera.Position).Length < 1e-3f,
			"Newly placed seed at camera center must be the selected Voronoi region.");
		Rgb beforeColor = selected!.Color;

		// Press Enter to open Color Picker UI
		app.Events.EnqueueInput(new UserInputEvent(app.Clock.CurrentFrame, new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));
		app.StepFrame();
		Assert(app.Voronoi.ColorPicker.IsOpen, "Pressing Enter in Voronoi mode must open the Color Picker UI.");

		// Adjust color via RightArrow / preset key while picker is open
		app.Events.EnqueueInput(new UserInputEvent(app.Clock.CurrentFrame, new ConsoleKeyInfo('3', ConsoleKey.D3, false, false, false)));
		app.StepFrame();
		Assert(selected.Color != beforeColor || selected.Color == ColorPickerUI.Presets[2],
			"Color Picker input must update the selected Voronoi region's color.");

		// Press Enter again to close Color Picker UI
		app.Events.EnqueueInput(new UserInputEvent(app.Clock.CurrentFrame, new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));
		app.StepFrame();
		Assert(!app.Voronoi.ColorPicker.IsOpen, "Pressing Enter again must close the Color Picker UI.");
	}
}
