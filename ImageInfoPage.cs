using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class ImageInfoPage : ToolPage
    {
        private TextBox _imageBox;
        private Label _stats;
        private HistogramView _histogram;
        private ImageCanvas _previewCanvas;
        private ListView _list;
        private Label _status;
        private string _path;
        private Bitmap _image;

        public ImageInfoPage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1120, 680);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "图片信息"; }
        }

        public override void Shutdown()
        {
            if (_previewCanvas != null)
            {
                _previewCanvas.SetImage(null);
            }
            if (_image != null)
            {
                _image.Dispose();
                _image = null;
            }
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 3;
            root.RowCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
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
            browse.Location = new Point(260, 8);
            browse.Size = new Size(80, 28);
            browse.Click += delegate { BrowseImage(); };
            left.Controls.Add(browse);

            _imageBox = new TextBox();
            _imageBox.Location = new Point(10, 42);
            _imageBox.Size = new Size(330, 25);
            _imageBox.ReadOnly = true;
            left.Controls.Add(_imageBox);

            Label statsTitle = new Label();
            statsTitle.Text = "色彩统计";
            statsTitle.Location = new Point(10, 78);
            statsTitle.AutoSize = true;
            left.Controls.Add(statsTitle);

            _stats = new Label();
            _stats.Location = new Point(10, 100);
            _stats.Size = new Size(330, 110);
            _stats.ForeColor = Color.FromArgb(60, 60, 60);
            left.Controls.Add(_stats);

            Label histTitle = new Label();
            histTitle.Text = "直方图（R/G/B）";
            histTitle.Location = new Point(10, 218);
            histTitle.AutoSize = true;
            left.Controls.Add(histTitle);

            _histogram = new HistogramView();
            _histogram.Location = new Point(10, 240);
            _histogram.Size = new Size(330, 150);
            left.Controls.Add(_histogram);

            Button strip = new Button();
            strip.Text = "去除元数据并另存";
            strip.Location = new Point(10, 402);
            strip.Size = new Size(160, 32);
            strip.Click += delegate { StripAndSave(); };
            left.Controls.Add(strip);

            Label note = new Label();
            note.Location = new Point(10, 446);
            note.Size = new Size(330, 150);
            note.ForeColor = Color.FromArgb(70, 70, 70);
            note.Text =
                "说明：\r\n" +
                "• 左侧列出尺寸、格式、DPI、文件大小，以及常见 EXIF\r\n" +
                "  信息（相机、拍摄时间、曝光、ISO、GPS 等）。\r\n" +
                "• 「去除元数据并另存」会重新编码图片，丢弃 EXIF/GPS\r\n" +
                "  等所有元数据（不修改原文件）。\r\n" +
                "• 支持 PNG / JPG / BMP / GIF / WebP / TIFF 输入。";
            left.Controls.Add(note);

            TableLayoutPanel previewGrid = new TableLayoutPanel();
            previewGrid.Dock = DockStyle.Fill;
            previewGrid.ColumnCount = 1;
            previewGrid.RowCount = 2;
            previewGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
            previewGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            previewGrid.Margin = new Padding(3, 3, 3, 3);
            root.Controls.Add(previewGrid, 1, 0);

            Label previewLabel = new Label();
            previewLabel.Text = "预览";
            previewLabel.Dock = DockStyle.Fill;
            previewLabel.TextAlign = ContentAlignment.MiddleLeft;
            previewGrid.Controls.Add(previewLabel, 0, 0);

            _previewCanvas = new ImageCanvas();
            _previewCanvas.Dock = DockStyle.Fill;
            _previewCanvas.ReadOnly = true;
            previewGrid.Controls.Add(_previewCanvas, 0, 1);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.Margin = new Padding(3, 3, 3, 3);
            _list.Columns.Add("项目", 150);
            _list.Columns.Add("值", 380);
            root.Controls.Add(_list, 2, 0);

            _status = new Label();
            _status.Dock = DockStyle.Bottom;
            _status.Height = 24;
            _status.Text = "请选择图片以查看信息";
            Controls.Add(_status);
            _status.BringToFront();
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
                if (_image != null)
                {
                    _image.Dispose();
                }
                _image = ImageUtil.LoadImage(dialog.FileName);
                _path = dialog.FileName;
                _imageBox.Text = dialog.FileName;
                _previewCanvas.SetImage(_image);

                _list.Items.Clear();
                List<MetaEntry> entries = ImageMeta.ReadMetadata(dialog.FileName);
                for (int i = 0; i < entries.Count; i++)
                {
                    ListViewItem item = new ListViewItem(entries[i].Name);
                    item.SubItems.Add(entries[i].Value);
                    _list.Items.Add(item);
                }

                _histogram.SetData(ImageTuning.Histogram(_image));
                _stats.Text = BuildStats(_image);
                _status.Text = "共 " + entries.Count + " 项信息";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法读取图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private string BuildStats(Bitmap bitmap)
        {
            ImageEffects.ColorStats s = ImageEffects.MeasureStats(bitmap, 512);
            string warm = s.WarmRatio >= 1.0 ? "偏暖" : "偏冷";
            string tint = s.TintRatio >= 1.0 ? "偏绿" : "偏品红";
            return
                "亮度：" + s.Bright.ToString("0.000") + "\r\n" +
                "对比度：" + s.Contrast.ToString("0.000") + "\r\n" +
                "饱和度：" + s.Sat.ToString("0.000") + "\r\n" +
                "冷暖：" + s.WarmRatio.ToString("0.00") + "（" + warm + "）\r\n" +
                "品绿：" + s.TintRatio.ToString("0.00") + "（" + tint + "）";
        }

        private void StripAndSave()
        {
            if (_image == null || string.IsNullOrEmpty(_path))
            {
                _status.Text = "请先选择图片";
                return;
            }

            string dir = Path.GetDirectoryName(_path);
            string name = Path.GetFileNameWithoutExtension(_path) + "_无元数据" + Path.GetExtension(_path);

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "另存为（不含元数据）";
            dialog.Filter = "原格式|*" + Path.GetExtension(_path) + "|PNG 图片|*.png|JPEG 图片|*.jpg|所有文件|*.*";
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
                ImageMeta.StripMetadata(_path, dialog.FileName);
                _status.Text = "已保存（已去除元数据）：" + dialog.FileName;
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
