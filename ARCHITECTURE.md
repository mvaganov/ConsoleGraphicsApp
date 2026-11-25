# ConsoleGraphicsEngine Architecture (LLM Reference)

A command-line-first 2D vector graphics and physics game engine targeting .NET 10 (`net10.0`). Renders anti-aliased shapes at 2x vertical pixel density using the Unicode upper-half-block glyph (`U+2580`), 24-bit ANSI colors, a layered terminal character grid, and differential front/back buffer terminal output.

---

## 1. Directory & Module Map

| Path | Namespace | Responsibility |
| :--- | :--- | :--- |
| `Program.cs` | `ConsoleGraphicsEngine` | CLI entry point (`--test`, `--frames N`, `--deterministic`, `--fast-float`). |
| `MathCore/FNum.cs` | `ConsoleGraphicsEngine.MathCore` | Dual-mode scalar type: Q32.32 deterministic fixed-point (`FNum.Deterministic = true`) or hardware `float` (`false`). |
| `MathCore/Vec2.cs` | `ConsoleGraphicsEngine.MathCore` | `Vec2` (hardware `float` 2D vector for rendering/particles/camera) and `FVec2` (`FNum`-backed 2D vector for deterministic physics). |
| `MathCore/RgbAndTransform.cs` | `ConsoleGraphicsEngine.MathCore` | `Rgb` (24-bit color + HSV conversion + luminance), `Transform2D` (2x3 affine matrix + inverse), `ScreenRect` (terminal cell rectangle). |
| `Rendering/ScreenShapes.cs` | `ConsoleGraphicsEngine.Rendering` | `ScreenShape`, `ScreenPolygon` (even-odd rule, convex/concave), `ScreenCircle` (inverse ellipse test). |
| `Rendering/Canvas.cs` | `ConsoleGraphicsEngine.Rendering` | Pixel back-buffer (`Width x Height`), text layer (`Width x Rows`), N x N super-sampled rasterizer, `DrawText` (world-space with optional backdrop), and `DrawOverlayText` (screen-space `ScreenRect` with `TextWrapMode`). |
| `Rendering/TerminalPresenter.cs` | `ConsoleGraphicsEngine.Rendering` | `Camera` (world-to-pixel view matrix) and `TerminalPresenter` (differential ANSI presenter). |
| `Engine/FrameClock.cs` | `ConsoleGraphicsEngine.Engine` | Strict frame-based clock (`CurrentFrame`, fixed `FixedDeltaTime = 1f / TargetFps`). |
| `Engine/EventQueue.cs` | `ConsoleGraphicsEngine.Engine` | Three-phase frame event queue: `UserInputEvent`, `TimedEvent` (frame-scheduled/recurring), `CollisionEvent`. |
| `Engine/KeyInputSystem.cs` | `ConsoleGraphicsEngine.Engine` | Named `KeyCallbackGroup` registry with state-based toggling (`SetGroupEnabled`) and frame-based held-key tracking. |
| `Engine/GameApplication.cs` | `ConsoleGraphicsEngine.Engine` | Main loop and state coordinator (`AppViewMode.Physics` vs `AppViewMode.Voronoi`, key group wiring, HUD). |
| `Physics/Bodies.cs` | `ConsoleGraphicsEngine.Physics` | `Body`, `CircleBody`, `PolygonBody` (centroid alignment, ear-clipping triangulation of concave shapes into convex triangles, boomerang generator). |
| `Physics/PhysicsWorld.cs` | `ConsoleGraphicsEngine.Physics` | Sub-stepped rigid-body solver: wall collisions, circle-vs-polygon, and SAT polygon-vs-polygon across convex parts. |
| `Particles/ParticleSystem.cs` | `ConsoleGraphicsEngine.Particles` | Fast non-deterministic `float` particle emitter and updater triggered by `CollisionEvent`s. |
| `Voronoi/VoronoiSystem.cs` | `ConsoleGraphicsEngine.Voronoi` | `VoronoiSystem` (camera-transformed Voronoi cell renderer, flashing center-selected region, seed placement) and modal `ColorPickerUI`. |
| `Testing/FunctionalityTests.cs` | `ConsoleGraphicsEngine.Testing` | Automated CLI test suite invoked via `dotnet run -- --test`. |

---

## 2. Strict Frame Execution Pipeline

Every call to `GameApplication.StepFrame()` advances simulation by exactly 1 frame (`Clock.FixedDeltaTime`) in this fixed order:

1. **Input Resolution (`Events.ResolveInputEvents()`)**: Drains queued `UserInputEvent`s and dispatches them to enabled `KeyCallbackGroup`s snapshot at the start of dispatch.
2. **Timed Event Resolution (`Events.ResolveTimedEvents(frame)`)**: Fires `TimedEvent` callbacks where `frame >= TargetFrame` (rescheduling recurring events).
3. **Continuous Camera Control**: Applies held-key motion (`WASD` pan, `QE` rotate, `-/+` zoom) if the `"CameraControls"` group is enabled.
4. **Physics Step (`World.Update(dt, frame)`)**: Runs 4 sub-steps using `FNum`/`FVec2`. Enqueues deduplicated `CollisionEvent`s into `EventQueue`.
5. **Collision Resolution (`Events.ResolveCollisionEvents()`)**: Invokes subscribed collision handlers (e.g. triggering `Particles.EmitBurst` at contact points).
6. **Particle Update (`Particles.Update(dt)`)**: Advances visual particles using fast hardware floats (`Vec2`).
7. **Render (`RenderFrame()`)**: Draws either `PhysicsWorld` + `ParticleSystem` or `VoronoiSystem` + `ColorPickerUI`, followed by modular `ScreenRect` HUD widgets via `Canvas.DrawOverlayText`.
8. **Clock Advance (`Clock.AdvanceFrame()`)**: Increments `CurrentFrame`.

---

## 3. Key Architectural Invariants

* **Coordinate Spaces**:
  * **World Space**: `+X` right, `+Y` up. Transformed via `Camera.GetViewTransform(pixelWidth, pixelHeight)`.
  * **Pixel Space**: `0..Width-1` horizontally, `0..Height-1` vertically (`+Y` down). `Height` is always even (`2 * Rows`).
  * **Terminal Cell Space**: `0..Width-1` columns, `0..Rows-1` rows. Used by `ScreenRect` and `Canvas.DrawOverlayText`.
* **Determinism Boundary**:
  * Gameplay and physics (`PhysicsWorld`, `Body`, `FNum`, `FVec2`, `FrameClock`, `TimedEvent`) must remain frame-deterministic when `FNum.Deterministic == true`.
  * Purely visual effects (`ParticleSystem`, `Canvas` rasterization, `VoronoiSystem` rendering) use hardware `float` (`Vec2`) for maximum throughput.
* **Concave Polygon Collisions**:
  * `PolygonBody` stores both the outer concave perimeter (`World` / `RenderWorld`) for even-odd rasterization and a list of ear-clipped convex triangles (`ConvexPartsWorld`) for exact SAT and circle-polygon collision detection.
* **Input State Isolation**:
  * Modal UIs (such as `ColorPickerUI`) disable `"CameraControls"` and `"VoronoiMode"` groups and enable `"ColorPickerUI"`, preventing key presses (e.g. `WASD`, `Enter`) from leaking into the underlying scene.

---

## 4. Instructions for LLMs Updating This File

Whenever you add, modify, or remove engine functionality in future iterations, update this document following these rules:

1. **Keep it concise and high-signal**: Do not paste large code blocks or verbose prose. Document contracts, invariants, data flow, and file ownership.
2. **Update Section 1 (Directory & Module Map)**: Add a row for any new file or class, or update the responsibility column if a module's role expands.
3. **Update Section 2 (Frame Execution Pipeline)**: If a new subsystem hooks into `GameApplication.StepFrame()` (e.g., audio cues, AI tick, entity component lifecycle, scene transitions), insert it at its exact ordinal step.
4. **Document New Input Groups or Event Types**:
   * When adding an `IGameEvent` implementation, list its producer and consumer phases.
   * When adding a `KeyCallbackGroup`, document which game states enable/disable it.
5. **Register Verification Coverage**: Every new feature must include a corresponding unit/integration check in `Testing/FunctionalityTests.cs` so `dotnet run -- --test` continues to validate the entire architecture.
