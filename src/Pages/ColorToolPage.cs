using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class ColorToolPage : ToolPage
    {
        private const int PreviewSize = 1200;

        private TextBox _imageBox;
        private ImageCanvas _canvas;
        private Panel _swatch;
        private Label _colorInfo;
        private FlowLayoutPanel _palette;
        private TrackBar _countBar;
        private Label _countValue;
        private Label _status;

        private Bitmap _source;
        private Bitmap _preview;
        private float _scale = 1f;
        private Color _picked = Color.White;
        private bool _hasPick;
        private List<Color> _paletteColors = new List<Color>();

        public ColorToolPage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1120, 680);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "颜色工具"; }
        }

        public override void Shutdown()
        {
            if (_source != null) { _source.Dispose(); }
            if (_preview != null) { _preview.Dispose(); }
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340f));
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
            browse.Location = new Point(240, 8);
            browse.Size = new Size(80, 28);
            browse.Click += delegate { BrowseImage(); };
            left.Controls.Add(browse);

            _imageBox = new TextBox();
            _imageBox.Location = new Point(10, 42);
            _imageBox.Size = new Size(310, 25);
            _imageBox.ReadOnly = true;
            left.Controls.Add(_imageBox);

            Label pickTitle = new Label();
            pickTitle.Text = "取色（点击右侧图片）";
            pickTitle.Location = new Point(10, 78);
            pickTitle.AutoSize = true;
            left.Controls.Add(pickTitle);

            _swatch = new Panel();
            _swatch.Location = new Point(10, 100);
            _swatch.Size = new Size(70, 46);
            _swatch.BorderStyle = BorderStyle.FixedSingle;
            _swatch.BackColor = Color.White;
            left.Controls.Add(_swatch);

            _colorInfo = new Label();
            _colorInfo.Location = new Point(90, 102);
            _colorInfo.Size = new Size(230, 46);
            _colorInfo.ForeColor = Color.FromArgb(60, 60, 60);
            _colorInfo.Text = "尚未取色\r\n";
            left.Controls.Add(_colorInfo);

            Button copy = new Button();
            copy.Text = "复制 HEX";
            copy.Location = new Point(10, 154);
            copy.Size = new Size(100, 28);
            copy.Click += delegate { CopyPicked(); };
            left.Controls.Add(copy);

            Label paletteTitle = new Label();
            paletteTitle.Text = "主色调色板";
            paletteTitle.Location = new Point(10, 192);
            paletteTitle.AutoSize = true;
            left.Controls.Add(paletteTitle);

            _countBar = new TrackBar();
            _countBar.AutoSize = false;
            _countBar.TickStyle = TickStyle.None;
            _countBar.Minimum = 2;
            _countBar.Maximum = 12;
            _countBar.Value = 6;
            _countBar.Location = new Point(10, 210);
            _countBar.Size = new Size(220, 30);
            _countBar.ValueChanged += delegate { _countValue.Text = _countBar.Value + " 色"; };
            left.Controls.Add(_countBar);

            _countValue = new Label();
            _countValue.Text = "6 色";
            _countValue.Location = new Point(236, 216);
            _countValue.Size = new Size(64, 20);
            _countValue.TextAlign = ContentAlignment.MiddleRight;
            left.Controls.Add(_countValue);

            Button extract = new Button();
            extract.Text = "提取主色";
            extract.Location = new Point(10, 246);
            extract.Size = new Size(100, 30);
            extract.Click += delegate { ExtractPalette(); };
            left.Controls.Add(extract);

            _palette = new FlowLayoutPanel();
            _palette.Location = new Point(10, 284);
            _palette.Size = new Size(310, 210);
            _palette.AutoScroll = true;
            _palette.BorderStyle = BorderStyle.FixedSingle;
            left.Controls.Add(_palette);

            Label note = new Label();
            note.Location = new Point(10, 502);
            note.Size = new Size(310, 90);
            note.ForeColor = Color.FromArgb(70, 70, 70);
            note.Text =
                "说明：\r\n" +
                "• 点击右侧图片任意位置取色，显示 RGB / HEX / HSV。\r\n" +
                "• 「提取主色」用中位切分法统计全图主色调色板，\r\n" +
                "  点击色块可复制其 HEX。";
            left.Controls.Add(note);

            _canvas = new ImageCanvas();
            _canvas.Dock = DockStyle.Fill;
            _canvas.Margin = new Padding(3, 3, 3, 3);
            _canvas.ReadOnly = true;
            _canvas.PixelClicked += delegate(Point p) { OnPick(p); };
            root.Controls.Add(_canvas, 1, 0);

            _status = new Label();
            _status.Text = "请选择图片，然后点击取色或提取主色";
            _status.Location = new Point(10, 14);
            _status.Size = new Size(600, 22);
            _status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            root.Controls.Add(_status, 1, 1);
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

                _source = loaded;
                _imageBox.Text = dialog.FileName;

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
                _canvas.SetImage(_preview);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private void OnPick(Point previewPoint)
        {
            if (_source == null)
            {
                return;
            }
            int sx = (int)Math.Round(previewPoint.X / _scale);
            int sy = (int)Math.Round(previewPoint.Y / _scale);
            sx = Math.Max(0, Math.Min(_source.Width - 1, sx));
            sy = Math.Max(0, Math.Min(_source.Height - 1, sy));
            _picked = _source.GetPixel(sx, sy);
            _hasPick = true;
            _swatch.BackColor = _picked;

            double h, s, v;
            PaletteExtractor.RgbToHsv(_picked, out h, out s, out v);
            _colorInfo.Text =
                "RGB：" + _picked.R + ", " + _picked.G + ", " + _picked.B + "\r\n" +
                "HEX：" + PaletteExtractor.Hex(_picked) + "\r\n" +
                "HSV：" + h.ToString("0") + "°, " + (s * 100).ToString("0") + "%, " + (v * 100).ToString("0") + "%";
        }

        private void CopyPicked()
        {
            if (!_hasPick)
            {
                _status.Text = "请先取色";
                return;
            }
            try
            {
                Clipboard.SetText(PaletteExtractor.Hex(_picked));
                _status.Text = "已复制：" + PaletteExtractor.Hex(_picked);
            }
            catch (Exception ex)
            {
                _status.Text = "复制失败：" + ex.Message;
            }
        }

        private void ExtractPalette()
        {
            if (_source == null)
            {
                _status.Text = "请先选择图片";
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                _paletteColors = PaletteExtractor.Extract(_source, _countBar.Value, 160);
                BuildPalette();
                _status.Text = "已提取 " + _paletteColors.Count + " 种主色";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "提取失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private void BuildPalette()
        {
            _palette.SuspendLayout();
            _palette.Controls.Clear();
            for (int i = 0; i < _paletteColors.Count; i++)
            {
                _palette.Controls.Add(CreateSwatch(_paletteColors[i]));
            }
            _palette.ResumeLayout();
        }

        private Control CreateSwatch(Color color)
        {
            Panel item = new Panel();
            item.Size = new Size(58, 74);
            item.Margin = new Padding(2, 2, 2, 2);

            Panel block = new Panel();
            block.Size = new Size(56, 46);
            block.BackColor = color;
            block.BorderStyle = BorderStyle.FixedSingle;
            block.Cursor = Cursors.Hand;
            string hex = PaletteExtractor.Hex(color);
            block.Click += delegate
            {
                try
                {
                    Clipboard.SetText(hex);
                    _status.Text = "已复制：" + hex;
                }
                catch (Exception ex)
                {
                    _status.Text = "复制失败：" + ex.Message;
                }
            };
            item.Controls.Add(block);

            Label label = new Label();
            label.Text = hex;
            label.Location = new Point(0, 50);
            label.Size = new Size(56, 18);
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Font = new Font("Consolas", 7.5f);
            item.Controls.Add(label);

            return item;
        }
    }
}
