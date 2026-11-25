# AGENTS.md — C# Command-Line Game Engine Guidelines

This repository contains **ConsoleGraphicsEngine** (`ConsoleGraphicsApp.csproj`), a C# (.NET 10) command-line-first 2D vector graphics and physics game engine. Follow these instructions when researching, modifying, or testing the project.

---

## 1. Build, Run, and Test Commands

Because the interactive game loop requires a real terminal (`Console.WindowWidth` / ANSI alternate screen buffer) and exits immediately if stdout is redirected without CLI flags, **always use the headless CLI arguments** when verifying changes from an agent shell:

* **Build the project**:
  ```powershell
  dotnet build
  ```
* **Run the automated functionality test suite** (must pass before completing any task):
  ```powershell
  dotnet run -- --test
  ```
* **Run a headless N-frame simulation smoke test**:
  ```powershell
  dotnet run -- --frames 300 --deterministic
  dotnet run -- --frames 300 --fast-float
  ```

---

## 2. Architecture & Code Organization

Read [ARCHITECTURE.md](ARCHITECTURE.md) first before making structural changes. Keep responsibilities cleanly separated across namespaces:

* `ConsoleGraphicsEngine.MathCore` (`MathCore/`): `FNum` (Q32.32 fixed-point / float hybrid), `Vec2`, `FVec2`, `Rgb`, `Transform2D`, `ScreenRect`.
* `ConsoleGraphicsEngine.Rendering` (`Rendering/`): `Canvas`, `ScreenShape`, `ScreenPolygon`, `ScreenCircle`, `Camera`, `TerminalPresenter`.
* `ConsoleGraphicsEngine.Engine` (`Engine/`): `FrameClock`, `EventQueue`, `KeyInputSystem`, `KeyCallbackGroup`, `GameApplication`.
* `ConsoleGraphicsEngine.Physics` (`Physics/`): `Body`, `CircleBody`, `PolygonBody`, `PhysicsWorld`.
* `ConsoleGraphicsEngine.Particles` (`Particles/`): `ParticleSystem`, `Particle`.
* `ConsoleGraphicsEngine.Voronoi` (`Voronoi/`): `VoronoiSystem`, `VoronoiSeed`, `ColorPickerUI`.
* `ConsoleGraphicsEngine.Testing` (`Testing/`): `FunctionalityTests`.

---

## 3. Core Engineering Standards

1. **Strict Frame-Based Advancement**:
   * Never depend on wall-clock delta time (`Stopwatch`) inside simulation, physics, or timed events.
   * Use `FrameClock.CurrentFrame` and `FrameClock.FixedDeltaTime` for all simulation advancement and `EventQueue.ScheduleTimed(...)` for delayed or recurring logic.
2. **Determinism vs. Throughput**:
   * Physics and gameplay state must use `FNum` and `FVec2` so toggling `FNum.Deterministic = true` produces bit-exact, reproducible trajectories across runs.
   * Rendering, UI layout, Voronoi pixel evaluation, and particle systems should use hardware `float` and `Vec2` for speed.
3. **Event-Driven Decoupling**:
   * Route user keystrokes, frame timers, and physics collisions through `EventQueue` rather than coupling subsystems directly.
   * Group all keyboard shortcuts into named `KeyCallbackGroup`s in `KeyInputSystem` and enable/disable groups when transitioning between modes or opening modal UIs.
4. **Modular Terminal UI**:
   * Use `Canvas.DrawOverlayText(ScreenRect, ...)` with `TextWrapMode` (`WordWrap`, `CharWrap`, `None`) and optional `backdrop` blending for HUD panels, modals, and menus.
   * Use `Canvas.DrawText(Vec2, ...)` (with optional `backdrop` blending) for labels attached to world-space coordinates.
5. **Performance & Allocations in Hot Loops**:
   * The software rasterizer (`Canvas.Fill`, `VoronoiSystem.Draw`), physics sub-steps (`PhysicsWorld.SubStep`), and terminal diffing (`TerminalPresenter.Present`) run every frame. Avoid per-pixel heap allocations or LINQ in hot paths; prefer `Span<T>`, `stackalloc`, or pre-allocated arrays/lists.
6. **Keep Documentation & Tests in Sync**:
   * Whenever you add a feature or modify engine behavior, add a corresponding automated test in `Testing/FunctionalityTests.cs` and update [ARCHITECTURE.md](ARCHITECTURE.md) per its update instructions.
