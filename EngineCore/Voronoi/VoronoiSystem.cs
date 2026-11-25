#nullable enable
using System;
using System.Collections.Generic;
using ConsoleGraphicsEngine.Engine;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Rendering;
using ConsoleGraphicsEngine.UI;

namespace ConsoleGraphicsEngine.Voronoi {
	public sealed class VoronoiSeed {
		public Vec2 Position { get; set; }
		public Rgb Color { get; set; }
		public VoronoiSeed(Vec2 position, Rgb color) {
			Position = position; Color = color;
		}
	}

	/// <summary>Modal RGB/preset editor for selected <see cref="VoronoiSeed"/> backed by <see cref="UIPanel"/>, <see cref="UISlider"/>, <see cref="UIButton"/>.</summary>
	public sealed class ColorPickerUI {
		public bool IsOpen { get; private set; }
		/// <summary>0=R, 1=G, 2=B, 3=Presets.</summary>
		public int ActiveChannel { get; private set; }
		public int SelectedPresetIndex { get; private set; }

		public static readonly Rgb[] Presets = new Rgb[] {
			new Rgb(235, 75, 95),
			new Rgb(250, 155, 50),
			new Rgb(240, 225, 75),
			new Rgb(85, 215, 115),
			new Rgb(65, 200, 230),
			new Rgb(90, 125, 245),
			new Rgb(185, 95, 240),
			new Rgb(240, 115, 190)
		};

		readonly UIPanel _panel = new UIPanel("ColorPickerPanel", new ScreenRect(0, 0, 40, 12), "=== VORONOI REGION COLOR PICKER ===");
		readonly UISlider _sliderR = new UISlider("SliderR", new ScreenRect(0, 0, 36, 1), "R", 128, new Rgb(255, 110, 110));
		readonly UISlider _sliderG = new UISlider("SliderG", new ScreenRect(0, 0, 36, 1), "G", 128, new Rgb(110, 255, 130));
		readonly UISlider _sliderB = new UISlider("SliderB", new ScreenRect(0, 0, 36, 1), "B", 128, new Rgb(110, 175, 255));
		readonly UIButton _closeBtn = new UIButton("CloseBtn", new ScreenRect(0, 0, 14, 1), "[Close: Enter]");

		public Action? OnCloseRequested { get; set; }

		public ColorPickerUI() {
			_panel.AddChild(_sliderR);
			_panel.AddChild(_sliderG);
			_panel.AddChild(_sliderB);
			_panel.AddChild(_closeBtn);
			_closeBtn.OnClick = () => OnCloseRequested?.Invoke();
		}

		public void Open() { IsOpen = true; ActiveChannel = 0; }
		public void Close() => IsOpen = false;
		public void NextChannel() => ActiveChannel = (ActiveChannel + 1) % 4;
		public void PrevChannel() => ActiveChannel = (ActiveChannel + 3) % 4;

		public void AdjustValue(VoronoiSeed? seed, int delta) {
			if (seed == null) return;
			Rgb c = seed.Color;
			if (ActiveChannel == 0) seed.Color = new Rgb(Math.Clamp(c.R + delta, 0, 255), c.G, c.B);
			else if (ActiveChannel == 1) seed.Color = new Rgb(c.R, Math.Clamp(c.G + delta, 0, 255), c.B);
			else if (ActiveChannel == 2) seed.Color = new Rgb(c.R, c.G, Math.Clamp(c.B + delta, 0, 255));
			else {
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

		public bool HandlePointer(PointerInputEvent ev, VoronoiSeed? selectedSeed, int cellCols, int cellRows) {
			if (!IsOpen || selectedSeed == null) return false;
			UpdateLayoutAndBindSeed(selectedSeed, cellCols, cellRows);

			if (_sliderR.HandlePointer(ev)) { ActiveChannel = 0; return true; }
			if (_sliderG.HandlePointer(ev)) { ActiveChannel = 1; return true; }
			if (_sliderB.HandlePointer(ev)) { ActiveChannel = 2; return true; }
			if (_closeBtn.HandlePointer(ev)) return true;
			return _panel.Bounds.ContainsCell(ev.CellX, ev.CellY);
		}

		void UpdateLayoutAndBindSeed(VoronoiSeed selectedSeed, int cellCols, int cellRows) {
			int boxW = Math.Min(48, Math.Max(28, cellCols - 4));
			int boxH = Math.Min(13, Math.Max(9, cellRows - 4));
			int boxX = Math.Max(1, (cellCols - boxW) / 2);
			int boxY = Math.Max(1, (cellRows - boxH) / 2);

			_panel.Bounds = new ScreenRect(boxX, boxY, boxW, boxH);
			_sliderR.Bounds = new ScreenRect(boxX + 2, boxY + 2, boxW - 4, 1);
			_sliderG.Bounds = new ScreenRect(boxX + 2, boxY + 3, boxW - 4, 1);
			_sliderB.Bounds = new ScreenRect(boxX + 2, boxY + 4, boxW - 4, 1);
			_closeBtn.Bounds = new ScreenRect(boxX + (boxW - 16) / 2, boxY + boxH - 1, 16, 1);

			_sliderR.IsFocused = ActiveChannel == 0;
			_sliderG.IsFocused = ActiveChannel == 1;
			_sliderB.IsFocused = ActiveChannel == 2;

			_sliderR.Value = selectedSeed.Color.R;
			_sliderG.Value = selectedSeed.Color.G;
			_sliderB.Value = selectedSeed.Color.B;

			_sliderR.OnValueChanged = val => selectedSeed.Color = new Rgb(val, selectedSeed.Color.G, selectedSeed.Color.B);
			_sliderG.OnValueChanged = val => selectedSeed.Color = new Rgb(selectedSeed.Color.R, val, selectedSeed.Color.B);
			_sliderB.OnValueChanged = val => selectedSeed.Color = new Rgb(selectedSeed.Color.R, selectedSeed.Color.G, val);
		}

		public void Draw(IRenderBackend backend, VoronoiSeed? selectedSeed) {
			if (!IsOpen || selectedSeed == null) return;
			UpdateLayoutAndBindSeed(selectedSeed, backend.CellColumns, backend.CellRows);
			_panel.Draw(backend);

			int boxX = _panel.Bounds.X, boxY = _panel.Bounds.Y, boxW = _panel.Bounds.Width, boxH = _panel.Bounds.Height;
			string presetPrefix = ActiveChannel == 3 ? "> Presets: " : "  Presets: ";
			backend.DrawOverlayText(new ScreenRect(boxX + 2, boxY + 5, boxW - 4, 1),
				$"{presetPrefix}[1-8] or Left/Right (#{SelectedPresetIndex + 1})",
				ActiveChannel == 3 ? new Rgb(255, 240, 150) : new Rgb(190, 195, 215), TextAlign.Left);

			Rgb c = selectedSeed.Color;
			backend.DrawOverlayText(new ScreenRect(boxX + 2, boxY + 7, boxW - 4, 2),
				$"RGB({c.R}, {c.G}, {c.B})",
				c.Luminance > 140 ? new Rgb(15, 15, 25) : new Rgb(245, 245, 250), TextAlign.Center, c, 1.0f);

			backend.DrawOverlayText(new ScreenRect(boxX + 2, boxY + boxH - 3, boxW - 4, 2),
				"W/S or Click: channel | A/D or Drag: adjust | Enter/Click: close",
				new Rgb(180, 190, 220), TextAlign.Center, null, 0f, TextWrapMode.WordWrap);
		}
	}

	public sealed class VoronoiSystem {
		readonly List<VoronoiSeed> _seeds = new List<VoronoiSeed>();
		readonly Random _rng = new Random(42);
		public ColorPickerUI ColorPicker { get; } = new ColorPickerUI();
		public IReadOnlyList<VoronoiSeed> Seeds => _seeds;

		public VoronoiSystem() {
			_seeds.Add(new VoronoiSeed(new Vec2(-35f, 18f), new Rgb(65, 145, 225)));
			_seeds.Add(new VoronoiSeed(new Vec2(32f, 20f), new Rgb(220, 95, 115)));
			_seeds.Add(new VoronoiSeed(new Vec2(-25f, -22f), new Rgb(75, 195, 130)));
			_seeds.Add(new VoronoiSeed(new Vec2(28f, -18f), new Rgb(215, 165, 65)));
			_seeds.Add(new VoronoiSeed(new Vec2(0f, 4f), new Rgb(155, 105, 220)));
		}

		public VoronoiSeed AddSeedAt(Vec2 worldPos) {
			float hue = (float)(_rng.NextDouble() * 360.0);
			float sat = 0.65f + (float)_rng.NextDouble() * 0.30f;
			float val = 0.70f + (float)_rng.NextDouble() * 0.25f;
			Rgb randomColor = Rgb.FromHsv(hue, sat, val);

			// Coalesce near-identical seed coords to avoid zero-area cell artifacts
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

		public int GetRegionIndexAt(Vec2 worldPoint) {
			if (_seeds.Count == 0) return -1;
			int bestIdx = 0;
			float bestDist2 = float.PositiveInfinity;
			for (int i = 0; i < _seeds.Count; i++) {
				float d2 = (_seeds[i].Position - worldPoint).LengthSquared;
				if (d2 < bestDist2) { bestDist2 = d2; bestIdx = i; }
			}
			return bestIdx;
		}

		public VoronoiSeed? GetSelectedSeed(Vec2 cameraCenter) {
			int idx = GetRegionIndexAt(cameraCenter);
			return idx >= 0 ? _seeds[idx] : null;
		}

		public void Draw(IRenderBackend backend, Camera camera, long currentFrame) {
			int w = backend.PixelWidth, h = backend.PixelHeight;
			Transform2D view = camera.GetViewTransform(w, h);
			Transform2D pixelToWorld = view.Inverse();
			backend.ViewTransform = view;

			int selectedIdx = GetRegionIndexAt(camera.Position);
			float pulse = 0.5f + 0.5f * MathF.Sin(currentFrame * 0.16f);
			float flashAmount = 0.10f + 0.16f * pulse;

			int seedCount = _seeds.Count;
			if (seedCount == 0) {
				backend.Clear(new Rgb(12, 12, 20));
				return;
			}

			Span<Vec2> seedPos = seedCount <= 128 ? stackalloc Vec2[seedCount] : new Vec2[seedCount];
			Span<Rgb> seedCol = seedCount <= 128 ? stackalloc Rgb[seedCount] : new Rgb[seedCount];
			for (int i = 0; i < seedCount; i++) {
				seedPos[i] = _seeds[i].Position;
				Rgb baseCol = _seeds[i].Color;
				seedCol[i] = i == selectedIdx ? Rgb.Lerp(baseCol, new Rgb(255, 255, 255), flashAmount) : baseCol;
			}

			Canvas? swCanvas = backend.SoftwareCanvas;
			if (swCanvas != null) {
				Rgb[] pixels = swCanvas.Pixels;
				Array.Clear(swCanvas.TextChars, 0, swCanvas.TextChars.Length);

				float m11 = pixelToWorld.M11, m12 = pixelToWorld.M12, tx = pixelToWorld.Tx;
				float m21 = pixelToWorld.M21, m22 = pixelToWorld.M22, ty = pixelToWorld.Ty;

				for (int py = 0; py < h; py++) {
					float fy = py + 0.5f;
					float wxBase = m12 * fy + tx + m11 * 0.5f;
					float wyBase = m22 * fy + ty + m21 * 0.5f;
					int rowOffset = py * w;

					for (int px = 0; px < w; px++) {
						float wx = wxBase + m11 * px, wy = wyBase + m21 * px;
						int best = 0;
						float bestD2 = float.PositiveInfinity, secondD2 = float.PositiveInfinity;
						for (int i = 0; i < seedCount; i++) {
							float dx = wx - seedPos[i].X, dy = wy - seedPos[i].Y;
							float d2 = dx * dx + dy * dy;
							if (d2 < bestD2) {
								secondD2 = bestD2; bestD2 = d2; best = i;
							} else if (d2 < secondD2) {
								secondD2 = d2;
							}
						}

						Rgb col = seedCol[best];
						if (seedCount > 1) {
							float d1 = MathF.Sqrt(bestD2), d2 = MathF.Sqrt(secondD2);
							if (d2 - d1 < 1.2f / MathF.Max(0.1f, camera.Zoom)) {
								col = Rgb.Lerp(col, new Rgb(12, 14, 24), 0.55f);
							}
						}
						pixels[rowOffset + px] = col;
					}
				}
			} else {
				backend.Clear(new Rgb(16, 18, 32));
			}

			float dotRadius = 2.2f / MathF.Max(0.1f, camera.Zoom);
			for (int i = 0; i < seedCount; i++) {
				Rgb ringColor = i == selectedIdx ? new Rgb(255, 255, 255) : new Rgb(15, 15, 25);
				backend.FillCircle(_seeds[i].Position, dotRadius * 1.4f, ringColor);
				backend.FillCircle(_seeds[i].Position, dotRadius * 0.85f, _seeds[i].Color);
			}

			float retSize = 3.5f / MathF.Max(0.1f, camera.Zoom);
			float retThick = 1.1f / MathF.Max(0.1f, camera.Zoom);
			backend.DrawLine(camera.Position - new Vec2(retSize, 0), camera.Position + new Vec2(retSize, 0), retThick, new Rgb(255, 255, 255));
			backend.DrawLine(camera.Position - new Vec2(0, retSize), camera.Position + new Vec2(0, retSize), retThick, new Rgb(255, 255, 255));

			if (ColorPicker.IsOpen && selectedIdx >= 0) {
				ColorPicker.Draw(backend, _seeds[selectedIdx]);
			}
		}
	}
}
