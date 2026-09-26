using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class StyleAdjustPage : ToolPage
    {
        private const string ManualPreset = "自定义（手动调节）";
        private const int PreviewSize = 1400;

        private TextBox _imageBox;
        private ComboBox _presetBox;
        private TrackBar _strengthBar;
        private Label _strengthValue;
        private Timer _previewTimer;

        private GroupBox _manualGroup;
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

        private CheckBox _vignetteBox;
        private CheckBox _softenBox;
        private CheckBox _sharpenBox;
        private CheckBox _grainBox;
        private CheckBox _lightLeakBox;
        private CheckBox _frameBox;
        private TrackBar _effectStrengthBar;
        private Label _effectStrengthValue;

        private ImageCanvas _originalCanvas;
        private ImageCanvas _resultCanvas;
        private Label _status;

        private Bitmap _sourceImage;
        private Bitmap _resultImage;

        public StyleAdjustPage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1120, 680);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "风格调整"; }
        }

        public override void Shutdown()
        {
            if (_sourceImage != null)
            {
                _sourceImage.Dispose();
            }
            if (_resultImage != null)
            {
                _resultImage.Dispose();
            }
        }

        private void BuildUi()
        {
            Label imageLabel = new Label();
            imageLabel.Text = "图片";
            imageLabel.Location = new Point(10, 14);
            imageLabel.AutoSize = true;
            Controls.Add(imageLabel);

            Button browse = new Button();
            browse.Text = "浏览...";
            browse.Location = new Point(200, 8);
            browse.Size = new Size(80, 28);
            browse.Click += delegate { BrowseImage(); };
            Controls.Add(browse);

            _imageBox = new TextBox();
            _imageBox.Location = new Point(10, 42);
            _imageBox.Size = new Size(270, 25);
            _imageBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _imageBox.ReadOnly = true;
            Controls.Add(_imageBox);

            Label presetLabel = new Label();
            presetLabel.Text = "风格滤镜";
            presetLabel.Location = new Point(10, 78);
            presetLabel.AutoSize = true;
            Controls.Add(presetLabel);

            _presetBox = new ComboBox();
            _presetBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _presetBox.Location = new Point(10, 98);
            _presetBox.Size = new Size(270, 25);
            _presetBox.Items.Add("原图");
            _presetBox.Items.Add("冷白明亮");
            _presetBox.Items.Add("暖阳");
            _presetBox.Items.Add("清新自然");
            _presetBox.Items.Add("日系通透");
            _presetBox.Items.Add("高级灰");
            _presetBox.Items.Add("复古胶片");
            _presetBox.Items.Add("青橙");
            _presetBox.Items.Add("黑白");
            _presetBox.Items.Add("高对比黑白");
            _presetBox.Items.Add("鲜艳增强");
            _presetBox.Items.Add("冷调夜景");
            _presetBox.Items.Add("蓝调忧郁");
            _presetBox.Items.Add("怀旧泛黄");
            _presetBox.Items.Add(ManualPreset);
            _presetBox.SelectedIndexChanged += delegate { OnPresetChanged(); };
            Controls.Add(_presetBox);

            Label strengthLabel = new Label();
            strengthLabel.Text = "强度";
            strengthLabel.Location = new Point(10, 132);
            strengthLabel.AutoSize = true;
            Controls.Add(strengthLabel);

            _strengthBar = new TrackBar();
            _strengthBar.AutoSize = false;
            _strengthBar.Minimum = 0;
            _strengthBar.Maximum = 100;
            _strengthBar.Value = 100;
            _strengthBar.TickStyle = TickStyle.None;
            _strengthBar.Location = new Point(10, 150);
            _strengthBar.Size = new Size(195, 30);
            _strengthBar.ValueChanged += delegate { OnStrengthChanged(); };
            Controls.Add(_strengthBar);

            _strengthValue = new Label();
            _strengthValue.Text = "100%";
            _strengthValue.Location = new Point(212, 160);
            _strengthValue.Size = new Size(68, 20);
            _strengthValue.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_strengthValue);

            _manualGroup = new GroupBox();
            _manualGroup.Text = "手动调节";
            _manualGroup.Location = new Point(10, 198);
            _manualGroup.Size = new Size(270, 236);
            Controls.Add(_manualGroup);
            _brightBar = AddSlider(_manualGroup, "亮度", 18, -100, 100, 0, out _brightValue);
            _contrastBar = AddSlider(_manualGroup, "对比度", 50, -100, 100, 0, out _contrastValue);
            _saturationBar = AddSlider(_manualGroup, "饱和度", 82, 0, 200, 100, out _saturationValue);
            _temperatureBar = AddSlider(_manualGroup, "色温", 114, -100, 100, 0, out _temperatureValue);
            _tintBar = AddSlider(_manualGroup, "色调", 146, -100, 100, 0, out _tintValue);

            Button resetButton = new Button();
            resetButton.Text = "重置";
            resetButton.Location = new Point(148, 182);
            resetButton.Size = new Size(112, 28);
            resetButton.Click += delegate { ResetManual(); };
            _manualGroup.Controls.Add(resetButton);

            GroupBox effectGroup = new GroupBox();
            effectGroup.Text = "特效";
            effectGroup.Location = new Point(10, 442);
            effectGroup.Size = new Size(270, 162);
            Controls.Add(effectGroup);

            _vignetteBox = AddCheck(effectGroup, "暗角", 12, 26);
            _softenBox = AddCheck(effectGroup, "柔焦", 100, 26);
            _sharpenBox = AddCheck(effectGroup, "锐化", 188, 26);
            _grainBox = AddCheck(effectGroup, "颗粒", 12, 54);
            _lightLeakBox = AddCheck(effectGroup, "漏光", 100, 54);
            _frameBox = AddCheck(effectGroup, "边框", 188, 54);

            Label effectStrengthLabel = new Label();
            effectStrengthLabel.Text = "特效强度";
            effectStrengthLabel.Location = new Point(12, 86);
            effectStrengthLabel.AutoSize = true;
            effectGroup.Controls.Add(effectStrengthLabel);

            _effectStrengthBar = new TrackBar();
            _effectStrengthBar.AutoSize = false;
            _effectStrengthBar.Minimum = 0;
            _effectStrengthBar.Maximum = 100;
            _effectStrengthBar.Value = 60;
            _effectStrengthBar.TickStyle = TickStyle.None;
            _effectStrengthBar.Location = new Point(12, 106);
            _effectStrengthBar.Size = new Size(190, 30);
            _effectStrengthBar.ValueChanged += delegate { OnEffectStrengthChanged(); };
            effectGroup.Controls.Add(_effectStrengthBar);

            _effectStrengthValue = new Label();
            _effectStrengthValue.Text = "60%";
            _effectStrengthValue.Location = new Point(204, 112);
            _effectStrengthValue.Size = new Size(56, 20);
            _effectStrengthValue.TextAlign = ContentAlignment.MiddleRight;
            effectGroup.Controls.Add(_effectStrengthValue);

            Label originalLabel = new Label();
            originalLabel.Text = "原图";
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
            _status.Text = "请选择图片，程序会自动应用所选风格滤镜";
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

            _presetBox.SelectedIndex = 1;
            SetManualEnabled(false);
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
            bar.ValueChanged += delegate { OnManualChanged(); };
            parent.Controls.Add(bar);

            valueLabel = new Label();
            valueLabel.Text = value.ToString();
            valueLabel.Location = new Point(204, y + 6);
            valueLabel.Size = new Size(54, 20);
            valueLabel.TextAlign = ContentAlignment.MiddleRight;
            parent.Controls.Add(valueLabel);

            return bar;
        }

        private CheckBox AddCheck(Control parent, string text, int x, int y)
        {
            CheckBox box = new CheckBox();
            box.Text = text;
            box.Location = new Point(x, y);
            box.AutoSize = true;
            box.CheckedChanged += delegate { SchedulePreview(); };
            parent.Controls.Add(box);
            return box;
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
                Bitmap bitmap = ImageUtil.LoadImage(dialog.FileName);
                if (_sourceImage != null)
                {
                    _sourceImage.Dispose();
                }
                _sourceImage = bitmap;
                _imageBox.Text = dialog.FileName;
                _originalCanvas.SetImage(_sourceImage);
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

        private void OnPresetChanged()
        {
            SetManualEnabled(_presetBox.SelectedIndex == _presetBox.Items.Count - 1);
            UpdatePreview();
        }

        private void SetManualEnabled(bool enabled)
        {
            _manualGroup.Enabled = enabled;
        }

        private void OnStrengthChanged()
        {
            _strengthValue.Text = _strengthBar.Value + "%";
            SchedulePreview();
        }

        private void OnEffectStrengthChanged()
        {
            _effectStrengthValue.Text = _effectStrengthBar.Value + "%";
            SchedulePreview();
        }

        private void ResetManual()
        {
            _brightBar.Value = 0;
            _contrastBar.Value = 0;
            _saturationBar.Value = 100;
            _temperatureBar.Value = 0;
            _tintBar.Value = 0;
            OnManualChanged();
        }

        private void OnManualChanged()
        {
            _brightValue.Text = _brightBar.Value.ToString();
            _contrastValue.Text = _contrastBar.Value.ToString();
            _saturationValue.Text = _saturationBar.Value.ToString();
            _temperatureValue.Text = _temperatureBar.Value.ToString();
            _tintValue.Text = _tintBar.Value.ToString();
            SchedulePreview();
        }

        private void SchedulePreview()
        {
            if (_sourceImage == null)
            {
                return;
            }
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private ColorMatrix CurrentMatrix()
        {
            return ImageEffects.Lerp(ImageEffects.Identity(), BaseMatrix(), _strengthBar.Value / 100f);
        }

        private ColorMatrix BaseMatrix()
        {
            int index = _presetBox.SelectedIndex;
            if (index == _presetBox.Items.Count - 1)
            {
                return BuildManualMatrix();
            }
            if (index <= 0)
            {
                return ImageEffects.Identity();
            }
            return ImageEffects.PresetMatrix((string)_presetBox.Items[index]);
        }

        private ColorMatrix BuildManualMatrix()
        {
            ColorMatrix matrix = ImageEffects.Saturation(_saturationBar.Value / 100f);
            matrix = ImageEffects.Multiply(matrix, ImageEffects.Contrast(_contrastBar.Value / 100f));
            matrix = ImageEffects.Multiply(matrix, ImageEffects.Brightness(_brightBar.Value / 100f));
            matrix = ImageEffects.Multiply(matrix, ImageEffects.Temperature(_temperatureBar.Value / 100f));
            matrix = ImageEffects.Multiply(matrix, ImageEffects.Tint(_tintBar.Value / 100f));
            return matrix;
        }

        private Bitmap Render(Bitmap baseImage)
        {
            Bitmap work = ImageUtil.ApplyColorMatrix(baseImage, CurrentMatrix());
            float strength = _effectStrengthBar.Value / 100f;

            if (_vignetteBox.Checked)
            {
                ImageEffects.Vignette(work, strength);
            }
            if (_softenBox.Checked)
            {
                ImageEffects.Soften(work, strength);
            }
            if (_sharpenBox.Checked)
            {
                ImageEffects.Sharpen(work, strength);
            }
            if (_grainBox.Checked)
            {
                ImageEffects.Grain(work, strength, 20240607);
            }
            if (_lightLeakBox.Checked)
            {
                ImageEffects.LightLeak(work, strength);
            }
            if (_frameBox.Checked)
            {
                ImageEffects.Frame(work, strength);
            }
            return work;
        }

        private void UpdatePreview()
        {
            if (_sourceImage == null)
            {
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap previewBase = ImageUtil.CreatePreview(_sourceImage, PreviewSize);
                Bitmap baseImage = previewBase != null ? previewBase : _sourceImage;
                Bitmap work = Render(baseImage);
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
                _status.Text = CurrentDescription();
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

        private string CurrentDescription()
        {
            string name = _presetBox.SelectedIndex == _presetBox.Items.Count - 1
                ? "自定义"
                : _presetBox.Text;
            return "当前风格：" + name + "（强度 " + _strengthBar.Value + "%，特效强度 " + _effectStrengthBar.Value + "%）";
        }

        private void SaveResult()
        {
            if (_sourceImage == null)
            {
                _status.Text = "请先选择图片";
                return;
            }

            string sourcePath = _imageBox.Text;
            string dir = Path.GetDirectoryName(sourcePath);
            string preset = _presetBox.SelectedIndex == _presetBox.Items.Count - 1 ? "自定义" : _presetBox.Text;
            string name = Path.GetFileNameWithoutExtension(sourcePath) + "_" + preset + ".png";

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
                Bitmap result = Render(_sourceImage);
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
