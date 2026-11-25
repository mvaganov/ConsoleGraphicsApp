#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ConsoleGraphicsEngine.MathCore;

namespace ConsoleGraphicsEngine.Rendering;

/// <summary>
/// World space: +X right, +Y UP. The camera decides which part of the world is visible.
/// </summary>
public sealed class Camera {
	public Vec2 Position; // world point shown at the center of the screen
	public Vec2 PivotPoint = new Vec2(0.5f, 0.5f); // where on the screen the 'center' is
	public float Angle; // view rotation in radians; positive = scene turns counter-clockwise
	public float Zoom = 1; // pixels per world unit

	/// <summary>world -> pixel transform: shift to the camera, rotate, scale (flip Y), move to screen center.</summary>
	public Transform2D GetViewTransform(int pixelWidth, int pixelHeight) =>
		Transform2D.Translation(pixelWidth * PivotPoint.X, pixelHeight * PivotPoint.Y)
		* Transform2D.Scale(Zoom, -Zoom)
		* Transform2D.Rotation(Angle)
		* Transform2D.Translation(-Position.X, -Position.Y);
}

/// <summary>
/// Owns the terminal while it is alive (alternate screen, hidden cursor) and
/// draws Canvas frames to it, rewriting only the glyphs that changed.
/// </summary>
public sealed class TerminalPresenter : IDisposable {
	const string CSI = "\u001b[";         // "Control Sequence Introducer"
	const char UpperHalfBlock = '\u2580'; // (char)9600
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

	public TerminalPresenter() {
		Native.EnableVirtualTerminal();
		Console.OutputEncoding = new UTF8Encoding(false);
		Write(CSI + "?1049h" + CSI + "?25l" + CSI + "0m" + CSI + "2J");
	}

	public void Invalidate() => _forceFull = true;

	public int Present(Canvas canvas) {
		int cols = canvas.Width;
		int rows = canvas.Height / 2;
		if (cols != _cols || rows != _rows) {
			_front = new Cell[cols * rows];
			_cols = cols;
			_rows = rows;
			_forceFull = true;
		}
		Rgb[] back = canvas.Pixels;
		_sb.Clear();
		if (_forceFull) _sb.Append(CSI).Append("0m").Append(CSI).Append("2J");
		int changed = 0;
		int lastRow = -1, lastCol = -2;
		bool colorsKnown = false;
		Rgb fg = default, bg = default;
		for (int row = 0; row < rows; row++) {
			for (int col = 0; col < cols; col++) {
				int cellIndex = row * cols + col;
				int topIndex = (2 * row) * cols + col;
				int bottomIndex = topIndex + cols;

				Cell cell;
				char text = canvas.TextChars[cellIndex];
				if (text == '\0')
					cell = new Cell(back[topIndex], back[bottomIndex], UpperHalfBlock);
				else
					cell = new Cell(canvas.TextColors[cellIndex], Rgb.Average(back[topIndex], back[bottomIndex]), text);

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

	public void Dispose() => Write(CSI + "0m" + CSI + "?25h" + CSI + "?1049l");

	static class Native {
		[DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int nStdHandle);
		[DllImport("kernel32.dll")] static extern bool GetConsoleMode(IntPtr handle, out uint mode);
		[DllImport("kernel32.dll")] static extern bool SetConsoleMode(IntPtr handle, uint mode);
		public static void EnableVirtualTerminal() {
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
			const int STD_OUTPUT_HANDLE = -11;
			const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
			IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
			if (GetConsoleMode(handle, out uint mode)) {
				SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
			}
		}
	}
}
