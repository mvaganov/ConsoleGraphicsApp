#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ConsoleGraphicsEngine.MathCore;

namespace ConsoleGraphicsEngine.Rendering {
	/// <summary>
	/// Exports <see cref="Canvas"/> frames as:
	/// 1. ASCII / ANSI terminal snapshots (for LLM text-only visual inspection of UI/layout/shapes)
	/// 2. 24-bit uncompressed <c>.bmp</c> images (saved to <c>local/</c> by default)
	/// 3. Multi-frame animated <c>.gif</c> (GIF89a with LZW encoder + RGB332 palette, saved to <c>local/</c> by default)
	/// </summary>
	public static class FrameExporter {
		const string LuminanceRamp = " .:-=+*#%@";

		/// <summary>Renders <see cref="Canvas"/> into a bordered ASCII snapshot combining pixel luminance and overlaid <see cref="Canvas.TextChars"/>.</summary>
		public static string DumpAsciiSnapshot(Canvas canvas, long frameNumber = 0) {
			int cols = canvas.Width, rows = canvas.Rows;
			var sb = new StringBuilder((cols + 4) * (rows + 3));
			sb.AppendLine($"+-{new string('-', cols)}-+ (Frame #{frameNumber}, {cols}x{rows} cells)");
			for (int r = 0; r < rows; r++) {
				sb.Append("| ");
				for (int c = 0; c < cols; c++) {
					int cellIdx = r * cols + c;
					char ch = canvas.TextChars[cellIdx];
					if (ch != '\0') {
						sb.Append(ch);
					} else {
						int topIdx = (2 * r) * cols + c;
						float lum = Rgb.Average(canvas.Pixels[topIdx], canvas.Pixels[topIdx + cols]).Luminance;
						int rampIdx = Math.Clamp((int)(lum / 256f * LuminanceRamp.Length), 0, LuminanceRamp.Length - 1);
						sb.Append(LuminanceRamp[rampIdx]);
					}
				}
				sb.AppendLine(" |");
			}
			sb.Append($"+-{new string('-', cols)}-+");
			return sb.ToString();
		}

		/// <summary>Renders <see cref="Canvas"/> into a single-frame 24-bit ANSI half-block string (no alt-screen escape codes).</summary>
		public static string DumpAnsiSnapshot(Canvas canvas) {
			int cols = canvas.Width, rows = canvas.Rows;
			var sb = new StringBuilder(cols * rows * 24);
			for (int r = 0; r < rows; r++) {
				for (int c = 0; c < cols; c++) {
					int cellIdx = r * cols + c;
					int topIdx = (2 * r) * cols + c;
					Rgb top = canvas.Pixels[topIdx], bot = canvas.Pixels[topIdx + cols];
					char ch = canvas.TextChars[cellIdx];
					if (ch == '\0') {
						sb.Append($"\u001b[38;2;{top.R};{top.G};{top.B}m\u001b[48;2;{bot.R};{bot.G};{bot.B}m\u2580");
					} else {
						Rgb fg = canvas.TextColors[cellIdx], bg = Rgb.Average(top, bot);
						sb.Append($"\u001b[38;2;{fg.R};{fg.G};{fg.B}m\u001b[48;2;{bg.R};{bg.G};{bg.B}m{ch}");
					}
				}
				sb.Append("\u001b[0m\n");
			}
			return sb.ToString();
		}

		/// <summary>
		/// Writes a 24-bit BMP image of <paramref name="canvas"/> (default <paramref name="pixelScale"/>=4 -> 4x8 pixel cells,
		/// rendering 3x7 bitmap glyphs with 1px horizontal/vertical buffer). Defaults to <c>local/</c>.
		/// </summary>
		public static string SaveBmp(Canvas canvas, string filePath = "local/frame.bmp", int pixelScale = 4) {
			EnsureDirectory(filePath);
			pixelScale = Math.Clamp(pixelScale, 1, 16);
			Rgb[] composited = CompositeCanvasWithText(canvas, pixelScale, out int outW, out int outH);

			int rowStride = (outW * 3 + 3) & ~3;
			int pixelDataSize = rowStride * outH;
			int fileSize = 54 + pixelDataSize;

			using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
			using var bw = new BinaryWriter(fs);

			// 14-byte BITMAPFILEHEADER
			bw.Write((byte)'B');
			bw.Write((byte)'M');
			bw.Write(fileSize);
			bw.Write(0);
			bw.Write(54);

			// 40-byte BITMAPINFOHEADER
			bw.Write(40);
			bw.Write(outW);
			bw.Write(outH); // positive = bottom-up rows
			bw.Write((short)1);
			bw.Write((short)24);
			bw.Write(0);
			bw.Write(pixelDataSize);
			bw.Write(2835);
			bw.Write(2835);
			bw.Write(0);
			bw.Write(0);

			byte[] rowBuf = new byte[rowStride];
			for (int y = outH - 1; y >= 0; y--) {
				Array.Clear(rowBuf, 0, rowStride);
				int srcRow = y * outW;
				for (int x = 0; x < outW; x++) {
					Rgb c = composited[srcRow + x];
					int o = x * 3;
					rowBuf[o] = c.B;
					rowBuf[o + 1] = c.G;
					rowBuf[o + 2] = c.R;
				}
				bw.Write(rowBuf);
			}
			return Path.GetFullPath(filePath);
		}

		// Composites Canvas pixels + 3x7 bitmap font inside each 4x8 logical cell (col 3 & row 7 always blank buffer)
		static Rgb[] CompositeCanvasWithText(Canvas canvas, int scale, out int outW, out int outH) {
			int w = canvas.Width, h = canvas.Height;
			outW = w * scale;
			outH = h * scale;
			var result = new Rgb[outW * outH];

			for (int py = 0; py < h; py++) {
				for (int px = 0; px < w; px++) {
					Rgb c = canvas.Pixels[py * w + px];
					for (int sy = 0; sy < scale; sy++) {
						int dstRow = (py * scale + sy) * outW + px * scale;
						for (int sx = 0; sx < scale; sx++) result[dstRow + sx] = c;
					}
				}
			}

			if (scale >= 4) {
				int rows = canvas.Rows;
				int boxW = scale, boxH = 2 * scale;
				for (int r = 0; r < rows; r++) {
					for (int c = 0; c < w; c++) {
						char ch = canvas.TextChars[r * w + c];
						if (ch == '\0' || ch == ' ') continue;
						Rgb fg = canvas.TextColors[r * w + c];
						uint glyphMask = GetMiniGlyph3x7(ch);
						int boxX = c * scale, boxY = r * 2 * scale;
						for (int gy = 0; gy < 7; gy++) {
							for (int gx = 0; gx < 3; gx++) {
								if (((glyphMask >> ((6 - gy) * 3 + (2 - gx))) & 1U) == 0) continue;
								int px0 = boxX + (gx * boxW) / 4, px1 = boxX + ((gx + 1) * boxW) / 4;
								int py0 = boxY + (gy * boxH) / 8, py1 = boxY + ((gy + 1) * boxH) / 8;
								for (int y = py0; y < py1 && y < outH; y++) {
									for (int x = px0; x < px1 && x < outW; x++) result[y * outW + x] = fg;
								}
							}
						}
					}
				}
			}
			return result;
		}

		// 21-bit (3x7) bitmap font: standard letters occupy rows 0..4 (rows 5..6 empty); descenders (j,y,p,q,g,Q,',',';') use rows 5..6.
		// Rendered into a 4x8 logical cell so col 3 and row 7 remain empty 1px buffers.
		internal static uint GetMiniGlyph3x7(char ch) => ch switch {
			// Lowercase descenders
			'j' => 0b010_000_010_010_010_110_000U,
			'p' => 0b000_111_101_111_100_100_000U,
			'q' => 0b000_111_101_111_001_001_000U,
			'y' => 0b000_101_101_010_010_100_000U,
			'g' => 0b000_111_101_111_001_111_000U,
			_ => char.ToUpperInvariant(ch) switch {
				'0' => 0b111_101_101_101_111_000_000U, '1' => 0b010_110_010_010_111_000_000U,
				'2' => 0b111_001_111_100_111_000_000U, '3' => 0b111_001_111_001_111_000_000U,
				'4' => 0b101_101_111_001_001_000_000U, '5' => 0b111_100_111_001_111_000_000U,
				'6' => 0b111_100_111_101_111_000_000U, '7' => 0b111_001_010_010_010_000_000U,
				'8' => 0b111_101_111_101_111_000_000U, '9' => 0b111_101_111_001_111_000_000U,
				'A' => 0b010_101_111_101_101_000_000U, 'B' => 0b110_101_110_101_110_000_000U,
				'C' => 0b111_100_100_100_111_000_000U, 'D' => 0b110_101_101_101_110_000_000U,
				'E' => 0b111_100_110_100_111_000_000U, 'F' => 0b111_100_110_100_100_000_000U,
				'G' => 0b111_100_101_101_111_000_000U, 'H' => 0b101_101_111_101_101_000_000U,
				'I' => 0b111_010_010_010_111_000_000U, 'J' => 0b011_001_001_101_111_000_000U,
				'K' => 0b101_101_110_101_101_000_000U, 'L' => 0b100_100_100_100_111_000_000U,
				'M' => 0b101_111_111_101_101_000_000U, 'N' => 0b111_101_101_101_101_000_000U,
				'O' => 0b111_101_101_101_111_000_000U, 'P' => 0b111_101_111_100_100_000_000U,
				'Q' => 0b111_101_101_101_111_001_000U, 'R' => 0b111_101_110_101_101_000_000U,
				'S' => 0b111_100_111_001_111_000_000U, 'T' => 0b111_010_010_010_010_000_000U,
				'U' => 0b101_101_101_101_111_000_000U, 'V' => 0b101_101_101_101_010_000_000U,
				'W' => 0b101_101_111_111_101_000_000U, 'X' => 0b101_101_010_101_101_000_000U,
				'Y' => 0b101_101_010_010_010_000_000U, 'Z' => 0b111_001_010_100_111_000_000U,
				':' => 0b000_010_000_010_000_000_000U, ';' => 0b000_010_000_000_010_010_100U,
				'.' => 0b000_000_000_000_010_000_000U, ',' => 0b000_000_000_000_010_010_100U,
				'!' => 0b010_010_010_000_010_000_000U, '?' => 0b111_001_011_000_010_000_000U,
				'-' => 0b000_000_111_000_000_000_000U, '+' => 0b000_010_111_010_000_000_000U,
				'_' => 0b000_000_000_000_000_111_000U, '*' => 0b000_101_010_101_000_000_000U,
				'#' => 0b101_111_101_111_101_000_000U, '=' => 0b000_111_000_111_000_000_000U,
				'(' => 0b001_010_010_010_010_001_000U, ')' => 0b100_010_010_010_010_100_000U,
				'[' => 0b011_010_010_010_010_011_000U, ']' => 0b110_010_010_010_010_110_000U,
				'|' => 0b010_010_010_010_010_010_000U, '/' => 0b001_001_010_010_100_100_000U,
				'\\' => 0b100_100_010_010_001_001_000U, '<' => 0b001_010_100_010_001_000_000U,
				'>' => 0b100_010_001_010_100_000_000U, '%' => 0b101_001_010_100_101_000_000U,
				_ => 0b000_010_101_010_000_000_000U
			}
		};

		static void EnsureDirectory(string filePath) {
			string? dir = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
		}
	}

	/// <summary>
	/// Self-contained GIF89a animated image recorder using a 256-color RGB332 palette and LZW compression.
	/// Saves multi-frame captures to <c>local/</c> by default.
	/// </summary>
	public sealed class GifRecorder {
		readonly List<byte[]> _indexedFrames = new List<byte[]>();
		public int Width { get; private set; }
		public int Height { get; private set; }
		public int PixelScale { get; }
		public int FrameCount => _indexedFrames.Count;

		public GifRecorder(int pixelScale = 4) {
			PixelScale = Math.Clamp(pixelScale, 1, 8);
		}

		public void CaptureFrame(Canvas canvas) {
			int w = canvas.Width * PixelScale;
			int h = canvas.Height * PixelScale;
			if (_indexedFrames.Count == 0) {
				Width = w;
				Height = h;
			} else if (w != Width || h != Height) {
				return;
			}

			var indices = new byte[Width * Height];
			for (int py = 0; py < canvas.Height; py++) {
				for (int px = 0; px < canvas.Width; px++) {
					Rgb c = canvas.Pixels[py * canvas.Width + px];
					byte idx = QuantizeRgb332(c);
					for (int sy = 0; sy < PixelScale; sy++) {
						int dstRow = (py * PixelScale + sy) * Width + px * PixelScale;
						for (int sx = 0; sx < PixelScale; sx++) indices[dstRow + sx] = idx;
					}
				}
			}

			int rows = canvas.Rows;
			if (PixelScale >= 4) {
				int boxW = PixelScale, boxH = 2 * PixelScale;
				for (int r = 0; r < rows; r++) {
					for (int c = 0; c < canvas.Width; c++) {
						char ch = canvas.TextChars[r * canvas.Width + c];
						if (ch == '\0' || ch == ' ') continue;
						byte fgIdx = QuantizeRgb332(canvas.TextColors[r * canvas.Width + c]);
						uint glyphMask = FrameExporter.GetMiniGlyph3x7(ch);
						int boxX = c * PixelScale, boxY = r * 2 * PixelScale;
						for (int gy = 0; gy < 7; gy++) {
							for (int gx = 0; gx < 3; gx++) {
								if (((glyphMask >> ((6 - gy) * 3 + (2 - gx))) & 1U) == 0) continue;
								int px0 = boxX + (gx * boxW) / 4, px1 = boxX + ((gx + 1) * boxW) / 4;
								int py0 = boxY + (gy * boxH) / 8, py1 = boxY + ((gy + 1) * boxH) / 8;
								for (int y = py0; y < py1 && y < Height; y++) {
									for (int x = px0; x < px1 && x < Width; x++) indices[y * Width + x] = fgIdx;
								}
							}
						}
					}
				}
			} else {
				for (int r = 0; r < rows; r++) {
					for (int c = 0; c < canvas.Width; c++) {
						if (canvas.TextChars[r * canvas.Width + c] == '\0') continue;
						byte fgIdx = QuantizeRgb332(canvas.TextColors[r * canvas.Width + c]);
						int cy = (r * 2) * PixelScale + PixelScale / 2;
						int cx = c * PixelScale + PixelScale / 2;
						if (cy < Height && cx < Width) indices[cy * Width + cx] = fgIdx;
					}
				}
			}
			_indexedFrames.Add(indices);
		}

		static byte QuantizeRgb332(Rgb c) {
			int r3 = (c.R * 7 + 127) / 255;
			int g3 = (c.G * 7 + 127) / 255;
			int b2 = (c.B * 3 + 127) / 255;
			return (byte)((r3 << 5) | (g3 << 2) | b2);
		}

		public string Save(string filePath = "local/capture.gif", int delayCentiseconds = 3) {
			string? dir = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

			using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
			using var bw = new BinaryWriter(fs);

			// Header: GIF89a + Logical Screen Descriptor (256-color Global Color Table = 0xF7)
			bw.Write(Encoding.ASCII.GetBytes("GIF89a"));
			bw.Write((ushort)Width);
			bw.Write((ushort)Height);
			bw.Write((byte)0xF7);
			bw.Write((byte)0);
			bw.Write((byte)0);

			// 256-entry RGB332 Global Color Table
			for (int i = 0; i < 256; i++) {
				byte r = (byte)(((i >> 5) & 7) * 255 / 7);
				byte g = (byte)(((i >> 2) & 7) * 255 / 7);
				byte b = (byte)((i & 3) * 255 / 3);
				bw.Write(r);
				bw.Write(g);
				bw.Write(b);
			}

			// Netscape 2.0 infinite loop application extension
			bw.Write(new byte[] {
				0x21, 0xFF, 0x0B,
				(byte)'N', (byte)'E', (byte)'T', (byte)'S', (byte)'C', (byte)'A', (byte)'P', (byte)'E',
				(byte)'2', (byte)'.', (byte)'0',
				0x03, 0x01, 0x00, 0x00, 0x00
			});

			for (int f = 0; f < _indexedFrames.Count; f++) {
				// Graphic Control Extension
				bw.Write((byte)0x21);
				bw.Write((byte)0xF9);
				bw.Write((byte)0x04);
				bw.Write((byte)0x00);
				bw.Write((ushort)Math.Max(1, delayCentiseconds));
				bw.Write((byte)0x00);
				bw.Write((byte)0x00);

				// Image Descriptor
				bw.Write((byte)0x2C);
				bw.Write((ushort)0);
				bw.Write((ushort)0);
				bw.Write((ushort)Width);
				bw.Write((ushort)Height);
				bw.Write((byte)0x00);

				// LZW Image Data (minCodeSize = 8)
				bw.Write((byte)8);
				WriteLzwSubBlocks(bw, _indexedFrames[f]);
			}

			// GIF Trailer
			bw.Write((byte)0x3B);
			return Path.GetFullPath(filePath);
		}

		// Standard GIF LZW encoder (minCodeSize=8, clearCode=256, eoiCode=257)
		static void WriteLzwSubBlocks(BinaryWriter bw, byte[] pixels) {
			const int ClearCode = 256, EoiCode = 257;
			int codeSize = 9, nextCode = 258;
			var dict = new Dictionary<int, int>(4096);
			var bytesOut = new List<byte>(pixels.Length);
			int bitBuf = 0, bitCount = 0;

			void EmitCode(int code, int bits) {
				bitBuf |= code << bitCount;
				bitCount += bits;
				while (bitCount >= 8) {
					bytesOut.Add((byte)(bitBuf & 0xFF));
					bitBuf >>= 8;
					bitCount -= 8;
				}
			}

			EmitCode(ClearCode, codeSize);
			if (pixels.Length > 0) {
				int prefix = pixels[0];
				for (int i = 1; i < pixels.Length; i++) {
					byte k = pixels[i];
					int key = (prefix << 8) | k;
					if (dict.TryGetValue(key, out int existingCode)) {
						prefix = existingCode;
					} else {
						EmitCode(prefix, codeSize);
						if (nextCode < 4096) {
							dict[key] = nextCode++;
							if (nextCode > (1 << codeSize) && codeSize < 12) codeSize++;
						} else {
							EmitCode(ClearCode, codeSize);
							dict.Clear();
							codeSize = 9;
							nextCode = 258;
						}
						prefix = k;
					}
				}
				EmitCode(prefix, codeSize);
			}
			EmitCode(EoiCode, codeSize);
			if (bitCount > 0) bytesOut.Add((byte)(bitBuf & 0xFF));

			int pos = 0;
			while (pos < bytesOut.Count) {
				int chunk = Math.Min(255, bytesOut.Count - pos);
				bw.Write((byte)chunk);
				for (int i = 0; i < chunk; i++) bw.Write(bytesOut[pos + i]);
				pos += chunk;
			}
			bw.Write((byte)0); // Block terminator
		}
	}
}
