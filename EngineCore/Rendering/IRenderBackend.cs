#nullable enable
using System.Collections.Generic;
using ConsoleGraphicsEngine.MathCore;

namespace ConsoleGraphicsEngine.Rendering {
	public enum TextAlign { Left, Center, Right }
	public enum TextWrapMode { None, CharWrap, WordWrap }

	/// <summary>
	/// Host-agnostic draw contract. Implemented by <see cref="CanvasRenderBackend"/> (software buffer for CLI &amp; Unity Texture2D blit)
	/// and <c>UnityNativeVectorBackend</c> (structured vector commands for GPU meshes/TextMeshPro).
	/// </summary>
	public interface IRenderBackend {
		int PixelWidth { get; }
		int PixelHeight { get; }
		int CellColumns { get; }
		int CellRows { get; }
		int SuperSampling { get; set; }
		Transform2D ViewTransform { get; set; }

		void Clear(Rgb color);
		void PushTransform(Transform2D transform);
		void PopTransform();

		void FillCircle(Vec2 center, float radius, Rgb color);
		void FillPolygon(IReadOnlyList<Vec2> vertices, Rgb color);
		void FillRect(Vec2 min, Vec2 max, Rgb color);
		void DrawLine(Vec2 a, Vec2 b, float thickness, Rgb color);

		void DrawText(Vec2 worldPos, string text, TextAlign align = TextAlign.Center,
					  Rgb? backdrop = null, float backdropOpacity = 0.7f);
		void DrawText(Vec2 worldPos, string text, Rgb color, TextAlign align = TextAlign.Center,
					  Rgb? backdrop = null, float backdropOpacity = 0.7f);

		void DrawOverlayText(ScreenRect rect, string text, Rgb color,
							 TextAlign align = TextAlign.Left,
							 Rgb? backdrop = null, float backdropOpacity = 0.7f,
							 TextWrapMode wrapMode = TextWrapMode.WordWrap);

		Canvas? SoftwareCanvas { get; }
	}

	/// <summary>World space (+X right, +Y up) &lt;-&gt; screen pixel/cell space (+X right, +Y down).</summary>
	public sealed class Camera {
		public Vec2 Position;
		public Vec2 PivotPoint = new Vec2(0.5f, 0.5f);
		public float Angle;
		public float Zoom = 1f;

		public Transform2D GetViewTransform(int pixelWidth, int pixelHeight) =>
			Transform2D.Translation(pixelWidth * PivotPoint.X, pixelHeight * PivotPoint.Y)
			* Transform2D.Scale(Zoom, -Zoom)
			* Transform2D.Rotation(Angle)
			* Transform2D.Translation(-Position.X, -Position.Y);

		public Vec2 ScreenPixelToWorld(Vec2 pixelPos, int pixelWidth, int pixelHeight) =>
			GetViewTransform(pixelWidth, pixelHeight).Inverse().Apply(pixelPos);

		public Vec2 ScreenCellToWorld(int cellX, int cellY, int pixelWidth, int pixelHeight) =>
			ScreenPixelToWorld(new Vec2(cellX + 0.5f, cellY * 2f + 1.0f), pixelWidth, pixelHeight);
	}
}
