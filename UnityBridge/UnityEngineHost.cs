#nullable enable
// Drop-in Unity MonoBehaviour bridge: copy/symlink `EngineCore/` + `UnityBridge/` into Unity `Assets/Scripts/`.
// Supports both `RetroPixelCanvas` (Texture2D blit) and `NativeHighResVector` rendering modes + procedural AudioClip synth.
#if UNITY_5_3_OR_NEWER || UNITY_2017_1_OR_NEWER
using System;
using UnityEngine;
using ConsoleGraphicsEngine.Engine;
using ConsoleGraphicsEngine.MathCore;
using ConsoleGraphicsEngine.Rendering;

namespace ConsoleGraphicsEngine.UnityBridge {
	public enum UnityRenderMode { RetroPixelCanvas, NativeHighResVector }

	public sealed class UnityEngineHost : MonoBehaviour, IAudioBackend {
		[Header("Display & Simulation Settings")]
		public UnityRenderMode RenderMode = UnityRenderMode.RetroPixelCanvas;
		public bool DeterministicFixedPoint = true;
		public int RetroPixelWidth = 160;
		public int RetroPixelHeight = 96;
		public int TargetFps = 60;

		public GameApplication App { get; private set; } = null!;
		private Texture2D? _retroTexture;
		private Color32[]? _colorBuffer;
		private CanvasRenderBackend? _canvasBackend;
		private UnityNativeVectorBackend? _vectorBackend;
		private AudioSource? _audioSource;
		private float _accumulator;

		void Awake() {
			Time.fixedDeltaTime = 1f / Mathf.Max(1, TargetFps);
			FNum.Deterministic = DeterministicFixedPoint;

			_canvasBackend = new CanvasRenderBackend(RetroPixelWidth, RetroPixelHeight);
			_vectorBackend = new UnityNativeVectorBackend(RetroPixelWidth, RetroPixelHeight);

			IRenderBackend activeBackend = RenderMode == UnityRenderMode.RetroPixelCanvas
				? (IRenderBackend)_canvasBackend
				: _vectorBackend;

			App = new GameApplication(RetroPixelWidth, RetroPixelHeight, TargetFps, activeBackend);
			App.Audio.Backend = this;

			_audioSource = gameObject.GetComponent<AudioSource>();
			if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
		}

		void Update() {
			FNum.Deterministic = DeterministicFixedPoint;
			IRenderBackend desired = RenderMode == UnityRenderMode.RetroPixelCanvas
				? (IRenderBackend)_canvasBackend!
				: _vectorBackend!;
			if (!ReferenceEquals(App.RenderBackend, desired)) App.SetRenderBackend(desired);

			ForwardUnityInput();

			_accumulator += Time.unscaledDeltaTime;
			float step = App.Clock.FixedDeltaTime;
			int maxSteps = 4;
			while (_accumulator >= step && maxSteps-- > 0) {
				App.StepFrame();
				_accumulator -= step;
			}

			if (RenderMode == UnityRenderMode.RetroPixelCanvas && _canvasBackend != null) {
				BlitCanvasToTexture(_canvasBackend.Canvas);
			}
		}

		void ForwardUnityInput() {
			long frame = App.Clock.CurrentFrame;
			CheckKey(UnityEngine.KeyCode.Escape, Engine.KeyCode.Escape, '\u001b', frame);
			CheckKey(UnityEngine.KeyCode.Return, Engine.KeyCode.Enter, '\r', frame);
			CheckKey(UnityEngine.KeyCode.Space, Engine.KeyCode.Space, ' ', frame);
			CheckKey(UnityEngine.KeyCode.V, Engine.KeyCode.V, 'v', frame);
			CheckKey(UnityEngine.KeyCode.F, Engine.KeyCode.F, 'f', frame);
			CheckKey(UnityEngine.KeyCode.R, Engine.KeyCode.R, 'r', frame);
			CheckKey(UnityEngine.KeyCode.W, Engine.KeyCode.W, 'w', frame);
			CheckKey(UnityEngine.KeyCode.A, Engine.KeyCode.A, 'a', frame);
			CheckKey(UnityEngine.KeyCode.S, Engine.KeyCode.S, 's', frame);
			CheckKey(UnityEngine.KeyCode.D, Engine.KeyCode.D, 'd', frame);
			CheckKey(UnityEngine.KeyCode.Q, Engine.KeyCode.Q, 'q', frame);
			CheckKey(UnityEngine.KeyCode.E, Engine.KeyCode.E, 'e', frame);
			CheckKey(UnityEngine.KeyCode.UpArrow, Engine.KeyCode.UpArrow, '\0', frame);
			CheckKey(UnityEngine.KeyCode.DownArrow, Engine.KeyCode.DownArrow, '\0', frame);
			CheckKey(UnityEngine.KeyCode.LeftArrow, Engine.KeyCode.LeftArrow, '\0', frame);
			CheckKey(UnityEngine.KeyCode.RightArrow, Engine.KeyCode.RightArrow, '\0', frame);
			CheckKey(UnityEngine.KeyCode.Equals, Engine.KeyCode.Plus, '+', frame);
			CheckKey(UnityEngine.KeyCode.Minus, Engine.KeyCode.Minus, '-', frame);

			for (int i = 1; i <= 9; i++) {
				UnityEngine.KeyCode uk = (UnityEngine.KeyCode)((int)UnityEngine.KeyCode.Alpha0 + i);
				Engine.KeyCode ek = (Engine.KeyCode)((int)Engine.KeyCode.D0 + i);
				CheckKey(uk, ek, (char)('0' + i), frame);
			}

			Vector3 mp = Input.mousePosition;
			float normX = Mathf.Clamp01(mp.x / Mathf.Max(1, Screen.width));
			float normY = 1f - Mathf.Clamp01(mp.y / Mathf.Max(1, Screen.height));
			int cellX = Mathf.Clamp((int)(normX * App.RenderBackend.CellColumns), 0, App.RenderBackend.CellColumns - 1);
			int cellY = Mathf.Clamp((int)(normY * App.RenderBackend.CellRows), 0, App.RenderBackend.CellRows - 1);
			Vec2 pixelPos = new Vec2(normX * App.RenderBackend.PixelWidth, normY * App.RenderBackend.PixelHeight);
			Vec2 worldPos = App.Camera.ScreenPixelToWorld(pixelPos, App.RenderBackend.PixelWidth, App.RenderBackend.PixelHeight);

			if (Input.GetMouseButtonDown(0))
				App.Events.EnqueuePointer(new PointerInputEvent(frame, PointerEventType.Down, PointerButton.Left, cellX, cellY, pixelPos, worldPos));
			else if (Input.GetMouseButton(0))
				App.Events.EnqueuePointer(new PointerInputEvent(frame, PointerEventType.Drag, PointerButton.Left, cellX, cellY, pixelPos, worldPos));
			else if (Input.GetMouseButtonUp(0))
				App.Events.EnqueuePointer(new PointerInputEvent(frame, PointerEventType.Up, PointerButton.Left, cellX, cellY, pixelPos, worldPos));

			if (Input.GetMouseButtonDown(1))
				App.Events.EnqueuePointer(new PointerInputEvent(frame, PointerEventType.Down, PointerButton.Right, cellX, cellY, pixelPos, worldPos));

			float wheel = Input.mouseScrollDelta.y;
			if (Mathf.Abs(wheel) > 0.01f)
				App.Events.EnqueuePointer(new PointerInputEvent(frame, PointerEventType.Scroll, PointerButton.None, cellX, cellY, pixelPos, worldPos, wheel));
		}

		void CheckKey(UnityEngine.KeyCode uKey, Engine.KeyCode eKey, char ch, long frame) {
			if (Input.GetKeyDown(uKey) || Input.GetKey(uKey)) {
				App.Events.EnqueueInput(new UserInputEvent(frame, new KeyEventData(eKey, ch)));
			}
		}

		void BlitCanvasToTexture(Canvas canvas) {
			int w = canvas.Width, h = canvas.Height;
			if (_retroTexture == null || _retroTexture.width != w || _retroTexture.height != h) {
				_retroTexture = new Texture2D(w, h, TextureFormat.RGBA32, false) {
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp
				};
				_colorBuffer = new Color32[w * h];
			}

			Rgb[] src = canvas.Pixels;
			// Flip Y: Canvas top=0, Unity Texture2D bottom=0
			for (int y = 0; y < h; y++) {
				int dstRow = (h - 1 - y) * w, srcRow = y * w;
				for (int x = 0; x < w; x++) {
					Rgb c = src[srcRow + x];
					_colorBuffer![dstRow + x] = new Color32(c.R, c.G, c.B, 255);
				}
			}
			_retroTexture.SetPixels32(_colorBuffer);
			_retroTexture.Apply(false);
		}

		void OnGUI() {
			if (RenderMode == UnityRenderMode.RetroPixelCanvas && _retroTexture != null) {
				GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _retroTexture, ScaleMode.ScaleToFit, false);
			} else if (RenderMode == UnityRenderMode.NativeHighResVector && _vectorBackend != null) {
				_vectorBackend.RenderOnGUI();
			}
		}

		public void PlayCue(AudioEvent ev) {
			if (_audioSource == null) return;
			int sampleRate = 22050;
			int samples = Mathf.Max(64, (int)(sampleRate * ev.SynthDurationSeconds));
			float[] data = new float[samples];
			float freq = ev.SynthFrequencyHz * ev.Pitch;
			for (int i = 0; i < samples; i++) {
				float t = i / (float)sampleRate;
				float envelope = 1f - (i / (float)samples);
				data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * ev.Volume * 0.35f;
			}
			AudioClip clip = AudioClip.Create(ev.CueId, samples, 1, sampleRate, false);
			clip.SetData(data, 0);
			_audioSource.PlayOneShot(clip);
		}
	}
}
#endif
