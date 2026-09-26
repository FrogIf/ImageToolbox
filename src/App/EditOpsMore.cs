using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class BrushBlurOp : EditOpPanel
    {
        private class Stroke
        {
            public List<Point> Points = new List<Point>();
            public int Radius;
        }

        private ComboBox _mode;
        private TrackBar _brush, _strength;
        private Label _brushV, _strengthV;
        private List<Stroke> _strokes = new List<Stroke>();
        private Stroke _current;

        private Bitmap _previewBase;
        private Bitmap _processed;
        private Bitmap _mask;
        private Bitmap _result;
        private Bitmap _stamp;
        private int _stampRadius;
        private float _previewScale = 1f;

        public BrushBlurOp()
        {
            EditOpUi.Title(this, "画笔打码", 10);
            EditOpUi.Caption(this, "方式", 44);
            _mode = EditOpUi.Combo(this, 64, new string[] { "模糊", "马赛克" }, 0);
            _mode.SelectedIndexChanged += delegate { RecomputeProcessed(); RaisePreview(); };
            _brush = EditOpUi.Slider(this, "笔刷", 100, 2, 40, 12, out _brushV);
            _strength = EditOpUi.Slider(this, "强度", 136, 1, 80, 12, out _strengthV);
            _brush.ValueChanged += delegate { _brushV.Text = _brush.Value + "%"; RaisePreview(); };
            _strength.ValueChanged += delegate { _strengthV.Text = _strength.Value.ToString(); RecomputeProcessed(); RaisePreview(); };
            EditOpUi.Button(this, "撤销一笔", 10, 178, 100, delegate { Undo(); });
            EditOpUi.Button(this, "清除全部", 118, 178, 100, delegate { _strokes.Clear(); _current = null; RebuildFromStrokes(); RaisePreview(); });
            EditOpUi.Note(this, "在画布上按住左键涂抹，涂过的地方被模糊/马赛克。", 220, 48);
            _brushV.Text = _brush.Value + "%";
            _strengthV.Text = _strength.Value.ToString();
        }

        public override bool LivePreview
        {
            get { return true; }
        }

        protected override void OnActivate()
        {
            if (Canvas != null)
            {
                Canvas.ReadOnly = false;
                Canvas.BrushEnabled = true;
            }
            BuildPreview();
        }

        protected override void OnDeactivate()
        {
            if (Canvas != null)
            {
                Canvas.BrushEnabled = false;
            }
            _current = null;
            DisposePreviews();
        }

        public override void DisposeResources()
        {
            DisposePreviews();
            _strokes.Clear();
        }

        protected override void OnResetState()
        {
            _strokes.Clear();
            _current = null;
            if (_previewBase != null)
            {
                RebuildFromStrokes();
            }
        }

        private void DisposePreviews()
        {
            if (_previewBase != null) { _previewBase.Dispose(); _previewBase = null; }
            if (_processed != null) { _processed.Dispose(); _processed = null; }
            if (_mask != null) { _mask.Dispose(); _mask = null; }
            if (_result != null) { _result.Dispose(); _result = null; }
            if (_stamp != null) { _stamp.Dispose(); _stamp = null; }
        }

        public override int BrushRadiusSession
        {
            get
            {
                if (Source == null) { return 8; }
                int min = Math.Min(Source.Width, Source.Height);
                return Math.Max(2, (int)Math.Round(min * _brush.Value / 100f) / 2);
            }
        }

        private void BuildPreview()
        {
            DisposePreviews();
            if (Source == null)
            {
                return;
            }
            Bitmap p = ImageUtil.CreatePreview(Source, 1000);
            _previewBase = p != null ? p : ImageFilters.Clone(Source);
            _previewScale = (float)_previewBase.Width / Source.Width;
            _processed = ImageFilters.Clone(_previewBase);
            ApplyProcessed();
            _mask = new Bitmap(_previewBase.Width, _previewBase.Height, PixelFormat.Format32bppArgb);
            _result = ImageFilters.Clone(_previewBase);
            _strokes.Clear();
            _current = null;
        }

        private void ApplyProcessed()
        {
            int w = _previewBase.Width;
            int h = _previewBase.Height;
            using (Graphics g = Graphics.FromImage(_processed))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(_previewBase, 0, 0, w, h);
            }
            if (_mode.SelectedIndex == 0)
            {
                ImageFilters.GaussianBlur(_processed, Math.Max(1, (int)Math.Round(_strength.Value * _previewScale)));
            }
            else
            {
                ImageFilters.Mosaic(_processed, Math.Max(2, (int)Math.Round(_strength.Value * _previewScale)));
            }
        }

        private void RecomputeProcessed()
        {
            if (_previewBase == null)
            {
                return;
            }
            ApplyProcessed();
            RebuildFromStrokes();
        }

        private void RebuildFromStrokes()
        {
            if (_previewBase == null)
            {
                return;
            }
            using (Graphics g = Graphics.FromImage(_mask))
            {
                g.Clear(Color.Transparent);
            }
            using (Graphics g = Graphics.FromImage(_result))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(_previewBase, 0, 0, _previewBase.Width, _previewBase.Height);
            }
            if (_strokes.Count == 0)
            {
                return;
            }
            for (int i = 0; i < _strokes.Count; i++)
            {
                Stroke stroke = _strokes[i];
                int radius = Math.Max(1, (int)Math.Round(stroke.Radius * _previewScale));
                using (Bitmap stamp = CreateStamp(radius * 2))
                {
                    PointF prev = new PointF(stroke.Points[0].X * _previewScale, stroke.Points[0].Y * _previewScale);
                    DrawStamp(stamp, radius, prev);
                    for (int j = 1; j < stroke.Points.Count; j++)
                    {
                        PointF cur = new PointF(stroke.Points[j].X * _previewScale, stroke.Points[j].Y * _previewScale);
                        DrawStampsBetween(stamp, radius, prev, cur);
                        prev = cur;
                    }
                }
            }
            BlendRegion(new Rectangle(0, 0, _result.Width, _result.Height));
        }

        private void Undo()
        {
            if (_strokes.Count == 0) { return; }
            _strokes.RemoveAt(_strokes.Count - 1);
            _current = null;
            RebuildFromStrokes();
            RaisePreview();
        }

        public override void OnBrushPoint(Point imagePoint, int action)
        {
            if (Source == null || _mask == null)
            {
                return;
            }
            if (action == 0)
            {
                _current = new Stroke();
                _current.Radius = BrushRadiusSession;
                _current.Points.Add(imagePoint);
                _strokes.Add(_current);
                _stampRadius = Math.Max(1, (int)Math.Round(_current.Radius * _previewScale));
                if (_stamp != null) { _stamp.Dispose(); }
                _stamp = CreateStamp(_stampRadius * 2);
                StampSegment(imagePoint, imagePoint);
            }
            else if (action == 1 && _current != null)
            {
                Point last = _current.Points[_current.Points.Count - 1];
                if (last.X != imagePoint.X || last.Y != imagePoint.Y)
                {
                    _current.Points.Add(imagePoint);
                    StampSegment(last, imagePoint);
                }
            }
            else if (action == 2)
            {
                _current = null;
            }
            RaisePreview();
        }

        private void StampSegment(Point a, Point b)
        {
            float r = _stampRadius;
            float ax = a.X * _previewScale, ay = a.Y * _previewScale;
            float bx = b.X * _previewScale, by = b.Y * _previewScale;
            using (Graphics g = Graphics.FromImage(_mask))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                PointF prev = new PointF(ax, ay);
                g.DrawImage(_stamp, ax - r, ay - r, r * 2f, r * 2f);
                DrawStampsBetween(g, _stamp, r, prev, new PointF(bx, by));
            }

            int minX = (int)Math.Floor(Math.Min(ax, bx) - r) - 1;
            int minY = (int)Math.Floor(Math.Min(ay, by) - r) - 1;
            int maxX = (int)Math.Ceiling(Math.Max(ax, bx) + r) + 1;
            int maxY = (int)Math.Ceiling(Math.Max(ay, by) + r) + 1;
            BlendRegion(new Rectangle(minX, minY, maxX - minX, maxY - minY));
        }

        private void DrawStampsBetween(Bitmap stamp, int radius, PointF a, PointF b)
        {
            using (Graphics g = Graphics.FromImage(_mask))
            {
                DrawStampsBetween(g, stamp, radius, a, b);
            }
        }

        private void DrawStampsBetween(Graphics g, Bitmap stamp, float radius, PointF a, PointF b)
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

        private void DrawStamp(Bitmap stamp, float radius, PointF center)
        {
            using (Graphics g = Graphics.FromImage(_mask))
            {
                g.DrawImage(stamp, center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
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

        private void BlendRegion(Rectangle rect)
        {
            Rectangle bounds = new Rectangle(0, 0, _result.Width, _result.Height);
            rect = Rectangle.Intersect(rect, bounds);
            if (rect.Width <= 0 || rect.Height <= 0) { return; }

            BitmapData bd = _previewBase.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData ed = _processed.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData md = _mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData rd = _result.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int rw = rect.Width;
                int rh = rect.Height;
                int bstride = bd.Stride, estride = ed.Stride, mstride = md.Stride, rstride = rd.Stride;
                byte[] bbuf = new byte[bstride * rh];
                byte[] ebuf = new byte[estride * rh];
                byte[] mbuf = new byte[mstride * rh];
                byte[] rbuf = new byte[rstride * rh];
                System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, bbuf, 0, bbuf.Length);
                System.Runtime.InteropServices.Marshal.Copy(ed.Scan0, ebuf, 0, ebuf.Length);
                System.Runtime.InteropServices.Marshal.Copy(md.Scan0, mbuf, 0, mbuf.Length);
                System.Runtime.InteropServices.Marshal.Copy(rd.Scan0, rbuf, 0, rbuf.Length);

                for (int y = 0; y < rh; y++)
                {
                    int bo = y * bstride, eo = y * estride, mo = y * mstride, ro = y * rstride;
                    for (int x = 0; x < rw; x++)
                    {
                        int i = bo + x * 4, ei = eo + x * 4, mi = mo + x * 4, o = ro + x * 4;
                        int m = mbuf[mi + 3];
                        if (m == 0) { continue; }
                        rbuf[o] = (byte)(bbuf[i] + (ebuf[ei] - bbuf[i]) * m / 255);
                        rbuf[o + 1] = (byte)(bbuf[i + 1] + (ebuf[ei + 1] - bbuf[i + 1]) * m / 255);
                        rbuf[o + 2] = (byte)(bbuf[i + 2] + (ebuf[ei + 2] - bbuf[i + 2]) * m / 255);
                        rbuf[o + 3] = bbuf[i + 3];
                    }
                }
                System.Runtime.InteropServices.Marshal.Copy(rbuf, 0, rd.Scan0, rbuf.Length);
            }
            finally
            {
                _previewBase.UnlockBits(bd);
                _processed.UnlockBits(ed);
                _mask.UnlockBits(md);
                _result.UnlockBits(rd);
            }
        }

        public override Bitmap RenderPreview()
        {
            if (_result == null) { return null; }
            return ImageFilters.Clone(_result);
        }

        public override Bitmap BuildResult()
        {
            if (Source == null || _strokes.Count == 0) { return null; }
            return RenderFull(Source);
        }

        private Bitmap RenderFull(Bitmap baseImage)
        {
            float scale = (float)baseImage.Width / Source.Width;
            int w = baseImage.Width;
            int h = baseImage.Height;
            Bitmap processed = ImageFilters.Clone(baseImage);
            if (_mode.SelectedIndex == 0)
            {
                ImageFilters.GaussianBlur(processed, Math.Max(1, (int)Math.Round(_strength.Value * scale)));
            }
            else
            {
                ImageFilters.Mosaic(processed, Math.Max(2, (int)Math.Round(_strength.Value * scale)));
            }

            Bitmap mask = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(mask))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.Clear(Color.Transparent);
                for (int i = 0; i < _strokes.Count; i++)
                {
                    Stroke stroke = _strokes[i];
                    int radius = Math.Max(1, (int)Math.Round(stroke.Radius * scale));
                    using (Bitmap stamp = CreateStamp(radius * 2))
                    {
                        PointF prev = new PointF(stroke.Points[0].X * scale, stroke.Points[0].Y * scale);
                        g.DrawImage(stamp, prev.X - radius, prev.Y - radius, radius * 2f, radius * 2f);
                        for (int j = 1; j < stroke.Points.Count; j++)
                        {
                            PointF cur = new PointF(stroke.Points[j].X * scale, stroke.Points[j].Y * scale);
                            DrawStampsBetween(g, stamp, radius, prev, cur);
                            prev = cur;
                        }
                    }
                }
            }

            Bitmap result = ImageFilters.MaskBlend(baseImage, processed, mask);
            processed.Dispose();
            mask.Dispose();
            return result;
        }
    }

    public class MattingOp : EditOpPanel
    {
        private ComboBox _mode;
        private TrackBar _tolerance;
        private Label _toleranceV;
        private CheckBox _invert;
        private Color _key = Color.White;
        private bool _hasKey;
        private Point _seed = Point.Empty;
        private bool _hasSeed;

        public MattingOp()
        {
            EditOpUi.Title(this, "抠图", 10);
            EditOpUi.Caption(this, "方式", 44);
            _mode = EditOpUi.Combo(this, 64, new string[] { "颜色阈值（全局）", "魔术棒（连续）" }, 0);
            _mode.SelectedIndexChanged += delegate { RaisePreview(); };
            _tolerance = EditOpUi.Slider(this, "容差", 100, 1, 120, 30, out _toleranceV);
            _tolerance.ValueChanged += delegate { _toleranceV.Text = _tolerance.Value.ToString(); RaisePreview(); };
            _invert = new CheckBox();
            _invert.Text = "反转（保留所选颜色）";
            _invert.Location = new Point(10, 140);
            _invert.AutoSize = true;
            _invert.CheckedChanged += delegate { RaisePreview(); };
            Controls.Add(_invert);
            EditOpUi.Button(this, "清除取样", 10, 170, 100, delegate { _hasKey = false; _hasSeed = false; RaisePreview(); });
            EditOpUi.Note(this, "点击左侧图片取色 / 选取连通区域，颜色相近处变透明。", 212, 48);
        }

        protected override void OnActivate()
        {
            if (Canvas != null) { Canvas.ReadOnly = true; }
        }

        public override void OnCanvasClick(Point imagePoint)
        {
            if (Source == null) { return; }
            int x = Math.Max(0, Math.Min(Source.Width - 1, imagePoint.X));
            int y = Math.Max(0, Math.Min(Source.Height - 1, imagePoint.Y));
            if (_mode.SelectedIndex == 0)
            {
                _key = Source.GetPixel(x, y);
                _hasKey = true;
            }
            else
            {
                _seed = new Point(x, y);
                _hasSeed = true;
            }
            RaisePreview();
        }

        protected override void OnResetState()
        {
            _hasKey = false;
            _hasSeed = false;
        }

        private Bitmap Render(Bitmap baseImage, float scale)
        {
            if (_mode.SelectedIndex == 0)
            {
                if (!_hasKey) { return null; }
                return ImageMatting.ByColor(baseImage, _key, _tolerance.Value, _invert.Checked);
            }
            if (!_hasSeed) { return null; }
            int sx = (int)Math.Round(_seed.X * scale);
            int sy = (int)Math.Round(_seed.Y * scale);
            return ImageMatting.MagicWand(baseImage, sx, sy, _tolerance.Value, _invert.Checked);
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null) { return null; }
            float scale = (float)PreviewSource.Width / Source.Width;
            return Render(PreviewSource, scale);
        }

        public override Bitmap BuildResult()
        {
            if (Source == null) { return null; }
            return Render(Source, 1f);
        }
    }

    public class CropOp : EditOpPanel
    {
        private int _pending;
        private Rectangle _selection = Rectangle.Empty;

        public CropOp()
        {
            EditOpUi.Title(this, "裁剪 / 旋转", 10);
            EditOpUi.Note(this, "先在画布上框选，再点「裁剪选区」；旋转/翻转点一次执行一次，可连续点击叠加。", 44, 48);

            EditOpUi.Button(this, "裁剪选区", 10, 100, 90, delegate { Do(5); });
            EditOpUi.Button(this, "顺 90°", 106, 100, 90, delegate { Do(1); });
            EditOpUi.Button(this, "逆 90°", 202, 100, 90, delegate { Do(2); });
            EditOpUi.Button(this, "水平翻转", 10, 138, 90, delegate { Do(3); });
            EditOpUi.Button(this, "垂直翻转", 106, 138, 90, delegate { Do(4); });
            EditOpUi.Button(this, "清除选区", 202, 138, 90, delegate { Clear(); });
            EditOpUi.Button(this, "重置（回到进入时）", 10, 176, 282, delegate { RequestReset(); });

            EditOpUi.Note(this, "旋转以 90° 为单位，可多次点击累加；「重置」撤销本次进入后的所有裁剪/旋转。", 218, 48);
        }

        public override bool WantsEntrySnapshot
        {
            get { return true; }
        }

        private void Do(int mode)
        {
            _pending = mode;
            RequestApply();
        }

        private void Clear()
        {
            _selection = Rectangle.Empty;
            if (Canvas != null) { Canvas.Selection = Rectangle.Empty; }
        }

        protected override void OnActivate()
        {
            if (Canvas != null)
            {
                Canvas.BrushEnabled = false;
                Canvas.ReadOnly = false;
            }
        }

        public override void OnCanvasSelection(Rectangle imageRect)
        {
            _selection = imageRect;
        }

        protected override void OnResetState()
        {
            _pending = 0;
            _selection = Rectangle.Empty;
        }

        private static Bitmap Flip(Bitmap src, bool horizontal)
        {
            Bitmap dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                if (horizontal) { g.Transform = new System.Drawing.Drawing2D.Matrix(-1, 0, 0, 1, src.Width, 0); }
                else { g.Transform = new System.Drawing.Drawing2D.Matrix(1, 0, 0, -1, 0, src.Height); }
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            return dst;
        }

        private static Bitmap Rotate90(Bitmap src, bool clockwise)
        {
            Bitmap dst = new Bitmap(src.Height, src.Width, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                if (clockwise) { g.TranslateTransform(dst.Width, 0); g.RotateTransform(90f); }
                else { g.TranslateTransform(0, dst.Height); g.RotateTransform(-90f); }
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            return dst;
        }

        public override Bitmap RenderPreview()
        {
            return null;
        }

        public override Bitmap BuildResult()
        {
            if (Source == null) { return null; }
            switch (_pending)
            {
                case 1: return Rotate90(Source, true);
                case 2: return Rotate90(Source, false);
                case 3: return Flip(Source, true);
                case 4: return Flip(Source, false);
                case 5:
                    Rectangle r = Rectangle.Intersect(_selection, new Rectangle(0, 0, Source.Width, Source.Height));
                    if (r.Width < 1 || r.Height < 1) { return null; }
                    return ImageLayout.Crop(Source, r);
                default:
                    return null;
            }
        }
    }

    public class IdPhotoOp : EditOpPanel
    {
        private static readonly int[] Widths = { 295, 413, 260, 390, 390 };
        private static readonly int[] Heights = { 413, 579, 378, 567, 472 };
        private static readonly string[] Names = { "一寸 295×413", "二寸 413×579", "小一寸 260×378", "大一寸 390×567", "小二寸 390×472" };
        private static readonly int[] PaperW = { 1500, 1800, 2480 };
        private static readonly int[] PaperH = { 1050, 1200, 3508 };
        private static readonly string[] PaperNames = { "5 寸 (1500×1050)", "6 寸 (1800×1200)", "A4 (2480×3508)" };

        private ComboBox _size;
        private ComboBox _mode;
        private ComboBox _bg;
        private ComboBox _output;
        private ComboBox _paper;
        private TrackBar _count;
        private Label _countV;
        private CheckBox _cut;
        private static readonly Color[] BgColors = { Color.White, Color.FromArgb(67, 142, 219), Color.FromArgb(216, 0, 0), Color.FromArgb(30, 30, 30) };

        public IdPhotoOp()
        {
            EditOpUi.Title(this, "证件照", 10);
            EditOpUi.Caption(this, "尺寸", 44);
            _size = EditOpUi.Combo(this, 64, Names, 0);
            _size.SelectedIndexChanged += delegate { RaisePreview(); };
            EditOpUi.Caption(this, "模式", 100);
            _mode = EditOpUi.Combo(this, 120, new string[] { "裁剪填满", "完整留白" }, 0);
            _mode.SelectedIndexChanged += delegate { RaisePreview(); };
            EditOpUi.Caption(this, "背景色", 156);
            _bg = EditOpUi.Combo(this, 176, new string[] { "白色", "蓝色", "红色", "深灰" }, 0);
            _bg.SelectedIndexChanged += delegate { RaisePreview(); };

            EditOpUi.Caption(this, "输出", 212);
            _output = EditOpUi.Combo(this, 232, new string[] { "单张证件照", "排版到相纸" }, 0);
            _output.SelectedIndexChanged += delegate { UpdateMode(); RaisePreview(); };
            EditOpUi.Caption(this, "相纸", 268);
            _paper = EditOpUi.Combo(this, 288, PaperNames, 1);
            _paper.SelectedIndexChanged += delegate { RaisePreview(); };
            _count = EditOpUi.Slider(this, "张数", 324, 0, 40, 0, out _countV);
            _count.ValueChanged += delegate { _countV.Text = _count.Value == 0 ? "自动" : _count.Value.ToString(); RaisePreview(); };
            _cut = new CheckBox();
            _cut.Text = "加裁剪线";
            _cut.Location = new Point(10, 362);
            _cut.AutoSize = true;
            _cut.CheckedChanged += delegate { RaisePreview(); };
            Controls.Add(_cut);
            EditOpUi.Note(this, "单张证件照会替换当前图；排版到相纸输出整张相纸。", 392, 48);
            UpdateMode();
        }

        private void UpdateMode()
        {
            bool sheet = _output.SelectedIndex == 1;
            _paper.Visible = sheet;
            _count.Visible = sheet;
            _countV.Visible = sheet;
            _cut.Visible = sheet;
            _countV.Text = _count.Value == 0 ? "自动" : _count.Value.ToString();
        }

        private Bitmap Build(Bitmap baseImage)
        {
            int i = Math.Max(0, _size.SelectedIndex);
            bool fill = _mode.SelectedIndex == 0;
            Color bg = BgColors[Math.Max(0, _bg.SelectedIndex)];
            Bitmap photo = ImageLayout.BuildIdPhoto(baseImage, Widths[i], Heights[i], fill, bg);
            if (_output.SelectedIndex == 0)
            {
                return photo;
            }
            int p = Math.Max(0, _paper.SelectedIndex);
            Bitmap sheet = ImageLayout.BuildSheet(photo, PaperW[p], PaperH[p], 99, _count.Value, 8, 20, Color.White, _cut.Checked);
            photo.Dispose();
            return sheet;
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null) { return null; }
            return Build(PreviewSource);
        }

        public override Bitmap BuildResult()
        {
            if (Source == null) { return null; }
            return Build(Source);
        }
    }

    public class ColorMatchOp : EditOpPanel
    {
        private Bitmap _refImage;
        private ImageCanvas _refCanvas;
        private TrackBar _bright, _contrast, _sat, _temp, _tint;
        private Label _brightV, _contrastV, _satV, _tempV, _tintV;

        public ColorMatchOp()
        {
            EditOpUi.Title(this, "取色配色", 10);
            EditOpUi.Button(this, "选择参考图", 10, 40, 110, delegate { BrowseRef(); });

            _refCanvas = new ImageCanvas();
            _refCanvas.Location = new Point(130, 40);
            _refCanvas.Size = new Size(170, 90);
            _refCanvas.ReadOnly = true;
            Controls.Add(_refCanvas);

            EditOpUi.Button(this, "取样生成参数", 10, 140, 130, delegate { Sample(); });
            _bright = EditOpUi.Slider(this, "亮度", 180, -100, 100, 0, out _brightV);
            _contrast = EditOpUi.Slider(this, "对比度", 216, -100, 100, 0, out _contrastV);
            _sat = EditOpUi.Slider(this, "饱和度", 252, 0, 200, 100, out _satV);
            _temp = EditOpUi.Slider(this, "色温", 288, -100, 100, 0, out _tempV);
            _tint = EditOpUi.Slider(this, "色调", 324, -100, 100, 0, out _tintV);
            EditOpUi.Note(this, "把参考图的整体影调/色调迁移到当前图片。", 362, 40);

            _bright.ValueChanged += OnChange;
            _contrast.ValueChanged += OnChange;
            _sat.ValueChanged += OnChange;
            _temp.ValueChanged += OnChange;
            _tint.ValueChanged += OnChange;
        }

        private void OnChange(object sender, EventArgs e)
        {
            _brightV.Text = _bright.Value.ToString();
            _contrastV.Text = _contrast.Value.ToString();
            _satV.Text = _sat.Value.ToString();
            _tempV.Text = _temp.Value.ToString();
            _tintV.Text = _tint.Value.ToString();
            RaisePreview();
        }

        protected override void OnResetState()
        {
            _bright.Value = 0;
            _contrast.Value = 0;
            _sat.Value = 100;
            _temp.Value = 0;
            _tint.Value = 0;
        }

        private void BrowseRef()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择参考图";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
            try
            {
                Bitmap b = ImageUtil.LoadImage(dialog.FileName);
                if (_refImage != null) { _refImage.Dispose(); }
                _refImage = b;
                _refCanvas.SetImage(_refImage);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载参考图：" + ex.Message);
            }
        }

        private void Sample()
        {
            if (_refImage == null || Source == null) { return; }
            ImageEffects.ColorStats refStats = ImageEffects.MeasureStats(_refImage, 256);
            double[] p = ImageEffects.EstimateAdjustment(Source, refStats, 3);
            _bright.Value = Clamp(p[0] * 100.0);
            _contrast.Value = Clamp(p[1] * 100.0);
            _sat.Value = Math.Max(0, Math.Min(200, (int)Math.Round(p[2] * 100.0)));
            _temp.Value = Clamp(p[3] * 100.0);
            _tint.Value = Clamp(p[4] * 100.0);
            RaisePreview();
        }

        private static int Clamp(double v)
        {
            return Math.Max(-100, Math.Min(100, (int)Math.Round(v)));
        }

        private double[] Params()
        {
            return new double[]
            {
                _bright.Value / 100.0, _contrast.Value / 100.0, _sat.Value / 100.0,
                _temp.Value / 100.0, _tint.Value / 100.0
            };
        }

        public override void DisposeResources()
        {
            if (_refImage != null) { _refImage.Dispose(); _refImage = null; }
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null) { return null; }
            return ImageUtil.ApplyColorMatrix(PreviewSource, ImageEffects.MatrixFromParams(Params()));
        }

        public override Bitmap BuildResult()
        {
            if (Source == null) { return null; }
            return ImageUtil.ApplyColorMatrix(Source, ImageEffects.MatrixFromParams(Params()));
        }
    }

    public class LocalOverlayOp : EditOpPanel
    {
        private Bitmap _overlay;
        private ImageCanvas _overlayCanvas;
        private ListBox _regions;
        private List<Rectangle> _list = new List<Rectangle>();
        private TrackBar _feather, _opacity;
        private Label _featherV, _opacityV;

        public LocalOverlayOp()
        {
            EditOpUi.Title(this, "局部覆盖", 10);
            EditOpUi.Button(this, "选择覆盖图", 10, 40, 110, delegate { BrowseOverlay(); });
            _overlayCanvas = new ImageCanvas();
            _overlayCanvas.Location = new Point(130, 40);
            _overlayCanvas.Size = new Size(170, 90);
            _overlayCanvas.ReadOnly = true;
            Controls.Add(_overlayCanvas);

            _regions = new ListBox();
            _regions.Location = new Point(10, 140);
            _regions.Size = new Size(290, 90);
            _regions.IntegralHeight = false;
            Controls.Add(_regions);

            EditOpUi.Button(this, "添加选区", 10, 240, 90, delegate { AddRegion(); });
            EditOpUi.Button(this, "移除", 106, 240, 70, delegate { RemoveRegion(); });
            EditOpUi.Button(this, "清空", 182, 240, 70, delegate { _list.Clear(); RefreshRegions(); RaisePreview(); });

            _feather = EditOpUi.Slider(this, "羽化", 282, 0, 80, 12, out _featherV);
            _opacity = EditOpUi.Slider(this, "不透明", 318, 0, 100, 100, out _opacityV);
            _feather.ValueChanged += delegate { _featherV.Text = _feather.Value.ToString(); RaisePreview(); };
            _opacity.ValueChanged += delegate { _opacityV.Text = _opacity.Value + "%"; RaisePreview(); };
            EditOpUi.Note(this, "在左侧图片框选后「添加选区」，可加多个；覆盖图按比例映射。", 356, 48);
        }

        protected override void OnActivate()
        {
            if (Canvas != null) { Canvas.ReadOnly = false; }
        }

        private void BrowseOverlay()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择覆盖图";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
            try
            {
                Bitmap b = ImageUtil.LoadImage(dialog.FileName);
                if (_overlay != null) { _overlay.Dispose(); }
                _overlay = b;
                _overlayCanvas.SetImage(_overlay);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载覆盖图：" + ex.Message);
            }
        }

        private void AddRegion()
        {
            if (Canvas == null || Source == null) { return; }
            Rectangle r = Rectangle.Intersect(Canvas.Selection, new Rectangle(0, 0, Source.Width, Source.Height));
            if (r.Width < 1 || r.Height < 1) { return; }
            _list.Add(r);
            RefreshRegions();
            RaisePreview();
        }

        private void RemoveRegion()
        {
            int i = _regions.SelectedIndex;
            if (i < 0 || i >= _list.Count) { return; }
            _list.RemoveAt(i);
            RefreshRegions();
            RaisePreview();
        }

        private void RefreshRegions()
        {
            _regions.Items.Clear();
            for (int i = 0; i < _list.Count; i++)
            {
                _regions.Items.Add("区域 " + (i + 1) + "：" + _list[i].Width + "x" + _list[i].Height);
            }
        }

        public override void DisposeResources()
        {
            if (_overlay != null) { _overlay.Dispose(); _overlay = null; }
        }

        protected override void OnResetState()
        {
            _list.Clear();
            RefreshRegions();
        }

        private Bitmap Compose(Bitmap target)
        {
            if (_overlay == null || _list.Count == 0) { return null; }
            float scale = (float)target.Width / Source.Width;
            Size targetSize = new Size(target.Width, target.Height);
            Size overlaySize = new Size(_overlay.Width, _overlay.Height);

            Bitmap effect = ImageFilters.Clone(target);
            using (Graphics g = Graphics.FromImage(effect))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                for (int i = 0; i < _list.Count; i++)
                {
                    Rectangle sc = new Rectangle(
                        (int)Math.Round(_list[i].X * scale), (int)Math.Round(_list[i].Y * scale),
                        (int)Math.Round(_list[i].Width * scale), (int)Math.Round(_list[i].Height * scale));
                    Rectangle src = ImageUtil.MapRegion(sc, targetSize, overlaySize);
                    src = Rectangle.Intersect(src, new Rectangle(0, 0, _overlay.Width, _overlay.Height));
                    if (src.Width < 1 || src.Height < 1) { continue; }
                    Rectangle dest = ImageUtil.MapRegion(src, overlaySize, targetSize);
                    g.DrawImage(_overlay, dest, src, GraphicsUnit.Pixel);
                }
            }

            Bitmap mask = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(mask))
            {
                g.Clear(Color.Transparent);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, 255, 255, 255)))
                {
                    for (int i = 0; i < _list.Count; i++)
                    {
                        Rectangle r = new Rectangle(
                            (int)Math.Round(_list[i].X * scale), (int)Math.Round(_list[i].Y * scale),
                            (int)Math.Round(_list[i].Width * scale), (int)Math.Round(_list[i].Height * scale));
                        g.FillRectangle(brush, r);
                    }
                }
            }
            int feather = Math.Max(0, (int)Math.Round(_feather.Value * scale));
            if (feather > 0) { ImageFilters.GaussianBlur(mask, feather); }
            if (_opacity.Value < 100)
            {
                ScaleAlpha(mask, _opacity.Value / 100f);
            }
            Bitmap result = ImageFilters.MaskBlend(target, effect, mask);
            effect.Dispose();
            mask.Dispose();
            return result;
        }

        private static void ScaleAlpha(Bitmap mask, float factor)
        {
            int w = mask.Width;
            int h = mask.Height;
            BitmapData data = mask.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] buf = new byte[stride * h];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                for (int i = 3; i < buf.Length; i += 4)
                {
                    buf[i] = (byte)(buf[i] * factor + 0.5f);
                }
                System.Runtime.InteropServices.Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally
            {
                mask.UnlockBits(data);
            }
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null) { return null; }
            return Compose(PreviewSource);
        }

        public override Bitmap BuildResult()
        {
            if (Source == null) { return null; }
            return Compose(Source);
        }
    }

    public class LayerComposeOp : EditOpPanel
    {
        private class Layer
        {
            public string Path;
            public Bitmap Image;
            public BlendMode Mode = BlendMode.Normal;
            public float Opacity = 1f;
        }

        private ListBox _listBox;
        private ComboBox _mode;
        private TrackBar _opacity;
        private Label _opacityV;
        private List<Layer> _layers = new List<Layer>();
        private bool _sync;

        public LayerComposeOp()
        {
            EditOpUi.Title(this, "图层合成", 10);
            EditOpUi.Button(this, "添加图层", 10, 40, 100, delegate { Add(); });
            EditOpUi.Button(this, "移除", 118, 40, 70, delegate { Remove(); });
            EditOpUi.Button(this, "上移", 196, 40, 50, delegate { MoveLayer(-1); });
            EditOpUi.Button(this, "下移", 252, 40, 50, delegate { MoveLayer(1); });

            _listBox = new ListBox();
            _listBox.Location = new Point(10, 78);
            _listBox.Size = new Size(290, 120);
            _listBox.IntegralHeight = false;
            _listBox.SelectedIndexChanged += delegate { SyncLayer(); };
            Controls.Add(_listBox);

            EditOpUi.Caption(this, "混合模式", 208);
            _mode = EditOpUi.Combo(this, 228, ImageBlend.ModeNames, 0);
            _mode.SelectedIndexChanged += delegate { ApplyLayerProp(); };
            _opacity = EditOpUi.Slider(this, "不透明", 264, 0, 100, 100, out _opacityV);
            _opacity.ValueChanged += delegate { _opacityV.Text = _opacity.Value + "%"; ApplyLayerProp(); };
            EditOpUi.Note(this, "图层自下而上叠加到当前图，自动缩放到相同尺寸。", 302, 40);
        }

        private void Add()
        {
            if (Source == null) { return; }
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择图层图片";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            dialog.Multiselect = true;
            if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
            try
            {
                for (int i = 0; i < dialog.FileNames.Length; i++)
                {
                    Layer layer = new Layer();
                    layer.Path = dialog.FileNames[i];
                    layer.Image = ImageUtil.LoadImage(dialog.FileNames[i]);
                    _layers.Add(layer);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图层：" + ex.Message);
            }
            Refresh(_layers.Count - 1);
            RaisePreview();
        }

        private void Remove()
        {
            int i = _listBox.SelectedIndex;
            if (i < 0 || i >= _layers.Count) { return; }
            if (_layers[i].Image != null) { _layers[i].Image.Dispose(); }
            _layers.RemoveAt(i);
            Refresh(Math.Min(i, _layers.Count - 1));
            RaisePreview();
        }

        private void MoveLayer(int delta)
        {
            int i = _listBox.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _layers.Count) { return; }
            Layer t = _layers[i];
            _layers[i] = _layers[j];
            _layers[j] = t;
            Refresh(j);
            RaisePreview();
        }

        private void Refresh(int select)
        {
            _sync = true;
            _listBox.Items.Clear();
            for (int i = 0; i < _layers.Count; i++)
            {
                _listBox.Items.Add((i + 1) + ". " + Path.GetFileName(_layers[i].Path));
            }
            if (select >= 0 && select < _layers.Count) { _listBox.SelectedIndex = select; }
            _sync = false;
            SyncLayer();
        }

        private void SyncLayer()
        {
            int i = _listBox.SelectedIndex;
            bool has = i >= 0 && i < _layers.Count;
            _mode.Enabled = has;
            _opacity.Enabled = has;
            if (!has) { return; }
            _sync = true;
            _mode.SelectedIndex = (int)_layers[i].Mode;
            _opacity.Value = (int)Math.Round(_layers[i].Opacity * 100f);
            _opacityV.Text = _opacity.Value + "%";
            _sync = false;
        }

        private void ApplyLayerProp()
        {
            if (_sync) { return; }
            int i = _listBox.SelectedIndex;
            if (i < 0 || i >= _layers.Count) { return; }
            _layers[i].Mode = (BlendMode)Math.Max(0, _mode.SelectedIndex);
            _layers[i].Opacity = _opacity.Value / 100f;
            RaisePreview();
        }

        public override void DisposeResources()
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].Image != null) { _layers[i].Image.Dispose(); }
            }
            _layers.Clear();
            _listBox.Items.Clear();
        }

        protected override void OnResetState()
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].Image != null) { _layers[i].Image.Dispose(); }
            }
            _layers.Clear();
            _listBox.Items.Clear();
        }

        private Bitmap Composite(Bitmap baseImage)
        {
            if (_layers.Count == 0) { return null; }
            Bitmap current = ImageFilters.Clone(baseImage);
            for (int i = 0; i < _layers.Count; i++)
            {
                Bitmap next = ImageBlend.Blend(current, _layers[i].Image, _layers[i].Mode, _layers[i].Opacity);
                current.Dispose();
                current = next;
            }
            return current;
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null) { return null; }
            return Composite(PreviewSource);
        }

        public override Bitmap BuildResult()
        {
            if (Source == null) { return null; }
            return Composite(Source);
        }
    }

    public class ColorToolOp : EditOpPanel
    {
        private Panel _swatch;
        private Label _info;
        private FlowLayoutPanel _palette;

        public ColorToolOp()
        {
            EditOpUi.Title(this, "颜色工具", 10);
            _swatch = new Panel();
            _swatch.Location = new Point(10, 44);
            _swatch.Size = new Size(60, 44);
            _swatch.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_swatch);
            _info = new Label();
            _info.Location = new Point(80, 46);
            _info.Size = new Size(220, 60);
            _info.Text = "点击左侧图片取色";
            Controls.Add(_info);

            EditOpUi.Button(this, "提取主色", 10, 100, 100, delegate { Extract(); });
            _palette = new FlowLayoutPanel();
            _palette.Location = new Point(10, 140);
            _palette.Size = new Size(290, 300);
            _palette.AutoScroll = true;
            _palette.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_palette);
        }

        public override bool CanApply
        {
            get { return false; }
        }

        protected override void OnActivate()
        {
            if (Canvas != null) { Canvas.ReadOnly = true; }
        }

        public override void OnCanvasClick(Point imagePoint)
        {
            if (Source == null) { return; }
            int x = Math.Max(0, Math.Min(Source.Width - 1, imagePoint.X));
            int y = Math.Max(0, Math.Min(Source.Height - 1, imagePoint.Y));
            Color c = Source.GetPixel(x, y);
            _swatch.BackColor = c;
            double h, s, v;
            PaletteExtractor.RgbToHsv(c, out h, out s, out v);
            _info.Text = "RGB " + c.R + "," + c.G + "," + c.B + "\r\nHEX " + PaletteExtractor.Hex(c) +
                "\r\nHSV " + h.ToString("0") + "°, " + (s * 100).ToString("0") + "%, " + (v * 100).ToString("0") + "%";
        }

        private void Extract()
        {
            if (Source == null) { return; }
            List<Color> colors = PaletteExtractor.Extract(Source, 8, 160);
            _palette.Controls.Clear();
            for (int i = 0; i < colors.Count; i++)
            {
                Panel block = new Panel();
                block.Size = new Size(48, 48);
                block.Margin = new Padding(2);
                block.BackColor = colors[i];
                block.BorderStyle = BorderStyle.FixedSingle;
                _palette.Controls.Add(block);
            }
        }
    }

    public class CompareOp : EditOpPanel
    {
        private Bitmap _b;
        private ComboBox _mode;
        private TrackBar _param;
        private Label _paramV;

        public CompareOp()
        {
            EditOpUi.Title(this, "图像对比", 10);
            EditOpUi.Button(this, "选择对比图 B", 10, 40, 120, delegate { BrowseB(); });
            EditOpUi.Caption(this, "方式", 80);
            _mode = EditOpUi.Combo(this, 100, new string[] { "左右并排", "上下并排", "滑块对比", "差异高亮" }, 0);
            _mode.SelectedIndexChanged += delegate { RaisePreview(); };
            _param = EditOpUi.Slider(this, "参数", 136, 0, 400, 150, out _paramV);
            _param.ValueChanged += delegate { _paramV.Text = _param.Value.ToString(); RaisePreview(); };
            EditOpUi.Note(this, "当前图作为 A，与所选 B 对比（查看用，不改变图片）。", 174, 48);
        }

        public override bool CanApply
        {
            get { return false; }
        }

        protected override void OnActivate()
        {
            if (Canvas != null) { Canvas.ReadOnly = true; }
        }

        private void BrowseB()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择对比图 B";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
            try
            {
                Bitmap b = ImageUtil.LoadImage(dialog.FileName);
                if (_b != null) { _b.Dispose(); }
                _b = b;
                RaisePreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图片：" + ex.Message);
            }
        }

        public override void DisposeResources()
        {
            if (_b != null) { _b.Dispose(); _b = null; }
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null || _b == null) { return null; }
            switch (_mode.SelectedIndex)
            {
                case 1: return ImageCompare.SideBySide(PreviewSource, _b, true, 8);
                case 2: return ImageCompare.Slider(PreviewSource, _b, _param.Value / 100f);
                case 3: return ImageCompare.Difference(PreviewSource, _b, _param.Value / 100f);
                default: return ImageCompare.SideBySide(PreviewSource, _b, false, 8);
            }
        }
    }

    public class InfoOp : EditOpPanel
    {
        private HistogramView _hist;
        private Label _stats;

        public InfoOp()
        {
            EditOpUi.Title(this, "图片信息", 10);
            _stats = new Label();
            _stats.Location = new Point(10, 44);
            _stats.Size = new Size(300, 150);
            Controls.Add(_stats);
            _hist = new HistogramView();
            _hist.Location = new Point(10, 200);
            _hist.Size = new Size(300, 150);
            Controls.Add(_hist);
            EditOpUi.Note(this, "查看当前图片的尺寸与色彩统计（不改变图片）。", 358, 40);
        }

        public override bool CanApply
        {
            get { return false; }
        }

        protected override void OnActivate()
        {
            if (Canvas != null) { Canvas.ReadOnly = true; }
            if (Source == null) { return; }
            ImageEffects.ColorStats s = ImageEffects.MeasureStats(Source, 512);
            _stats.Text =
                "尺寸：" + Source.Width + " x " + Source.Height + " 像素\r\n" +
                "亮度：" + s.Bright.ToString("0.000") + "\r\n" +
                "对比度：" + s.Contrast.ToString("0.000") + "\r\n" +
                "饱和度：" + s.Sat.ToString("0.000") + "\r\n" +
                "冷暖：" + s.WarmRatio.ToString("0.00") + "\r\n" +
                "品绿：" + s.TintRatio.ToString("0.00");
            _hist.SetData(ImageTuning.Histogram(Source));
            RaisePreview();
        }
    }
}
