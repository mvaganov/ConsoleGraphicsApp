#nullable enable
using System;
using System.IO;
using ConsoleGraphicsEngine.CliHost;
using ConsoleGraphicsEngine.Engine;
using ConsoleGraphicsEngine.Logging;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Rendering;
using ConsoleGraphicsEngine.Scripting;
using ConsoleGraphicsEngine.Testing;

namespace ConsoleGraphicsEngine {
	public static class Program {
		public const string HelpText =
			"ConsoleGraphicsApp - Unity-Portable Command-Line Vector Graphics Game Engine\n" +
			"Usage: dotnet run -- [options]\n" +
			"Options:\n" +
			"  --test, -t             Run 16-part automated functionality, Roslyn & Unity-portability test suite\n" +
			"  --frames <N>           Run N headless simulation frames and print summary\n" +
			"  --script \"<cmds>\"      Execute batched input script (e.g. \"v,space,wait:5,enter,3,enter\")\n" +
			"  --script-file <path>   Execute batched input script from file\n" +
			"  --checksum             Print deterministic 64-bit FNV-1a state checksum (#if DEBUG)\n" +
			"  --dump-ascii           Dump bordered ASCII luminance + text snapshot of final frame\n" +
			"  --dump-ansi            Dump 24-bit ANSI half-block snapshot of final frame\n" +
			"  --save-bmp [path]      Save final frame as 24-bit BMP (default: local/frame.bmp)\n" +
			"  --save-gif [path]      Record simulation frames as animated GIF89a (default: local/capture.gif)\n" +
			"  --gif-step <N>         Capture every Nth frame when recording GIF (default: 2)\n" +
			"  --analyze              Run Roslyn static architecture/style analyzer\n" +
			"  --doc-symbols          Generate PREPROCESSOR_SYMBOLS.md via Roslyn directive discovery\n" +
			"  --deterministic        Use portable Q32.32 deterministic fixed-point physics (default)\n" +
			"  --fast-float           Use hardware float physics\n" +
			"  --help, -h             Show this help message";

		public static void PrintHelp() => Console.WriteLine(HelpText);

		public static int Main(string[] args) {
			Log.Sink = new ConsoleLogSink();

			bool runTests = false, printChecksum = false, dumpAscii = false, dumpAnsi = false;
			bool runAnalyze = false, docSymbols = false;
			int headlessFrames = 0, gifStep = 2;
			string? scriptText = null, bmpPath = null, gifPath = null;

			for (int i = 0; i < args.Length; i++) {
				string arg = args[i];
				if (arg is "--test" or "-t") {
					runTests = true;
				} else if (arg == "--deterministic") {
					FNum.Deterministic = true;
				} else if (arg == "--fast-float") {
					FNum.Deterministic = false;
				} else if (arg == "--checksum") {
					printChecksum = true;
				} else if (arg == "--dump-ascii") {
					dumpAscii = true;
				} else if (arg == "--dump-ansi") {
					dumpAnsi = true;
				} else if (arg == "--analyze") {
					runAnalyze = true;
				} else if (arg == "--doc-symbols") {
					docSymbols = true;
				} else if (arg == "--frames" && i + 1 < args.Length && int.TryParse(args[i + 1], out int f)) {
					headlessFrames = Math.Max(1, f);
					i++;
				} else if (arg == "--gif-step" && i + 1 < args.Length && int.TryParse(args[i + 1], out int gs)) {
					gifStep = Math.Max(1, gs);
					i++;
				} else if (arg == "--script" && i + 1 < args.Length) {
					scriptText = args[++i];
				} else if (arg == "--script-file" && i + 1 < args.Length) {
					scriptText = File.ReadAllText(args[++i]);
				} else if (arg == "--save-bmp") {
					if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) bmpPath = args[++i];
					else bmpPath = "local/frame.bmp";
				} else if (arg == "--save-gif") {
					if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) gifPath = args[++i];
					else gifPath = "local/capture.gif";
				} else if (arg is "--help" or "-h") {
					PrintHelp();
					return 0;
				}
			}

			if (docSymbols) {
				string path = RoslynProjectAnalyzer.WritePreprocessorMarkdownFile();
				Console.WriteLine($"Generated preprocessor symbol documentation: {path}");
				if (!runTests && !runAnalyze && headlessFrames == 0 && scriptText == null) return 0;
			}

			if (runAnalyze) {
				RoslynAnalysisReport report = RoslynProjectAnalyzer.AnalyzeProject();
				Console.WriteLine($"Roslyn analyzed {report.FilesAnalyzed} files, found {report.SymbolOccurrences.Count} preprocessor directive symbols, {report.Violations.Count} violations.");
				foreach (string v in report.Violations) Console.Error.WriteLine($"  [VIOLATION] {v}");
				if (!report.IsClean) return 1;
				if (!runTests && headlessFrames == 0 && scriptText == null) return 0;
			}

			if (runTests) return FunctionalityTests.RunAllTests();

			bool isHeadlessRun = headlessFrames > 0 || scriptText != null || printChecksum || dumpAscii || dumpAnsi || bmpPath != null || gifPath != null;
			if (isHeadlessRun) {
				var sim = new GameApplication(80, 40, 60);
				var scriptEngine = new InputScriptEngine();
				if (!string.IsNullOrEmpty(scriptText)) scriptEngine.LoadScript(scriptText);

				GifRecorder? gifRecorder = gifPath != null ? new GifRecorder(pixelScale: 4) : null;
				int targetFrames = headlessFrames > 0 ? headlessFrames : (scriptText != null ? 0 : 1);

				int f = 0;
				while (f < targetFrames || !scriptEngine.IsFinished) {
					scriptEngine.TickBeforeFrame(sim);
					sim.StepFrame();
					if (gifRecorder != null && (f % gifStep == 0)) gifRecorder.CaptureFrame(sim.Canvas);
					f++;
					if (f > 20000) break;
				}

				if (gifRecorder != null && gifRecorder.FrameCount == 0) gifRecorder.CaptureFrame(sim.Canvas);

				string checksumSuffix = "";
#if DEBUG
				if (printChecksum) checksumSuffix = $", Checksum={StateChecksum.ComputeHex(sim)}";
#else
				if (printChecksum) checksumSuffix = ", Checksum=(stripped in Release build)";
#endif

				Console.WriteLine($"Simulated {sim.Clock.CurrentFrame} frames (Deterministic={FNum.Deterministic}). " +
								  $"Mode={sim.ViewMode}, Entities={sim.ActiveScene.Entities.Count}, Collisions={sim.TotalCollisionsResolved}, " +
								  $"AudioCues={sim.Audio.TotalCuesPlayed}, ActiveParticles={sim.Particles.ActiveCount}, " +
								  $"CirclePos=({sim.World.Circle.Position.X}, {sim.World.Circle.Position.Y}){checksumSuffix}");

				if (dumpAscii) Console.WriteLine(FrameExporter.DumpAsciiSnapshot(sim.Canvas, sim.Clock.CurrentFrame));
				if (dumpAnsi) Console.Write(FrameExporter.DumpAnsiSnapshot(sim.Canvas));
				if (bmpPath != null) {
					string savedBmp = FrameExporter.SaveBmp(sim.Canvas, bmpPath, pixelScale: 4);
					Console.WriteLine($"Saved BMP snapshot: {savedBmp}");
				}
				if (gifPath != null && gifRecorder != null) {
					string savedGif = gifRecorder.Save(gifPath, delayCentiseconds: Math.Max(2, gifStep * 100 / 60));
					Console.WriteLine($"Saved animated GIF ({gifRecorder.FrameCount} frames): {savedGif}");
				}
				return 0;
			}

			if (Console.IsOutputRedirected) {
				Console.Error.WriteLine("Interactive mode requires a terminal (or pass --test / --frames N / --script for headless execution).");
				return 1;
			}

			var app = new GameApplication();
			ConsoleInputAdapter.RunInteractiveLoop(app);
			return 0;
		}
	}
}
