#nullable enable
using System;
using System.Collections.Generic;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Rendering;

namespace ConsoleGraphicsEngine.Voronoi;

public sealed class VoronoiSeed {
	public Vec2 Position { get; set; }
	public Rgb Color { get; set; }
	public VoronoiSeed(Vec2 position, Rgb color) {
		Position = position;
		Color = color;
	}
}

/// <summary>
/// Interactive modal Color Picker UI for modifying the selected Voronoi region's RGB color.
/// Drawn using modular <see cref="Canvas.DrawOverlayText"/> rectangular UI regions.
/// </summary>
public sealed class ColorPickerUI {
	public bool IsOpen { get; private set; }
	/// <summary>0 = R, 1 = G, 2 = B, 3 = Preset Swatches.</summary>
	public int ActiveChannel { get; private set; }
	public int SelectedPresetIndex { get; private set; }

	public static readonly Rgb[] Presets = new Rgb[] {
		new Rgb(235, 75, 95),   // Crimson
		new Rgb(250, 155, 50),  // Amber
		new Rgb(240, 225, 75),  // Gold
		new Rgb(85, 215, 115),  // Emerald
		new Rgb(65, 200, 230),  // Cyan
		new Rgb(90, 125, 245),  // Cobalt
		new Rgb(185, 95, 240),  // Violet
		new Rgb(240, 115, 190)  // Rose
	};

	public void Open() {
		IsOpen = true;
		ActiveChannel = 0;
	}

	public void Close() {
		IsOpen = false;
	}

	public void NextChannel() => ActiveChannel = (ActiveChannel + 1) % 4;
	public void PrevChannel() => ActiveChannel = (ActiveChannel + 3) % 4;

	public void AdjustValue(VoronoiSeed? seed, int delta) {
		if (seed == null) return;
		Rgb c = seed.Color;
		if (ActiveChannel == 0) {
			seed.Color = new Rgb(Math.Clamp(c.R + delta, 0, 255), c.G, c.B);
		} else if (ActiveChannel == 1) {
			seed.Color = new Rgb(c.R, Math.Clamp(c.G + delta, 0, 255), c.B);
		} else if (ActiveChannel == 2) {
			seed.Color = new Rgb(c.R, c.G, Math.Clamp(c.B + delta, 0, 255));
		} else {
			int step = delta > 0 ? 1 : -1;
			SelectedPresetIndex = (SelectedPresetIndex + step + Presets.Length) % Presets.Length;
			seed.Color = Presets[SelectedPresetIndex];
		}
	}

	public void ApplyPreset(VoronoiSeed? seed, int presetIndex) {
		if (seed == null || presetIndex < 0 || presetIndex >= Presets.Length) return;
		SelectedPresetIndex = presetIndex;
		seed.Color = Presets[presetIndex];
	}

	public void Draw(Canvas canvas, VoronoiSeed? selectedSeed) {
		if (!IsOpen || selectedSeed == null) return;

		int boxW = Math.Min(46, Math.Max(28, canvas.Width - 4));
		int boxH = Math.Min(12, Math.Max(8, canvas.Rows - 4));
		int boxX = Math.Max(1, (canvas.Width - boxW) / 2);
		int boxY = Math.Max(1, (canvas.Rows - boxH) / 2);

		var outerRect = new ScreenRect(boxX, boxY, boxW, boxH);
		canvas.DrawOverlayText(outerRect, "", new Rgb(240, 240, 255), TextAlign.Left, new Rgb(14, 16, 30), 0.92f);

		var headerRect = new ScreenRect(boxX + 1, boxY, boxW - 2, 1);
		canvas.DrawOverlayText(headerRect, "=== VORONOI REGION COLOR PICKER ===", new Rgb(255, 220, 110), TextAlign.Center);

		Rgb c = selectedSeed.Color;
		DrawSliderRow(canvas, boxX + 2, boxY + 2, boxW - 4, "R", c.R, new Rgb(255, 110, 110), ActiveChannel == 0);
		DrawSliderRow(canvas, boxX + 2, boxY + 3, boxW - 4, "G", c.G, new Rgb(110, 255, 130), ActiveChannel == 1);
		DrawSliderRow(canvas, boxX + 2, boxY + 4, boxW - 4, "B", c.B, new Rgb(110, 175, 255), ActiveChannel == 2);

		string presetPrefix = ActiveChannel == 3 ? "> Presets: " : "  Presets: ";
		var presetRect = new ScreenRect(boxX + 2, boxY + 5, boxW - 4, 1);
		canvas.DrawOverlayText(presetRect, $"{presetPrefix}[1-8] or Left/Right (#{SelectedPresetIndex + 1})",
			ActiveChannel == 3 ? new Rgb(255, 240, 150) : new Rgb(190, 195, 215), TextAlign.Left);

		// Color preview swatch box
		var swatchRect = new ScreenRect(boxX + 2, boxY + 7, boxW - 4, 2);
		canvas.DrawOverlayText(swatchRect, $"RGB({c.R}, {c.G}, {c.B})",
			c.Luminance > 140 ? new Rgb(15, 15, 25) : new Rgb(245, 245, 250),
			TextAlign.Center, c, 1.0f);

		var footerRect = new ScreenRect(boxX + 2, boxY + boxH - 2, boxW - 4, 2);
		canvas.DrawOverlayText(footerRect,
			"Up/Down or W/S: channel | Left/Right or A/D: adjust | Enter: close",
			new Rgb(180, 190, 220), TextAlign.Center, null, 0f, TextWrapMode.WordWrap);
	}

	static void DrawSliderRow(Canvas canvas, int x, int y, int width, string label, byte value, Rgb labelColor, bool selected) {
		int barWidth = Math.Max(8, width - 14);
		int filled = (int)Math.Round(value / 255.0 * barWidth);
		string bar = new string('#', filled) + new string('-', barWidth - filled);
		string prefix = selected ? "> " : "  ";
		string line = $"{prefix}{label}: [{bar}] {value,3}";
		var rect = new ScreenRect(x, y, width, 1);
		Rgb textColor = selected ? new Rgb(255, 255, 255) : labelColor;
		Rgb? bg = selected ? new Rgb(40, 48, 82) : null;
		canvas.DrawOverlayText(rect, line, textColor, TextAlign.Left, bg, 0.85f, TextWrapMode.None);
	}
}

/// <summary>
/// Voronoi diagram state and renderer. Uses the same <see cref="Camera"/> (movement, rotation, zoom)
/// as the physics world, selects the Voronoi cell under the camera center, flashes it subtly,
/// allows adding new seeds at the camera center with Space, and opens the ColorPickerUI with Enter.
/// </summary>
public sealed class VoronoiSystem {
	readonly List<VoronoiSeed> _seeds = new();
	readonly Random _rng = new Random(42);
	public ColorPickerUI ColorPicker { get; } = new ColorPickerUI();
	public IReadOnlyList<VoronoiSeed> Seeds => _seeds;

	public VoronoiSystem() {
		// Initial default Voronoi seeds around the origin
		_seeds.Add(new VoronoiSeed(new Vec2(-35f, 18f), new Rgb(65, 145, 225)));
		_seeds.Add(new VoronoiSeed(new Vec2(32f, 20f), new Rgb(220, 95, 115)));
		_seeds.Add(new VoronoiSeed(new Vec2(-25f, -22f), new Rgb(75, 195, 130)));
		_seeds.Add(new VoronoiSeed(new Vec2(28f, -18f), new Rgb(215, 165, 65)));
		_seeds.Add(new VoronoiSeed(new Vec2(0f, 4f), new Rgb(155, 105, 220)));
	}

	/// <summary>Adds a new Voronoi seed point at the given world position with a vibrant random color.</summary>
	public VoronoiSeed AddSeedAt(Vec2 worldPos) {
		float hue = (float)(_rng.NextDouble() * 360.0);
		float sat = 0.65f + (float)_rng.NextDouble() * 0.30f;
		float val = 0.70f + (float)_rng.NextDouble() * 0.25f;
		Rgb randomColor = Rgb.FromHsv(hue, sat, val);

		// If a seed is already nearly at the exact same position, update its color instead of stacking duplicates
		for (int i = 0; i < _seeds.Count; i++) {
			if ((_seeds[i].Position - worldPos).LengthSquared < 0.05f) {
				_seeds[i].Color = randomColor;
				return _seeds[i];
			}
		}

		var seed = new VoronoiSeed(worldPos, randomColor);
		_seeds.Add(seed);
		return seed;
	}

	/// <summary>Returns the index of the Voronoi region closest to the given world point (e.g. camera.Position).</summary>
	public int GetRegionIndexAt(Vec2 worldPoint) {
		if (_seeds.Count == 0) return -1;
		int bestIdx = 0;
		float bestDist2 = float.PositiveInfinity;
		for (int i = 0; i < _seeds.Count; i++) {
			float d2 = (_seeds[i].Position - worldPoint).LengthSquared;
			if (d2 < bestDist2) {
				bestDist2 = d2;
				bestIdx = i;
			}
		}
		return bestIdx;
	}

	public VoronoiSeed? GetSelectedSeed(Vec2 cameraCenter) {
		int idx = GetRegionIndexAt(cameraCenter);
		return idx >= 0 ? _seeds[idx] : null;
	}

	/// <summary>
	/// Renders the Voronoi diagram into the canvas using the active camera view transform,
	/// subtly flashing the selected region that the center of the camera is looking at.
	/// </summary>
	public void Draw(Canvas canvas, Camera camera, long currentFrame) {
		int w = canvas.Width;
		int h = canvas.Height;
		Transform2D view = camera.GetViewTransform(w, h);
		Transform2D pixelToWorld = view.Inverse();
		canvas.Transform = view;

		int selectedIdx = GetRegionIndexAt(camera.Position);
		// Subtle sine-wave flash at ~2 Hz
		float pulse = 0.5f + 0.5f * MathF.Sin(currentFrame * 0.16f);
		float flashAmount = 0.10f + 0.16f * pulse; // blends 10%..26% toward white

		int seedCount = _seeds.Count;
		if (seedCount == 0) {
			canvas.Clear(new Rgb(12, 12, 20));
			return;
		}

		// Pre-extract positions and display colors for tight inner pixel loop
		Span<Vec2> seedPos = seedCount <= 128 ? stackalloc Vec2[seedCount] : new Vec2[seedCount];
		Span<Rgb> seedCol = seedCount <= 128 ? stackalloc Rgb[seedCount] : new Rgb[seedCount];
		for (int i = 0; i < seedCount; i++) {
			seedPos[i] = _seeds[i].Position;
			Rgb baseCol = _seeds[i].Color;
			if (i == selectedIdx) {
				seedCol[i] = Rgb.Lerp(baseCol, new Rgb(255, 255, 255), flashAmount);
			} else {
				seedCol[i] = baseCol;
			}
		}

		Rgb[] pixels = canvas.Pixels;
		Array.Clear(canvas.TextChars, 0, canvas.TextChars.Length);

		// Incremental row traversal using affine pixelToWorld columns
		float m11 = pixelToWorld.M11, m12 = pixelToWorld.M12, tx = pixelToWorld.Tx;
		float m21 = pixelToWorld.M21, m22 = pixelToWorld.M22, ty = pixelToWorld.Ty;

		for (int py = 0; py < h; py++) {
			float fy = py + 0.5f;
			float wxBase = m12 * fy + tx + m11 * 0.5f;
			float wyBase = m22 * fy + ty + m21 * 0.5f;
			int rowOffset = py * w;

			for (int px = 0; px < w; px++) {
				float wx = wxBase + m11 * px;
				float wy = wyBase + m21 * px;

				int best = 0;
				float bestD2 = float.PositiveInfinity;
				float secondD2 = float.PositiveInfinity;
				for (int i = 0; i < seedCount; i++) {
					float dx = wx - seedPos[i].X;
					float dy = wy - seedPos[i].Y;
					float d2 = dx * dx + dy * dy;
					if (d2 < bestD2) {
						secondD2 = bestD2;
						bestD2 = d2;
						best = i;
					} else if (d2 < secondD2) {
						secondD2 = d2;
					}
				}

				Rgb col = seedCol[best];
				// Subtle dark Voronoi cell border where distance to 1st and 2nd nearest seeds is nearly equal
				if (seedCount > 1) {
					float d1 = MathF.Sqrt(bestD2);
					float d2 = MathF.Sqrt(secondD2);
					float edgeWorldThickness = 1.2f / MathF.Max(0.1f, camera.Zoom);
					if (d2 - d1 < edgeWorldThickness) {
						col = Rgb.Lerp(col, new Rgb(12, 14, 24), 0.55f);
					}
				}
				pixels[rowOffset + px] = col;
			}
		}

		// Draw seed dots in world space
		float dotRadius = 2.2f / MathF.Max(0.1f, camera.Zoom);
		for (int i = 0; i < seedCount; i++) {
			Rgb ringColor = i == selectedIdx ? new Rgb(255, 255, 255) : new Rgb(15, 15, 25);
			canvas.FillCircle(_seeds[i].Position, dotRadius * 1.4f, ringColor);
			canvas.FillCircle(_seeds[i].Position, dotRadius * 0.85f, _seeds[i].Color);
		}

		// Draw camera center reticle
		float retSize = 3.5f / MathF.Max(0.1f, camera.Zoom);
		float retThick = 1.1f / MathF.Max(0.1f, camera.Zoom);
		canvas.DrawLine(camera.Position - new Vec2(retSize, 0), camera.Position + new Vec2(retSize, 0), retThick, new Rgb(255, 255, 255));
		canvas.DrawLine(camera.Position - new Vec2(0, retSize), camera.Position + new Vec2(0, retSize), retThick, new Rgb(255, 255, 255));

		// Draw ColorPicker modal if open
		if (ColorPicker.IsOpen && selectedIdx >= 0) {
			ColorPicker.Draw(canvas, _seeds[selectedIdx]);
		}
	}
}
