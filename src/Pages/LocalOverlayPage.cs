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
    public class LocalOverlayPage : ToolPage
    {
        private TextBox _targetBox;
        private TextBox _overlayBox;
        private ImageCanvas _targetCanvas;
        private ImageCanvas _overlayCanvas;
        private ImageCanvas _resultCanvas;
        private ListBox _regionList;
        private TrackBar _featherBar;
        private Label _featherValue;
        private TrackBar _opacityBar;
        private Label _opacityValue;
        private Label _status;
        private Label _info;
        private Timer _debounce;

        private Bitmap _targetImage;
        private Bitmap _overlayImage;
        private Bitmap _resultImage;
        private string _targetPath;
        private List<Rectangle> _regions = new List<Rectangle>();

        public LocalOverlayPage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1120, 680);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "局部覆盖"; }
        }

        public override void Shutdown()
        {
            if (_targetImage != null) { _targetImage.Dispose(); }
            if (_overlayImage != null) { _overlayImage.Dispose(); }
            if (_resultImage != null) { _resultImage.Dispose(); }
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            Controls.Add(root);

            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.AutoScroll = true;
            left.Margin = new Padding(3, 3, 3, 3);
            root.Controls.Add(left, 0, 0);

            Label targetLabel = new Label();
            targetLabel.Text = "需要修改的图片";
            targetLabel.Location = new Point(10, 14);
            targetLabel.AutoSize = true;
            left.Controls.Add(targetLabel);

            Button targetBrowse = new Button();
            targetBrowse.Text = "浏览...";
            targetBrowse.Location = new Point(200, 8);
            targetBrowse.Size = new Size(80, 28);
            targetBrowse.Click += delegate { BrowseTarget(); };
            left.Controls.Add(targetBrowse);

            _targetBox = new TextBox();
            _targetBox.Location = new Point(10, 42);
            _targetBox.Size = new Size(270, 25);
            _targetBox.ReadOnly = true;
            left.Controls.Add(_targetBox);

            Label overlayLabel = new Label();
            overlayLabel.Text = "用于覆盖的图片";
            overlayLabel.Location = new Point(10, 78);
            overlayLabel.AutoSize = true;
            left.Controls.Add(overlayLabel);

            Button overlayBrowse = new Button();
            overlayBrowse.Text = "浏览...";
            overlayBrowse.Location = new Point(200, 72);
            overlayBrowse.Size = new Size(80, 28);
            overlayBrowse.Click += delegate { BrowseOverlay(); };
            left.Controls.Add(overlayBrowse);

            _overlayBox = new TextBox();
            _overlayBox.Location = new Point(10, 106);
            _overlayBox.Size = new Size(270, 25);
            _overlayBox.ReadOnly = true;
            left.Controls.Add(_overlayBox);

            Label regionLabel = new Label();
            regionLabel.Text = "覆盖区域（可在右侧框选后添加）";
            regionLabel.Location = new Point(10, 142);
            regionLabel.AutoSize = true;
            left.Controls.Add(regionLabel);

            _regionList = new ListBox();
            _regionList.Location = new Point(10, 164);
            _regionList.Size = new Size(270, 120);
            _regionList.IntegralHeight = false;
            left.Controls.Add(_regionList);

            Button addRegion = new Button();
            addRegion.Text = "添加区域";
            addRegion.Location = new Point(10, 292);
            addRegion.Size = new Size(88, 30);
            addRegion.Click += delegate { AddRegion(); };
            left.Controls.Add(addRegion);

            Button removeRegion = new Button();
            removeRegion.Text = "移除";
            removeRegion.Location = new Point(104, 292);
            removeRegion.Size = new Size(88, 30);
            removeRegion.Click += delegate { RemoveRegion(); };
            left.Controls.Add(removeRegion);

            Button clearRegions = new Button();
            clearRegions.Text = "清空";
            clearRegions.Location = new Point(198, 292);
            clearRegions.Size = new Size(82, 30);
            clearRegions.Click += delegate { ClearRegions(); };
            left.Controls.Add(clearRegions);

            Label featherLabel = new Label();
            featherLabel.Text = "边缘羽化";
            featherLabel.Location = new Point(10, 334);
            featherLabel.AutoSize = true;
            left.Controls.Add(featherLabel);

            _featherBar = new TrackBar();
            _featherBar.AutoSize = false;
            _featherBar.TickStyle = TickStyle.None;
            _featherBar.Minimum = 0;
            _featherBar.Maximum = 80;
            _featherBar.Value = 12;
            _featherBar.Location = new Point(10, 352);
            _featherBar.Size = new Size(200, 30);
            _featherBar.ValueChanged += delegate { _featherValue.Text = _featherBar.Value + " px"; SchedulePreview(); };
            left.Controls.Add(_featherBar);

            _featherValue = new Label();
            _featherValue.Text = "12 px";
            _featherValue.Location = new Point(216, 358);
            _featherValue.Size = new Size(64, 20);
            _featherValue.TextAlign = ContentAlignment.MiddleRight;
            left.Controls.Add(_featherValue);

            Label opacityLabel = new Label();
            opacityLabel.Text = "不透明度";
            opacityLabel.Location = new Point(10, 392);
            opacityLabel.AutoSize = true;
            left.Controls.Add(opacityLabel);

            _opacityBar = new TrackBar();
            _opacityBar.AutoSize = false;
            _opacityBar.TickStyle = TickStyle.None;
            _opacityBar.Minimum = 0;
            _opacityBar.Maximum = 100;
            _opacityBar.Value = 100;
            _opacityBar.Location = new Point(10, 410);
            _opacityBar.Size = new Size(200, 30);
            _opacityBar.ValueChanged += delegate { _opacityValue.Text = _opacityBar.Value + "%"; SchedulePreview(); };
            left.Controls.Add(_opacityBar);

            _opacityValue = new Label();
            _opacityValue.Text = "100%";
            _opacityValue.Location = new Point(216, 416);
            _opacityValue.Size = new Size(64, 20);
            _opacityValue.TextAlign = ContentAlignment.MiddleRight;
            left.Controls.Add(_opacityValue);

            Label note = new Label();
            note.Location = new Point(10, 450);
            note.Size = new Size(270, 150);
            note.ForeColor = Color.FromArgb(70, 70, 70);
            note.Text =
                "说明：\r\n" +
                "• 在目标图上拖动框选，点「添加区域」记录，\r\n" +
                "  可添加多个区域。\r\n" +
                "• 每个区域按相对位置映射覆盖图（覆盖图会被\r\n" +
                "  缩放到区域大小）。\r\n" +
                "• 边缘羽化让接缝过渡自然，不透明度控制覆盖强度。";
            left.Controls.Add(note);

            TableLayoutPanel right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.ColumnCount = 1;
            right.RowCount = 2;
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 200f));
            right.Margin = new Padding(3, 3, 3, 3);
            root.Controls.Add(right, 1, 0);

            TableLayoutPanel targetGrid = new TableLayoutPanel();
            targetGrid.Dock = DockStyle.Fill;
            targetGrid.ColumnCount = 1;
            targetGrid.RowCount = 2;
            targetGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
            targetGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            right.Controls.Add(targetGrid, 0, 0);

            Label targetCanvasLabel = new Label();
            targetCanvasLabel.Text = "目标图（拖动框选，可多次添加）";
            targetCanvasLabel.Dock = DockStyle.Fill;
            targetCanvasLabel.TextAlign = ContentAlignment.MiddleLeft;
            targetGrid.Controls.Add(targetCanvasLabel, 0, 0);

            _targetCanvas = new ImageCanvas();
            _targetCanvas.Dock = DockStyle.Fill;
            _targetCanvas.Margin = new Padding(0, 0, 0, 4);
            _targetCanvas.SelectionChanged += delegate { OnSelectionChanged(); };
            targetGrid.Controls.Add(_targetCanvas, 0, 1);

            TableLayoutPanel bottom = new TableLayoutPanel();
            bottom.Dock = DockStyle.Fill;
            bottom.ColumnCount = 2;
            bottom.RowCount = 1;
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            right.Controls.Add(bottom, 0, 1);

            TableLayoutPanel overlayGrid = new TableLayoutPanel();
            overlayGrid.Dock = DockStyle.Fill;
            overlayGrid.ColumnCount = 1;
            overlayGrid.RowCount = 2;
            overlayGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
            overlayGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            overlayGrid.Margin = new Padding(0, 0, 4, 0);
            bottom.Controls.Add(overlayGrid, 0, 0);

            Label overlayCanvasLabel = new Label();
            overlayCanvasLabel.Text = "覆盖图（当前选区对应）";
            overlayCanvasLabel.Dock = DockStyle.Fill;
            overlayCanvasLabel.TextAlign = ContentAlignment.MiddleLeft;
            overlayGrid.Controls.Add(overlayCanvasLabel, 0, 0);

            _overlayCanvas = new ImageCanvas();
            _overlayCanvas.Dock = DockStyle.Fill;
            _overlayCanvas.ReadOnly = true;
            overlayGrid.Controls.Add(_overlayCanvas, 0, 1);

            TableLayoutPanel resultGrid = new TableLayoutPanel();
            resultGrid.Dock = DockStyle.Fill;
            resultGrid.ColumnCount = 1;
            resultGrid.RowCount = 2;
            resultGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
            resultGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            resultGrid.Margin = new Padding(4, 0, 0, 0);
            bottom.Controls.Add(resultGrid, 1, 0);

            Label resultLabel = new Label();
            resultLabel.Text = "结果预览";
            resultLabel.Dock = DockStyle.Fill;
            resultLabel.TextAlign = ContentAlignment.MiddleLeft;
            resultGrid.Controls.Add(resultLabel, 0, 0);

            _resultCanvas = new ImageCanvas();
            _resultCanvas.Dock = DockStyle.Fill;
            _resultCanvas.ReadOnly = true;
            resultGrid.Controls.Add(_resultCanvas, 0, 1);

            Panel bottomBar = new Panel();
            bottomBar.Dock = DockStyle.Fill;
            bottomBar.Margin = new Padding(3, 0, 3, 3);
            root.Controls.Add(bottomBar, 0, 1);
            root.SetColumnSpan(bottomBar, 2);

            Button preview = new Button();
            preview.Text = "预览结果";
            preview.Location = new Point(10, 6);
            preview.Size = new Size(100, 30);
            preview.Click += delegate { PreviewResult(); };
            bottomBar.Controls.Add(preview);

            Button save = new Button();
            save.Text = "保存为 PNG";
            save.Location = new Point(120, 6);
            save.Size = new Size(120, 30);
            save.Click += delegate { SaveResult(); };
            bottomBar.Controls.Add(save);

            _status = new Label();
            _status.Text = "请选择两张图片，在目标图上框选后点「添加区域」";
            _status.Location = new Point(252, 12);
            _status.Size = new Size(600, 22);
            _status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            bottomBar.Controls.Add(_status);

            _info = new Label();
            _info.TextAlign = ContentAlignment.MiddleRight;
            _info.Location = new Point(860, 12);
            _info.Size = new Size(180, 22);
            _info.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            bottomBar.Controls.Add(_info);

            _debounce = new Timer();
            _debounce.Interval = 150;
            _debounce.Tick += delegate { _debounce.Stop(); PreviewResult(); };
        }

        private void BrowseTarget()
        {
            string path = PickImage();
            if (path == null)
            {
                return;
            }
            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap bitmap = ImageUtil.LoadImage(path);
                if (_targetImage != null) { _targetImage.Dispose(); }
                _targetImage = bitmap;
                _targetPath = path;
                _targetBox.Text = path;
                _targetCanvas.SetImage(_targetImage);
                _regions.Clear();
                RefreshRegions();
                ResetResult();
                _status.Text = "已加载目标图，框选后点「添加区域」";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
            UpdateInfo();
        }

        private void BrowseOverlay()
        {
            string path = PickImage();
            if (path == null)
            {
                return;
            }
            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap bitmap = ImageUtil.LoadImage(path);
                if (_overlayImage != null) { _overlayImage.Dispose(); }
                _overlayImage = bitmap;
                _overlayBox.Text = path;
                _overlayCanvas.SetImage(_overlayImage);
                _overlayCanvas.Selection = MapSelectionToOverlay();
                ResetResult();
                _status.Text = "已加载覆盖图";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
            UpdateInfo();
        }

        private string PickImage()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择图片";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                return dialog.FileName;
            }
            return null;
        }

        private void AddRegion()
        {
            if (_targetImage == null)
            {
                _status.Text = "请先选择目标图";
                return;
            }
            Rectangle sel = _targetCanvas.Selection;
            sel = Rectangle.Intersect(sel, new Rectangle(0, 0, _targetImage.Width, _targetImage.Height));
            if (sel.Width < 1 || sel.Height < 1)
            {
                _status.Text = "请先在目标图上拖动框选一个区域";
                return;
            }
            _regions.Add(sel);
            RefreshRegions();
            _status.Text = "已添加区域 " + _regions.Count;
            PreviewResult();
        }

        private void RemoveRegion()
        {
            int index = _regionList.SelectedIndex;
            if (index < 0 || index >= _regions.Count)
            {
                return;
            }
            _regions.RemoveAt(index);
            RefreshRegions();
            PreviewResult();
        }

        private void ClearRegions()
        {
            _regions.Clear();
            RefreshRegions();
            ResetResult();
            _status.Text = "已清空所有区域";
        }

        private void RefreshRegions()
        {
            _regionList.Items.Clear();
            for (int i = 0; i < _regions.Count; i++)
            {
                Rectangle r = _regions[i];
                _regionList.Items.Add("区域 " + (i + 1) + "：(" + r.X + "," + r.Y + ") " + r.Width + "x" + r.Height);
            }
        }

        private void OnSelectionChanged()
        {
            _overlayCanvas.Selection = MapSelectionToOverlay();
            UpdateInfo();
        }

        private Rectangle MapSelectionToOverlay()
        {
            if (_targetImage == null || _overlayImage == null)
            {
                return Rectangle.Empty;
            }
            return ImageUtil.MapRegion(
                _targetCanvas.Selection,
                new Size(_targetImage.Width, _targetImage.Height),
                new Size(_overlayImage.Width, _overlayImage.Height));
        }

        private void ResetResult()
        {
            if (_resultImage != null)
            {
                _resultImage.Dispose();
                _resultImage = null;
            }
            _resultCanvas.SetImage(null);
        }

        private void UpdateInfo()
        {
            string target = _targetImage == null ? "-" : _targetImage.Width + "x" + _targetImage.Height;
            string overlay = _overlayImage == null ? "-" : _overlayImage.Width + "x" + _overlayImage.Height;
            _info.Text = "目标 " + target + " / 覆盖 " + overlay + " / 区域 " + _regions.Count;
        }

        private void SchedulePreview()
        {
            if (_targetImage == null || _overlayImage == null || _regions.Count == 0)
            {
                return;
            }
            _debounce.Stop();
            _debounce.Start();
        }

        private void PreviewResult()
        {
            if (_targetImage == null || _overlayImage == null)
            {
                _status.Text = "请先选择目标图与覆盖图";
                return;
            }
            if (_regions.Count == 0)
            {
                _status.Text = "请至少添加一个区域";
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap result = Compose(_targetImage, _overlayImage, _regions, _featherBar.Value, _opacityBar.Value / 100f);
                if (_resultImage != null)
                {
                    _resultImage.Dispose();
                }
                _resultImage = result;
                _resultCanvas.SetImage(_resultImage);
                _status.Text = "已预览 " + _regions.Count + " 个区域";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "预览失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private static Bitmap Compose(Bitmap target, Bitmap overlay, List<Rectangle> regions, int feather, float opacity)
        {
            Size targetSize = new Size(target.Width, target.Height);
            Size overlaySize = new Size(overlay.Width, overlay.Height);

            Bitmap effect = ImageFilters.Clone(target);
            using (Graphics g = Graphics.FromImage(effect))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingMode = CompositingMode.SourceOver;
                for (int i = 0; i < regions.Count; i++)
                {
                    Rectangle src = ImageUtil.MapRegion(regions[i], targetSize, overlaySize);
                    src = Rectangle.Intersect(src, new Rectangle(0, 0, overlay.Width, overlay.Height));
                    if (src.Width < 1 || src.Height < 1)
                    {
                        continue;
                    }
                    Rectangle dest = ImageUtil.MapRegion(src, overlaySize, targetSize);
                    g.DrawImage(overlay, dest, src, GraphicsUnit.Pixel);
                }
            }

            Bitmap mask = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(mask))
            {
                g.Clear(Color.Transparent);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, 255, 255, 255)))
                {
                    for (int i = 0; i < regions.Count; i++)
                    {
                        g.FillRectangle(brush, regions[i]);
                    }
                }
            }
            if (feather > 0)
            {
                ImageFilters.GaussianBlur(mask, feather);
            }
            if (opacity < 1f)
            {
                ScaleAlpha(mask, opacity);
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
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                for (int i = 3; i < buf.Length; i += 4)
                {
                    buf[i] = (byte)(buf[i] * factor + 0.5f);
                }
                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally
            {
                mask.UnlockBits(data);
            }
        }

        private void SaveResult()
        {
            if (_targetImage == null || _overlayImage == null || _regions.Count == 0)
            {
                _status.Text = "请先添加区域";
                return;
            }

            string dir = Path.GetDirectoryName(_targetPath);
            string name = Path.GetFileNameWithoutExtension(_targetPath) + "_局部覆盖.png";

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
                Bitmap result = Compose(_targetImage, _overlayImage, _regions, _featherBar.Value, _opacityBar.Value / 100f);
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
