using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ImageToolbox
{
    // 选区：PS 风格的矩形框选 / 圆形框选 / 画笔 / 套索 / 多边形套索 / 磁性套索。
    // 选区是图层像素坐标系下的任意形状遮罩，支持 新建/加选/减选/交集，并可对选区
    // 执行 删除(变透明)/填充颜色/复制/剪切/贴入，以及对选区本身做 全选/反选/取消/羽化。
    public class SelectionOp : EditOpPanel
    {
        private const int ToolRect = 0;
        private const int ToolEllipse = 1;
        private const int ToolBrush = 2;
        private const int ToolLasso = 3;
        private const int ToolPolygon = 4;
        private const int ToolMagnetic = 5;

        private ComboBox _tool;
        private ComboBox _combine;
        private TrackBar _brush;
        private TrackBar _magnet;
        private TrackBar _feather;
        private Label _brushV;
        private Label _magnetV;
        private Label _featherV;
        private Panel _swatch;

        private ImageSelection _sel;
        private Color _fillColor = Color.Black;
        private int _combineMode;
        private int _pending;        // 1 删除, 2 填充, 3 贴入
        private int _featherPx;
        private int _brushRadius = 40;
        private int _magnetRange = 24;

        private bool _dragging;
        private Point _start;
        private Point _end;
        private List<Point> _free;
        private List<Point> _poly;
        private Point _cursor;
        private bool _hasCursor;
        private bool _ignoreClick;

        private int _version;
        private int[] _outline;
        private int _outlineVersion = -1;
        private int _antPhase;
        private Timer _ants;

        private Bitmap _clipboard;

        private byte[] _lum;
        private Bitmap _lumSource;

        public SelectionOp()
        {
            EditOpUi.Title(this, "选区", 10);

            EditOpUi.Caption(this, "方式", 44);
            _tool = EditOpUi.Combo(this, 64, new string[]
            {
                "矩形框选", "圆形框选", "画笔", "套索", "多边形套索", "磁性套索"
            }, 0);
            _tool.SelectedIndexChanged += delegate { OnToolChanged(); };

            EditOpUi.Caption(this, "运算", 100);
            _combine = EditOpUi.Combo(this, 120, new string[] { "新建选区", "加选", "减选", "交集" }, 0);
            _combine.SelectedIndexChanged += delegate { _combineMode = _combine.SelectedIndex; };

            _brush = EditOpUi.Slider(this, "画笔", 156, 1, 200, 40, out _brushV);
            _brush.ValueChanged += delegate { _brushRadius = _brush.Value; _brushV.Text = _brush.Value.ToString(); UpdateBrushCursor(); };

            _magnet = EditOpUi.Slider(this, "磁力", 192, 4, 60, 24, out _magnetV);
            _magnet.ValueChanged += delegate { _magnetRange = _magnet.Value; _magnetV.Text = _magnet.Value.ToString(); };

            _feather = EditOpUi.Slider(this, "羽化", 228, 0, 60, 0, out _featherV);
            _feather.ValueChanged += delegate { _featherPx = _feather.Value; _featherV.Text = _feather.Value.ToString(); };

            EditOpUi.Button(this, "全选", 10, 268, 78, delegate { SelectAll(); });
            EditOpUi.Button(this, "反选", 94, 268, 78, delegate { InvertSel(); });
            EditOpUi.Button(this, "取消选择", 178, 268, 78, delegate { ClearSel(); });

            EditOpUi.Button(this, "删除", 10, 304, 78, delegate { DoAction(1); });
            EditOpUi.Button(this, "填充", 94, 304, 78, delegate { DoAction(2); });
            _swatch = new Panel();
            _swatch.Location = new Point(180, 304);
            _swatch.Size = new Size(30, 22);
            _swatch.BorderStyle = BorderStyle.FixedSingle;
            _swatch.BackColor = _fillColor;
            Controls.Add(_swatch);
            EditOpUi.Button(this, "颜色", 214, 304, 42, delegate { PickColor(); });

            EditOpUi.Button(this, "复制", 10, 340, 78, delegate { CopySel(); });
            EditOpUi.Button(this, "剪切", 94, 340, 78, delegate { CutSel(); });
            EditOpUi.Button(this, "贴入", 178, 340, 78, delegate { DoAction(3); });

            EditOpUi.Button(this, "完成多边形", 10, 376, 120, delegate { FinishPolygon(); });
            EditOpUi.Button(this, "清除路径", 138, 376, 118, delegate { CancelPath(); });

            EditOpUi.Note(this, "矩形/圆形拖动框选；画笔涂抹；套索按住拖动；多边形逐点单击、双击或点回起点闭合；磁性套索沿边缘拖动自动吸附，磁力控制吸附范围。运算控制与已有选区的合并方式。删除/填充/贴入会立即作用到当前图层；复制会新建一个图层并放入选中内容（同时写入剪贴板，可再「贴入」）。羽化在应用时生效。", 416, 104);

            _ants = new Timer();
            _ants.Interval = 110;
            _ants.Tick += delegate { _antPhase++; if (Canvas != null && Canvas.Visible) { Canvas.Invalidate(); } };
        }

        public override bool CanApply
        {
            get { return false; }
        }

        public override bool HasPendingResult
        {
            get { return false; }
        }

        // 复制（action 4）的结果要作为新图层插入，而不是替换当前图层。
        public override bool ResultIsNewLayer
        {
            get { return _pending == 4; }
        }

        // 历史记录里按当前即时动作显示更细的名称。
        public override string HistoryLabel
        {
            get
            {
                switch (_pending)
                {
                    case 1: return "选区 · 删除";
                    case 2: return "选区 · 填充";
                    case 3: return "选区 · 贴入";
                    case 4: return "选区 · 复制到图层";
                    default: return "选区";
                }
            }
        }

        public override bool WantsCanvasDrag
        {
            get
            {
                int t = _tool.SelectedIndex;
                return t == ToolRect || t == ToolEllipse || t == ToolLasso || t == ToolMagnetic;
            }
        }

        public override bool WantsTransformBox
        {
            get { return true; }
        }

        public override int BrushRadiusSession
        {
            get { return _brushRadius; }
        }

        public override Cursor TransformCursor(Point layerPoint)
        {
            return Cursors.Cross;
        }

        protected override void OnActivate()
        {
            EnsureSelection();
            ApplyCanvasMode();
            if (_ants != null) { _ants.Start(); }
        }

        protected override void OnDeactivate()
        {
            if (_ants != null) { _ants.Stop(); }
        }

        protected override void OnDetach()
        {
            _sel = null;
            _poly = null;
            _free = null;
            _outline = null;
            _outlineVersion = -1;
            _version = 0;
        }

        public override void DisposeResources()
        {
            if (_clipboard != null) { _clipboard.Dispose(); _clipboard = null; }
        }

        private void EnsureSelection()
        {
            if (Source == null) { _sel = null; return; }
            if (_sel == null || _sel.Width != Source.Width || _sel.Height != Source.Height)
            {
                _sel = new ImageSelection(Source.Width, Source.Height);
                _version++;
            }
        }

        private void ApplyCanvasMode()
        {
            if (Canvas == null) { return; }
            int t = _tool.SelectedIndex;
            bool brush = (t == ToolBrush);
            bool drag = (t == ToolRect || t == ToolEllipse || t == ToolLasso || t == ToolMagnetic);
            Canvas.LockAspect = 0f;
            Canvas.Selection = Rectangle.Empty;
            Canvas.DragEnabled = drag;
            Canvas.BrushEnabled = brush;
            Canvas.ReadOnly = !brush;
            UpdateBrushCursor();
        }

        // 画笔大小按显示缩放换算成画布显示图坐标，保证光标圈与实际刷子一致。
        private void UpdateBrushCursor()
        {
            if (Canvas == null || Source == null || PreviewSource == null) { return; }
            float s = (float)PreviewSource.Width / Source.Width;
            int r = (int)Math.Round(_brushRadius * s);
            if (r < 1) { r = 1; }
            Canvas.BrushRadius = r;
        }

        private void OnToolChanged()
        {
            if (_tool.SelectedIndex != ToolPolygon) { _poly = null; }
            _dragging = false;
            _free = null;
            _lum = null;
            _lumSource = null;
            ApplyCanvasMode();
            if (Canvas != null) { Canvas.Invalidate(); }
        }

        private void SelectAll()
        {
            EnsureSelection();
            if (_sel == null) { return; }
            _sel.SelectAll();
            _version++;
            if (Canvas != null) { Canvas.Invalidate(); }
        }

        private void InvertSel()
        {
            EnsureSelection();
            if (_sel == null) { return; }
            _sel.Invert();
            _version++;
            if (Canvas != null) { Canvas.Invalidate(); }
        }

        private void ClearSel()
        {
            EnsureSelection();
            if (_sel == null) { return; }
            _sel.Clear();
            _version++;
            if (Canvas != null) { Canvas.Invalidate(); }
        }

        private void CancelPath()
        {
            _dragging = false;
            _free = null;
            _poly = null;
            if (Canvas != null) { Canvas.Invalidate(); }
        }

        private void DoAction(int action)
        {
            EnsureSelection();
            if (_sel == null || Source == null || _sel.IsEmpty)
            {
                return;
            }
            _pending = action;
            RequestApply();
        }

        private void CutSel()
        {
            EnsureSelection();
            if (Source == null || _sel == null || _sel.IsEmpty) { return; }
            CopySelectionToClipboard();
            DoAction(1);
        }

        private void PickColor()
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = _fillColor;
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _fillColor = dialog.Color;
                    _swatch.BackColor = _fillColor;
                }
            }
        }

        // 复制：把选中内容放到一个自动新建的图层里（其余透明）；同时写入剪贴板，仍可「贴入」。
        private void CopySel()
        {
            EnsureSelection();
            if (Source == null || _sel == null || _sel.IsEmpty) { return; }
            CopySelectionToClipboard();
            DoAction(4);
        }

        private void CopySelectionToClipboard()
        {
            Rectangle b = _sel.Bounds();
            if (b.Width < 1 || b.Height < 1) { return; }

            Bitmap piece = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(piece))
            {
                g.Clear(Color.Transparent);
                g.DrawImage(Source, new Rectangle(0, 0, b.Width, b.Height), b, GraphicsUnit.Pixel);
            }
            ApplyMaskToPiece(piece, b, _sel.EffectiveMask(_featherPx));

            if (_clipboard != null) { _clipboard.Dispose(); }
            _clipboard = piece;
            try { Clipboard.SetImage(piece); }
            catch { }
        }

        // 把 piece 每个像素的 alpha 乘以选区覆盖度（piece 左上角对应遮罩的 (b.X,b.Y)）。
        private void ApplyMaskToPiece(Bitmap piece, Rectangle b, byte[] mask)
        {
            BitmapData d = piece.LockBits(new Rectangle(0, 0, piece.Width, piece.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int st = d.Stride;
                byte[] buf = new byte[st * piece.Height];
                Marshal.Copy(d.Scan0, buf, 0, buf.Length);
                for (int y = 0; y < piece.Height; y++)
                {
                    int row = y * st;
                    int mrow = (b.Y + y) * _sel.Width + b.X;
                    for (int x = 0; x < piece.Width; x++)
                    {
                        int i = row + x * 4;
                        int m = mask[mrow + x];
                        buf[i + 3] = (byte)(buf[i + 3] * m / 255);
                    }
                }
                Marshal.Copy(buf, 0, d.Scan0, buf.Length);
            }
            finally
            {
                piece.UnlockBits(d);
            }
        }

        // ---- 画布交互 ----

        public override void OnCanvasDrag(Point p, int action)
        {
            if (Source == null) { return; }
            EnsureSelection();
            if (_sel == null) { return; }
            int t = _tool.SelectedIndex;
            Point q = ClampToSource(p);

            if (action == 0)
            {
                _dragging = true;
                _start = q;
                _end = q;
                if (t == ToolLasso || t == ToolMagnetic)
                {
                    _free = new List<Point>();
                    _free.Add(q);
                }
                if (t == ToolMagnetic) { EnsureLum(); }
                if (Canvas != null) { Canvas.Invalidate(); }
                return;
            }

            if (action == 1)
            {
                _end = q;
                if (t == ToolLasso && _free != null) { AddFree(q); }
                else if (t == ToolMagnetic)
                {
                    EnsureLum();
                    AddFree(Snap(q));
                }
                if (Canvas != null) { Canvas.Invalidate(); }
                return;
            }

            // action == 2：结束
            if (!_dragging) { return; }
            _dragging = false;
            if (t == ToolRect || t == ToolEllipse)
            {
                Rectangle r = RectFrom(_start, _end);
                if (r.Width > 0 && r.Height > 0)
                {
                    byte[] shape = (t == ToolRect)
                        ? ImageSelection.ShapeRect(Source.Width, Source.Height, r)
                        : ImageSelection.ShapeEllipse(Source.Width, Source.Height, r);
                    CombineShape(shape);
                }
            }
            else if (t == ToolLasso || t == ToolMagnetic)
            {
                if (_free != null && _free.Count >= 3)
                {
                    CombineShape(ImageSelection.ShapePolygon(Source.Width, Source.Height, _free));
                }
            }
            _free = null;
            if (Canvas != null) { Canvas.Invalidate(); }
        }

        public override void OnBrushPoint(Point p, int action)
        {
            if (Source == null || _tool.SelectedIndex != ToolBrush) { return; }
            EnsureSelection();
            if (_sel == null) { return; }

            if (action == 0 && _combineMode == 0) { _sel.Clear(); }
            if (action == 2)
            {
                _version++;
                if (Canvas != null) { Canvas.Invalidate(); }
                return;
            }
            Point q = ClampToSource(p);
            int mode = (_combineMode == 0) ? 1 : _combineMode;
            _sel.Stamp(q.X, q.Y, _brushRadius, mode);
            _version++;
            if (Canvas != null) { Canvas.Invalidate(); }
        }

        public override void OnCanvasClick(Point p)
        {
            if (Source == null || _tool.SelectedIndex != ToolPolygon) { return; }
            if (_ignoreClick) { _ignoreClick = false; return; }
            EnsureSelection();
            if (_sel == null) { return; }
            Point q = ClampToSource(p);
            if (_poly == null) { _poly = new List<Point>(); }
            if (_poly.Count >= 1)
            {
                Point first = _poly[0];
                int tol = PickTolerance();
                if (Math.Abs(q.X - first.X) <= tol && Math.Abs(q.Y - first.Y) <= tol)
                {
                    FinishPolygon();
                    return;
                }
            }
            if (_poly.Count == 0 || Dist(_poly[_poly.Count - 1], q) >= 1)
            {
                _poly.Add(q);
            }
            if (Canvas != null) { Canvas.Invalidate(); }
        }

        public override void OnCanvasDoubleClick(Point p)
        {
            if (_tool.SelectedIndex != ToolPolygon) { return; }
            if (_poly != null && _poly.Count >= 2)
            {
                _ignoreClick = true;
                FinishPolygon();
            }
        }

        public override void OnCanvasHover(Point p)
        {
            _hasCursor = true;
            _cursor = ClampToSource(p);
            if (_tool.SelectedIndex == ToolPolygon && _poly != null && _poly.Count > 0 && Canvas != null)
            {
                Canvas.Invalidate();
            }
        }

        private void FinishPolygon()
        {
            if (_poly != null && _poly.Count >= 3 && Source != null)
            {
                CombineShape(ImageSelection.ShapePolygon(Source.Width, Source.Height, _poly));
            }
            _poly = null;
            if (Canvas != null) { Canvas.Invalidate(); }
        }

        private void CombineShape(byte[] shape)
        {
            if (_sel == null) { return; }
            _sel.Combine(shape, _combineMode);
            _version++;
        }

        private void AddFree(Point p)
        {
            if (_free == null) { return; }
            if (_free.Count == 0 || Dist(_free[_free.Count - 1], p) >= 2) { _free.Add(p); }
        }

        private static int Dist(Point a, Point b)
        {
            int dx = a.X - b.X, dy = a.Y - b.Y;
            return (int)Math.Sqrt((double)(dx * dx + dy * dy));
        }

        private static Rectangle RectFrom(Point a, Point b)
        {
            int x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
            int w = Math.Abs(a.X - b.X), h = Math.Abs(a.Y - b.Y);
            if (w < 1) { w = 1; }
            if (h < 1) { h = 1; }
            return new Rectangle(x, y, w, h);
        }

        private Point ClampToSource(Point p)
        {
            if (Source == null) { return p; }
            int x = Math.Max(0, Math.Min(Source.Width, p.X));
            int y = Math.Max(0, Math.Min(Source.Height, p.Y));
            return new Point(x, y);
        }

        // 单击回到起点闭合的判定容差（把 8 个客户区像素换算到图层坐标）。
        private int PickTolerance()
        {
            float d = 1f;
            if (Source != null && PreviewSource != null && PreviewSource.Width > 0)
            {
                d = (float)Source.Width / PreviewSource.Width;
            }
            float v = (Canvas != null) ? Canvas.ViewScale : 1f;
            if (v <= 0f) { v = 1f; }
            int tol = (int)Math.Round(8f * d / v);
            if (tol < 3) { tol = 3; }
            return tol;
        }

        // ---- 磁性套索的边缘吸附 ----

        private void EnsureLum()
        {
            if (Source == null) { _lum = null; _lumSource = null; return; }
            if (_lum != null && object.ReferenceEquals(_lumSource, Source)) { return; }
            int w = Source.Width, h = Source.Height;
            int stride;
            byte[] px = ImageFilters.CopyPixels(Source, out stride);
            byte[] lum = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                int lrow = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x * 4;
                    lum[lrow + x] = (byte)((px[i + 2] * 77 + px[i + 1] * 151 + px[i] * 28) >> 8);
                }
            }
            _lum = lum;
            _lumSource = Source;
        }

        // 吸附搜索半径（图层像素）：换算成固定的客户区像素距离，这样在大图上把鼠标离
        // 边缘稍远一点也能吸上（否则全分辨率下 6px 可能还不到一个屏幕像素）。
        private int SnapRadius()
        {
            float d = 1f;
            if (Source != null && PreviewSource != null && PreviewSource.Width > 0)
            {
                d = (float)Source.Width / PreviewSource.Width;
            }
            float v = (Canvas != null) ? Canvas.ViewScale : 1f;
            if (v <= 0f) { v = 1f; }
            int r = (int)Math.Round((float)_magnetRange * d / v);
            if (r < 4) { r = 4; }
            if (r > 160) { r = 160; }
            return r;
        }

        private Point Snap(Point p)
        {
            if (_lum == null || Source == null) { return p; }
            int w = Source.Width, h = Source.Height;
            int R = SnapRadius();
            int best = int.MinValue, bx = p.X, by = p.Y, bestMag = 0;
            for (int dy = -R; dy <= R; dy++)
            {
                int y = p.Y + dy;
                if (y < 1 || y >= h - 1) { continue; }
                int row = y * w;
                int dy2 = dy * dy;
                for (int dx = -R; dx <= R; dx++)
                {
                    int x = p.X + dx;
                    if (x < 1 || x >= w - 1) { continue; }
                    int gx = _lum[row + x + 1] - _lum[row + x - 1];
                    int gy = _lum[row + w + x] - _lum[row - w + x];
                    if (gx < 0) { gx = -gx; }
                    if (gy < 0) { gy = -gy; }
                    int mag = gx + gy;
                    // 距离只是弱惩罚：真实边缘的强度占主导，但相近强度时取更近的。
                    int score = mag * 200 - (dx * dx + dy2);
                    if (score > best) { best = score; bestMag = mag; bx = x; by = y; }
                }
            }
            if (bestMag < 8) { return p; }
            return new Point(bx, by);
        }

        // ---- 结果 ----

        public override Bitmap RenderPreview()
        {
            return null;
        }

        public override Bitmap BuildResult()
        {
            int action = _pending;
            _pending = 0;
            if (Source == null || _sel == null || action == 0 || _sel.IsEmpty) { return null; }
            byte[] mask = _sel.EffectiveMask(_featherPx);
            if (action == 1)
            {
                Bitmap r = ImageFilters.Clone(Source);
                ApplyDelete(r, mask);
                return r;
            }
            if (action == 2)
            {
                Bitmap r = ImageFilters.Clone(Source);
                ApplyFill(r, mask, _fillColor);
                return r;
            }
            if (action == 3)
            {
                return PasteMasked(mask);
            }
            if (action == 4)
            {
                return CopyToNewLayer(mask);
            }
            return null;
        }

        // 把选中内容做成一张与文档同尺寸、其余透明的位图，供「复制到新图层」使用。
        private Bitmap CopyToNewLayer(byte[] mask)
        {
            Rectangle b = _sel.Bounds();
            if (b.Width < 1 || b.Height < 1) { return null; }
            Bitmap result = new Bitmap(Source.Width, Source.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result)) { g.Clear(Color.Transparent); }
            using (Bitmap piece = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(piece))
                {
                    g.Clear(Color.Transparent);
                    g.DrawImage(Source, new Rectangle(0, 0, b.Width, b.Height), b, GraphicsUnit.Pixel);
                }
                ApplyMaskToPiece(piece, b, mask);
                using (Graphics g = Graphics.FromImage(result))
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.DrawImage(piece, b.X, b.Y);
                }
            }
            return result;
        }

        private static int Blend(int a, int b, int t)
        {
            return a + (b - a) * t / 255;
        }

        private static void ApplyDelete(Bitmap bmp, byte[] mask)
        {
            int w = bmp.Width, h = bmp.Height;
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int st = d.Stride;
                byte[] buf = new byte[st * h];
                Marshal.Copy(d.Scan0, buf, 0, buf.Length);
                for (int y = 0; y < h; y++)
                {
                    int row = y * st;
                    int mrow = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        int m = mask[mrow + x];
                        if (m <= 0) { continue; }
                        int i = row + x * 4;
                        buf[i + 3] = (byte)(buf[i + 3] * (255 - m) / 255);
                    }
                }
                Marshal.Copy(buf, 0, d.Scan0, buf.Length);
            }
            finally
            {
                bmp.UnlockBits(d);
            }
        }

        private static void ApplyFill(Bitmap bmp, byte[] mask, Color color)
        {
            int w = bmp.Width, h = bmp.Height;
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int st = d.Stride;
                byte[] buf = new byte[st * h];
                Marshal.Copy(d.Scan0, buf, 0, buf.Length);
                for (int y = 0; y < h; y++)
                {
                    int row = y * st;
                    int mrow = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        int m = mask[mrow + x];
                        if (m <= 0) { continue; }
                        int i = row + x * 4;
                        buf[i] = (byte)Blend(buf[i], color.B, m);
                        buf[i + 1] = (byte)Blend(buf[i + 1], color.G, m);
                        buf[i + 2] = (byte)Blend(buf[i + 2], color.R, m);
                        buf[i + 3] = (byte)Blend(buf[i + 3], 255, m);
                    }
                }
                Marshal.Copy(buf, 0, d.Scan0, buf.Length);
            }
            finally
            {
                bmp.UnlockBits(d);
            }
        }

        private Bitmap PasteMasked(byte[] mask)
        {
            if (_clipboard == null) { return null; }
            Rectangle b = _sel.Bounds();
            if (b.Width < 1 || b.Height < 1) { return null; }
            Bitmap result = ImageFilters.Clone(Source);
            using (Bitmap piece = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(piece))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    // 平铺翻转采样：缩放时不让插值核采到源图外的“透明”像素，
                    // 否则贴入内容四周会多出一圈半透明白边。
                    using (ImageAttributes wrap = new ImageAttributes())
                    {
                        wrap.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(_clipboard,
                            new Rectangle(0, 0, b.Width, b.Height),
                            0, 0, _clipboard.Width, _clipboard.Height,
                            GraphicsUnit.Pixel, wrap);
                    }
                }
                ApplyMaskToPiece(piece, b, mask);
                using (Graphics g = Graphics.FromImage(result))
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.DrawImage(piece, b.X, b.Y);
                }
            }
            return result;
        }

        // ---- 叠加绘制 ----

        public override void PaintCanvasOverlay(Graphics g, Func<PointF, PointF> map)
        {
            if (Source == null) { return; }
            EnsureOutline();

            if (_outline != null && _outline.Length > 0)
            {
                using (GraphicsPath path = new GraphicsPath())
                {
                    for (int i = 0; i + 3 < _outline.Length; i += 4)
                    {
                        PointF a = map(new PointF(_outline[i], _outline[i + 1]));
                        PointF b = map(new PointF(_outline[i + 2], _outline[i + 3]));
                        path.StartFigure();
                        path.AddLine(a, b);
                    }
                    using (Pen black = new Pen(Color.FromArgb(210, 0, 0, 0), 1f))
                    using (Pen white = new Pen(Color.White, 1f))
                    {
                        white.DashStyle = DashStyle.Dash;
                        white.DashPattern = new float[] { 4f, 4f };
                        white.DashOffset = _antPhase % 8;
                        g.DrawPath(black, path);
                        g.DrawPath(white, path);
                    }
                }
            }

            int t = _tool.SelectedIndex;
            if (_dragging && (t == ToolRect || t == ToolEllipse))
            {
                Rectangle r = RectFrom(_start, _end);
                PointF p0 = map(new PointF(r.Left, r.Top));
                PointF p1 = map(new PointF(r.Right, r.Bottom));
                using (Pen pen = new Pen(Color.FromArgb(0, 174, 255), 1f))
                {
                    if (t == ToolRect) { g.DrawRectangle(pen, p0.X, p0.Y, p1.X - p0.X, p1.Y - p0.Y); }
                    else { g.DrawEllipse(pen, p0.X, p0.Y, p1.X - p0.X, p1.Y - p0.Y); }
                }
            }
            if (_dragging && (t == ToolLasso || t == ToolMagnetic) && _free != null && _free.Count > 1)
            {
                DrawPolyline(g, map, _free, false);
            }
            if (!_dragging && t == ToolPolygon && _poly != null && _poly.Count > 0)
            {
                DrawPolyline(g, map, _poly, true);
            }
        }

        private void DrawPolyline(Graphics g, Func<PointF, PointF> map, List<Point> pts, bool polygon)
        {
            using (Pen pen = new Pen(Color.FromArgb(0, 174, 255), 1f))
            {
                PointF prev = map(new PointF(pts[0].X, pts[0].Y));
                for (int i = 1; i < pts.Count; i++)
                {
                    PointF cur = map(new PointF(pts[i].X, pts[i].Y));
                    g.DrawLine(pen, prev, cur);
                    prev = cur;
                }
                if (polygon)
                {
                    if (_hasCursor)
                    {
                        PointF c = map(new PointF(_cursor.X, _cursor.Y));
                        g.DrawLine(pen, prev, c);
                        PointF first = map(new PointF(pts[0].X, pts[0].Y));
                        g.DrawLine(pen, c, first);
                    }
                    using (SolidBrush fill = new SolidBrush(Color.White))
                    using (Pen hp = new Pen(Color.FromArgb(0, 174, 255), 1f))
                    {
                        for (int i = 0; i < pts.Count; i++)
                        {
                            PointF q = map(new PointF(pts[i].X, pts[i].Y));
                            g.FillRectangle(fill, q.X - 3f, q.Y - 3f, 6f, 6f);
                            g.DrawRectangle(hp, q.X - 3f, q.Y - 3f, 6f, 6f);
                        }
                    }
                }
            }
        }

        private void EnsureOutline()
        {
            if (_sel == null) { _outline = null; _outlineVersion = _version; return; }
            if (_outline != null && _outlineVersion == _version) { return; }
            _outline = _sel.BuildOutline(800);
            _outlineVersion = _version;
        }
    }
}
