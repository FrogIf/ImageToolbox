using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class BrushBlurPage : ToolPage
    {
        private const int PreviewSize = 900;

        private TextBox _imageBox;
        private ComboBox _toolBox;
        private TrackBar _brushBar;
        private Label _brushValue;
        private Label _strengthLabel;
        private TrackBar _strengthBar;
        private Label _strengthValue;
        private Label _status;
        private ImageCanvas _canvas;
        private Timer _debounce;

        private Bitmap _source;
        private Bitmap _previewBase;
        private Bitmap _previewProcessed;
        private Bitmap _resultImage;
        private Bitmap _maskPreview;
        private Bitmap _stamp;
        private int _stampRadius;
        private float _previewScale = 1f;
        private List<Stroke> _strokes = new List<Stroke>();
        private Stroke _current;
        private bool _configuring;
        private string _sourcePath;

        private class Stroke
        {
            public List<Point> Points = new List<Point>();
            public int Radius;
        }

        public BrushBlurPage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1120, 680);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "画笔打码"; }
        }

        public override void Shutdown()
        {
            if (_source != null) { _source.Dispose(); }
            if (_previewBase != null) { _previewBase.Dispose(); }
            if (_previewProcessed != null) { _previewProcessed.Dispose(); }
            if (_resultImage != null) { _resultImage.Dispose(); }
            if (_maskPreview != null) { _maskPreview.Dispose(); }
            if (_stamp != null) { _stamp.Dispose(); }
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
            Controls.Add(root);

            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.AutoScroll = true;
            left.Margin = new Padding(3, 3, 3, 3);
            root.Controls.Add(left, 0, 0);

            Label imageLabel = new Label();
            imageLabel.Text = "图片";
            imageLabel.Location = new Point(10, 14);
            imageLabel.AutoSize = true;
            left.Controls.Add(imageLabel);

            Button browse = new Button();
            browse.Text = "浏览...";
            browse.Location = new Point(200, 8);
            browse.Size = new Size(80, 28);
            browse.Click += delegate { BrowseImage(); };
            left.Controls.Add(browse);

            _imageBox = new TextBox();
            _imageBox.Location = new Point(10, 42);
            _imageBox.Size = new Size(270, 25);
            _imageBox.ReadOnly = true;
            left.Controls.Add(_imageBox);

            Label toolLabel = new Label();
            toolLabel.Text = "打码方式";
            toolLabel.Location = new Point(10, 78);
            toolLabel.AutoSize = true;
            left.Controls.Add(toolLabel);

            _toolBox = new ComboBox();
            _toolBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _toolBox.Location = new Point(10, 98);
            _toolBox.Size = new Size(270, 25);
            _toolBox.Items.Add("模糊");
            _toolBox.Items.Add("马赛克");
            _toolBox.SelectedIndexChanged += delegate { OnToolChanged(); };
            left.Controls.Add(_toolBox);

            Label brushLabel = new Label();
            brushLabel.Text = "笔刷大小";
            brushLabel.Location = new Point(10, 132);
            brushLabel.AutoSize = true;
            left.Controls.Add(brushLabel);

            _brushBar = new TrackBar();
            _brushBar.AutoSize = false;
            _brushBar.TickStyle = TickStyle.None;
            _brushBar.Minimum = 2;
            _brushBar.Maximum = 40;
            _brushBar.Value = 12;
            _brushBar.Location = new Point(10, 150);
            _brushBar.Size = new Size(200, 30);
            _brushBar.ValueChanged += delegate { OnBrushChanged(); };
            left.Controls.Add(_brushBar);

            _brushValue = new Label();
            _brushValue.Text = "12%";
            _brushValue.Location = new Point(216, 156);
            _brushValue.Size = new Size(64, 20);
            _brushValue.TextAlign = ContentAlignment.MiddleRight;
            left.Controls.Add(_brushValue);

            _strengthLabel = new Label();
            _strengthLabel.Text = "模糊半径";
            _strengthLabel.Location = new Point(10, 190);
            _strengthLabel.AutoSize = true;
            left.Controls.Add(_strengthLabel);

            _strengthBar = new TrackBar();
            _strengthBar.AutoSize = false;
            _strengthBar.TickStyle = TickStyle.None;
            _strengthBar.Minimum = 1;
            _strengthBar.Maximum = 80;
            _strengthBar.Value = 12;
            _strengthBar.Location = new Point(10, 208);
            _strengthBar.Size = new Size(200, 30);
            _strengthBar.ValueChanged += delegate { OnStrengthChanged(); };
            left.Controls.Add(_strengthBar);

            _strengthValue = new Label();
            _strengthValue.Text = "12";
            _strengthValue.Location = new Point(216, 214);
            _strengthValue.Size = new Size(64, 20);
            _strengthValue.TextAlign = ContentAlignment.MiddleRight;
            left.Controls.Add(_strengthValue);

            Button undoButton = new Button();
            undoButton.Text = "撤销一笔";
            undoButton.Location = new Point(10, 250);
            undoButton.Size = new Size(130, 30);
            undoButton.Click += delegate { UndoStroke(); };
            left.Controls.Add(undoButton);

            Button clearButton = new Button();
            clearButton.Text = "清除全部";
            clearButton.Location = new Point(150, 250);
            clearButton.Size = new Size(130, 30);
            clearButton.Click += delegate { ClearStrokes(); };
            left.Controls.Add(clearButton);

            Label note = new Label();
            note.Location = new Point(10, 292);
            note.Size = new Size(268, 210);
            note.ForeColor = Color.FromArgb(70, 70, 70);
            note.Text =
                "用法：\r\n" +
                "• 在右侧图片上按住左键涂抹，涂过的地方被模糊\r\n" +
                "  （或马赛克），用来遮挡敏感信息。\r\n" +
                "• 笔刷大小按图片短边的百分比计，\r\n" +
                "  大图小图观感一致。\r\n" +
                "• 强度：模糊方式下为半径；马赛克方式下为色块大小。\r\n" +
                "• 「撤销一笔」删除最后一次涂抹，「清除全部」清空。\r\n\r\n" +
                "保存时按原图全分辨率重新计算，预览用降采样图。";
            left.Controls.Add(note);

            _canvas = new ImageCanvas();
            _canvas.Dock = DockStyle.Fill;
            _canvas.Margin = new Padding(3, 3, 3, 3);
            _canvas.BrushEnabled = true;
            _canvas.BrushStarted += delegate(Point p) { StartStroke(p); };
            _canvas.BrushMoved += delegate(Point p) { MoveStroke(p); };
            _canvas.BrushFinished += delegate { FinishStroke(); };
            root.Controls.Add(_canvas, 1, 0);

            Button save = new Button();
            save.Text = "保存为 PNG";
            save.Location = new Point(10, 8);
            save.Size = new Size(120, 32);
            save.Click += delegate { SaveResult(); };
            root.Controls.Add(save, 0, 1);

            _status = new Label();
            _status.Text = "请选择图片，然后在图片上涂抹要打码的区域";
            _status.Location = new Point(140, 14);
            _status.Size = new Size(600, 22);
            _status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            root.Controls.Add(_status, 1, 1);

            _debounce = new Timer();
            _debounce.Interval = 120;
            _debounce.Tick += delegate
            {
                _debounce.Stop();
                RecomputeProcessed();
                RebuildResultFromMask();
            };

            _toolBox.SelectedIndex = 0;
        }

        private void BrowseImage()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择图片";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap loaded = ImageUtil.LoadImage(dialog.FileName);
                DisposePreview();

                _source = loaded;
                _sourcePath = dialog.FileName;
                _imageBox.Text = dialog.FileName;
                _strokes.Clear();
                _current = null;

                Bitmap preview = ImageUtil.CreatePreview(_source, PreviewSize);
                if (preview != null)
                {
                    _previewBase = preview;
                    _previewScale = (float)preview.Width / _source.Width;
                }
                else
                {
                    _previewBase = ImageFilters.Clone(_source);
                    _previewScale = 1f;
                }

                RecomputeProcessed();

                _maskPreview = new Bitmap(_previewBase.Width, _previewBase.Height, PixelFormat.Format32bppArgb);
                _resultImage = ImageFilters.Clone(_previewBase);

                _canvas.SetImage(_resultImage);
                _canvas.BrushRadius = BrushRadiusPreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private void DisposePreview()
        {
            if (_previewBase != null) { _previewBase.Dispose(); _previewBase = null; }
            if (_previewProcessed != null) { _previewProcessed.Dispose(); _previewProcessed = null; }
            if (_resultImage != null) { _resultImage.Dispose(); _resultImage = null; }
            if (_maskPreview != null) { _maskPreview.Dispose(); _maskPreview = null; }
            if (_stamp != null) { _stamp.Dispose(); _stamp = null; }
        }

        private void OnToolChanged()
        {
            _configuring = true;
            if (_toolBox.SelectedIndex == 0)
            {
                _strengthLabel.Text = "模糊半径";
                _strengthBar.Minimum = 1;
                _strengthBar.Maximum = 80;
                _strengthBar.Value = 12;
                _strengthValue.Text = "12";
            }
            else
            {
                _strengthLabel.Text = "马赛克块";
                _strengthBar.Minimum = 3;
                _strengthBar.Maximum = 80;
                _strengthBar.Value = 16;
                _strengthValue.Text = "16";
            }
            _configuring = false;

            if (_previewBase != null)
            {
                RecomputeProcessed();
                RebuildResultFromMask();
            }
        }

        private void OnBrushChanged()
        {
            _brushValue.Text = _brushBar.Value + "%";
            if (_source != null)
            {
                _canvas.BrushRadius = BrushRadiusPreview();
            }
        }

        private void OnStrengthChanged()
        {
            _strengthValue.Text = _strengthBar.Value.ToString();
            if (_configuring || _source == null)
            {
                return;
            }
            _debounce.Stop();
            _debounce.Start();
        }

        private int BrushRadiusSource()
        {
            if (_source == null)
            {
                return 8;
            }
            int min = Math.Min(_source.Width, _source.Height);
            int diameter = (int)Math.Round(min * _brushBar.Value / 100f);
            return Math.Max(2, diameter / 2);
        }

        private int BrushRadiusPreview()
        {
            return Math.Max(1, (int)Math.Round(BrushRadiusSource() * _previewScale));
        }

        private Point ToSource(Point previewPoint)
        {
            if (_previewScale <= 0f || _source == null)
            {
                return previewPoint;
            }
            int x = (int)Math.Round(previewPoint.X / _previewScale);
            int y = (int)Math.Round(previewPoint.Y / _previewScale);
            x = Math.Max(0, Math.Min(_source.Width, x));
            y = Math.Max(0, Math.Min(_source.Height, y));
            return new Point(x, y);
        }

        private void StartStroke(Point previewPoint)
        {
            if (_source == null || _maskPreview == null)
            {
                return;
            }
            if (_stamp != null)
            {
                _stamp.Dispose();
            }
            _stampRadius = BrushRadiusPreview();
            _stamp = CreateStamp(_stampRadius * 2);

            _current = new Stroke();
            _current.Radius = BrushRadiusSource();
            _current.Points.Add(ToSource(previewPoint));
            _strokes.Add(_current);

            PointF p = new PointF(previewPoint.X, previewPoint.Y);
            PaintSegment(p, p);
        }

        private void MoveStroke(Point previewPoint)
        {
            if (_current == null)
            {
                return;
            }
            Point sp = ToSource(previewPoint);
            Point last = _current.Points[_current.Points.Count - 1];
            if (sp.X == last.X && sp.Y == last.Y)
            {
                return;
            }
            PointF a = new PointF(last.X * _previewScale, last.Y * _previewScale);
            PointF b = new PointF(previewPoint.X, previewPoint.Y);
            _current.Points.Add(sp);
            PaintSegment(a, b);
        }

        private void FinishStroke()
        {
            _current = null;
        }

        private void UndoStroke()
        {
            if (_strokes.Count == 0)
            {
                return;
            }
            _strokes.RemoveAt(_strokes.Count - 1);
            _current = null;
            RebuildMaskAndResult();
        }

        private void ClearStrokes()
        {
            _strokes.Clear();
            _current = null;
            RebuildMaskAndResult();
        }

        private void RecomputeProcessed()
        {
            if (_previewBase == null)
            {
                return;
            }
            if (_previewProcessed != null)
            {
                _previewProcessed.Dispose();
            }
            _previewProcessed = ImageFilters.Clone(_previewBase);

            if (_toolBox.SelectedIndex == 0)
            {
                int r = Math.Max(1, (int)Math.Round(_strengthBar.Value * _previewScale));
                ImageFilters.GaussianBlur(_previewProcessed, r);
            }
            else
            {
                int b = Math.Max(2, (int)Math.Round(_strengthBar.Value * _previewScale));
                ImageFilters.Mosaic(_previewProcessed, b);
            }
        }

        private void PaintSegment(PointF a, PointF b)
        {
            float r = _stampRadius;
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max(1, (int)(dist / Math.Max(1f, r * 0.4f)));

            using (Graphics g = Graphics.FromImage(_maskPreview))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                for (int i = 0; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    float cx = a.X + dx * t;
                    float cy = a.Y + dy * t;
                    g.DrawImage(_stamp, cx - r, cy - r, r * 2f, r * 2f);
                }
            }

            int minX = (int)Math.Floor(Math.Min(a.X, b.X) - r) - 1;
            int minY = (int)Math.Floor(Math.Min(a.Y, b.Y) - r) - 1;
            int maxX = (int)Math.Ceiling(Math.Max(a.X, b.X) + r) + 1;
            int maxY = (int)Math.Ceiling(Math.Max(a.Y, b.Y) + r) + 1;
            Rectangle dirty = new Rectangle(minX, minY, maxX - minX, maxY - minY);

            BlendRegion(_resultImage, _previewBase, _previewProcessed, _maskPreview, dirty);
            _canvas.Invalidate();
        }

        private void RebuildResultFromMask()
        {
            if (_previewBase == null || _previewProcessed == null || _maskPreview == null || _resultImage == null)
            {
                return;
            }
            using (Graphics g = Graphics.FromImage(_resultImage))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(_previewBase, 0, 0, _previewBase.Width, _previewBase.Height);
            }
            BlendRegion(
                _resultImage,
                _previewBase,
                _previewProcessed,
                _maskPreview,
                new Rectangle(0, 0, _resultImage.Width, _resultImage.Height));
            _canvas.Invalidate();
        }

        private void RebuildMaskAndResult()
        {
            if (_maskPreview == null)
            {
                return;
            }
            using (Graphics g = Graphics.FromImage(_maskPreview))
            {
                g.Clear(Color.Transparent);
                StampStrokes(g, _strokes, _previewScale);
            }
            RebuildResultFromMask();
        }

        private static void StampStrokes(Graphics g, List<Stroke> strokes, float scale)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            for (int i = 0; i < strokes.Count; i++)
            {
                Stroke stroke = strokes[i];
                int radius = Math.Max(1, (int)Math.Round(stroke.Radius * scale));
                using (Bitmap stamp = CreateStamp(radius * 2))
                {
                    PointF previous = new PointF(stroke.Points[0].X * scale, stroke.Points[0].Y * scale);
                    DrawStamp(g, stamp, radius, previous);
                    for (int j = 1; j < stroke.Points.Count; j++)
                    {
                        PointF current = new PointF(stroke.Points[j].X * scale, stroke.Points[j].Y * scale);
                        float dx = current.X - previous.X;
                        float dy = current.Y - previous.Y;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                        int steps = Math.Max(1, (int)(dist / Math.Max(1f, radius * 0.4f)));
                        for (int k = 1; k <= steps; k++)
                        {
                            float t = (float)k / steps;
                            DrawStamp(g, stamp, radius, new PointF(previous.X + dx * t, previous.Y + dy * t));
                        }
                        previous = current;
                    }
                }
            }
        }

        private static void DrawStamp(Graphics g, Bitmap stamp, int radius, PointF center)
        {
            g.DrawImage(stamp, center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
        }

        private static Bitmap CreateStamp(int diameter)
        {
            if (diameter < 2)
            {
                diameter = 2;
            }
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

        private static void BlendRegion(Bitmap result, Bitmap baseImage, Bitmap effect, Bitmap mask, Rectangle rect)
        {
            Rectangle bounds = new Rectangle(0, 0, result.Width, result.Height);
            rect = Rectangle.Intersect(rect, bounds);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            BitmapData bd = baseImage.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData ed = effect.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData md = mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData rd = result.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int rw = rect.Width;
                int rh = rect.Height;
                int bstride = bd.Stride;
                int estride = ed.Stride;
                int mstride = md.Stride;
                int rstride = rd.Stride;

                byte[] bbuf = new byte[bstride * rh];
                byte[] ebuf = new byte[estride * rh];
                byte[] mbuf = new byte[mstride * rh];
                byte[] rbuf = new byte[rstride * rh];
                Marshal.Copy(bd.Scan0, bbuf, 0, bbuf.Length);
                Marshal.Copy(ed.Scan0, ebuf, 0, ebuf.Length);
                Marshal.Copy(md.Scan0, mbuf, 0, mbuf.Length);
                Marshal.Copy(rd.Scan0, rbuf, 0, rbuf.Length);

                for (int y = 0; y < rh; y++)
                {
                    int bo = y * bstride;
                    int eo = y * estride;
                    int mo = y * mstride;
                    int ro = y * rstride;
                    for (int x = 0; x < rw; x++)
                    {
                        int i = bo + x * 4;
                        int ei = eo + x * 4;
                        int mi = mo + x * 4;
                        int o = ro + x * 4;
                        int m = mbuf[mi + 3];
                        if (m == 0)
                        {
                            continue;
                        }
                        rbuf[o] = (byte)(bbuf[i] + (ebuf[ei] - bbuf[i]) * m / 255);
                        rbuf[o + 1] = (byte)(bbuf[i + 1] + (ebuf[ei + 1] - bbuf[i + 1]) * m / 255);
                        rbuf[o + 2] = (byte)(bbuf[i + 2] + (ebuf[ei + 2] - bbuf[i + 2]) * m / 255);
                        rbuf[o + 3] = bbuf[i + 3];
                    }
                }

                Marshal.Copy(rbuf, 0, rd.Scan0, rbuf.Length);
            }
            finally
            {
                baseImage.UnlockBits(bd);
                effect.UnlockBits(ed);
                mask.UnlockBits(md);
                result.UnlockBits(rd);
            }
        }

        private void SaveResult()
        {
            if (_source == null)
            {
                _status.Text = "请先选择图片";
                return;
            }

            string dir = Path.GetDirectoryName(_sourcePath);
            string name = Path.GetFileNameWithoutExtension(_sourcePath) + "_打码.png";

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "保存结果";
            dialog.Filter = "PNG 图片|*.png";
            if (!string.IsNullOrEmpty(dir))
            {
                dialog.InitialDirectory = dir;
            }
            dialog.FileName = name;
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                int w = _source.Width;
                int h = _source.Height;
                using (Bitmap fullBase = ImageFilters.Clone(_source))
                using (Bitmap fullProcessed = ImageFilters.Clone(_source))
                {
                    if (_toolBox.SelectedIndex == 0)
                    {
                        ImageFilters.GaussianBlur(fullProcessed, Math.Max(1, _strengthBar.Value));
                    }
                    else
                    {
                        ImageFilters.Mosaic(fullProcessed, Math.Max(2, _strengthBar.Value));
                    }

                    using (Bitmap mask = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(mask))
                        {
                            g.Clear(Color.Transparent);
                            StampStrokes(g, _strokes, 1f);
                        }
                        using (Bitmap output = ImageFilters.MaskBlend(fullBase, fullProcessed, mask))
                        {
                            ImageUtil.SavePng(output, dialog.FileName);
                        }
                    }
                }
                _status.Text = "已保存：" + dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
        }
    }
}
