#nullable enable
using System;
using System.Collections.Generic;
using ConsoleGraphicsEngine.MathCore;

namespace ConsoleGraphicsEngine.Rendering {
	/// <summary>
	/// Software back-buffer: <see cref="Pixels"/> is <c>Width * Height</c> (<see cref="Height"/> always even = <c>2 * Rows</c>);
	/// <see cref="TextChars"/> is <c>Width * Rows</c> overlaid on half-block pixel pairs.
	/// </summary>
	public sealed class Canvas {
		public int Width { get; }
		public int Height { get; }
		public Rgb[] Pixels { get; }
		public int Rows => Height / 2;
		public char[] TextChars { get; }
		public Rgb[] TextColors { get; }
		public Transform2D Transform { get; set; } = Transform2D.Identity;
		readonly Stack<Transform2D> _stack = new Stack<Transform2D>();
		int _superSampling = 3;
		float[] _offsets = Array.Empty<float>();

		public Canvas(int width, int height) {
			Width = Math.Max(1, width);
			Height = Math.Max(2, height & ~1);
			Pixels = new Rgb[Width * Height];
			TextChars = new char[Width * Rows];
			TextColors = new Rgb[Width * Rows];
			RebuildOffsets();
		}

		public int SuperSampling {
			get => _superSampling;
			set { _superSampling = Math.Clamp(value, 1, 16); RebuildOffsets(); }
		}

		void RebuildOffsets() {
			_offsets = new float[_superSampling];
			for (int i = 0; i < _superSampling; i++) _offsets[i] = (i + 0.5f) / _superSampling;
		}

		public void PushTransform(Transform2D t) {
			_stack.Push(Transform);
			Transform = Transform * t;
		}

		public void PopTransform() => Transform = _stack.Pop();

		public void Clear(Rgb color) {
			Array.Fill(Pixels, color);
			Array.Clear(TextChars, 0, TextChars.Length);
		}

		public void FillCircle(Vec2 center, float radius, Rgb color) =>
			Fill(new ScreenCircle(center, radius, Transform), color);

		public void FillPolygon(IReadOnlyList<Vec2> vertices, Rgb color) {
			if (vertices.Count < 3) return;
			Fill(new ScreenPolygon(vertices, Transform), color);
		}

		public void FillRect(Vec2 min, Vec2 max, Rgb color) =>
			FillPolygon(new[] { min, new Vec2(max.X, min.Y), max, new Vec2(min.X, max.Y) }, color);

		public void DrawLine(Vec2 a, Vec2 b, float thickness, Rgb color) {
			Vec2 d = b - a;
			float len = d.Length;
			if (len < 1e-9f) return;
			Vec2 n = d.Perp / len * (thickness * 0.5f);
			FillPolygon(new[] { a - n, b - n, b + n, a + n }, color);
		}

		public void DrawText(Vec2 position, string text, TextAlign align = TextAlign.Center,
							 Rgb? backdrop = null, float backdropOpacity = 0.7f) =>
			PutText(position, text, default, true, align, backdrop, backdropOpacity);

		public void DrawText(Vec2 position, string text, Rgb color, TextAlign align = TextAlign.Center,
							 Rgb? backdrop = null, float backdropOpacity = 0.7f) =>
			PutText(position, text, color, false, align, backdrop, backdropOpacity);

		void PutText(Vec2 position, string text, Rgb color, bool autoContrast, TextAlign align,
					 Rgb? backdrop, float backdropOpacity) {
			if (string.IsNullOrEmpty(text)) return;
			Vec2 p = Transform.Apply(position);
			if (float.IsNaN(p.X) || float.IsInfinity(p.X) || float.IsNaN(p.Y) || float.IsInfinity(p.Y)) return;
			double rowD = Math.Floor(p.Y / 2.0);
			if (rowD < 0 || rowD >= Rows) return;
			double startD = align switch {
				TextAlign.Left => Math.Floor(p.X),
				TextAlign.Right => Math.Floor(p.X) + 1 - text.Length,
				_ => Math.Floor(p.X - text.Length / 2.0 + 0.5)
			};
			if (startD >= Width || startD + text.Length <= 0) return;
			int row = (int)rowD;
			int start = (int)startD;

			if (backdrop.HasValue) {
				int colStart = Math.Max(0, start);
				int colEnd = Math.Min(Width - 1, start + text.Length - 1);
				for (int col = colStart; col <= colEnd; col++) {
					int top = (2 * row) * Width + col;
					Pixels[top] = Rgb.Lerp(Pixels[top], backdrop.Value, backdropOpacity);
					Pixels[top + Width] = Rgb.Lerp(Pixels[top + Width], backdrop.Value, backdropOpacity);
				}
			}
			WriteCells(row, start, text, color, autoContrast, 0, Width);
		}

		/// <summary>Blends optional <paramref name="backdrop"/> over <paramref name="rect"/> (2 pixel rows per cell row) and writes wrapped text.</summary>
		public void DrawOverlayText(ScreenRect rect, string text, Rgb color,
									TextAlign align = TextAlign.Left,
									Rgb? backdrop = null, float backdropOpacity = 0.7f,
									TextWrapMode wrapMode = TextWrapMode.WordWrap) {
			if (rect.Width <= 0 || rect.Height <= 0) return;

			int rStart = Math.Max(0, rect.Y), rEnd = Math.Min(Rows - 1, rect.Bottom - 1);
			int cStart = Math.Max(0, rect.X), cEnd = Math.Min(Width - 1, rect.Right - 1);

			if (backdrop.HasValue && rStart <= rEnd && cStart <= cEnd) {
				for (int r = rStart; r <= rEnd; r++) {
					for (int c = cStart; c <= cEnd; c++) {
						int top = (2 * r) * Width + c;
						Pixels[top] = Rgb.Lerp(Pixels[top], backdrop.Value, backdropOpacity);
						Pixels[top + Width] = Rgb.Lerp(Pixels[top + Width], backdrop.Value, backdropOpacity);
					}
				}
			}

			if (string.IsNullOrEmpty(text)) return;

			List<string> lines = WrapText(text, rect.Width, wrapMode);
			int maxLines = Math.Min(lines.Count, rect.Height);
			for (int i = 0; i < maxLines; i++) {
				int row = rect.Y + i;
				if (row < 0 || row >= Rows) continue;
				string line = lines[i];
				int start = align switch {
					TextAlign.Left => rect.X,
					TextAlign.Right => rect.Right - line.Length,
					_ => rect.X + (rect.Width - line.Length) / 2
				};
				WriteCells(row, start, line, color, false, Math.Max(0, rect.X), Math.Min(Width, rect.Right));
			}
		}

		public static List<string> WrapText(string text, int maxWidth, TextWrapMode wrapMode) {
			var result = new List<string>();
			if (maxWidth <= 0 || string.IsNullOrEmpty(text)) return result;

			string[] paragraphs = text.Replace("\r\n", "\n").Split('\n');
			foreach (string para in paragraphs) {
				if (wrapMode == TextWrapMode.None) {
					result.Add(para.Length <= maxWidth ? para : para.Substring(0, maxWidth));
					continue;
				}
				if (para.Length == 0) {
					result.Add("");
					continue;
				}
				if (wrapMode == TextWrapMode.CharWrap) {
					for (int i = 0; i < para.Length; i += maxWidth) {
						result.Add(para.Substring(i, Math.Min(maxWidth, para.Length - i)));
					}
					continue;
				}
				int pos = 0;
				while (pos < para.Length) {
					while (pos < para.Length && para[pos] == ' ') pos++;
					if (pos >= para.Length) break;

					if (para.Length - pos <= maxWidth) {
						result.Add(para.Substring(pos));
						break;
					}
					int breakAt = para.LastIndexOf(' ', pos + maxWidth, maxWidth + 1);
					if (breakAt <= pos) {
						result.Add(para.Substring(pos, maxWidth));
						pos += maxWidth;
					} else {
						result.Add(para.Substring(pos, breakAt - pos));
						pos = breakAt + 1;
					}
				}
			}
			return result;
		}

		void WriteCells(int row, int firstCol, string text, Rgb color, bool autoContrast, int clipLeft, int clipRight) {
			for (int i = 0; i < text.Length; i++) {
				int col = firstCol + i;
				if (col < clipLeft || col >= clipRight || col < 0 || col >= Width) continue;
				char ch = text[i];
				if (ch == ' ' || char.IsControl(ch) || char.IsSurrogate(ch)) continue;
				int cell = row * Width + col;
				TextChars[cell] = ch;
				TextColors[cell] = autoContrast ? ContrastColor(row, col) : color;
			}
		}

		Rgb ContrastColor(int row, int col) {
			Rgb under = Rgb.Average(Pixels[(2 * row) * Width + col], Pixels[(2 * row + 1) * Width + col]);
			return under.Luminance > 140 ? new Rgb(12, 12, 24) : new Rgb(245, 245, 245);
		}

		void Fill(ScreenShape shape, Rgb color) {
			if (!shape.GetYRange(out float minY, out float maxY)) return;
			// Clamp in float before int cast to prevent overflow on far off-screen coordinates
			int yStart = (int)Math.Clamp(Math.Floor(minY), 0, Height);
			int yEnd = (int)Math.Clamp(Math.Ceiling(maxY) - 1, -1, Height - 1);
			int n = _superSampling, total = n * n;
			float[] offs = _offsets;
			bool convex = shape.IsConvex;
			for (int py = yStart; py <= yEnd; py++) {
				if (!shape.GetXSpan(py, py + 1, out float minX, out float maxX)) continue;
				int xStart = (int)Math.Clamp(Math.Floor(minX), 0, Width);
				int xEnd = (int)Math.Clamp(Math.Ceiling(maxX) - 1, -1, Width - 1);
				for (int px = xStart; px <= xEnd; px++) {
					if (convex && shape.Contains(px, py) && shape.Contains(px + 1, py)
							   && shape.Contains(px, py + 1) && shape.Contains(px + 1, py + 1)) {
						Pixels[py * Width + px] = color;
						continue;
					}
					int hits = 0;
					for (int sy = 0; sy < n; sy++) {
						float y = py + offs[sy];
						for (int sx = 0; sx < n; sx++)
							if (shape.Contains(px + offs[sx], y)) hits++;
					}
					if (hits == 0) continue;
					int index = py * Width + px;
					Pixels[index] = hits == total ? color : Rgb.Lerp(Pixels[index], color, (float)hits / total);
				}
			}
		}
	}

	public sealed class CanvasRenderBackend : IRenderBackend {
		public Canvas Canvas { get; private set; }
		public Canvas? SoftwareCanvas => Canvas;
		public int PixelWidth => Canvas.Width;
		public int PixelHeight => Canvas.Height;
		public int CellColumns => Canvas.Width;
		public int CellRows => Canvas.Rows;

		public int SuperSampling {
			get => Canvas.SuperSampling;
			set => Canvas.SuperSampling = value;
		}

		public Transform2D ViewTransform {
			get => Canvas.Transform;
			set => Canvas.Transform = value;
		}

		public CanvasRenderBackend(int pixelWidth, int pixelHeight) {
			Canvas = new Canvas(pixelWidth, pixelHeight);
		}

		public void Resize(int pixelWidth, int pixelHeight) {
			int s = Canvas.SuperSampling;
			Canvas = new Canvas(pixelWidth, pixelHeight) { SuperSampling = s };
		}

		public void Clear(Rgb color) => Canvas.Clear(color);
		public void PushTransform(Transform2D transform) => Canvas.PushTransform(transform);
		public void PopTransform() => Canvas.PopTransform();
		public void FillCircle(Vec2 center, float radius, Rgb color) => Canvas.FillCircle(center, radius, color);
		public void FillPolygon(IReadOnlyList<Vec2> vertices, Rgb color) => Canvas.FillPolygon(vertices, color);
		public void FillRect(Vec2 min, Vec2 max, Rgb color) => Canvas.FillRect(min, max, color);
		public void DrawLine(Vec2 a, Vec2 b, float thickness, Rgb color) => Canvas.DrawLine(a, b, thickness, color);

		public void DrawText(Vec2 worldPos, string text, TextAlign align = TextAlign.Center,
							 Rgb? backdrop = null, float backdropOpacity = 0.7f) =>
			Canvas.DrawText(worldPos, text, align, backdrop, backdropOpacity);

		public void DrawText(Vec2 worldPos, string text, Rgb color, TextAlign align = TextAlign.Center,
							 Rgb? backdrop = null, float backdropOpacity = 0.7f) =>
			Canvas.DrawText(worldPos, text, color, align, backdrop, backdropOpacity);

		public void DrawOverlayText(ScreenRect rect, string text, Rgb color,
									TextAlign align = TextAlign.Left,
									Rgb? backdrop = null, float backdropOpacity = 0.7f,
									TextWrapMode wrapMode = TextWrapMode.WordWrap) =>
			Canvas.DrawOverlayText(rect, text, color, align, backdrop, backdropOpacity, wrapMode);
	}
}
