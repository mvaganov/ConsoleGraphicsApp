#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ConsoleGraphicsEngine.Engine;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Rendering;

namespace ConsoleGraphicsEngine.CliHost {
	/// <summary>Differential ANSI half-block presenter (<c>U+2580</c> = 2 vertical pixels/cell) + SGR-1006 mouse init.</summary>
	public sealed class TerminalPresenter : IDisposable {
		const string CSI = "\u001b[";
		const char UpperHalfBlock = '\u2580';
		readonly Stream _out = Console.OpenStandardOutput();
		readonly StringBuilder _sb = new StringBuilder(1 << 16);

		readonly struct Cell : IEquatable<Cell> {
			public readonly Rgb Fg, Bg;
			public readonly char Glyph;
			public Cell(Rgb fg, Rgb bg, char glyph) { Fg = fg; Bg = bg; Glyph = glyph; }
			public bool Equals(Cell o) => Glyph == o.Glyph && Fg == o.Fg && Bg == o.Bg;
			public override bool Equals(object? obj) => obj is Cell o && Equals(o);
			public override int GetHashCode() => HashCode.Combine(Fg, Bg, Glyph);
		}

		Cell[] _front = Array.Empty<Cell>();
		int _cols, _rows;
		bool _forceFull = true;

		public TerminalPresenter(bool enableMouseReporting = true) {
			Native.EnableVirtualTerminal();
			Console.OutputEncoding = new UTF8Encoding(false);
			string mouseInit = enableMouseReporting ? (CSI + "?1000h" + CSI + "?1002h" + CSI + "?1003h" + CSI + "?1006h") : "";
			Write(CSI + "?1049h" + CSI + "?25l" + mouseInit + CSI + "0m" + CSI + "2J");
		}

		public void Invalidate() => _forceFull = true;

		public int Present(Canvas canvas) {
			int cols = canvas.Width, rows = canvas.Height / 2;
			if (cols != _cols || rows != _rows) {
				_front = new Cell[cols * rows];
				_cols = cols;
				_rows = rows;
				_forceFull = true;
			}
			Rgb[] back = canvas.Pixels;
			_sb.Clear();
			if (_forceFull) _sb.Append(CSI).Append("0m").Append(CSI).Append("2J");
			int changed = 0, lastRow = -1, lastCol = -2;
			bool colorsKnown = false;
			Rgb fg = default, bg = default;
			for (int row = 0; row < rows; row++) {
				for (int col = 0; col < cols; col++) {
					int cellIndex = row * cols + col;
					int topIndex = (2 * row) * cols + col;
					int bottomIndex = topIndex + cols;

					char text = canvas.TextChars[cellIndex];
					Cell cell = text == '\0'
						? new Cell(back[topIndex], back[bottomIndex], UpperHalfBlock)
						: new Cell(canvas.TextColors[cellIndex], Rgb.Average(back[topIndex], back[bottomIndex]), text);

					if (!_forceFull && cell.Equals(_front[cellIndex])) continue;
					_front[cellIndex] = cell;
					changed++;
					if (row != lastRow || col != lastCol + 1) {
						_sb.Append(CSI).Append(row + 1).Append(';').Append(col + 1).Append('H');
					}
					if (!colorsKnown || fg != cell.Fg) AppendColor(38, cell.Fg);
					if (!colorsKnown || bg != cell.Bg) AppendColor(48, cell.Bg);
					fg = cell.Fg; bg = cell.Bg; colorsKnown = true;
					_sb.Append(cell.Glyph);
					lastRow = row; lastCol = col;
				}
			}
			_forceFull = false;
			if (_sb.Length > 0) {
				byte[] bytes = Encoding.UTF8.GetBytes(_sb.ToString());
				_out.Write(bytes, 0, bytes.Length);
				_out.Flush();
			}
			return changed;
		}

		void AppendColor(int mode, Rgb c) => _sb.Append(CSI).Append(mode).Append(";2;")
			.Append(c.R).Append(';').Append(c.G).Append(';').Append(c.B).Append('m');

		void Write(string s) {
			byte[] bytes = Encoding.UTF8.GetBytes(s);
			_out.Write(bytes, 0, bytes.Length);
			_out.Flush();
		}

		public void Dispose() =>
			Write(CSI + "?1006l" + CSI + "?1003l" + CSI + "?1002l" + CSI + "?1000l" + CSI + "0m" + CSI + "?25h" + CSI + "?1049l");

		static class Native {
			[DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int nStdHandle);
			[DllImport("kernel32.dll")] static extern bool GetConsoleMode(IntPtr handle, out uint mode);
			[DllImport("kernel32.dll")] static extern bool SetConsoleMode(IntPtr handle, uint mode);
			public static void EnableVirtualTerminal() {
				if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
				const int STD_OUTPUT_HANDLE = -11;
				const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
				IntPtr outHandle = GetStdHandle(STD_OUTPUT_HANDLE);
				if (GetConsoleMode(outHandle, out uint outMode)) {
					SetConsoleMode(outHandle, outMode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
				}
			}
		}
	}

	/// <summary>Bridges <see cref="ConsoleKeyInfo"/> and ANSI SGR-1006 (<c>ESC[&lt;Cb;Cx;CyM/m</c>) to <see cref="EventQueue"/>.</summary>
	public static class ConsoleInputAdapter {
		public static void PollTerminalInput(GameApplication app) {
			while (Console.KeyAvailable) {
				ConsoleKeyInfo key = Console.ReadKey(true);
				if (key.Key == ConsoleKey.Escape && Console.KeyAvailable) {
					ConsoleKeyInfo next1 = Console.ReadKey(true);
					if (next1.KeyChar == '[' && Console.KeyAvailable) {
						ConsoleKeyInfo next2 = Console.ReadKey(true);
						if (next2.KeyChar == '<') {
							var seq = new StringBuilder(16);
							char terminator = '\0';
							while (Console.KeyAvailable && seq.Length < 32) {
								char c = Console.ReadKey(true).KeyChar;
								if (c == 'M' || c == 'm') { terminator = c; break; }
								seq.Append(c);
							}
							if (terminator != '\0' && TryParseSgrMouse(seq.ToString(), terminator, app, out PointerInputEvent? pev) && pev != null) {
								app.Events.EnqueuePointer(pev);
								continue;
							}
						}
					}
				}
				app.Events.EnqueueInput(new UserInputEvent(app.Clock.CurrentFrame, MapConsoleKey(key)));
			}
		}

		/// <summary>Decodes SGR-1006 <c>Cb;Cx;Cy</c> (1-based cell coords; bit 5=motion, bit 6=wheel, 'M'=press, 'm'=release).</summary>
		public static bool TryParseSgrMouse(string payload, char terminator, GameApplication app, out PointerInputEvent? pointerEvent) {
			pointerEvent = null;
			string[] parts = payload.Split(';');
			if (parts.Length != 3) return false;
			if (!int.TryParse(parts[0], out int cb) ||
				!int.TryParse(parts[1], out int cx1Based) ||
				!int.TryParse(parts[2], out int cy1Based)) {
				return false;
			}

			int cellX = Math.Clamp(cx1Based - 1, 0, Math.Max(0, app.RenderBackend.CellColumns - 1));
			int cellY = Math.Clamp(cy1Based - 1, 0, Math.Max(0, app.RenderBackend.CellRows - 1));
			Vec2 pixelPos = new Vec2(cellX + 0.5f, cellY * 2f + 1.0f);
			Vec2 worldPos = app.Camera.ScreenCellToWorld(cellX, cellY, app.RenderBackend.PixelWidth, app.RenderBackend.PixelHeight);

			bool isMotion = (cb & 32) != 0;
			bool isWheel = (cb & 64) != 0;
			int lowBits = cb & 3;

			if (isWheel) {
				pointerEvent = new PointerInputEvent(app.Clock.CurrentFrame, PointerEventType.Scroll, PointerButton.None,
					cellX, cellY, pixelPos, worldPos, (lowBits == 0) ? +1f : -1f);
				return true;
			}

			PointerButton btn = lowBits switch {
				0 => PointerButton.Left,
				1 => PointerButton.Middle,
				2 => PointerButton.Right,
				_ => PointerButton.None
			};

			PointerEventType type = terminator == 'm'
				? PointerEventType.Up
				: isMotion
					? (btn == PointerButton.None ? PointerEventType.Move : PointerEventType.Drag)
					: PointerEventType.Down;

			pointerEvent = new PointerInputEvent(app.Clock.CurrentFrame, type, btn, cellX, cellY, pixelPos, worldPos);
			return true;
		}

		public static KeyEventData MapConsoleKey(ConsoleKeyInfo k) {
			KeyCode code = k.Key switch {
				ConsoleKey.Escape => KeyCode.Escape,
				ConsoleKey.Enter => KeyCode.Enter,
				ConsoleKey.Spacebar => KeyCode.Space,
				ConsoleKey.Tab => KeyCode.Tab,
				ConsoleKey.Backspace => KeyCode.Backspace,
				ConsoleKey.UpArrow => KeyCode.UpArrow,
				ConsoleKey.DownArrow => KeyCode.DownArrow,
				ConsoleKey.LeftArrow => KeyCode.LeftArrow,
				ConsoleKey.RightArrow => KeyCode.RightArrow,
				ConsoleKey.A => KeyCode.A,
				ConsoleKey.B => KeyCode.B,
				ConsoleKey.C => KeyCode.C,
				ConsoleKey.D => KeyCode.D,
				ConsoleKey.E => KeyCode.E,
				ConsoleKey.F => KeyCode.F,
				ConsoleKey.G => KeyCode.G,
				ConsoleKey.H => KeyCode.H,
				ConsoleKey.I => KeyCode.I,
				ConsoleKey.J => KeyCode.J,
				ConsoleKey.K => KeyCode.K,
				ConsoleKey.L => KeyCode.L,
				ConsoleKey.M => KeyCode.M,
				ConsoleKey.N => KeyCode.N,
				ConsoleKey.O => KeyCode.O,
				ConsoleKey.P => KeyCode.P,
				ConsoleKey.Q => KeyCode.Q,
				ConsoleKey.R => KeyCode.R,
				ConsoleKey.S => KeyCode.S,
				ConsoleKey.T => KeyCode.T,
				ConsoleKey.U => KeyCode.U,
				ConsoleKey.V => KeyCode.V,
				ConsoleKey.W => KeyCode.W,
				ConsoleKey.X => KeyCode.X,
				ConsoleKey.Y => KeyCode.Y,
				ConsoleKey.Z => KeyCode.Z,
				ConsoleKey.D0 or ConsoleKey.NumPad0 => KeyCode.D0,
				ConsoleKey.D1 or ConsoleKey.NumPad1 => KeyCode.D1,
				ConsoleKey.D2 or ConsoleKey.NumPad2 => KeyCode.D2,
				ConsoleKey.D3 or ConsoleKey.NumPad3 => KeyCode.D3,
				ConsoleKey.D4 or ConsoleKey.NumPad4 => KeyCode.D4,
				ConsoleKey.D5 or ConsoleKey.NumPad5 => KeyCode.D5,
				ConsoleKey.D6 or ConsoleKey.NumPad6 => KeyCode.D6,
				ConsoleKey.D7 or ConsoleKey.NumPad7 => KeyCode.D7,
				ConsoleKey.D8 or ConsoleKey.NumPad8 => KeyCode.D8,
				ConsoleKey.D9 or ConsoleKey.NumPad9 => KeyCode.D9,
				ConsoleKey.OemPlus or ConsoleKey.Add => KeyCode.Plus,
				ConsoleKey.OemMinus or ConsoleKey.Subtract => KeyCode.Minus,
				_ => KeyCode.None
			};

			return new KeyEventData(code, k.KeyChar,
				(k.Modifiers & ConsoleModifiers.Shift) != 0,
				(k.Modifiers & ConsoleModifiers.Control) != 0,
				(k.Modifiers & ConsoleModifiers.Alt) != 0);
		}

		public static void RunInteractiveLoop(GameApplication app) {
			Console.CancelKeyPress += (s, e) => { e.Cancel = true; app.IsRunning = false; };

			using var terminal = new TerminalPresenter(enableMouseReporting: true);
			var wallClock = Stopwatch.StartNew();
			double nextFrameTarget = wallClock.Elapsed.TotalSeconds;
			double lastStatWallSeconds = nextFrameTarget;
			int statFrames = 0, statCells = 0, termCols = -1, termRows = -1;

			while (app.IsRunning) {
				int cols = Math.Max(1, Console.WindowWidth), rows = Math.Max(1, Console.WindowHeight);
				if (cols != termCols || rows != termRows) {
					termCols = cols; termRows = rows;
					app.ResizeViewport(cols, rows);
					terminal.Invalidate();
				}

				PollTerminalInput(app);
				app.StepFrame();

				statCells += terminal.Present(app.Canvas);
				statFrames++;

				double now = wallClock.Elapsed.TotalSeconds;
				if (now - lastStatWallSeconds >= 0.25) {
					app.MeasuredFps = statFrames / (now - lastStatWallSeconds);
					app.AvgGlyphsPerFrame = statCells / Math.Max(1, statFrames);
					lastStatWallSeconds = now;
					statFrames = 0;
					statCells = 0;
				}

				nextFrameTarget += app.Clock.FixedDeltaTime;
				double spare = nextFrameTarget - wallClock.Elapsed.TotalSeconds;
				if (spare > 0.002) {
					Thread.Sleep((int)(spare * 1000));
				} else if (spare < -0.25) {
					nextFrameTarget = wallClock.Elapsed.TotalSeconds;
				}
			}
		}
	}
}
