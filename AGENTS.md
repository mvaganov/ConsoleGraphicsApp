# AGENTS.md — C# Command-Line & Unity-Portable Game Engine Guidelines

**ConsoleGraphicsEngine** (`ConsoleGraphicsApp.csproj`): C# command-line-first 2D vector graphics & physics engine architected so `EngineCore/` and `UnityBridge/` drop or symlink directly into Unity (`Assets/Scripts/`) with zero changes.

---

## 1. Build, Run, and Test Commands

Interactive mode requires a terminal (`Console.WindowWidth` / ANSI alt-screen) and exits if stdout is redirected without flags. Always verify changes headlessly:

```powershell
dotnet build
dotnet build -c Release
dotnet run -- --test
dotnet run -- --analyze
dotnet run -- --doc-symbols
dotnet run -- --frames 120 --checksum --dump-ascii
dotnet run -- --script "v,space,wait:5,enter,3,enter" --save-bmp local/voronoi.bmp --save-gif local/run.gif
```

---

## 2. Formatting & Code-Style Standards

Enforced automatically via [.editorconfig](.editorconfig), [.gitattributes](.gitattributes), [.vscode/settings.json](.vscode/settings.json), and `RoslynProjectAnalyzer`:

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

## 4. Core Engine Invariants & Agent Tooling

Read [ARCHITECTURE.md](ARCHITECTURE.md) and [PREPROCESSOR_SYMBOLS.md](PREPROCESSOR_SYMBOLS.md) before making structural changes:

1. **Strict `EngineCore/` Isolation**:
   * Zero `System.Console`, `ConsoleKey`, `ConsoleColor`, P/Invoke, or `.NET 7+/10`-only APIs (`Int128`) in `EngineCore/`. Use `Engine.KeyCode`, `PointerInputEvent`, `IRenderBackend`, and `Logging.Log`.
2. **Preprocessor Directives & Debug Stripping**:
   * Guard debug-only diagnostics (`StateChecksum`, `assert_checksum`) with `#if DEBUG` and logging internals with `#if DEBUG || KEEP_LOGS`.
   * Whenever `#if` directives change, regenerate [PREPROCESSOR_SYMBOLS.md](PREPROCESSOR_SYMBOLS.md) with `dotnet run -- --doc-symbols`.
3. **Visual & Deterministic Verification**:
   * Use `--checksum` (`#if DEBUG`), `--script "<cmds>"`, `--dump-ascii`, `--save-bmp`, and `--save-gif` (outputting to gitignored `local/`) to inspect visual layout and deterministic physics regressions directly from the command line.
4. **Keep Documentation & Tests in Sync**:
   * Every new feature or behavior change requires a corresponding test in `Testing/FunctionalityTests.cs` and an update to [ARCHITECTURE.md](ARCHITECTURE.md).
