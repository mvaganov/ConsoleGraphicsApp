# Preprocessor Symbols Reference

Auto-discovered via Roslyn syntax-tree trivia analysis (`CliHost/RoslynProjectAnalyzer.cs`). Re-generate or verify anytime with `dotnet run -- --doc-symbols` or `dotnet run -- --test`.

## 1. Symbol Catalog & Purpose

| Symbol | Purpose & Build Behavior |
| :--- | :--- |
| `DEBUG` | Standard debug build symbol. Enables deterministic 64-bit FNV-1a StateChecksum, script assert_checksum invariants, and Log output. |
| `KEEP_LOGS` | Opt-in symbol to retain ConsoleGraphicsEngine.Logging.Log file/sink output even in Release builds (adapted from ConsoleMrV.Log). |
| `UNITY_2017_1_OR_NEWER` | Unity Engine compiler symbol (Unity 2017.1+). Paired with UNITY_5_3_OR_NEWER to guard UnityEngine namespace references. |
| `UNITY_5_3_OR_NEWER` | Unity Engine compiler symbol (Unity 5.3+). Enables UnityEngineHost MonoBehaviour bridge and UnityNativeVectorBackend OnGUI rendering. |

## 2. Exact Source Locations

| Symbol | File | Line | Directive | Condition / Expression |
| :--- | :--- | :--- | :--- | :--- |
| `DEBUG` | `EngineCore/Engine/StateChecksum.cs` | 2 | `#if` | `DEBUG` |
| `DEBUG` | `EngineCore/Logging/Log.cs` | 67 | `#if` | `DEBUG || KEEP_LOGS` |
| `DEBUG` | `EngineCore/Logging/Log.cs` | 106 | `#if` | `DEBUG || KEEP_LOGS` |
| `DEBUG` | `EngineCore/Scripting/InputScriptEngine.cs` | 119 | `#if` | `DEBUG` |
| `DEBUG` | `Program.cs` | 121 | `#if` | `DEBUG` |
| `DEBUG` | `Testing/FunctionalityTests.cs` | 309 | `#if` | `DEBUG || KEEP_LOGS` |
| `DEBUG` | `Testing/FunctionalityTests.cs` | 324 | `#if` | `DEBUG` |
| `DEBUG` | `Testing/FunctionalityTests.cs` | 367 | `#if` | `DEBUG` |
| `KEEP_LOGS` | `EngineCore/Logging/Log.cs` | 67 | `#if` | `DEBUG || KEEP_LOGS` |
| `KEEP_LOGS` | `EngineCore/Logging/Log.cs` | 106 | `#if` | `DEBUG || KEEP_LOGS` |
| `KEEP_LOGS` | `Testing/FunctionalityTests.cs` | 309 | `#if` | `DEBUG || KEEP_LOGS` |
| `UNITY_2017_1_OR_NEWER` | `UnityBridge/UnityEngineHost.cs` | 4 | `#if` | `UNITY_5_3_OR_NEWER || UNITY_2017_1_OR_NEWER` |
| `UNITY_2017_1_OR_NEWER` | `UnityBridge/UnityNativeVectorBackend.cs` | 101 | `#if` | `UNITY_5_3_OR_NEWER || UNITY_2017_1_OR_NEWER` |
| `UNITY_5_3_OR_NEWER` | `UnityBridge/UnityEngineHost.cs` | 4 | `#if` | `UNITY_5_3_OR_NEWER || UNITY_2017_1_OR_NEWER` |
| `UNITY_5_3_OR_NEWER` | `UnityBridge/UnityNativeVectorBackend.cs` | 101 | `#if` | `UNITY_5_3_OR_NEWER || UNITY_2017_1_OR_NEWER` |
