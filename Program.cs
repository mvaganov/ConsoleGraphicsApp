#nullable enable
using System;
using ConsoleGraphicsEngine.CliHost;
using ConsoleGraphicsEngine.Engine;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Testing;

namespace ConsoleGraphicsEngine {
	public static class Program {
		public const string HelpText =
			"ConsoleGraphicsApp - Unity-Portable Command-Line Vector Graphics Game Engine\n" +
			"Usage: dotnet run -- [options]\n" +
			"Options:\n" +
			"  --test, -t         Run 11-part automated functionality & Unity-portability test suite\n" +
			"  --frames <N>       Run N headless simulation frames and print summary\n" +
			"  --deterministic    Use portable Q32.32 deterministic fixed-point physics (default)\n" +
			"  --fast-float       Use hardware float physics\n" +
			"  --help, -h         Show this help message";

		public static void PrintHelp() => Console.WriteLine(HelpText);

		public static int Main(string[] args) {
			bool runTests = false;
			int headlessFrames = 0;

			for (int i = 0; i < args.Length; i++) {
				string arg = args[i];
				if (arg is "--test" or "-t") {
					runTests = true;
				} else if (arg == "--deterministic") {
					FNum.Deterministic = true;
				} else if (arg == "--fast-float") {
					FNum.Deterministic = false;
				} else if (arg == "--frames" && i + 1 < args.Length && int.TryParse(args[i + 1], out int f)) {
					headlessFrames = Math.Max(1, f);
					i++;
				} else if (arg is "--help" or "-h") {
					PrintHelp();
					return 0;
				}
			}

			if (runTests) return FunctionalityTests.RunAllTests();

			if (headlessFrames > 0) {
				var sim = new GameApplication(100, 50, 60);
				for (int f = 0; f < headlessFrames; f++) sim.StepFrame();
				Console.WriteLine($"Simulated {sim.Clock.CurrentFrame} frames (Deterministic={FNum.Deterministic}). " +
								  $"Entities={sim.ActiveScene.Entities.Count}, Collisions={sim.TotalCollisionsResolved}, " +
								  $"AudioCues={sim.Audio.TotalCuesPlayed}, ActiveParticles={sim.Particles.ActiveCount}, " +
								  $"CirclePos=({sim.World.Circle.Position.X}, {sim.World.Circle.Position.Y})");
				return 0;
			}

			if (Console.IsOutputRedirected) {
				Console.Error.WriteLine("Interactive mode requires a terminal (or pass --test / --frames N for headless verification).");
				return 1;
			}

			var app = new GameApplication();
			ConsoleInputAdapter.RunInteractiveLoop(app);
			return 0;
		}
	}
}
