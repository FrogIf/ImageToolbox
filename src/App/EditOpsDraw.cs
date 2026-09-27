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

    // 绘画 / 标注：直线、箭头、画笔、记号笔、橡皮擦、模糊、马赛克、文字、形状。
    // 矢量图元（线/箭头/画笔/记号笔/文字/形状）以“源图坐标”记录，预览与全分辨率都重绘；
    // 栅格效果（橡皮擦/模糊/马赛克）直接在底图上按蒙版作用，全分辨率时按组重放。
    public class DrawOp : EditOpPanel
    {
        private const int ToolLine = 0;
        private const int ToolArrow = 1;
        private const int ToolPen = 2;
        private const int ToolMarker = 3;
        private const int ToolEraser = 4;
        private const int ToolBlur = 5;
        private const int ToolMosaic = 6;
        private const int ToolText = 7;
        private const int ToolShape = 8;

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
            public List<Point> Points;
        }

        private ComboBox _tool;
        private ComboBox _shape;
        private CheckBox _fill;
        private Panel _swatch;
        private TrackBar _width, _strength, _font;
        private Label _widthV, _strengthV, _fontV;
        private TextBox _textBox;
        private Button _okButton;
        private Button _cancelButton;
        private float _textFontPx = -1f;
        private Point _textPoint;
        private Color _color = Color.FromArgb(255, 230, 40, 40);

        private readonly List<Item> _items = new List<Item>();
        private readonly List<Effect> _effects = new List<Effect>();
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

        public DrawOp()
        {
            EditOpUi.Title(this, "绘画 / 标注", 10);

            EditOpUi.Caption(this, "工具", 44);
            _tool = EditOpUi.Combo(this, 64, new string[]
            {
                "直线", "箭头", "画笔", "记号笔", "橡皮擦", "模糊", "马赛克", "文字", "形状"
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
            _fill.Location = new Point(206, 123);
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
            _strength = EditOpUi.Slider(this, "强度", 226, 1, 80, 20, out _strengthV);
            _strength.ValueChanged += delegate { _strengthV.Text = _strength.Value.ToString(); RaisePreview(); };
            _font = EditOpUi.Slider(this, "字号", 262, 8, 240, 48, out _fontV);
            _font.ValueChanged += delegate { _fontV.Text = _font.Value.ToString(); PlaceTextOverlay(); RaisePreview(); };

            EditOpUi.Button(this, "撤销一笔", 10, 304, 100, delegate { UndoLast(); });
            EditOpUi.Button(this, "清除全部", 118, 304, 100, delegate { ClearAll(); });
            EditOpUi.Note(this, "在画布上拖动绘制；直线/箭头/形状为按住拖出，文字为单击后就地输入（回车换行，✓ 确认）。橡皮擦/模糊/马赛克为涂抹式，强度控制模糊半径或马赛克块大小。", 344, 84);

            UpdateSwatch();
            UpdateToolUi();
        }

        public override bool LivePreview
        {
            get { return true; }
        }

        public override bool ReusablePreview
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
            _items.Clear();
            _effects.Clear();
        }

        protected override void OnResetState()
        {
            RemoveTextOverlay();
            _items.Clear();
            _effects.Clear();
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
            if (_committed == null || _work == null || _previewBase == null) { return; }
            using (Graphics g = Graphics.FromImage(_committed))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(_previewBase, new Rectangle(0, 0, _committed.Width, _committed.Height));
            }
            ComposeWork();
        }

        // _work = _committed（含栅格效果）+ 所有矢量图元 + 正在绘制的图元。
        private void ComposeWork()
        {
            if (_work == null || _committed == null) { return; }
            using (Graphics g = Graphics.FromImage(_work))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(_committed, new Rectangle(0, 0, _work.Width, _work.Height));
            }
            using (Graphics g = Graphics.FromImage(_work))
            {
                g.ScaleTransform(_previewScale, _previewScale);
                for (int i = 0; i < _items.Count; i++) { DrawItem(g, _items[i]); }
                if (_current != null) { DrawItem(g, _current); }
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
                            _current.Points.Add(imagePoint);
                            ComposeWork();
                        }
                    }
                    else
                    {
                        _current.End = imagePoint;
                        ComposeWork();
                    }
                }
            }
            else if (action == 2)
            {
                if (_effect != null) { EndEffect(); }
                else if (_current != null)
                {
                    if (!IsFreehand(_current.Tool) && _current.End == _current.Start)
                    {
                        _current = null;
                    }
                    else
                    {
                        _items.Add(_current);
                        _current = null;
                    }
                    ComposeWork();
                }
            }
            RaisePreview();
        }

        private static bool IsFreehand(int tool)
        {
            return tool == ToolPen || tool == ToolMarker;
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

        private Item NewItem(int tool)
        {
            Item it = new Item();
            it.Tool = tool;
            it.Color = _color;
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
            _effect.Points = new List<Point>();
            _effect.Points.Add(point);

            _stampRadius = Math.Max(1, (int)Math.Round((_width.Value / 2f) * _previewScale));
            if (_stamp != null) { _stamp.Dispose(); }
            _stamp = CreateStamp(_stampRadius * 2);

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
            _effects.Add(_effect);
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

        private static Bitmap CreateStamp(int diameter)
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
            return stamp;
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
            if (_effects.Count > 0)
            {
                _effects.RemoveAt(_effects.Count - 1);
                RebuildFromEffects();
                RaisePreview();
            }
            else if (_items.Count > 0)
            {
                _items.RemoveAt(_items.Count - 1);
                ComposeWork();
                RaisePreview();
            }
        }

        private void ClearAll()
        {
            _items.Clear();
            _effects.Clear();
            _current = null;
            _effect = null;
            RebuildFromEffects();
            RaisePreview();
        }

        private void RebuildFromEffects()
        {
            if (_committed == null || _previewBase == null) { return; }
            using (Graphics g = Graphics.FromImage(_committed))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(_previewBase, new Rectangle(0, 0, _committed.Width, _committed.Height));
            }
            // 逐笔重放栅格效果（按顺序），使“撤销一笔”正确。
            for (int i = 0; i < _effects.Count; i++)
            {
                ReplayEffectPreview(_effects[i]);
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
                using (Bitmap stamp = CreateStamp((int)Math.Round(r * 2)))
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
            it.Color = _color;
            it.Width = _width.Value;
            it.Text = text;
            it.FontSize = _font.Value;
            it.Start = point;
            it.End = point;
            _items.Add(it);
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
            Color c = it.Color;
            if (it.Tool == ToolMarker) { c = Color.FromArgb(120, it.Color); }
            Pen pen = new Pen(c, Math.Max(1f, it.Width));
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
                using (SolidBrush brush = new SolidBrush(it.Tool == ToolMarker ? Color.FromArgb(120, it.Color) : it.Color))
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
            return _work;
        }

        public override Bitmap BuildResult()
        {
            if (Source == null || (_items.Count == 0 && _effects.Count == 0))
            {
                return null;
            }
            Bitmap result = Clone(Source);
            if (_effects.Count > 0) { ApplyEffectsFull(result); }
            if (_items.Count > 0)
            {
                using (Graphics g = Graphics.FromImage(result))
                {
                    for (int i = 0; i < _items.Count; i++) { DrawItem(g, _items[i]); }
                }
            }
            return result;
        }

        // 全分辨率重放栅格效果：相同（种类+强度）的笔画合并成一组，每组只做一次处理。
        private void ApplyEffectsFull(Bitmap result)
        {
            int w = result.Width, h = result.Height;
            Dictionary<int, List<Effect>> groups = new Dictionary<int, List<Effect>>();
            List<int> order = new List<int>();
            for (int i = 0; i < _effects.Count; i++)
            {
                Effect e = _effects[i];
                int key = e.Kind * 1000 + e.Strength;
                List<Effect> list;
                if (!groups.TryGetValue(key, out list))
                {
                    list = new List<Effect>();
                    groups[key] = list;
                    order.Add(key);
                }
                list.Add(e);
            }

            for (int i = 0; i < order.Count; i++)
            {
                List<Effect> strokes = groups[order[i]];
                Bitmap mask = BuildMaskFull(strokes, w, h);
                if (strokes[0].Kind == EffectErase)
                {
                    ApplyMasked(result, new Rectangle(0, 0, w, h), mask, null, true);
                }
                else
                {
                    Bitmap processed = Process(result, strokes[0].Kind, strokes[0].Strength, 1f);
                    ApplyMasked(result, new Rectangle(0, 0, w, h), mask, processed, false);
                    processed.Dispose();
                }
                mask.Dispose();
            }
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
                    using (Bitmap stamp = CreateStamp((int)Math.Round(r * 2)))
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
