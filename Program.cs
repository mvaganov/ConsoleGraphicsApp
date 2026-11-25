#nullable enable
using System;
using ConsoleGraphicsEngine.Engine;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Testing;

namespace ConsoleGraphicsEngine;

public static class Program {
	public static string HelpText = @"ConsoleGraphicsApp - Command-Line Vector Graphics Game Engine
Usage: dotnet run -- [options]
Options:
	--test, -t         Run the automated functionality test suite and exit
	--frames <N>       Run N headless simulation frames and print summary
	--deterministic    Use Q32.32 deterministic fixed-point physics (default)
	--fast-float       Use hardware float physics
	--help, -h         Show this help message";

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

		if (runTests) {
			return FunctionalityTests.RunAllTests();
		}

		if (headlessFrames > 0) {
			var sim = new GameApplication(100, 50, 60);
			for (int f = 0; f < headlessFrames; f++) {
				sim.StepFrame();
			}
			Console.WriteLine($"Simulated {sim.Clock.CurrentFrame} frames (Deterministic={FNum.Deterministic}). " +
								$"Collisions={sim.TotalCollisionsResolved}, ActiveParticles={sim.Particles.ActiveCount}, " +
								$"CirclePos=({sim.World.Circle.Position.X}, {sim.World.Circle.Position.Y})");
			return 0;
		}

		if (Console.IsOutputRedirected) {
			Console.Error.WriteLine("Please run this in a real terminal window (or pass --test / --frames N for headless verification).");
			return 1;
		}

		var app = new GameApplication();
		app.RunInteractiveTerminalLoop();
		return 0;
	}

	static void PrintHelp() {
		Console.WriteLine(HelpText);
	}
}
