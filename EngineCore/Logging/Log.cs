#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ConsoleGraphicsEngine.Logging {
	/// <summary>Platform-neutral 16-color palette matching <c>System.ConsoleColor</c> ordinals (0..15).</summary>
	public enum LogColor {
		Black = 0, DarkBlue = 1, DarkGreen = 2, DarkCyan = 3,
		DarkRed = 4, DarkMagenta = 5, DarkYellow = 6, Gray = 7,
		DarkGray = 8, Blue = 9, Green = 10, Cyan = 11,
		Red = 12, Magenta = 13, Yellow = 14, White = 15
	}

	public readonly struct LogColorPair {
		public readonly LogColor Fore, Back;
		public LogColorPair(LogColor fore, LogColor back) { Fore = fore; Back = back; }
		public static readonly LogColorPair Default = new LogColorPair(LogColor.Gray, LogColor.Black);
		public LogColorPair WithFore(LogColor fore) => new LogColorPair(fore, Back);
		public LogColorPair WithBack(LogColor back) => new LogColorPair(Fore, back);
		public LogColorPair Invert() => new LogColorPair(Back, Fore);
	}

	/// <summary>Host-supplied sink for colored log output (implemented by <c>CliHost</c> and <c>UnityBridge</c>).</summary>
	public interface ILogSink {
		LogColorPair CurrentColor { get; set; }
		void Write(string text);
	}

	/// <summary>
	/// Structured logger adapted from <c>ConsoleMrV.Log</c> with inline escape-char color switching,
	/// caller <c>file:line</c> metadata, file output (<c>local/log.txt</c>), and zero <c>System.Console</c> dependency in <c>EngineCore</c>.
	/// Stripped in Release builds unless <c>DEBUG</c> or <c>KEEP_LOGS</c> is defined.
	/// </summary>
	public static class Log {
		public enum Code {
			None = 0,
			Critical = 1,
			Error = 2,
			Warning = 3,
			Info = 4,
			Debug = 5,
			Verbose = 6,
			SuccessfulAsserts = 8
		}

		public sealed class Visibility {
			public readonly Code code;
			public readonly LogColorPair color;
			public readonly char escapeCode;
			public Visibility(Code code, LogColorPair color, char escapeCode) {
				this.code = code; this.color = color; this.escapeCode = escapeCode;
			}
		}

		public static Code visibility = Code.Warning;
		public static Visibility[] Visibilities = Array.Empty<Visibility>();
		private static string VisibilityEscapeCodes = "";
		public static string LogFile = "local/log.txt";
		public static bool IdentifySourceCode = true;
		public static bool AssertThrowsException = true;
		public static ILogSink? Sink { get; set; }

		static Log() {
			GenerateVisibilities();
#if DEBUG || KEEP_LOGS
			InitLogFile();
#endif
		}

		public static void InitLogFile() {
			if (string.IsNullOrEmpty(LogFile)) return;
			try {
				string? dir = Path.GetDirectoryName(LogFile);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
				File.WriteAllText(LogFile, DateTime.Now.ToString("O") + "\n");
			} catch {
				// Ignore I/O errors in sandboxed/read-only hosts
			}
		}

		public static char UnhighlightChar => Visibilities[0].escapeCode;
		public static string Unhighlight => Visibilities[0].escapeCode.ToString();

		public static void GenerateVisibilities() {
			Visibilities = new Visibility[] {
				new Visibility(Code.None, LogColorPair.Default, '\x01'),
				new Visibility(Code.Critical, LogColorPair.Default.WithFore(LogColor.Magenta), '\x02'),
				new Visibility(Code.Error, LogColorPair.Default.WithFore(LogColor.Red), '\x03'),
				new Visibility(Code.Warning, LogColorPair.Default.WithFore(LogColor.Yellow), '\x04'),
				new Visibility(Code.Info, LogColorPair.Default.WithFore(LogColor.White), '\x05'),
				new Visibility(Code.Debug, LogColorPair.Default.WithFore(LogColor.Gray), '\x06'),
				new Visibility(Code.Verbose, LogColorPair.Default.WithFore(LogColor.DarkGray), '\x0b'),
				new Visibility(Code.SuccessfulAsserts, LogColorPair.Default.WithFore(LogColor.DarkMagenta), '\x0c')
			};
			var sb = new StringBuilder(Visibilities.Length);
			for (int i = 0; i < Visibilities.Length; i++) sb.Append(Visibilities[i].escapeCode);
			VisibilityEscapeCodes = sb.ToString();
		}

		public static void PrintWithVisibility(string msg, Code minimumVisibility, bool endline) =>
			PrintWithVisibilityInternal(msg, minimumVisibility, endline, IdentifySourceCode);

		private static void PrintWithVisibilityInternal(string msg, Code level, bool endline, bool identifySourceCode) {
#if DEBUG || KEEP_LOGS
			if (visibility < level) return;
			if (identifySourceCode) ShowSourceCodeMetaData(level);
			ForcePrintWithVisibility(msg, level, endline);
#endif
		}

		public static void e(string msg) => PrintWithVisibilityInternal(msg, Code.Error, true, IdentifySourceCode);
		public static void w(string msg) => PrintWithVisibilityInternal(msg, Code.Warning, true, IdentifySourceCode);
		public static void i(string msg) => PrintWithVisibilityInternal(msg, Code.Info, true, IdentifySourceCode);
		public static void d(string msg) => PrintWithVisibilityInternal(msg, Code.Debug, true, IdentifySourceCode);
		public static void v(string msg) => PrintWithVisibilityInternal(msg, Code.Verbose, true, IdentifySourceCode);

		public static void Assert(bool condition, string message) {
			if (condition) {
				PrintWithVisibilityInternal(message, Code.SuccessfulAsserts, true, IdentifySourceCode);
			} else {
				PrintWithVisibilityInternal(message, Code.Critical, true, IdentifySourceCode);
				if (AssertThrowsException) {
					var t = new StackTrace();
					PrintWithVisibilityInternal(t.ToString(), Code.Critical, true, false);
					throw new InvalidOperationException(message);
				}
			}
		}

		static Visibility GetVisibilityEntry(Code level) {
			for (int i = 0; i < Visibilities.Length; i++) {
				if (Visibilities[i].code == level) return Visibilities[i];
			}
			return Visibilities[0];
		}

		public static void ForcePrintWithVisibility(string message, Code level, bool endLine) {
			LogColorPair oldColor = Sink != null ? Sink.CurrentColor : LogColorPair.Default;
			if (Sink != null) Sink.CurrentColor = GetVisibilityEntry(level).color;
			PrintWithColorEscape(message);
			if (Sink != null) Sink.CurrentColor = oldColor;
			if (endLine) Write("\n");
		}

		public static void PrintWithColorEscape(string message) {
			bool escapeCodeUsed = false;
			int start = 0;
			for (int i = 0; i < message.Length; i++) {
				int escapeIndex = VisibilityEscapeCodes.IndexOf(message[i]);
				if (escapeIndex >= 0) {
					escapeCodeUsed = true;
					Write(message.Substring(start, i - start));
					if (Sink != null) Sink.CurrentColor = Visibilities[escapeIndex].color;
					start = i + 1;
				}
			}
			Write(!escapeCodeUsed ? message : message.Substring(start, message.Length - start));
		}

		public static void Print(string message) {
			LogColorPair oldColor = Sink != null ? Sink.CurrentColor : LogColorPair.Default;
			PrintWithColorEscape(message);
			if (Sink != null) Sink.CurrentColor = oldColor;
		}

		public static string StackPosition(int framesBack = 1, bool includeFullPath = false) {
			StackFrame? frame = new StackTrace(true).GetFrame(framesBack);
			string? currentFile = frame?.GetFileName();
			if (string.IsNullOrEmpty(currentFile)) return "unknown:0";
			if (!includeFullPath) {
				int lastFolder = Math.Max(currentFile.LastIndexOf('/'), currentFile.LastIndexOf('\\')) + 1;
				int fileSuffix = currentFile.LastIndexOf('.');
				if (fileSuffix < lastFolder) fileSuffix = currentFile.Length;
				currentFile = currentFile.Substring(lastFolder, fileSuffix - lastFolder);
			}
			int currentLine = frame?.GetFileLineNumber() ?? 0;
			return $"{currentFile}:{currentLine} ";
		}

		private static void ShowSourceCodeMetaData(Code level) {
			LogColorPair oldColor = Sink != null ? Sink.CurrentColor : LogColorPair.Default;
			if (Sink != null) Sink.CurrentColor = GetVisibilityEntry(level).color.Invert();
			PrintWithColorEscape(StackPosition(4));
			if (Sink != null) Sink.CurrentColor = oldColor;
		}

		private static void Write(string text) {
			Sink?.Write(text);
			AppendToLog(text);
		}

		private static void AppendToLog(string text) {
			if (string.IsNullOrEmpty(LogFile)) return;
			try {
				File.AppendAllText(LogFile, text);
			} catch {
				// Ignore file lock or read-only errors
			}
		}
	}
}
