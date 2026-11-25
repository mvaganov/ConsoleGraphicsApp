# AGENTS.md — C# Command-Line & Unity-Portable Game Engine Guidelines

**ConsoleGraphicsEngine** (`ConsoleGraphicsApp.csproj`): C# command-line-first 2D vector graphics & physics engine architected so `EngineCore/` and `UnityBridge/` drop or symlink directly into Unity (`Assets/Scripts/`) with zero changes.

---

## 1. Build, Run, and Test Commands

Interactive mode requires a terminal (`Console.WindowWidth` / ANSI alt-screen) and exits if stdout is redirected without flags. Always verify changes headlessly:

```powershell
dotnet build
dotnet run -- --test
dotnet run -- --frames 300 --deterministic
dotnet run -- --frames 300 --fast-float
```

---

## 2. Formatting & Code-Style Standards

Enforced automatically via [.editorconfig](.editorconfig), [.gitattributes](.gitattributes), and [.vscode/settings.json](.vscode/settings.json):

1. **K&R / Same-Line Opening Braces Everywhere**:
   * Opening `{` MUST be on the same line as namespaces, types, methods, properties, lambdas, and control structures (`if`, `else`, `for`, `while`, `switch`, `try`, `catch`).
2. **Tabs & LF Line Endings**:
   * Indent C# code with tabs (`\t`, 4-column display width).
   * Use Unix newlines (`LF`, `\n`) across all repository files.
3. **Expression-Bodied Members (`=>`)**:
   * Prefer one-line arrow functions/properties (`=>`) whenever a method, operator, or accessor fits cleanly on a single line.

---

## 3. Token & Context Efficiency (Agent-First Readability)

Optimize code and comments for high signal-to-token ratio so LLM agents parse and reason about files with minimal context waste:

1. **Self-Documenting Identifiers Without Bloat**:
   * Name types, methods, and parameters accurately enough that their purpose is obvious without a comment, but terse enough to avoid cognitive load.
2. **Zero Redundant Comments**:
   * Never write comments that restate what is already obvious from a signature, variable name, or standard control flow.
3. **Telegraphic Phrase Comments & Loaded Domain Terms**:
   * Skip complete English sentences, filler words, and strict grammar in code comments. Use dense technical phrases and domain terminology (e.g., *"O(N^2) ear-clipping CCW triangulation"*, *"Snapshot active flags to prevent mid-dispatch re-entry"*, *"Clamp in float before int cast to prevent off-screen overflow"*).
4. **High-Value Context & Cross-References (`<see cref="..."/>`)**:
   * Do comment non-obvious invariants, coordinate-space flips, bit-twiddling, edge-case guards, and bug pitfalls.
   * Use `<see cref="..."/>` tags to link callers, data structures, and invariants across files so agents can jump directly to related context.
5. **CLI `HelpText` / `PrintHelp` at Top of File**:
   * Every CLI entry file (`Program.cs` and any future linting/data/asset CLI utilities) MUST place `HelpText` and `PrintHelp()` at the top of the class so agents reading the file top-down immediately see all capabilities and flags.

---

## 4. Core Engine Invariants

Read [ARCHITECTURE.md](ARCHITECTURE.md) before making structural changes:

1. **Strict `EngineCore/` Isolation**:
   * Zero `System.Console`, `ConsoleKey`, P/Invoke, or `.NET 7+/10`-only APIs (`Int128`) in `EngineCore/`. Use `Engine.KeyCode`, `PointerInputEvent`, and `IRenderBackend`.
2. **Frame-Deterministic Simulation**:
   * Never use wall-clock delta time (`Stopwatch`) in simulation. Advance strictly by `FrameClock.FixedDeltaTime` and `FrameClock.CurrentFrame`.
   * Use `FNum`/`FVec2` for physics and gameplay state; use hardware `float`/`Vec2` for rendering, UI, Voronoi pixel evaluation, and particles.
3. **Deterministic Failure Diagnostics**:
   * When adding assertions in `Testing/FunctionalityTests.cs`, include actual vs. expected values and frame numbers in failure messages so agent test runs diagnose bugs in one turn without interactive debugging.
4. **Keep Documentation & Tests in Sync**:
   * Every new feature or behavior change requires a corresponding test in `Testing/FunctionalityTests.cs` and an update to [ARCHITECTURE.md](ARCHITECTURE.md).
