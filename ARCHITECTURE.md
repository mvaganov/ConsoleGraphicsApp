# ConsoleGraphicsEngine Architecture (LLM Reference)

Command-line-first, **zero-step Unity-portable** 2D vector graphics and physics game engine.
`EngineCore/` has **zero dependencies** on `System.Console`, `UnityEngine`, or `.NET 7+/10`-only BCL types (`netstandard2.1` / C# 9 compatible); the exact same source files compile in `.NET 10` CLI and Unity 2022.3 LTS / Unity 6 (Mono & IL2CPP for WebGL, iOS, Android).

---

## 1. Directory & Module Map

| Path | Namespace | Responsibility |
| :--- | :--- | :--- |
| `Program.cs` | `ConsoleGraphicsEngine` | CLI entry point (`HelpText` / `PrintHelp` at top; `--test`, `--frames N`, `--deterministic`, `--fast-float`). |
| `EngineCore/MathCore/FNum.cs` | `ConsoleGraphicsEngine.MathCore` | Dual-mode scalar: 64-bit Q32.32 deterministic fixed-point (`FNum.Deterministic = true`, no `Int128`) or float (`false`). |
| `EngineCore/MathCore/Vec2.cs` | `ConsoleGraphicsEngine.MathCore` | `Vec2` (float 2D vector for rendering/particles/camera) and `FVec2` (`FNum` 2D vector for deterministic physics). |
| `EngineCore/MathCore/RgbAndTransform.cs` | `ConsoleGraphicsEngine.MathCore` | `Rgb` (24-bit color + HSV + Rec.709 luma), `Transform2D` (2x3 affine matrix + inverse), `ScreenRect` (cell rect + `ContainsCell`). |
| `EngineCore/Rendering/IRenderBackend.cs` | `ConsoleGraphicsEngine.Rendering` | Host-agnostic draw contract (`IRenderBackend`), `TextAlign`, `TextWrapMode`, `Camera` (`ScreenPixelToWorld`, `ScreenCellToWorld`). |
| `EngineCore/Rendering/ScreenShapes.cs` | `ConsoleGraphicsEngine.Rendering` | `ScreenShape`, `ScreenPolygon` (even-odd ray crossing, convexity check), `ScreenCircle` (inverse 2x2 ellipse test). |
| `EngineCore/Rendering/Canvas.cs` | `ConsoleGraphicsEngine.Rendering` | `Canvas` software pixel/text back-buffer (`Width x Height` pixels, `Width x Rows` cells) and `CanvasRenderBackend`. |
| `EngineCore/Engine/EventQueue.cs` | `ConsoleGraphicsEngine.Engine` | Platform-neutral `KeyCode`, `KeyEventData`, `PointerInputEvent` (`Move`/`Down`/`Drag`/`Up`/`Scroll`), `TimedEvent`, `CollisionEvent`, `AudioEvent`, 4-phase `EventQueue`. |
| `EngineCore/Engine/KeyInputSystem.cs` | `ConsoleGraphicsEngine.Engine` | State-togglable `KeyCallbackGroup` registry (`OnKey`, `OnChar`, `OnAnyKey`, `OnPointer`), held-key frame window, virtual cursor fallback. |
| `EngineCore/Engine/FrameClockAndAudio.cs` | `ConsoleGraphicsEngine.Engine` | `FrameClock` (fixed-step frame counter), `IAudioBackend`, `AudioSystem` (semantic cues + procedural synth params). |
| `EngineCore/Engine/GameApplication.cs` | `ConsoleGraphicsEngine.Engine` | Host-agnostic frame coordinator (`StepFrame()`, `ActiveScene`, `PhysicsWorld`, `ParticleSystem`, `VoronoiSystem`, HUD). |
| `EngineCore/SceneGraph/SceneAndEntities.cs` | `ConsoleGraphicsEngine.SceneGraph` | Unity-mirroring `Scene`, `Entity`, `Component`, `TransformComponent`, `RigidBodyComponent`, `ShapeRendererComponent`. |
| `EngineCore/UI/UIWidgets.cs` | `ConsoleGraphicsEngine.UI` | Cell-grid widgets (`UIWidget`, `UIPanel`, `UIButton`, `UISlider`) with pointer click/drag hit-testing + keyboard focus. |
| `EngineCore/Physics/Bodies.cs` | `ConsoleGraphicsEngine.Physics` | `Body`, `CircleBody`, `PolygonBody` (centroid centering, area inertia, O(N^2) ear-clipping concave decomposition, boomerang generator). |
| `EngineCore/Physics/PhysicsWorld.cs` | `ConsoleGraphicsEngine.Physics` | 4x sub-stepped impulse solver: wall bounce, circle-vs-polygon, SAT polygon-vs-polygon across convex parts. |
| `EngineCore/Particles/ParticleSystem.cs` | `ConsoleGraphicsEngine.Particles` | Hardware-float particle emitter & updater drawn via `IRenderBackend`. |
| `EngineCore/Voronoi/VoronoiSystem.cs` | `ConsoleGraphicsEngine.Voronoi` | `VoronoiSystem` (affine scanline Voronoi cell renderer, flashing center-selected region, seed placement) + modal `ColorPickerUI`. |
| `CliHost/TerminalHost.cs` | `ConsoleGraphicsEngine.CliHost` | `TerminalPresenter` (differential ANSI half-block `U+2580` output + SGR-1006 mouse init) + `ConsoleInputAdapter`. |
| `UnityBridge/UnityEngineHost.cs` | `ConsoleGraphicsEngine.UnityBridge` | Drop-in Unity `MonoBehaviour` (`#if UNITY_5_3_OR_NEWER`) for `RetroPixelCanvas` (`Texture2D` blit) or `NativeHighResVector` + procedural `AudioClip` synth. |
| `UnityBridge/UnityNativeVectorBackend.cs` | `ConsoleGraphicsEngine.UnityBridge` | Vector command recorder (`RecordedDrawCommand`) implementing `IRenderBackend` without software rasterization. |
| `Testing/FunctionalityTests.cs` | `ConsoleGraphicsEngine.Testing` | 11-part automated CLI test suite (`dotnet run -- --test`). |

---

## 2. Strict Frame Execution Pipeline (`GameApplication.StepFrame()`)

1. **Virtual Cursor Fallback (`KeyInput.UpdateVirtualCursorFallback(...)`)**: Defaults pointer to screen/camera center until hardware mouse/touch arrives.
2. **Input & Pointer Resolution (`Events.ResolveInputEvents()`)**: Drains `UserInputEvent` + `PointerInputEvent` queues into enabled `KeyCallbackGroup`s (snapshot before dispatch to prevent mid-dispatch re-entry).
3. **Timed Event Resolution (`Events.ResolveTimedEvents(frame)`)**: Fires `TimedEvent`s where `frame >= TargetFrame` (reschedules recurring).
4. **Continuous Camera Control**: Applies held-key motion (`WASD` pan, `QE` rotate, `-/+` zoom) when `"CameraControls"` is enabled.
5. **Physics Step (`World.Update(dt, frame)`)**: 4 sub-steps in `FNum`/`FVec2`; enqueues per-pair deduplicated `CollisionEvent`s.
6. **Scene Sync & Update (`ActiveScene.SyncTransformsFromPhysics()` & `ActiveScene.Update(dt, frame)`)**: Copies `Body` transforms to `Entity.Transform` and ticks `Component`s.
7. **Collision Resolution (`Events.ResolveCollisionEvents()`)**: Triggers `Particles.EmitBurst` and enqueues `AudioEvent`s.
8. **Audio Resolution (`Events.ResolveAudioEvents()`)**: Updates `AudioSystem` history and invokes `IAudioBackend.PlayCue`.
9. **Particle Update (`Particles.Update(dt)`)**: Advances float particles (`Vec2`).
10. **Render (`RenderFrame()`)**: Issues draw calls to `IRenderBackend` (`CanvasRenderBackend` or `UnityNativeVectorBackend`).
11. **Clock Advance (`Clock.AdvanceFrame()`)**: Increments `CurrentFrame`.

---

## 3. Architectural Invariants & Conventions

* **CLI Top-of-File Help Convention**:
  * Every CLI entry point (`Program.cs`, plus any future custom linting, asset, or data CLI tools built for LLM agents) MUST place `HelpText` and `PrintHelp()` at the very top of the entry class so agents reading the file top-down acquire full functional and flag context within the first 25 lines.
* **Coordinate Spaces**:
  * **World**: `+X` right, `+Y` up (`Camera.GetViewTransform`).
  * **Pixel**: `0..Width-1` right, `0..Height-1` down (`Height == 2 * Rows`).
  * **Cell**: `0..Width-1` cols, `0..Rows-1` rows (`ScreenRect`, `DrawOverlayText`, `UIWidget`).
* **Unity Portability Boundary**:
  * `EngineCore/` must never reference `System.Console`, `ConsoleKey`, P/Invoke, or `.NET 7+/10`-only types (`Int128`).

---

## 4. Instructions for LLMs Updating This File

1. **High-density, low-token updates**: Document contracts, invariants, data flow, and file ownership using terse domain terminology; omit boilerplate prose.
2. **Update Section 1 & Section 2**: Add any new file/module to Section 1 and any new `StepFrame()` phase to Section 2 in exact execution order.
3. **Enforce CLI & Test Invariants**: Any new CLI tool must expose `HelpText`/`PrintHelp()` at the top of its entry file, and any new engine feature must add a deterministic verification check in `Testing/FunctionalityTests.cs`.
