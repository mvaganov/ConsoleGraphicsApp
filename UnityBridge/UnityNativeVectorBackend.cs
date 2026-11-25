#nullable enable
using System;
using System.Collections.Generic;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Rendering;

namespace ConsoleGraphicsEngine.UnityBridge {
	public enum VectorCommandType { Circle, Polygon, Line, WorldText, OverlayText }

	public readonly struct RecordedDrawCommand {
		public readonly VectorCommandType Type;
		public readonly Vec2 PositionA, PositionB;
		public readonly float RadiusOrThickness;
		public readonly Vec2[]? Vertices;
		public readonly Rgb Color;
		public readonly Rgb? Backdrop;
		public readonly float BackdropOpacity;
		public readonly string? Text;
		public readonly ScreenRect OverlayRect;
		public readonly TextAlign Align;
		public readonly TextWrapMode WrapMode;

		public RecordedDrawCommand(VectorCommandType type, Vec2 posA, Vec2 posB, float radiusOrThickness,
								   Vec2[]? vertices, Rgb color, Rgb? backdrop, float backdropOpacity,
								   string? text, ScreenRect overlayRect, TextAlign align, TextWrapMode wrapMode) {
			Type = type; PositionA = posA; PositionB = posB; RadiusOrThickness = radiusOrThickness;
			Vertices = vertices; Color = color; Backdrop = backdrop; BackdropOpacity = backdropOpacity;
			Text = text; OverlayRect = overlayRect; Align = align; WrapMode = wrapMode;
		}
	}

	/// <summary>Records structured <see cref="RecordedDrawCommand"/>s for GPU vector/mesh rendering without pixel rasterization.</summary>
	public sealed class UnityNativeVectorBackend : IRenderBackend {
		public int PixelWidth { get; private set; }
		public int PixelHeight { get; private set; }
		public int CellColumns => PixelWidth;
		public int CellRows => PixelHeight / 2;
		public int SuperSampling { get; set; } = 1;
		public Transform2D ViewTransform { get; set; } = Transform2D.Identity;
		public Rgb ClearColor { get; private set; }
		public Canvas? SoftwareCanvas => null;

		readonly Stack<Transform2D> _stack = new Stack<Transform2D>();
		readonly List<RecordedDrawCommand> _commands = new List<RecordedDrawCommand>(256);
		public IReadOnlyList<RecordedDrawCommand> Commands => _commands;

		public UnityNativeVectorBackend(int logicalWidth = 160, int logicalHeight = 96) {
			PixelWidth = Math.Max(2, logicalWidth);
			PixelHeight = Math.Max(2, logicalHeight & ~1);
		}

		public void Clear(Rgb color) {
			ClearColor = color;
			_commands.Clear();
		}

		public void PushTransform(Transform2D transform) {
			_stack.Push(ViewTransform);
			ViewTransform = ViewTransform * transform;
		}

		public void PopTransform() => ViewTransform = _stack.Pop();

		public void FillCircle(Vec2 center, float radius, Rgb color) =>
			_commands.Add(new RecordedDrawCommand(VectorCommandType.Circle, center, Vec2.Zero, radius,
				null, color, null, 0f, null, default, TextAlign.Center, TextWrapMode.None));

		public void FillPolygon(IReadOnlyList<Vec2> vertices, Rgb color) {
			if (vertices.Count < 3) return;
			var copy = new Vec2[vertices.Count];
			for (int i = 0; i < vertices.Count; i++) copy[i] = vertices[i];
			_commands.Add(new RecordedDrawCommand(VectorCommandType.Polygon, Vec2.Zero, Vec2.Zero, 0f,
				copy, color, null, 0f, null, default, TextAlign.Center, TextWrapMode.None));
		}

		public void FillRect(Vec2 min, Vec2 max, Rgb color) =>
			FillPolygon(new[] { min, new Vec2(max.X, min.Y), max, new Vec2(min.X, max.Y) }, color);

		public void DrawLine(Vec2 a, Vec2 b, float thickness, Rgb color) =>
			_commands.Add(new RecordedDrawCommand(VectorCommandType.Line, a, b, thickness,
				null, color, null, 0f, null, default, TextAlign.Center, TextWrapMode.None));

		public void DrawText(Vec2 worldPos, string text, TextAlign align = TextAlign.Center,
							 Rgb? backdrop = null, float backdropOpacity = 0.7f) =>
			DrawText(worldPos, text, new Rgb(245, 245, 250), align, backdrop, backdropOpacity);

		public void DrawText(Vec2 worldPos, string text, Rgb color, TextAlign align = TextAlign.Center,
							 Rgb? backdrop = null, float backdropOpacity = 0.7f) {
			if (string.IsNullOrEmpty(text)) return;
			_commands.Add(new RecordedDrawCommand(VectorCommandType.WorldText, worldPos, Vec2.Zero, 0f,
				null, color, backdrop, backdropOpacity, text, default, align, TextWrapMode.None));
		}

		public void DrawOverlayText(ScreenRect rect, string text, Rgb color,
									TextAlign align = TextAlign.Left,
									Rgb? backdrop = null, float backdropOpacity = 0.7f,
									TextWrapMode wrapMode = TextWrapMode.WordWrap) =>
			_commands.Add(new RecordedDrawCommand(VectorCommandType.OverlayText, Vec2.Zero, Vec2.Zero, 0f,
				null, color, backdrop, backdropOpacity, text, rect, align, wrapMode));

#if UNITY_5_3_OR_NEWER || UNITY_2017_1_OR_NEWER
		public void RenderOnGUI() {
			float cellW = UnityEngine.Screen.width / (float)Math.Max(1, CellColumns);
			float cellH = UnityEngine.Screen.height / (float)Math.Max(1, CellRows);

			for (int i = 0; i < _commands.Count; i++) {
				RecordedDrawCommand cmd = _commands[i];
				if (cmd.Type == VectorCommandType.OverlayText && !string.IsNullOrEmpty(cmd.Text)) {
					UnityEngine.Rect r = new UnityEngine.Rect(
						cmd.OverlayRect.X * cellW, cmd.OverlayRect.Y * cellH,
						cmd.OverlayRect.Width * cellW, cmd.OverlayRect.Height * cellH);
					UnityEngine.GUI.color = new UnityEngine.Color32(cmd.Color.R, cmd.Color.G, cmd.Color.B, 255);
					UnityEngine.GUI.Label(r, cmd.Text);
				}
			}
		}
#endif
	}
}
