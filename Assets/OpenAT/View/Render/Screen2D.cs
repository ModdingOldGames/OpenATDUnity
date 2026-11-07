using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace OpenAT.View.Render
{
    /// <summary>
    /// Virtual-pixel renderer. Everything is drawn in "original" pixels into a framebuffer that is
    /// always <see cref="Height"/> (480) pixels high and as wide as the window's aspect ratio allows
    /// (at least 640). The framebuffer is shown full-window with nearest-neighbour scaling.
    ///
    /// Drawing is immediate-mode (like the original's blitter): call Draw*/Fill* between
    /// <see cref="BeginFrame"/> and <see cref="EndFrame"/>; quads are batched per texture.
    /// Coordinates: origin top-left, y down. Textures are uploaded top-down (no flip).
    /// </summary>
    public sealed class Screen2D : MonoBehaviour
    {
        public const int BaseWidth = 640;
        public const int BaseHeight = 480;
        /// <summary>Upper bound for very wide windows (32:9).</summary>
        public const int MaxWidth = 1708;

        public int Width { get; private set; } = BaseWidth;
        public int Height => BaseHeight;

        /// <summary>X offset that centres 640-wide original layouts.</summary>
        public int CenterX => (Width - BaseWidth) / 2;

        /// <summary>When false the width is fixed to 640 (original 4:3 look, pillar-boxed).</summary>
        public bool Widescreen = true;

        public RenderTexture Target => _rt;

        RenderTexture _rt;
        RawImage _image;
        AspectRatioFitter _fitter;
        Material _material;
        CommandBuffer _cmd;
        Mesh _mesh;
        MaterialPropertyBlock _props;
        Texture2D _white;

        readonly List<Vector3> _verts = new List<Vector3>(4096);
        readonly List<Vector2> _uvs = new List<Vector2>(4096);
        readonly List<Color> _colors = new List<Color>(4096);   // float: keeps dark colours exact after the linear conversion
        readonly List<Batch> _batches = new List<Batch>(64);
        readonly Stack<RectInt> _clipStack = new Stack<RectInt>();
        RectInt _clip;
        Color _clear = Color.black;

        struct Batch { public Texture Tex; public int Start; public int Count; }

        public Texture2D White => _white;

        void Awake()
        {
            _linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            _material = new Material(Resources.Load<Shader>("OpenATSprite")) { hideFlags = HideFlags.HideAndDontSave };
            _cmd = new CommandBuffer { name = "OpenAT Screen2D" };
            _mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            _mesh.MarkDynamic();
            _props = new MaterialPropertyBlock();
            _white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
            CreatePresenter();
            Resize();
        }

        void CreatePresenter()
        {
            var canvasGo = new GameObject("OpenAT Screen", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -100;
            var bg = new GameObject("Background", typeof(RawImage));
            bg.transform.SetParent(canvasGo.transform, false);
            var bgImage = bg.GetComponent<RawImage>();
            bgImage.color = Color.black;
            Stretch(bgImage.rectTransform);
            var imgGo = new GameObject("Framebuffer", typeof(RawImage), typeof(AspectRatioFitter));
            imgGo.transform.SetParent(canvasGo.transform, false);
            _image = imgGo.GetComponent<RawImage>();
            _image.raycastTarget = false;
            Stretch(_image.rectTransform);
            _fitter = imgGo.GetComponent<AspectRatioFitter>();
            _fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>Virtual width for the current window.</summary>
        int DesiredWidth()
        {
            if (!Widescreen || Screen.height <= 0) return BaseWidth;
            int w = Mathf.RoundToInt(Screen.width * (float)BaseHeight / Screen.height);
            w &= ~1;
            return Mathf.Clamp(w, BaseWidth, MaxWidth);
        }

        void Resize()
        {
            int w = DesiredWidth();
            if (_rt != null && _rt.width == w) return;
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            Width = w;
            _rt = new RenderTexture(w, BaseHeight, 0, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "OpenAT framebuffer",
            };
            _rt.Create();
            _image.texture = _rt;
            _fitter.aspectRatio = (float)w / BaseHeight;
        }

        // ------------------------------------------------------------------ frame

        public void BeginFrame(Color clear)
        {
            ResetTransform();
            Resize();
            _clear = clear;
            _verts.Clear(); _uvs.Clear(); _colors.Clear(); _batches.Clear();
            _clipStack.Clear();
            _clip = new RectInt(0, 0, Width, Height);
        }

        public void EndFrame()
        {
            _cmd.Clear();
            _cmd.SetRenderTarget(_rt);
            _cmd.ClearRenderTarget(true, true, _clear);
            var proj = Matrix4x4.Ortho(0, Width, Height, 0, -1, 1);
            _cmd.SetViewProjectionMatrices(Matrix4x4.identity, proj);
            if (_verts.Count > 0)
            {
                _mesh.Clear();
                _mesh.indexFormat = _verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                _mesh.SetVertices(_verts);
                _mesh.SetUVs(0, _uvs);
                _mesh.SetColors(_colors);
                _mesh.subMeshCount = _batches.Count;
                var idx = new List<int>();
                for (int b = 0; b < _batches.Count; b++)
                {
                    idx.Clear();
                    var batch = _batches[b];
                    for (int q = 0; q < batch.Count; q++)
                    {
                        int v = batch.Start + q * 4;
                        idx.Add(v); idx.Add(v + 1); idx.Add(v + 2);
                        idx.Add(v); idx.Add(v + 2); idx.Add(v + 3);
                    }
                    _mesh.SetTriangles(idx, b, false);
                }
                _mesh.RecalculateBounds();
                for (int b = 0; b < _batches.Count; b++)
                {
                    _props.SetTexture("_MainTex", _batches[b].Tex);
                    _cmd.DrawMesh(_mesh, Matrix4x4.identity, _material, b, 0, _props);
                }
            }
            Graphics.ExecuteCommandBuffer(_cmd);
        }

        // ------------------------------------------------------------------ clipping

        public void PushClip(RectInt r)
        {
            _clipStack.Push(_clip);
            int x0 = Mathf.Max(_clip.xMin, r.xMin), y0 = Mathf.Max(_clip.yMin, r.yMin);
            int x1 = Mathf.Min(_clip.xMax, r.xMax), y1 = Mathf.Min(_clip.yMax, r.yMax);
            _clip = new RectInt(x0, y0, Mathf.Max(0, x1 - x0), Mathf.Max(0, y1 - y0));
        }

        public void PopClip() => _clip = _clipStack.Count > 0 ? _clipStack.Pop() : new RectInt(0, 0, Width, Height);

        // ------------------------------------------------------------------ drawing

        /// <summary>Draws a whole texture at (x,y) in virtual pixels.</summary>
        public void Draw(Texture tex, int x, int y) => Draw(tex, x, y, new RectInt(0, 0, tex.width, tex.height), Color.white);

        public void Draw(Texture tex, int x, int y, Color32 tint) => Draw(tex, x, y, new RectInt(0, 0, tex.width, tex.height), tint);

        /// <summary>Draws the <paramref name="src"/> part (top-down texture pixels) at (x,y).</summary>
        public void Draw(Texture tex, int x, int y, RectInt src, Color32 tint, bool flipX = false)
        {
            DrawScaled(tex, new Rect(x, y, src.width, src.height), new Rect(src.x, src.y, src.width, src.height), tint, flipX);
        }

        /// <summary>Draws a source rect into a destination rect (both in pixels, top-down).</summary>
        public void DrawScaled(Texture tex, Rect dst, Rect src, Color32 tint, bool flipX = false)
        {
            if (_tScale != 1f || _tX != 0f || _tY != 0f)
                dst = new Rect(_tX + dst.x * _tScale, _tY + dst.y * _tScale, dst.width * _tScale, dst.height * _tScale);
            if (tex == null || dst.width <= 0 || dst.height <= 0) return;
            // CPU clipping keeps one batch per texture regardless of clip changes.
            float sx = src.width / dst.width, sy = src.height / dst.height;
            float x0 = dst.xMin, y0 = dst.yMin, x1 = dst.xMax, y1 = dst.yMax;
            float u0 = src.xMin, v0 = src.yMin, u1 = src.xMax, v1 = src.yMax;
            if (flipX) { (u0, u1) = (u1, u0); sx = -sx; }
            if (x0 < _clip.xMin) { u0 += (_clip.xMin - x0) * sx; x0 = _clip.xMin; }
            if (y0 < _clip.yMin) { v0 += (_clip.yMin - y0) * sy; y0 = _clip.yMin; }
            if (x1 > _clip.xMax) { u1 -= (x1 - _clip.xMax) * sx; x1 = _clip.xMax; }
            if (y1 > _clip.yMax) { v1 -= (y1 - _clip.yMax) * sy; y1 = _clip.yMax; }
            if (x1 <= x0 || y1 <= y0) return;

            float iw = 1f / tex.width, ih = 1f / tex.height;
            AddQuad(tex, x0, y0, x1, y1, u0 * iw, v0 * ih, u1 * iw, v1 * ih, tint);
        }

        float _tX, _tY, _tScale = 1f;

        /// <summary>
        /// Draws everything that follows scaled by <paramref name="scale"/> and moved to (x, y): content drawn
        /// at (0,0) lands at (x, y). Used to zoom a composed page (kiosk newspapers). Not nestable.
        /// </summary>
        public void SetTransform(float x, float y, float scale) { _tX = x; _tY = y; _tScale = scale; }

        public void ResetTransform() { _tX = 0f; _tY = 0f; _tScale = 1f; }

        public void FillRect(RectInt r, Color32 c) => DrawScaled(_white, new Rect(r.x, r.y, r.width, r.height), new Rect(0, 0, 1, 1), c);

        public void FrameRect(RectInt r, Color32 c)
        {
            FillRect(new RectInt(r.x, r.y, r.width, 1), c);
            FillRect(new RectInt(r.x, r.yMax - 1, r.width, 1), c);
            FillRect(new RectInt(r.x, r.y, 1, r.height), c);
            FillRect(new RectInt(r.xMax - 1, r.y, 1, r.height), c);
        }

        void AddQuad(Texture tex, float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1, Color32 c)
        {
            if (_batches.Count == 0 || _batches[_batches.Count - 1].Tex != tex)
                _batches.Add(new Batch { Tex = tex, Start = _verts.Count, Count = 0 });
            var b = _batches[_batches.Count - 1];
            b.Count++;
            _batches[_batches.Count - 1] = b;
            // Texture rows were uploaded top-down, so texture v grows downwards like screen y.
            _verts.Add(new Vector3(x0, y0)); _uvs.Add(new Vector2(u0, v0));
            _verts.Add(new Vector3(x1, y0)); _uvs.Add(new Vector2(u1, v0));
            _verts.Add(new Vector3(x1, y1)); _uvs.Add(new Vector2(u1, v1));
            _verts.Add(new Vector3(x0, y1)); _uvs.Add(new Vector2(u0, v1));
            Color f = c;
            if (_linear) f = new Color(_toLinear[c.r], _toLinear[c.g], _toLinear[c.b], f.a);
            _colors.Add(f); _colors.Add(f); _colors.Add(f); _colors.Add(f);
        }

        // The project renders in Linear colour space: textures are converted on sampling, but vertex colours
        // (fills and tints) are not, so a colour meant as the original's (4,8,24) would show as (34,50,86).
        // Converting the vertex colour to linear makes fills and tints come out as written. Alpha stays.
        // Set in Awake: Unity forbids reading the colour space during component construction (editor).
        static bool _linear;
        static readonly float[] _toLinear = BuildToLinear();

        static float[] BuildToLinear()
        {
            var t = new float[256];
            for (int i = 0; i < 256; i++)
            {
                double v = i / 255.0;   // sRGB to linear
                t[i] = (float)(v <= 0.04045 ? v / 12.92 : System.Math.Pow((v + 0.055) / 1.055, 2.4));
            }
            return t;
        }

        // ------------------------------------------------------------------ input

        /// <summary>Mouse position in virtual pixels (top-left origin).</summary>
        public Vector2Int MousePosition
        {
            get
            {
                var m = Mouse.current;
                if (m == null) return new Vector2Int(-1, -1);
                Vector2 p = m.position.ReadValue();
                // The framebuffer is fitted into the window; undo letter/pillar boxing.
                float scale = Mathf.Min(Screen.width / (float)Width, Screen.height / (float)Height);
                float ox = (Screen.width - Width * scale) / 2, oy = (Screen.height - Height * scale) / 2;
                int x = Mathf.FloorToInt((p.x - ox) / scale);
                int y = Mathf.FloorToInt((Screen.height - p.y - oy) / scale);
                return new Vector2Int(x, y);
            }
        }

        void OnDestroy()
        {
            if (_rt != null) _rt.Release();
            _cmd?.Release();
        }
    }
}
