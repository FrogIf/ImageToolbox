using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
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

        public override bool DocumentLevel
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

        public override bool DocumentLevel
        {
            get { return true; }
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
        private ComboBox _mode;
        private Rectangle _selection;
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
            _overlayCanvas.ZoomEnabled = false;
            Controls.Add(_overlayCanvas);

            EditOpUi.Caption(this, "覆盖位置", 144);
            _mode = EditOpUi.Combo(this, 140, new string[] { "相对", "绝对" }, 0);
            _mode.Location = new Point(76, 140);
            _mode.Width = 224;
            _mode.SelectedIndexChanged += delegate { RaisePreview(); };

            _regions = new ListBox();
            _regions.Location = new Point(10, 174);
            _regions.Size = new Size(290, 84);
            _regions.IntegralHeight = false;
            Controls.Add(_regions);

            EditOpUi.Button(this, "添加选区", 10, 266, 90, delegate { AddRegion(); });
            EditOpUi.Button(this, "移除", 106, 266, 70, delegate { RemoveRegion(); });
            EditOpUi.Button(this, "清空", 182, 266, 70, delegate { _list.Clear(); RefreshRegions(); RaisePreview(); });

            _feather = EditOpUi.Slider(this, "羽化", 308, 0, 80, 12, out _featherV);
            _opacity = EditOpUi.Slider(this, "不透明", 344, 0, 100, 100, out _opacityV);
            _feather.ValueChanged += delegate { _featherV.Text = _feather.Value.ToString(); RaisePreview(); };
            _opacity.ValueChanged += delegate { _opacityV.Text = _opacity.Value + "%"; RaisePreview(); };
            EditOpUi.Note(this, "在左侧图片框选后「添加选区」，可加多个。相对：按比例取覆盖图上对应的一块，再缩放到底图选区；绝对：用选区的像素位置和大小，1:1 在覆盖图上截取并贴到底图同一位置（覆盖图比底图大时用此项）。", 382, 84);
        }

        protected override void OnActivate()
        {
            if (Canvas != null) { Canvas.ReadOnly = false; }
        }

        public override void OnCanvasSelection(Rectangle imageRect)
        {
            _selection = imageRect;
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
            if (Source == null) { return; }
            Rectangle r = Rectangle.Intersect(_selection, new Rectangle(0, 0, Source.Width, Source.Height));
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
            Size sourceSize = new Size(Source.Width, Source.Height);
            Size overlaySize = new Size(_overlay.Width, _overlay.Height);
            Rectangle overlayRect = new Rectangle(0, 0, _overlay.Width, _overlay.Height);
            bool absolute = (_mode.SelectedIndex == 1);
            Rectangle canvas = new Rectangle(0, 0, target.Width, target.Height);

            Bitmap effect = ImageFilters.Clone(target);
            Bitmap mask = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(effect))
            using (Graphics gm = Graphics.FromImage(mask))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, 255, 255, 255)))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                gm.Clear(Color.Transparent);
                for (int i = 0; i < _list.Count; i++)
                {
                    Rectangle region = _list[i];
                    Rectangle src, paint;
                    if (absolute)
                    {
                        src = Rectangle.Intersect(region, overlayRect);
                        if (src.Width < 1 || src.Height < 1) { continue; }
                        paint = new Rectangle(region.X, region.Y, src.Width, src.Height);
                    }
                    else
                    {
                        src = ImageUtil.MapRegion(region, sourceSize, overlaySize);
                        src = Rectangle.Intersect(src, overlayRect);
                        if (src.Width < 1 || src.Height < 1) { continue; }
                        paint = ImageUtil.MapRegion(src, overlaySize, sourceSize);
                    }
                    Rectangle dest = new Rectangle(
                        (int)Math.Round(paint.X * scale), (int)Math.Round(paint.Y * scale),
                        (int)Math.Round(paint.Width * scale), (int)Math.Round(paint.Height * scale));
                    if (dest.Width < 1 || dest.Height < 1) { continue; }
                    Rectangle clipped = Rectangle.Intersect(dest, canvas);
                    if (clipped.Width < 1 || clipped.Height < 1) { continue; }
                    g.DrawImage(_overlay, dest, src, GraphicsUnit.Pixel);
                    gm.FillRectangle(brush, clipped);
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
        private Bitmap _infoSource;

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
            if (object.ReferenceEquals(_infoSource, Source)) { return; }
            _infoSource = Source;
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
