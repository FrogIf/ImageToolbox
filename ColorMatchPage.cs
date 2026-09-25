using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class ColorMatchPage : ToolPage
    {
        private const int PreviewSize = 1400;

        private TextBox _refBox;
        private ImageCanvas _refCanvas;
        private TextBox _targetBox;
        private TrackBar _brightBar;
        private TrackBar _contrastBar;
        private TrackBar _saturationBar;
        private TrackBar _temperatureBar;
        private TrackBar _tintBar;
        private Label _brightValue;
        private Label _contrastValue;
        private Label _saturationValue;
        private Label _temperatureValue;
        private Label _tintValue;
        private ImageCanvas _originalCanvas;
        private ImageCanvas _resultCanvas;
        private Label _status;
        private Label _info;
        private Timer _previewTimer;
        private bool _suppress;

        private Bitmap _refImage;
        private Bitmap _targetImage;
        private Bitmap _resultImage;

        public ColorMatchPage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1120, 680);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "取色配色"; }
        }

        public override void Shutdown()
        {
            if (_refImage != null)
            {
                _refImage.Dispose();
            }
            if (_targetImage != null)
            {
                _targetImage.Dispose();
            }
            if (_resultImage != null)
            {
                _resultImage.Dispose();
            }
        }

        private void BuildUi()
        {
            Label refLabel = new Label();
            refLabel.Text = "参考图（风格来源）";
            refLabel.Location = new Point(10, 14);
            refLabel.AutoSize = true;
            Controls.Add(refLabel);

            Button refBrowse = new Button();
            refBrowse.Text = "浏览...";
            refBrowse.Location = new Point(200, 8);
            refBrowse.Size = new Size(80, 28);
            refBrowse.Click += delegate { BrowseReference(); };
            Controls.Add(refBrowse);

            _refBox = new TextBox();
            _refBox.Location = new Point(10, 40);
            _refBox.Size = new Size(270, 25);
            _refBox.ReadOnly = true;
            Controls.Add(_refBox);

            _refCanvas = new ImageCanvas();
            _refCanvas.Location = new Point(10, 72);
            _refCanvas.Size = new Size(270, 84);
            _refCanvas.ReadOnly = true;
            Controls.Add(_refCanvas);

            Label targetLabel = new Label();
            targetLabel.Text = "目标图（被处理）";
            targetLabel.Location = new Point(10, 166);
            targetLabel.AutoSize = true;
            Controls.Add(targetLabel);

            Button targetBrowse = new Button();
            targetBrowse.Text = "浏览...";
            targetBrowse.Location = new Point(200, 160);
            targetBrowse.Size = new Size(80, 28);
            targetBrowse.Click += delegate { BrowseTarget(); };
            Controls.Add(targetBrowse);

            _targetBox = new TextBox();
            _targetBox.Location = new Point(10, 192);
            _targetBox.Size = new Size(270, 25);
            _targetBox.ReadOnly = true;
            Controls.Add(_targetBox);

            Button sample = new Button();
            sample.Text = "取样并生成参数";
            sample.Location = new Point(10, 226);
            sample.Size = new Size(270, 32);
            sample.Click += delegate { Sample(); };
            Controls.Add(sample);

            GroupBox paramGroup = new GroupBox();
            paramGroup.Text = "匹配参数（可微调）";
            paramGroup.Location = new Point(10, 268);
            paramGroup.Size = new Size(270, 236);
            Controls.Add(paramGroup);

            _brightBar = AddSlider(paramGroup, "亮度", 18, -100, 100, 0, out _brightValue);
            _contrastBar = AddSlider(paramGroup, "对比度", 50, -100, 100, 0, out _contrastValue);
            _saturationBar = AddSlider(paramGroup, "饱和度", 82, 0, 200, 100, out _saturationValue);
            _temperatureBar = AddSlider(paramGroup, "色温", 114, -100, 100, 0, out _temperatureValue);
            _tintBar = AddSlider(paramGroup, "色调", 146, -100, 100, 0, out _tintValue);

            Button reset = new Button();
            reset.Text = "重置";
            reset.Location = new Point(148, 182);
            reset.Size = new Size(112, 28);
            reset.Click += delegate { ResetParams(); };
            paramGroup.Controls.Add(reset);

            _info = new Label();
            _info.Text = "选择两张图片后，点“取样并生成参数”。\r\n程序会分析参考图的整体影调与色调，\r\n推导出下方参数并套用到目标图。";
            _info.Location = new Point(10, 512);
            _info.Size = new Size(270, 92);
            Controls.Add(_info);

            Label originalLabel = new Label();
            originalLabel.Text = "目标图（原图）";
            originalLabel.Dock = DockStyle.Fill;
            originalLabel.TextAlign = ContentAlignment.MiddleLeft;

            _originalCanvas = new ImageCanvas();
            _originalCanvas.Dock = DockStyle.Fill;
            _originalCanvas.Margin = new Padding(3, 0, 8, 3);
            _originalCanvas.ReadOnly = true;

            Label resultLabel = new Label();
            resultLabel.Text = "效果预览";
            resultLabel.Dock = DockStyle.Fill;
            resultLabel.TextAlign = ContentAlignment.MiddleLeft;

            _resultCanvas = new ImageCanvas();
            _resultCanvas.Dock = DockStyle.Fill;
            _resultCanvas.Margin = new Padding(8, 0, 3, 3);
            _resultCanvas.ReadOnly = true;

            TableLayoutPanel imagesGrid = new TableLayoutPanel();
            imagesGrid.Location = new Point(300, 12);
            imagesGrid.Size = new Size(810, 566);
            imagesGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            imagesGrid.ColumnCount = 2;
            imagesGrid.RowCount = 2;
            imagesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            imagesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            imagesGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            imagesGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            imagesGrid.Controls.Add(originalLabel, 0, 0);
            imagesGrid.Controls.Add(resultLabel, 1, 0);
            imagesGrid.Controls.Add(_originalCanvas, 0, 1);
            imagesGrid.Controls.Add(_resultCanvas, 1, 1);
            Controls.Add(imagesGrid);

            Button save = new Button();
            save.Text = "保存为 PNG";
            save.Location = new Point(10, 612);
            save.Size = new Size(120, 32);
            save.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            save.Click += delegate { SaveResult(); };
            Controls.Add(save);

            _status = new Label();
            _status.Text = "就绪";
            _status.Location = new Point(140, 618);
            _status.Size = new Size(970, 22);
            _status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_status);

            _previewTimer = new Timer();
            _previewTimer.Interval = 150;
            _previewTimer.Tick += delegate
            {
                _previewTimer.Stop();
                UpdatePreview();
            };
        }

        private TrackBar AddSlider(Control parent, string text, int y, int min, int max, int value, out Label valueLabel)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(8, y + 6);
            label.AutoSize = true;
            parent.Controls.Add(label);

            TrackBar bar = new TrackBar();
            bar.AutoSize = false;
            bar.Minimum = min;
            bar.Maximum = max;
            bar.Value = value;
            bar.TickStyle = TickStyle.None;
            bar.Location = new Point(64, y);
            bar.Size = new Size(138, 30);
            bar.ValueChanged += delegate { OnSliderChanged(); };
            parent.Controls.Add(bar);

            valueLabel = new Label();
            valueLabel.Text = value.ToString();
            valueLabel.Location = new Point(204, y + 6);
            valueLabel.Size = new Size(54, 20);
            valueLabel.TextAlign = ContentAlignment.MiddleRight;
            parent.Controls.Add(valueLabel);

            return bar;
        }

        private void BrowseReference()
        {
            string path = PickImage();
            if (path == null)
            {
                return;
            }
            Bitmap bitmap = LoadBitmap(path);
            if (bitmap == null)
            {
                return;
            }
            if (_refImage != null)
            {
                _refImage.Dispose();
            }
            _refImage = bitmap;
            _refBox.Text = path;
            _refCanvas.SetImage(_refImage);
            _status.Text = "已加载参考图";
        }

        private void BrowseTarget()
        {
            string path = PickImage();
            if (path == null)
            {
                return;
            }
            Bitmap bitmap = LoadBitmap(path);
            if (bitmap == null)
            {
                return;
            }
            if (_targetImage != null)
            {
                _targetImage.Dispose();
            }
            _targetImage = bitmap;
            _targetBox.Text = path;
            _originalCanvas.SetImage(_targetImage);
            UpdatePreview();
            _status.Text = "已加载目标图";
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

        private Bitmap LoadBitmap(string path)
        {
            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                return ImageUtil.LoadImage(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private void Sample()
        {
            if (_refImage == null)
            {
                _status.Text = "请先选择参考图";
                return;
            }
            if (_targetImage == null)
            {
                _status.Text = "请先选择目标图";
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                ImageEffects.ColorStats refStats = ImageEffects.MeasureStats(_refImage, 256);
                ImageEffects.ColorStats targetStats = ImageEffects.MeasureStats(_targetImage, 256);
                double[] p = ImageEffects.EstimateAdjustment(_targetImage, refStats, 3);
                SetSliderValues(p);
                UpdatePreview();
                _info.Text = string.Format(
                    "参考: 亮度{0:F2} 对比{1:F2} 饱和{2:F2}\r\n目标: 亮度{3:F2} 对比{4:F2} 饱和{5:F2}\r\n推导滑块: 亮度{6} 对比{7} 饱和{8}\r\n色温{9} 色调{10}",
                    refStats.Bright, refStats.Contrast, refStats.Sat,
                    targetStats.Bright, targetStats.Contrast, targetStats.Sat,
                    _brightBar.Value, _contrastBar.Value, _saturationBar.Value,
                    _temperatureBar.Value, _tintBar.Value);
                _status.Text = "已取样并生成参数，可手动微调";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "取样失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private void SetSliderValues(double[] p)
        {
            _suppress = true;
            _brightBar.Value = (int)Math.Round(p[0] * 100.0);
            _contrastBar.Value = (int)Math.Round(p[1] * 100.0);
            _saturationBar.Value = (int)Math.Round(p[2] * 100.0);
            _temperatureBar.Value = (int)Math.Round(p[3] * 100.0);
            _tintBar.Value = (int)Math.Round(p[4] * 100.0);
            _suppress = false;
            UpdateSliderLabels();
        }

        private void ResetParams()
        {
            _suppress = true;
            _brightBar.Value = 0;
            _contrastBar.Value = 0;
            _saturationBar.Value = 100;
            _temperatureBar.Value = 0;
            _tintBar.Value = 0;
            _suppress = false;
            UpdateSliderLabels();
            UpdatePreview();
        }

        private void OnSliderChanged()
        {
            if (_suppress)
            {
                return;
            }
            UpdateSliderLabels();
            SchedulePreview();
        }

        private void UpdateSliderLabels()
        {
            _brightValue.Text = _brightBar.Value.ToString();
            _contrastValue.Text = _contrastBar.Value.ToString();
            _saturationValue.Text = _saturationBar.Value.ToString();
            _temperatureValue.Text = _temperatureBar.Value.ToString();
            _tintValue.Text = _tintBar.Value.ToString();
        }

        private void SchedulePreview()
        {
            if (_targetImage == null)
            {
                return;
            }
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private double[] CurrentParams()
        {
            return new double[]
            {
                _brightBar.Value / 100.0,
                _contrastBar.Value / 100.0,
                _saturationBar.Value / 100.0,
                _temperatureBar.Value / 100.0,
                _tintBar.Value / 100.0
            };
        }

        private void UpdatePreview()
        {
            if (_targetImage == null)
            {
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap previewBase = ImageUtil.CreatePreview(_targetImage, PreviewSize);
                Bitmap baseImage = previewBase != null ? previewBase : _targetImage;
                Bitmap work = ImageUtil.ApplyColorMatrix(baseImage, ImageEffects.MatrixFromParams(CurrentParams()));
                if (previewBase != null)
                {
                    previewBase.Dispose();
                }
                if (_resultImage != null)
                {
                    _resultImage.Dispose();
                }
                _resultImage = work;
                _resultCanvas.SetImage(_resultImage);
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

        private void SaveResult()
        {
            if (_targetImage == null)
            {
                _status.Text = "请先选择目标图";
                return;
            }

            string dir = Path.GetDirectoryName(_targetBox.Text);
            string name = Path.GetFileNameWithoutExtension(_targetBox.Text) + "_取色配色.png";

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
                Bitmap result = ImageUtil.ApplyColorMatrix(_targetImage, ImageEffects.MatrixFromParams(CurrentParams()));
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
