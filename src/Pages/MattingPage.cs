using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class MattingPage : ToolPage
    {
        private const int PreviewSize = 1200;

        private TextBox _imageBox;
        private ComboBox _modeBox;
        private Panel _swatch;
        private Label _pickInfo;
        private TrackBar _toleranceBar;
        private Label _toleranceValue;
        private CheckBox _invertBox;
        private ImageCanvas _canvas;
        private Label _status;
        private Timer _debounce;

        private Bitmap _source;
        private Bitmap _preview;
        private Bitmap _display;
        private float _scale = 1f;
        private Color _keyColor = Color.White;
        private bool _hasKey;
        private Point _seedPreview = Point.Empty;
        private bool _hasSeed;
        private string _sourcePath;

        public MattingPage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1120, 680);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "抠图"; }
        }

        public override void Shutdown()
        {
            if (_source != null) { _source.Dispose(); }
            if (_preview != null) { _preview.Dispose(); }
            if (_display != null) { _display.Dispose(); }
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

            Label modeLabel = new Label();
            modeLabel.Text = "抠图方式";
            modeLabel.Location = new Point(10, 78);
            modeLabel.AutoSize = true;
            left.Controls.Add(modeLabel);

            _modeBox = new ComboBox();
            _modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _modeBox.Location = new Point(10, 98);
            _modeBox.Size = new Size(270, 25);
            _modeBox.Items.Add("颜色阈值（全局）");
            _modeBox.Items.Add("魔术棒（连续）");
            _modeBox.SelectedIndexChanged += delegate { SchedulePreview(); };
            left.Controls.Add(_modeBox);

            Label pickLabel = new Label();
            pickLabel.Text = "取样颜色";
            pickLabel.Location = new Point(10, 132);
            pickLabel.AutoSize = true;
            left.Controls.Add(pickLabel);

            _swatch = new Panel();
            _swatch.Location = new Point(80, 130);
            _swatch.Size = new Size(40, 22);
            _swatch.BorderStyle = BorderStyle.FixedSingle;
            _swatch.BackColor = Color.White;
            left.Controls.Add(_swatch);

            _pickInfo = new Label();
            _pickInfo.Location = new Point(128, 132);
            _pickInfo.Size = new Size(152, 20);
            _pickInfo.ForeColor = Color.FromArgb(70, 70, 70);
            _pickInfo.Text = "点击图片取样";
            left.Controls.Add(_pickInfo);

            Label tolLabel = new Label();
            tolLabel.Text = "容差";
            tolLabel.Location = new Point(10, 166);
            tolLabel.AutoSize = true;
            left.Controls.Add(tolLabel);

            _toleranceBar = new TrackBar();
            _toleranceBar.AutoSize = false;
            _toleranceBar.TickStyle = TickStyle.None;
            _toleranceBar.Minimum = 1;
            _toleranceBar.Maximum = 120;
            _toleranceBar.Value = 30;
            _toleranceBar.Location = new Point(10, 184);
            _toleranceBar.Size = new Size(200, 30);
            _toleranceBar.ValueChanged += delegate { _toleranceValue.Text = _toleranceBar.Value.ToString(); SchedulePreview(); };
            left.Controls.Add(_toleranceBar);

            _toleranceValue = new Label();
            _toleranceValue.Text = "30";
            _toleranceValue.Location = new Point(216, 190);
            _toleranceValue.Size = new Size(64, 20);
            _toleranceValue.TextAlign = ContentAlignment.MiddleRight;
            left.Controls.Add(_toleranceValue);

            _invertBox = new CheckBox();
            _invertBox.Text = "反转（保留所选颜色）";
            _invertBox.Location = new Point(10, 222);
            _invertBox.AutoSize = true;
            _invertBox.CheckedChanged += delegate { SchedulePreview(); };
            left.Controls.Add(_invertBox);

            Label note = new Label();
            note.Location = new Point(10, 256);
            note.Size = new Size(268, 250);
            note.ForeColor = Color.FromArgb(70, 70, 70);
            note.Text =
                "用法：\r\n" +
                "• 颜色阈值（全局）：点击图片取一个颜色，\r\n" +
                "  全图与该颜色相近的像素变为透明。\r\n" +
                "• 魔术棒（连续）：点击图片某处，从该点向四周\r\n" +
                "  扩散，颜色相近的连通区域变为透明。\r\n" +
                "• 容差越大，判定为“相近”的范围越宽。\r\n" +
                "• 勾选“反转”可反过来保留所选颜色。\r\n" +
                "• 灰白棋盘格表示透明区域。\r\n\r\n" +
                "保存为 PNG 可保留透明背景。";
            left.Controls.Add(note);

            _canvas = new ImageCanvas();
            _canvas.Dock = DockStyle.Fill;
            _canvas.Margin = new Padding(3, 3, 3, 3);
            _canvas.ReadOnly = true;
            _canvas.PixelClicked += delegate(Point p) { OnPick(p); };
            root.Controls.Add(_canvas, 1, 0);

            Button save = new Button();
            save.Text = "保存为 PNG";
            save.Location = new Point(10, 8);
            save.Size = new Size(120, 32);
            save.Click += delegate { SaveResult(); };
            root.Controls.Add(save, 0, 1);

            _status = new Label();
            _status.Text = "请选择图片，然后在图片上点击取色/选取区域";
            _status.Location = new Point(140, 14);
            _status.Size = new Size(600, 22);
            _status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            root.Controls.Add(_status, 1, 1);

            _debounce = new Timer();
            _debounce.Interval = 120;
            _debounce.Tick += delegate { _debounce.Stop(); UpdatePreview(); };

            _modeBox.SelectedIndex = 0;
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
                if (_source != null) { _source.Dispose(); }
                if (_preview != null) { _preview.Dispose(); _preview = null; }
                if (_display != null) { _display.Dispose(); _display = null; }

                _source = loaded;
                _sourcePath = dialog.FileName;
                _imageBox.Text = dialog.FileName;
                _hasKey = false;
                _hasSeed = false;

                Bitmap preview = ImageUtil.CreatePreview(_source, PreviewSize);
                if (preview != null)
                {
                    _preview = preview;
                    _scale = (float)preview.Width / _source.Width;
                }
                else
                {
                    _preview = ImageFilters.Clone(_source);
                    _scale = 1f;
                }
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

            UpdatePreview();
        }

        private void OnPick(Point previewPoint)
        {
            if (_source == null || _preview == null)
            {
                return;
            }

            int sx = (int)Math.Round(previewPoint.X / _scale);
            int sy = (int)Math.Round(previewPoint.Y / _scale);
            sx = Math.Max(0, Math.Min(_source.Width - 1, sx));
            sy = Math.Max(0, Math.Min(_source.Height - 1, sy));

            if (_modeBox.SelectedIndex == 0)
            {
                _keyColor = _source.GetPixel(sx, sy);
                _hasKey = true;
                _swatch.BackColor = _keyColor;
                _pickInfo.Text = "R" + _keyColor.R + " G" + _keyColor.G + " B" + _keyColor.B;
            }
            else
            {
                _seedPreview = previewPoint;
                _hasSeed = true;
                _pickInfo.Text = "起点 " + sx + "," + sy;
            }

            UpdatePreview();
        }

        private void SchedulePreview()
        {
            if (_source == null)
            {
                return;
            }
            _debounce.Stop();
            _debounce.Start();
        }

        private void UpdatePreview()
        {
            if (_source == null || _preview == null)
            {
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap matted = BuildMatte(_preview, _scale);
                if (_display != null)
                {
                    _display.Dispose();
                }
                _display = CompositeChecker(matted);
                matted.Dispose();
                _canvas.SetImage(_display);
                _status.Text = "预览中（保存时按原图全分辨率重新计算）";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "处理失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private Bitmap BuildMatte(Bitmap image, float scale)
        {
            if (_modeBox.SelectedIndex == 0)
            {
                if (!_hasKey)
                {
                    return ImageFilters.Clone(image);
                }
                return ImageMatting.ByColor(image, _keyColor, _toleranceBar.Value, _invertBox.Checked);
            }
            if (!_hasSeed)
            {
                return ImageFilters.Clone(image);
            }
            int sx = (int)Math.Round(_seedPreview.X / scale);
            int sy = (int)Math.Round(_seedPreview.Y / scale);
            return ImageMatting.MagicWand(image, sx, sy, _toleranceBar.Value, _invertBox.Checked);
        }

        private static Bitmap CompositeChecker(Bitmap source)
        {
            int w = source.Width;
            int h = source.Height;
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                int cell = 16;
                using (SolidBrush light = new SolidBrush(Color.FromArgb(255, 220, 220, 220)))
                using (SolidBrush dark = new SolidBrush(Color.FromArgb(255, 170, 170, 170)))
                {
                    for (int y = 0; y < h; y += cell)
                    {
                        for (int x = 0; x < w; x += cell)
                        {
                            bool even = ((x / cell) + (y / cell)) % 2 == 0;
                            g.FillRectangle(even ? light : dark, x, y, cell, cell);
                        }
                    }
                }
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, w, h));
            }
            return result;
        }

        private void SaveResult()
        {
            if (_source == null)
            {
                _status.Text = "请先选择图片";
                return;
            }

            string dir = Path.GetDirectoryName(_sourcePath);
            string name = Path.GetFileNameWithoutExtension(_sourcePath) + "_抠图.png";

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
                Bitmap result = BuildMatte(_source, 1f);
                try
                {
                    ImageUtil.SavePng(result, dialog.FileName);
                }
                finally
                {
                    result.Dispose();
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
