using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ImageToolbox
{
    // 透明背景的多行输入框：WinForms 原生 TextBox 不支持半透明 BackColor。
    // 这里让输入框带 WS_EX_TRANSPARENT，并在 WM_ERASEBKGND 里把父画布对应区域的
    // 图像画进自己的背景；父控件 ImageCanvas 处理 WM_CTLCOLOREDIT 返回空画刷，
    // 使编辑控件不再填充自己的底色。
    internal class CanvasTextBox : TextBox
    {
        public CanvasTextBox()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.Transparent;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x20; // WS_EX_TRANSPARENT
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_ERASEBKGND = 0x0014;
            if (m.Msg == WM_ERASEBKGND)
            {
                ImageCanvas canvas = Parent as ImageCanvas;
                if (canvas != null)
                {
                    using (Graphics g = Graphics.FromHdc(m.WParam))
                    {
                        GraphicsState state = g.Save();
                        g.TranslateTransform(-Left, -Top);
                        canvas.PaintView(g);
                        g.Restore(state);
                    }
                    m.Result = (IntPtr)1;
                    return;
                }
            }
            base.WndProc(ref m);
        }
    }

    // 绘画 / 标注：直线、箭头、画笔、橡皮擦、模糊、马赛克、文字、形状。
    // 矢量图元（线/箭头/画笔/文字/形状）以“源图坐标”记录，预览与全分辨率都重绘；
    // 栅格效果（橡皮擦/模糊/马赛克）直接在底图上按蒙版作用，全分辨率时按组重放。
    public class DrawOp : EditOpPanel
    {
        private const int ToolLine = 0;
        private const int ToolArrow = 1;
        private const int ToolPen = 2;
        private const int ToolEraser = 3;
        private const int ToolBlur = 4;
        private const int ToolMosaic = 5;
        private const int ToolText = 6;
        private const int ToolShape = 7;

        private const int ShapeRect = 0;
        private const int ShapeRoundRect = 1;
        private const int ShapeEllipse = 2;
        private const int ShapeTriangle = 3;
        private const int ShapeStar = 4;

        private const int EffectErase = 0;
        private const int EffectBlur = 1;
        private const int EffectMosaic = 2;

        private class Item
        {
            public int Tool;
            public int ShapeKind;
            public Color Color;
            public float Width;
            public bool Fill;
            public Point Start;
            public Point End;
            public List<Point> Points;
            public string Text;
            public float FontSize;
        }

        private class Effect
        {
            public int Kind;
            public int Strength;
            public int Width;
            public float Opacity;
            public List<Point> Points;
        }

        private ComboBox _tool;
        private ComboBox _shape;
        private CheckBox _fill;
        private Panel _swatch;
        private TrackBar _width, _strength, _font, _opacity;
        private Label _widthV, _strengthV, _fontV, _opacityV;
        private TextBox _textBox;
        private Button _okButton;
        private Button _cancelButton;
        private float _textFontPx = -1f;
        private Point _textPoint;
        private Color _color = Color.FromArgb(255, 230, 40, 40);

        // 按绘制顺序记录所有已完成笔画：Item=矢量图元，Effect=栅格效果（橡皮擦/模糊/马赛克）。
        // 单一有序列表使栅格效果能作用于它之前画的图元（否则橡皮擦擦不掉未应用的直线）。
        private readonly List<object> _strokes = new List<object>();
        private Item _current;
        private Effect _effect;
        private Point _anchor;
        private Point _last;

        private Bitmap _previewBase;
        private Bitmap _committed;
        private Bitmap _work;
        private Bitmap _mask;
        private Bitmap _processed;
        private Bitmap _stamp;
        private int _stampRadius;
        private float _previewScale = 1f;
        private Rectangle _dirty;            // 自上次 RenderPreview 起，_work 变化的区域（预览坐标）
        private bool _dirtyWhole = true;     // 变化覆盖整图（无法局部重合成）
        private Rectangle _reportedDirty;
        private bool _reportedWhole = true;

        public DrawOp()
        {
            EditOpUi.Title(this, "绘画 / 标注", 10);

            EditOpUi.Caption(this, "工具", 44);
            _tool = EditOpUi.Combo(this, 64, new string[]
            {
                "直线", "箭头", "画笔", "橡皮擦", "模糊", "马赛克", "文字", "形状"
            }, 0);
            _tool.SelectedIndexChanged += delegate
            {
                if (_tool.SelectedIndex != ToolText) { RemoveTextOverlay(); }
                UpdateToolUi();
                RaisePreview();
            };

            EditOpUi.Caption(this, "形状", 100);
            _shape = EditOpUi.Combo(this, 120, new string[] { "矩形", "圆角矩形", "椭圆", "三角形", "五角星" }, 0);
            _shape.Location = new Point(58, 120);
            _shape.Width = 140;
            _shape.SelectedIndexChanged += delegate { RaisePreview(); };
            _fill = new CheckBox();
            _fill.Text = "填充";
            _fill.Location = new Point(204, 123);
            _fill.AutoSize = true;
            _fill.CheckedChanged += delegate { RaisePreview(); };
            Controls.Add(_fill);

            _swatch = new Panel();
            _swatch.Location = new Point(10, 154);
            _swatch.Size = new Size(44, 28);
            _swatch.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_swatch);
            EditOpUi.Button(this, "选择颜色", 62, 152, 110, delegate { PickColor(); });

            _width = EditOpUi.Slider(this, "粗细", 190, 1, 80, 8, out _widthV);
            _width.ValueChanged += delegate { _widthV.Text = _width.Value.ToString(); RaisePreview(); };
            _opacity = EditOpUi.Slider(this, "不透明度", 226, 0, 100, 100, out _opacityV);
            _opacity.ValueChanged += delegate { _opacityV.Text = _opacity.Value.ToString(); };
            _strength = EditOpUi.Slider(this, "强度", 262, 1, 80, 20, out _strengthV);
            _strength.ValueChanged += delegate { _strengthV.Text = _strength.Value.ToString(); RaisePreview(); };
            _font = EditOpUi.Slider(this, "字号", 298, 8, 240, 48, out _fontV);
            _font.ValueChanged += delegate { _fontV.Text = _font.Value.ToString(); PlaceTextOverlay(); RaisePreview(); };

            EditOpUi.Button(this, "撤销一笔", 10, 340, 100, delegate { UndoLast(); });
            EditOpUi.Button(this, "清除全部", 118, 340, 100, delegate { ClearAll(); });
            EditOpUi.Note(this, "在画布上拖动绘制；直线/箭头/形状为按住拖出，文字为单击后就地输入（回车换行，✓ 确认）。橡皮擦/模糊/马赛克为涂抹式，强度控制模糊半径或马赛克块大小。不透明度控制整笔的透明程度。", 380, 84);

            UpdateSwatch();
            UpdateToolUi();
        }

        public override bool LivePreview
        {
            get { return true; }
        }

        // 画笔的预览就是主要反馈，必须同步刷新才能紧贴鼠标（多图层下尤其明显）。
        public override bool ImmediatePreview
        {
            get { return true; }
        }

        public override bool ReusablePreview
        {
            get { return true; }
        }

        // 正在绘制时新建图层：未应用的笔迹自动迁移到新图层，不提示是否应用。
        public override bool CarriesOverToAddedLayer
        {
            get { return true; }
        }

        // 没有已完成的笔画时不算“有未应用结果”，这样切换到本工具但还没画任何东西时，
        // 编辑器直接显示整图合成预览（与盖印/导出一致），不会因为透明图层边缘混出白边。
        public override bool HasPendingResult
        {
            get { return _strokes.Count > 0 || _current != null || _effect != null; }
        }

        // 有选区时只在选区内绘制。
        public override bool RespectsSelection
        {
            get { return true; }
        }

        protected override void OnActivate()
        {
            if (Canvas != null)
            {
                Canvas.ReadOnly = false;
                Canvas.BrushEnabled = true;
                Canvas.ViewChanged += OnViewChanged;
            }
            BuildBitmaps();
        }

        protected override void OnDeactivate()
        {
            if (Canvas != null)
            {
                Canvas.BrushEnabled = false;
                Canvas.ViewChanged -= OnViewChanged;
            }
            CommitTextEditCore();
            _current = null;
            _effect = null;
            DisposeBitmaps();
        }

        public override void DisposeResources()
        {
            RemoveTextOverlay();
            DisposeBitmaps();
            _strokes.Clear();
        }

        protected override void OnResetState()
        {
            RemoveTextOverlay();
            _strokes.Clear();
            _current = null;
            _effect = null;
            Rebuild();
        }

        public override int BrushRadiusSession
        {
            get { return Math.Max(1, _width.Value / 2); }
        }

        // ---- 位图与合成 ----

        private void BuildBitmaps()
        {
            DisposeBitmaps();
            if (PreviewSource == null || Source == null)
            {
                return;
            }
            _previewScale = (float)PreviewSource.Width / Source.Width;
            _previewBase = Clone(PreviewSource);
            _committed = Clone(PreviewSource);
            _work = Clone(PreviewSource);
            _mask = new Bitmap(PreviewSource.Width, PreviewSource.Height, PixelFormat.Format32bppArgb);
            Rebuild();
        }

        private void DisposeBitmaps()
        {
            if (_previewBase != null) { _previewBase.Dispose(); _previewBase = null; }
            if (_committed != null) { _committed.Dispose(); _committed = null; }
            if (_work != null) { _work.Dispose(); _work = null; }
            if (_mask != null) { _mask.Dispose(); _mask = null; }
            if (_processed != null) { _processed.Dispose(); _processed = null; }
            if (_stamp != null) { _stamp.Dispose(); _stamp = null; }
        }

        private static Bitmap Clone(Bitmap source)
        {
            Bitmap b = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
            }
            return b;
        }

        private void Rebuild()
        {
            // 从底图按顺序重放所有已完成笔画（重新挂载位图后仍保留已画内容）。
            RebuildFromStrokes();
        }

        // 标记 _work 的变化区域（预览坐标）；调用了 MarkDirtyWhole 后本帧将整图重合成。
        private void MarkDirty(Rectangle r)
        {
            if (_dirtyWhole || _work == null) { return; }
            r = Rectangle.Intersect(r, new Rectangle(0, 0, _work.Width, _work.Height));
            if (r.Width <= 0 || r.Height <= 0) { return; }
            _dirty = _dirty.IsEmpty ? r : Rectangle.Union(_dirty, r);
        }

        private void MarkDirtyWhole()
        {
            _dirtyWhole = true;
        }

        // 图元的外接矩形（源图坐标），保守放大以覆盖线宽、箭头、端点等。
        private Rectangle ItemBounds(Item it)
        {
            if (it == null) { return Rectangle.Empty; }
            float pad = Math.Max(1f, it.Width) * 3f + 12f;
            float x0, y0, x1, y1;
            if (it.Points != null && it.Points.Count > 0)
            {
                x0 = x1 = it.Points[0].X;
                y0 = y1 = it.Points[0].Y;
                for (int i = 1; i < it.Points.Count; i++)
                {
                    x0 = Math.Min(x0, it.Points[i].X); y0 = Math.Min(y0, it.Points[i].Y);
                    x1 = Math.Max(x1, it.Points[i].X); y1 = Math.Max(y1, it.Points[i].Y);
                }
            }
            else
            {
                x0 = Math.Min(it.Start.X, it.End.X); y0 = Math.Min(it.Start.Y, it.End.Y);
                x1 = Math.Max(it.Start.X, it.End.X); y1 = Math.Max(it.Start.Y, it.End.Y);
            }
            float fx = x0 * _previewScale - pad, fy = y0 * _previewScale - pad;
            float fw = (x1 - x0) * _previewScale + pad * 2f, fh = (y1 - y0) * _previewScale + pad * 2f;
            return Rectangle.Round(new RectangleF(fx, fy, fw, fh));
        }

        private void MarkCurrentDirty()
        {
            if (_current != null && _current.Tool != ToolText) { MarkDirty(ItemBounds(_current)); }
        }

        // 自由笔画一帧只新增一段：脏区取该段的范围（含线宽），避免随笔画变长而整段重合成。
        private void MarkSegmentDirty(Point a, Point b, float width)
        {
            float pad = Math.Max(1f, width) * 0.75f + 4f;
            float x0 = Math.Min(a.X, b.X) * _previewScale - pad;
            float y0 = Math.Min(a.Y, b.Y) * _previewScale - pad;
            float x1 = Math.Max(a.X, b.X) * _previewScale + pad;
            float y1 = Math.Max(a.Y, b.Y) * _previewScale + pad;
            MarkDirty(Rectangle.Round(new RectangleF(x0, y0, x1 - x0, y1 - y0)));
        }

        // 把一个已完成的矢量图元按预览比例烘焙进 _committed，使后续的橡皮擦/模糊/马赛克能作用到它。
        private void BakeItem(Item item)
        {
            if (_committed == null || item == null) { return; }
            using (Graphics g = Graphics.FromImage(_committed))
            {
                g.ScaleTransform(_previewScale, _previewScale);
                DrawItem(g, item);
            }
            if (item.Tool == ToolText) { MarkDirtyWhole(); }
            else { MarkDirty(ItemBounds(item)); }
        }

        // _work = _committed（已按顺序烘焙：底图 + 矢量图元 + 栅格效果）+ 正在绘制的矢量图元。
        private void ComposeWork()
        {
            if (_work == null || _committed == null) { return; }
            using (Graphics g = Graphics.FromImage(_work))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(_committed, new Rectangle(0, 0, _work.Width, _work.Height));
            }
            if (_current != null)
            {
                using (Graphics g = Graphics.FromImage(_work))
                {
                    g.ScaleTransform(_previewScale, _previewScale);
                    DrawItem(g, _current);
                }
            }
        }

        // ---- 鼠标输入 ----

        public override void OnBrushPoint(Point imagePoint, int action)
        {
            if (Source == null || _committed == null) { return; }
            int tool = Math.Max(0, _tool.SelectedIndex);

            if (action == 0)
            {
                _anchor = imagePoint;
                _last = imagePoint;
                if (tool == ToolText)
                {
                    BeginTextEdit(imagePoint);
                    return;
                }
                if (IsRaster(tool)) { BeginEffect(tool, imagePoint); }
                else
                {
                    _current = NewItem(tool);
                    _current.Start = imagePoint;
                    _current.End = imagePoint;
                    if (IsFreehand(tool))
                    {
                        _current.Points = new List<Point>();
                        _current.Points.Add(imagePoint);
                    }
                    MarkCurrentDirty();
                    ComposeWork();
                }
            }
            else if (action == 1)
            {
                if (_effect != null) { ContinueEffect(imagePoint); }
                else if (_current != null)
                {
                    if (IsFreehand(_current.Tool))
                    {
                        Point lp = _current.Points[_current.Points.Count - 1];
                        if (lp.X != imagePoint.X || lp.Y != imagePoint.Y)
                        {
                            MarkSegmentDirty(lp, imagePoint, _current.Width);
                            _current.Points.Add(imagePoint);
                            ComposeWork();
                        }
                    }
                    else
                    {
                        MarkCurrentDirty();
                        _current.End = imagePoint;
                        MarkCurrentDirty();
                        ComposeWork();
                    }
                }
            }
            else if (action == 2)
            {
                if (_effect != null) { EndEffect(); }
                else if (_current != null)
                {
                    MarkCurrentDirty();
                    if (!IsFreehand(_current.Tool) && _current.End == _current.Start)
                    {
                        _current = null;
                    }
                    else
                    {
                        _strokes.Add(_current);
                        BakeItem(_current);
                        _current = null;
                    }
                    ComposeWork();
                }
            }
            RaisePreview();
        }

        private static bool IsFreehand(int tool)
        {
            return tool == ToolPen;
        }

        private static bool IsRaster(int tool)
        {
            return tool == ToolEraser || tool == ToolBlur || tool == ToolMosaic;
        }

        private static int EffectKindFor(int tool)
        {
            if (tool == ToolEraser) { return EffectErase; }
            if (tool == ToolBlur) { return EffectBlur; }
            if (tool == ToolMosaic) { return EffectMosaic; }
            return -1;
        }

        // 笔刷不透明度（0-100%），作用于新绘制的矢量图元与栅格笔画。
        private float OpacityFactor
        {
            get { return _opacity.Value / 100f; }
        }

        private Color ApplyOpacity(Color c)
        {
            int a = (int)Math.Round(c.A * OpacityFactor);
            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        private Item NewItem(int tool)
        {
            Item it = new Item();
            it.Tool = tool;
            it.Color = ApplyOpacity(_color);
            it.Width = _width.Value;
            it.Fill = _fill.Checked;
            it.ShapeKind = Math.Max(0, _shape.SelectedIndex);
            return it;
        }

        // ---- 栅格效果（橡皮擦 / 模糊 / 马赛克）----

        private void BeginEffect(int tool, Point point)
        {
            _effect = new Effect();
            _effect.Kind = EffectKindFor(tool);
            _effect.Strength = _strength.Value;
            _effect.Width = _width.Value;
            _effect.Opacity = OpacityFactor;
            _effect.Points = new List<Point>();
            _effect.Points.Add(point);

            _stampRadius = Math.Max(1, (int)Math.Round((_width.Value / 2f) * _previewScale));
            if (_stamp != null) { _stamp.Dispose(); }
            _stamp = CreateStamp(_stampRadius * 2, _effect.Opacity);

            using (Graphics g = Graphics.FromImage(_mask)) { g.Clear(Color.Transparent); }
            if (_processed != null) { _processed.Dispose(); _processed = null; }
            if (_effect.Kind != EffectErase)
            {
                _processed = Process(_committed, _effect.Kind, _effect.Strength, _previewScale);
            }
            StampSegment(point, point);
        }

        private void ContinueEffect(Point point)
        {
            Point lp = _effect.Points[_effect.Points.Count - 1];
            if (lp.X == point.X && lp.Y == point.Y) { return; }
            _effect.Points.Add(point);
            StampSegment(lp, point);
        }

        private void EndEffect()
        {
            if (_effect != null) { _strokes.Add(_effect); }
            _effect = null;
            if (_processed != null) { _processed.Dispose(); _processed = null; }
            if (_stamp != null) { _stamp.Dispose(); _stamp = null; }
        }

        private void StampSegment(Point a, Point b)
        {
            if (_effect == null || _mask == null) { return; }
            float r = _stampRadius;
            float ax = a.X * _previewScale, ay = a.Y * _previewScale;
            float bx = b.X * _previewScale, by = b.Y * _previewScale;
            using (Graphics g = Graphics.FromImage(_mask))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(_stamp, ax - r, ay - r, r * 2f, r * 2f);
                DrawStampsBetween(g, _stamp, r, new PointF(ax, ay), new PointF(bx, by));
            }

            int minX = (int)Math.Floor(Math.Min(ax, bx) - r) - 1;
            int minY = (int)Math.Floor(Math.Min(ay, by) - r) - 1;
            int maxX = (int)Math.Ceiling(Math.Max(ax, bx) + r) + 1;
            int maxY = (int)Math.Ceiling(Math.Max(ay, by) + r) + 1;
            MarkDirty(new Rectangle(minX, minY, maxX - minX, maxY - minY));
            ApplyMasked(_committed, new Rectangle(minX, minY, maxX - minX, maxY - minY),
                _mask, _processed, _effect.Kind == EffectErase);
            ComposeWork();
        }

        private static Bitmap Process(Bitmap source, int kind, int strength, float scale)
        {
            Bitmap clone = Clone(source);
            if (kind == EffectBlur)
            {
                ImageFilters.GaussianBlur(clone, Math.Max(1, (int)Math.Round(strength * scale)));
            }
            else
            {
                ImageFilters.Mosaic(clone, Math.Max(2, (int)Math.Round(strength * scale)));
            }
            return clone;
        }

        // 在 target 的 rect 区域内，按 mask 的 alpha 作用效果：erase 减小 alpha，
        // 否则把颜色向 processed 插值。
        private static void ApplyMasked(Bitmap target, Rectangle rect, Bitmap mask, Bitmap processed, bool erase)
        {
            rect = Rectangle.Intersect(rect, new Rectangle(0, 0, target.Width, target.Height));
            if (rect.Width <= 0 || rect.Height <= 0) { return; }

            BitmapData td = target.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            BitmapData md = mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData pd = null;
            if (!erase) { pd = processed.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb); }
            try
            {
                int w = rect.Width, h = rect.Height;
                byte[] tb = new byte[td.Stride * h];
                byte[] mb = new byte[md.Stride * h];
                byte[] pb = null;
                Marshal.Copy(td.Scan0, tb, 0, tb.Length);
                Marshal.Copy(md.Scan0, mb, 0, mb.Length);
                if (!erase) { pb = new byte[pd.Stride * h]; Marshal.Copy(pd.Scan0, pb, 0, pb.Length); }

                for (int y = 0; y < h; y++)
                {
                    int to = y * td.Stride, mo = y * md.Stride, po = erase ? 0 : y * pd.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        int ti = to + x * 4, mi = mo + x * 4;
                        int m = mb[mi + 3];
                        if (m == 0) { continue; }
                        if (erase)
                        {
                            tb[ti + 3] = (byte)(tb[ti + 3] * (255 - m) / 255);
                        }
                        else
                        {
                            int pi = po + x * 4;
                            tb[ti] = (byte)(tb[ti] + (pb[pi] - tb[ti]) * m / 255);
                            tb[ti + 1] = (byte)(tb[ti + 1] + (pb[pi + 1] - tb[ti + 1]) * m / 255);
                            tb[ti + 2] = (byte)(tb[ti + 2] + (pb[pi + 2] - tb[ti + 2]) * m / 255);
                        }
                    }
                }
                Marshal.Copy(tb, 0, td.Scan0, tb.Length);
            }
            finally
            {
                target.UnlockBits(td);
                mask.UnlockBits(md);
                if (!erase) { processed.UnlockBits(pd); }
            }
        }

        private static Bitmap CreateStamp(int diameter, float opacity)
        {
            if (diameter < 2) { diameter = 2; }
            Bitmap stamp = new Bitmap(diameter, diameter, PixelFormat.Format32bppArgb);
            int inset = Math.Max(1, diameter / 10);
            using (Graphics g = Graphics.FromImage(stamp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, 255, 255, 255)))
                {
                    g.FillEllipse(brush, inset, inset, diameter - 1 - inset * 2, diameter - 1 - inset * 2);
                }
            }
            ImageFilters.GaussianBlur(stamp, inset);
            if (opacity >= 0.999f) { return stamp; }

            // 按不透明度缩放笔刷 alpha（在模糊之后），使整笔（含模糊/马赛克）都是半透明的。
            Bitmap scaled = new Bitmap(stamp.Width, stamp.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(scaled))
            {
                ColorMatrix cm = new ColorMatrix();
                cm.Matrix33 = opacity;
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(cm);
                    g.DrawImage(stamp, new Rectangle(0, 0, stamp.Width, stamp.Height),
                        0, 0, stamp.Width, stamp.Height, GraphicsUnit.Pixel, ia);
                }
            }
            stamp.Dispose();
            return scaled;
        }

        private static void DrawStampsBetween(Graphics g, Bitmap stamp, float radius, PointF a, PointF b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max(1, (int)(dist / Math.Max(1f, radius * 0.4f)));
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float cx = a.X + dx * t;
                float cy = a.Y + dy * t;
                g.DrawImage(stamp, cx - radius, cy - radius, radius * 2f, radius * 2f);
            }
        }

        // ---- 撤销 / 清除 ----

        private void UndoLast()
        {
            if (_strokes.Count > 0)
            {
                _strokes.RemoveAt(_strokes.Count - 1);
                RebuildFromStrokes();
                RaisePreview();
            }
        }

        private void ClearAll()
        {
            _strokes.Clear();
            _current = null;
            _effect = null;
            RebuildFromStrokes();
            RaisePreview();
        }

        private void RebuildFromStrokes()
        {
            if (_committed == null || _previewBase == null) { return; }
            MarkDirtyWhole();
            using (Graphics g = Graphics.FromImage(_committed))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(_previewBase, new Rectangle(0, 0, _committed.Width, _committed.Height));
            }
            // 按绘制顺序重放所有笔画（矢量图元 + 栅格效果），使“撤销一笔”正确。
            for (int i = 0; i < _strokes.Count; i++)
            {
                if (_strokes[i] is Item) { BakeItem((Item)_strokes[i]); }
                else { ReplayEffectPreview((Effect)_strokes[i]); }
            }
            ComposeWork();
        }

        private void ReplayEffectPreview(Effect effect)
        {
            float r = Math.Max(1f, (effect.Width / 2f) * _previewScale);
            using (Graphics g = Graphics.FromImage(_mask))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (Bitmap stamp = CreateStamp((int)Math.Round(r * 2), effect.Opacity))
                {
                    PointF prev = new PointF(effect.Points[0].X * _previewScale, effect.Points[0].Y * _previewScale);
                    g.DrawImage(stamp, prev.X - r, prev.Y - r, r * 2f, r * 2f);
                    for (int j = 1; j < effect.Points.Count; j++)
                    {
                        PointF cur = new PointF(effect.Points[j].X * _previewScale, effect.Points[j].Y * _previewScale);
                        DrawStampsBetween(g, stamp, r, prev, cur);
                        prev = cur;
                    }
                }
            }
            Bitmap processed = null;
            if (effect.Kind != EffectErase)
            {
                processed = Process(_committed, effect.Kind, effect.Strength, _previewScale);
            }
            ApplyMasked(_committed, new Rectangle(0, 0, _committed.Width, _committed.Height),
                _mask, processed, effect.Kind == EffectErase);
            if (processed != null) { processed.Dispose(); }
        }

        // ---- 文字就地编辑 ----

        private void BeginTextEdit(Point imagePoint)
        {
            if (_textBox != null) { CommitTextEdit(); }
            if (Canvas == null) { return; }
            _textPoint = imagePoint;

            TextBox box = new CanvasTextBox();
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Multiline = true;
            box.AcceptsReturn = true;
            box.WordWrap = false;
            box.ScrollBars = ScrollBars.None;
            box.ForeColor = _color;
            box.KeyDown += TextBoxKeyDown;
            box.TextChanged += TextBoxChanged;
            _textBox = box;
            _textFontPx = -1f;

            _okButton = MakeTextToolButton("✓", delegate { CommitTextEdit(); });
            _cancelButton = MakeTextToolButton("✕", delegate { RemoveTextOverlay(); });

            Canvas.TextEditing = true;
            Canvas.Controls.Add(box);
            Canvas.Controls.Add(_okButton);
            Canvas.Controls.Add(_cancelButton);
            PlaceTextOverlay();
            box.BringToFront();
            _okButton.BringToFront();
            _cancelButton.BringToFront();
            box.Focus();
        }

        private static Button MakeTextToolButton(string glyph, EventHandler onClick)
        {
            Button button = new Button();
            button.Text = glyph;
            button.Size = new Size(36, 32);
            button.TabStop = false;
            button.FlatStyle = FlatStyle.System;
            button.Padding = new Padding(0);
            button.Font = new Font("Segoe UI Symbol", 12F);
            button.Click += onClick;
            return button;
        }

        private void PlaceTextOverlay()
        {
            if (_textBox == null || Canvas == null) { return; }
            float screenFont = Math.Max(6f, _font.Value * _previewScale * Canvas.ViewScale);
            if (Math.Abs(screenFont - _textFontPx) > 0.01f)
            {
                Font old = _textBox.Font;
                _textBox.Font = MakeFont(screenFont);
                if (old != null && !object.ReferenceEquals(old, _textBox.Font)) { old.Dispose(); }
                _textFontPx = screenFont;
            }

            Size size = MeasureTextSize();
            PointF c = Canvas.ImageToClient(new PointF(_textPoint.X * _previewScale, _textPoint.Y * _previewScale));
            int x = (int)Math.Round(c.X);
            int y = (int)Math.Round(c.Y);
            x = Math.Max(0, Math.Min(Math.Max(0, Canvas.ClientSize.Width - size.Width), x));
            y = Math.Max(0, Math.Min(Math.Max(0, Canvas.ClientSize.Height - size.Height), y));
            _textBox.Location = new Point(x, y);
            _textBox.Size = size;

            int bx = x + size.Width + 4;
            int by = y;
            if (bx + 80 > Canvas.ClientSize.Width) { bx = x; by = y + size.Height + 4; }
            if (_okButton != null) { _okButton.Location = new Point(bx, by); }
            if (_cancelButton != null) { _cancelButton.Location = new Point(bx + 40, by); }
        }

        private Size MeasureTextSize()
        {
            string text = _textBox.Text;
            string[] lines = text.Length == 0 ? new string[] { "MMMM" } : text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int width = 100;
            for (int i = 0; i < lines.Length; i++)
            {
                int w = TextRenderer.MeasureText(lines[i].Length == 0 ? " " : lines[i], _textBox.Font).Width;
                if (w > width) { width = w; }
            }
            int lineHeight = _textBox.Font.Height;
            int height = lineHeight * lines.Length + 8;
            return new Size(Math.Max(100, width + 14), Math.Max(lineHeight + 8, height));
        }

        private void TextBoxChanged(object sender, EventArgs e)
        {
            PlaceTextOverlay();
        }

        private void TextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                RemoveTextOverlay();
            }
        }

        private void CommitTextEdit()
        {
            if (CommitTextEditCore()) { RaisePreview(); }
        }

        private bool CommitTextEditCore()
        {
            if (_textBox == null) { return false; }
            string text = NormalizeText(_textBox.Text);
            Point point = _textPoint;
            RemoveTextOverlay();
            if (text.Trim().Length == 0 || _committed == null) { return false; }

            Item it = new Item();
            it.Tool = ToolText;
            it.Color = ApplyOpacity(_color);
            it.Width = _width.Value;
            it.Text = text;
            it.FontSize = _font.Value;
            it.Start = point;
            it.End = point;
            _strokes.Add(it);
            BakeItem(it);
            ComposeWork();
            return true;
        }

        private static string NormalizeText(string text)
        {
            return (text == null) ? "" : text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private void RemoveTextOverlay()
        {
            Control box = _textBox;
            Control ok = _okButton;
            Control cancel = _cancelButton;
            if (box == null && ok == null && cancel == null) { return; }
            _textBox = null;
            _okButton = null;
            _cancelButton = null;
            _textFontPx = -1f;
            if (Canvas != null) { Canvas.TextEditing = false; }
            DetachControl(box);
            DetachControl(ok);
            DetachControl(cancel);
        }

        private static void DetachControl(Control control)
        {
            if (control == null) { return; }
            Control parent = control.Parent;
            if (parent != null) { parent.Controls.Remove(control); }
            control.Dispose();
        }

        private void OnViewChanged()
        {
            if (_textBox != null) { PlaceTextOverlay(); }
        }

        // ---- 控件状态 ----

        private void PickColor()
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = _color;
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _color = dialog.Color;
                    UpdateSwatch();
                    if (_textBox != null) { _textBox.ForeColor = _color; }
                    RaisePreview();
                }
            }
        }

        private void UpdateSwatch()
        {
            if (_swatch != null) { _swatch.BackColor = _color; }
        }

        private void UpdateToolUi()
        {
            int tool = Math.Max(0, _tool.SelectedIndex);
            bool shape = (tool == ToolShape);
            bool text = (tool == ToolText);
            bool effect = IsRaster(tool) && tool != ToolEraser;
            if (_shape != null) { _shape.Enabled = shape; }
            if (_fill != null) { _fill.Enabled = shape; }
            if (_font != null) { _font.Enabled = text; }
            if (_strength != null) { _strength.Enabled = effect; }
        }

        // ---- 渲染 ----

        private void DrawItem(Graphics g, Item it)
        {
            if (it.Tool == ToolText) { DrawText(g, it); return; }
            if (it.Tool == ToolShape) { DrawShape(g, it); return; }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (it.Tool == ToolLine)
            {
                using (Pen pen = MakePen(it))
                {
                    g.DrawLine(pen, it.Start, it.End);
                }
            }
            else if (it.Tool == ToolArrow)
            {
                DrawArrow(g, it);
            }
            else
            {
                DrawFreehand(g, it);
            }
        }

        private static Pen MakePen(Item it)
        {
            Pen pen = new Pen(it.Color, Math.Max(1f, it.Width));
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            return pen;
        }

        private static void DrawFreehand(Graphics g, Item it)
        {
            List<Point> pts = it.Points;
            if (pts == null || pts.Count == 0) { return; }
            bool same = true;
            for (int i = 1; i < pts.Count; i++)
            {
                if (pts[i].X != pts[0].X || pts[i].Y != pts[0].Y) { same = false; break; }
            }
            if (pts.Count == 1 || same)
            {
                float r = Math.Max(1f, it.Width) / 2f;
                using (SolidBrush brush = new SolidBrush(it.Color))
                {
                    g.FillEllipse(brush, pts[0].X - r, pts[0].Y - r, r * 2f, r * 2f);
                }
                return;
            }
            PointF[] pf = new PointF[pts.Count];
            for (int i = 0; i < pts.Count; i++) { pf[i] = new PointF(pts[i].X, pts[i].Y); }
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddLines(pf);
                using (Pen pen = MakePen(it)) { g.DrawPath(pen, path); }
            }
        }

        private void DrawText(Graphics g, Item it)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Font font = MakeFont(it.FontSize))
            {
                using (SolidBrush brush = new SolidBrush(it.Color))
                {
                    g.DrawString(it.Text, font, brush, it.Start.X, it.Start.Y);
                }
            }
        }

        private static Font MakeFont(float size)
        {
            float px = Math.Max(1f, size);
            try { return new Font("Microsoft YaHei UI", px, FontStyle.Regular, GraphicsUnit.Pixel); }
            catch (Exception) { return new Font(FontFamily.GenericSansSerif, px, FontStyle.Regular, GraphicsUnit.Pixel); }
        }

        private static RectangleF Normalize(Point a, Point b)
        {
            float x = Math.Min(a.X, b.X);
            float y = Math.Min(a.Y, b.Y);
            float w = Math.Abs(a.X - b.X);
            float h = Math.Abs(a.Y - b.Y);
            return new RectangleF(x, y, w, h);
        }

        private void DrawShape(Graphics g, Item it)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = Normalize(it.Start, it.End);
            if (r.Width < 0.5f && r.Height < 0.5f) { return; }
            using (Pen pen = new Pen(it.Color, Math.Max(1f, it.Width)))
            {
                pen.LineJoin = LineJoin.Miter;
                pen.MiterLimit = 10f;
                switch (it.ShapeKind)
                {
                    case ShapeEllipse:
                        g.DrawEllipse(pen, r);
                        if (it.Fill) { FillEllipse(g, it.Color, r); }
                        break;
                    case ShapeTriangle:
                        PointF[] tri = TrianglePoints(r);
                        g.DrawPolygon(pen, tri);
                        if (it.Fill) { FillPolygon(g, it.Color, tri); }
                        break;
                    case ShapeStar:
                        PointF[] star = StarPoints(r);
                        g.DrawPolygon(pen, star);
                        if (it.Fill) { FillPolygon(g, it.Color, star); }
                        break;
                    case ShapeRoundRect:
                        using (GraphicsPath path = RoundRect(r, Math.Max(4f, it.Width * 1.6f)))
                        {
                            g.DrawPath(pen, path);
                            if (it.Fill)
                            {
                                using (SolidBrush brush = new SolidBrush(it.Color)) { g.FillPath(brush, path); }
                            }
                        }
                        break;
                    default:
                        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                        if (it.Fill)
                        {
                            using (SolidBrush brush = new SolidBrush(it.Color)) { g.FillRectangle(brush, r); }
                        }
                        break;
                }
            }
        }

        private static void FillEllipse(Graphics g, Color c, RectangleF r)
        {
            using (SolidBrush brush = new SolidBrush(c)) { g.FillEllipse(brush, r); }
        }

        private static void FillPolygon(Graphics g, Color c, PointF[] pts)
        {
            using (SolidBrush brush = new SolidBrush(c)) { g.FillPolygon(brush, pts); }
        }

        private static PointF[] TrianglePoints(RectangleF r)
        {
            return new PointF[]
            {
                new PointF(r.X + r.Width / 2f, r.Y),
                new PointF(r.X + r.Width, r.Y + r.Height),
                new PointF(r.X, r.Y + r.Height)
            };
        }

        private static PointF[] StarPoints(RectangleF r)
        {
            float cx = r.X + r.Width / 2f;
            float cy = r.Y + r.Height / 2f;
            float outer = Math.Min(r.Width, r.Height) / 2f;
            float inner = outer * 0.42f;
            PointF[] pts = new PointF[10];
            for (int i = 0; i < 10; i++)
            {
                double ang = -Math.PI / 2 + i * Math.PI / 5;
                float rad = (i % 2 == 0) ? outer : inner;
                pts[i] = new PointF(cx + (float)Math.Cos(ang) * rad, cy + (float)Math.Sin(ang) * rad);
            }
            return pts;
        }

        private static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            if (d < 1f)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // 箭头：箭杆画到箭头底部（平头，避免圆头把尖端顶成钝角），再叠加一个尖三角。
        private static void DrawArrow(Graphics g, Item it)
        {
            PointF from = it.Start;
            PointF to = it.End;
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.5f) { return; }
            float w = Math.Max(1f, it.Width);
            float ang = (float)Math.Atan2(dy, dx);
            float head = Math.Max(w * 3.5f, 14f);
            if (head > len) { head = len; }
            float half = head * 0.42f;

            PointF basePt = new PointF(to.X - (float)Math.Cos(ang) * head, to.Y - (float)Math.Sin(ang) * head);
            float px = -(float)Math.Sin(ang);
            float py = (float)Math.Cos(ang);
            PointF p1 = new PointF(basePt.X + px * half, basePt.Y + py * half);
            PointF p2 = new PointF(basePt.X - px * half, basePt.Y - py * half);

            using (Pen pen = MakePen(it))
            {
                pen.StartCap = LineCap.Flat;
                pen.EndCap = LineCap.Flat;
                g.DrawLine(pen, from, basePt);
            }
            using (SolidBrush brush = new SolidBrush(it.Color))
            {
                g.FillPolygon(brush, new PointF[] { to, p1, p2 });
            }
        }

        // ---- 预览 / 结果 ----

        public override Bitmap RenderPreview()
        {
            // 把自上次以来的变化区域交给编辑器（用于只重合成脏区），并重新开始累计。
            _reportedDirty = _dirty;
            _reportedWhole = _dirtyWhole;
            _dirty = Rectangle.Empty;
            _dirtyWhole = false;
            return _work;
        }

        public override bool TryGetPreviewDirtyRect(out Rectangle rect)
        {
            rect = _reportedDirty;
            return !_reportedWhole;
        }

        public override Bitmap BuildResult()
        {
            if (Source == null || _strokes.Count == 0)
            {
                return null;
            }
            Bitmap result = Clone(Source);
            ApplyStrokesFull(result);
            return result;
        }

        // 全分辨率按绘制顺序重放：矢量图元直接重绘，栅格效果就地作用，因此橡皮擦/模糊/马赛克
        // 能作用到它之前画的图元上。相邻的同种（种类+强度）效果合并成一组，每组只做一次处理。
        private void ApplyStrokesFull(Bitmap result)
        {
            int i = 0;
            while (i < _strokes.Count)
            {
                object s = _strokes[i];
                if (s is Item)
                {
                    using (Graphics g = Graphics.FromImage(result)) { DrawItem(g, (Item)s); }
                    i++;
                    continue;
                }
                Effect first = (Effect)s;
                List<Effect> run = new List<Effect>();
                while (i < _strokes.Count && _strokes[i] is Effect)
                {
                    Effect e = (Effect)_strokes[i];
                    if (e.Kind != first.Kind || e.Strength != first.Strength) { break; }
                    run.Add(e);
                    i++;
                }
                ApplyEffectRunFull(result, run);
            }
        }

        private static void ApplyEffectRunFull(Bitmap result, List<Effect> run)
        {
            int w = result.Width, h = result.Height;
            Bitmap mask = BuildMaskFull(run, w, h);
            if (run[0].Kind == EffectErase)
            {
                ApplyMasked(result, new Rectangle(0, 0, w, h), mask, null, true);
            }
            else
            {
                Bitmap processed = Process(result, run[0].Kind, run[0].Strength, 1f);
                ApplyMasked(result, new Rectangle(0, 0, w, h), mask, processed, false);
                processed.Dispose();
            }
            mask.Dispose();
        }

        private static Bitmap BuildMaskFull(List<Effect> strokes, int w, int h)
        {
            Bitmap mask = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(mask))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                for (int i = 0; i < strokes.Count; i++)
                {
                    Effect e = strokes[i];
                    float r = Math.Max(1f, e.Width / 2f);
                    using (Bitmap stamp = CreateStamp((int)Math.Round(r * 2), e.Opacity))
                    {
                        PointF prev = new PointF(e.Points[0].X, e.Points[0].Y);
                        g.DrawImage(stamp, prev.X - r, prev.Y - r, r * 2f, r * 2f);
                        for (int j = 1; j < e.Points.Count; j++)
                        {
                            PointF cur = new PointF(e.Points[j].X, e.Points[j].Y);
                            DrawStampsBetween(g, stamp, r, prev, cur);
                            prev = cur;
                        }
                    }
                }
            }
            return mask;
        }
    }
}
