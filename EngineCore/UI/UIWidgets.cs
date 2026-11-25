#nullable enable
using System;
using System.Collections.Generic;
using ConsoleGraphicsEngine.Engine;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Rendering;

namespace ConsoleGraphicsEngine.UI {
	/// <summary>Cell-grid UI widget supporting pointer hit-testing (<see cref="PointerInputEvent"/>) and keyboard focus.</summary>
	public abstract class UIWidget {
		public string Id { get; }
		public ScreenRect Bounds { get; set; }
		public bool IsVisible { get; set; } = true;
		public bool IsFocused { get; set; }
		public bool IsHovered { get; set; }

		protected UIWidget(string id, ScreenRect bounds) {
			Id = id; Bounds = bounds;
		}

		public virtual bool HandlePointer(PointerInputEvent ev) {
			if (!IsVisible) return false;
			IsHovered = Bounds.ContainsCell(ev.CellX, ev.CellY);
			return IsHovered;
		}

		public abstract void Draw(IRenderBackend backend);
	}

	public sealed class UIButton : UIWidget {
		public string Label { get; set; }
		public Rgb TextColor { get; set; } = new Rgb(245, 245, 255);
		public Rgb BackgroundColor { get; set; } = new Rgb(35, 45, 78);
		public Rgb HighlightColor { get; set; } = new Rgb(70, 95, 165);
		public Action? OnClick { get; set; }

		public UIButton(string id, ScreenRect bounds, string label, Action? onClick = null) : base(id, bounds) {
			Label = label; OnClick = onClick;
		}

		public override bool HandlePointer(PointerInputEvent ev) {
			if (!IsVisible) return false;
			IsHovered = Bounds.ContainsCell(ev.CellX, ev.CellY);
			if (IsHovered && ev.Type == PointerEventType.Down && ev.Button == PointerButton.Left) {
				OnClick?.Invoke();
				return true;
			}
			return false;
		}

		public override void Draw(IRenderBackend backend) {
			if (!IsVisible) return;
			Rgb bg = (IsFocused || IsHovered) ? HighlightColor : BackgroundColor;
			Rgb fg = (IsFocused || IsHovered) ? new Rgb(255, 240, 150) : TextColor;
			backend.DrawOverlayText(Bounds, Label, fg, TextAlign.Center, bg, 0.9f, TextWrapMode.None);
		}
	}

	/// <summary>0..255 horizontal slider; track starts at <c>Bounds.X + 6</c> with width <c>Bounds.Width - 14</c>.</summary>
	public sealed class UISlider : UIWidget {
		public string Label { get; set; }
		public byte Value { get; set; }
		public Rgb AccentColor { get; set; }
		public Action<byte>? OnValueChanged { get; set; }

		public UISlider(string id, ScreenRect bounds, string label, byte initialValue, Rgb accentColor, Action<byte>? onValueChanged = null)
			: base(id, bounds) {
			Label = label; Value = initialValue; AccentColor = accentColor; OnValueChanged = onValueChanged;
		}

		public override bool HandlePointer(PointerInputEvent ev) {
			if (!IsVisible) return false;
			IsHovered = Bounds.ContainsCell(ev.CellX, ev.CellY);
			if (IsHovered && (ev.Type == PointerEventType.Down || ev.Type == PointerEventType.Drag) && ev.Button == PointerButton.Left) {
				IsFocused = true;
				int barStartCol = Bounds.X + 6;
				int barWidth = Math.Max(8, Bounds.Width - 14);
				float t = (ev.CellX - barStartCol) / (float)Math.Max(1, barWidth - 1);
				byte newVal = (byte)Math.Clamp((int)Math.Round(t * 255f), 0, 255);
				if (newVal != Value) {
					Value = newVal;
					OnValueChanged?.Invoke(Value);
				}
				return true;
			}
			return false;
		}

		public void Adjust(int delta) {
			byte newVal = (byte)Math.Clamp(Value + delta, 0, 255);
			if (newVal != Value) {
				Value = newVal;
				OnValueChanged?.Invoke(Value);
			}
		}

		public override void Draw(IRenderBackend backend) {
			if (!IsVisible) return;
			int barWidth = Math.Max(8, Bounds.Width - 14);
			int filled = (int)Math.Round(Value / 255.0 * barWidth);
			string bar = new string('#', filled) + new string('-', barWidth - filled);
			string prefix = IsFocused ? "> " : "  ";
			Rgb textColor = (IsFocused || IsHovered) ? new Rgb(255, 255, 255) : AccentColor;
			Rgb? bg = (IsFocused || IsHovered) ? new Rgb(40, 48, 82) : (Rgb?)null;
			backend.DrawOverlayText(Bounds, $"{prefix}{Label}: [{bar}] {Value,3}", textColor, TextAlign.Left, bg, 0.85f, TextWrapMode.None);
		}
	}

	public sealed class UIPanel : UIWidget {
		public string Title { get; set; }
		public Rgb BackdropColor { get; set; } = new Rgb(14, 16, 30);
		public float BackdropOpacity { get; set; } = 0.92f;
		readonly List<UIWidget> _children = new List<UIWidget>();
		public IReadOnlyList<UIWidget> Children => _children;

		public UIPanel(string id, ScreenRect bounds, string title = "") : base(id, bounds) {
			Title = title;
		}

		public void ClearChildren() => _children.Clear();

		public T AddChild<T>(T widget) where T : UIWidget {
			_children.Add(widget);
			return widget;
		}

		public override bool HandlePointer(PointerInputEvent ev) {
			if (!IsVisible) return false;
			bool handled = false;
			for (int i = 0; i < _children.Count; i++) {
				if (_children[i].HandlePointer(ev)) handled = true;
			}
			return handled || Bounds.ContainsCell(ev.CellX, ev.CellY);
		}

		public override void Draw(IRenderBackend backend) {
			if (!IsVisible) return;
			backend.DrawOverlayText(Bounds, "", new Rgb(240, 240, 255), TextAlign.Left, BackdropColor, BackdropOpacity);
			if (!string.IsNullOrEmpty(Title)) {
				var headerRect = new ScreenRect(Bounds.X + 1, Bounds.Y, Math.Max(1, Bounds.Width - 2), 1);
				backend.DrawOverlayText(headerRect, Title, new Rgb(255, 220, 110), TextAlign.Center);
			}
			for (int i = 0; i < _children.Count; i++) _children[i].Draw(backend);
		}
	}
}
