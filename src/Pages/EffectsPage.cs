using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class EffectsPage : ToolPage
    {
        private const int PreviewSize = 1000;

        private TextBox _imageBox;
        private ComboBox _effectBox;
        private Label _p1Label;
        private TrackBar _p1Bar;
        private Label _p1Value;
        private Label _p2Label;
        private TrackBar _p2Bar;
        private Label _p2Value;
        private Label _note;
        private Timer _previewTimer;

        private ImageCanvas _originalCanvas;
        private ImageCanvas _resultCanvas;
        private Label _status;

        private Bitmap _sourceImage;
        private Bitmap _resultImage;

        public EffectsPage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1120, 680);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "特效"; }
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

            Label effectLabel = new Label();
            effectLabel.Text = "特效";
            effectLabel.Location = new Point(10, 78);
            effectLabel.AutoSize = true;
            left.Controls.Add(effectLabel);

            _effectBox = new ComboBox();
            _effectBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _effectBox.Location = new Point(10, 98);
            _effectBox.Size = new Size(270, 25);
            _effectBox.Items.Add("马赛克");
            _effectBox.Items.Add("高斯模糊");
            _effectBox.Items.Add("动感模糊");
            _effectBox.Items.Add("油画");
            _effectBox.Items.Add("素描");
            _effectBox.Items.Add("浮雕");
            _effectBox.Items.Add("边缘检测");
            _effectBox.Items.Add("背景虚化");
            _effectBox.Items.Add("纸张纹理");
            _effectBox.Items.Add("光晕");
            _effectBox.Items.Add("圆角");
            _effectBox.Items.Add("圆形头像");
            _effectBox.Items.Add("投影");
            _effectBox.SelectedIndexChanged += delegate { OnEffectChanged(); };
            left.Controls.Add(_effectBox);

            _p1Label = new Label();
            _p1Label.Location = new Point(10, 132);
            _p1Label.AutoSize = true;
            left.Controls.Add(_p1Label);

            _p1Bar = AddBar(left, 150);
            _p1Value = AddValue(left, 160);

            _p2Label = new Label();
            _p2Label.Location = new Point(10, 190);
            _p2Label.AutoSize = true;
            left.Controls.Add(_p2Label);

            _p2Bar = AddBar(left, 208);
            _p2Value = AddValue(left, 218);

            Button reset = new Button();
            reset.Text = "重置参数";
            reset.Location = new Point(10, 250);
            reset.Size = new Size(100, 30);
            reset.Click += delegate { ConfigureEffect(); UpdatePreview(); };
            left.Controls.Add(reset);

            _note = new Label();
            _note.Location = new Point(10, 292);
            _note.Size = new Size(268, 210);
            _note.ForeColor = Color.FromArgb(70, 70, 70);
            left.Controls.Add(_note);

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
            imagesGrid.Dock = DockStyle.Fill;
            imagesGrid.Margin = new Padding(3, 3, 3, 3);
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
            root.Controls.Add(imagesGrid, 1, 0);

            Button save = new Button();
            save.Text = "保存为 PNG";
            save.Location = new Point(10, 8);
            save.Size = new Size(120, 32);
            save.Click += delegate { SaveResult(); };
            root.Controls.Add(save, 0, 1);

            _status = new Label();
            _status.Text = "请选择图片，再选择要应用的特效";
            _status.Location = new Point(140, 14);
            _status.Size = new Size(600, 22);
            _status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            root.Controls.Add(_status, 1, 1);

            _previewTimer = new Timer();
            _previewTimer.Interval = 150;
            _previewTimer.Tick += delegate
            {
                _previewTimer.Stop();
                UpdatePreview();
            };

            _effectBox.SelectedIndex = 0;
        }

        private TrackBar AddBar(Control parent, int y)
        {
            TrackBar bar = new TrackBar();
            bar.AutoSize = false;
            bar.TickStyle = TickStyle.None;
            bar.Location = new Point(10, y);
            bar.Size = new Size(200, 30);
            bar.ValueChanged += delegate { OnParamChanged(); };
            parent.Controls.Add(bar);
            return bar;
        }

        private Label AddValue(Control parent, int y)
        {
            Label label = new Label();
            label.Location = new Point(216, y);
            label.Size = new Size(64, 20);
            label.TextAlign = ContentAlignment.MiddleRight;
            parent.Controls.Add(label);
            return label;
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

        private void OnEffectChanged()
        {
            ConfigureEffect();
            UpdatePreview();
        }

        private void OnParamChanged()
        {
            _p1Value.Text = _p1Bar.Value.ToString();
            _p2Value.Text = _p2Bar.Value.ToString();
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

        private void SetSlider(TrackBar bar, Label value, int min, int max, int current)
        {
            bar.Minimum = min;
            bar.Maximum = max;
            bar.Value = current;
            value.Text = current.ToString();
        }

        private void SetP1(string text, int min, int max, int current)
        {
            _p1Label.Text = text;
            _p1Label.Visible = true;
            _p1Bar.Visible = true;
            _p1Value.Visible = true;
            SetSlider(_p1Bar, _p1Value, min, max, current);
        }

        private void SetP2(string text, int min, int max, int current)
        {
            _p2Label.Text = text;
            _p2Label.Visible = true;
            _p2Bar.Visible = true;
            _p2Value.Visible = true;
            SetSlider(_p2Bar, _p2Value, min, max, current);
        }

        private void HideP1()
        {
            _p1Label.Visible = false;
            _p1Bar.Visible = false;
            _p1Value.Visible = false;
        }

        private void HideP2()
        {
            _p2Label.Visible = false;
            _p2Bar.Visible = false;
            _p2Value.Visible = false;
        }

        private void ConfigureEffect()
        {
            string name = SelectedEffect();
            HideP1();
            HideP2();

            switch (name)
            {
                case "马赛克":
                    SetP1("块大小", 3, 60, 12);
                    _note.Text = "把画面分成方块并取平均值，形成打码/像素化效果。\r\n块大小越大越模糊，常用于遮挡敏感信息。";
                    break;

                case "高斯模糊":
                    SetP1("半径", 1, 40, 8);
                    _note.Text = "对整个画面做柔和的模糊，半径越大越糊。\r\n可用于背景柔化或隐私遮挡。";
                    break;

                case "动感模糊":
                    SetP1("长度", 2, 60, 16);
                    SetP2("角度", 0, 180, 0);
                    _note.Text = "沿指定角度做定向模糊，产生速度感。\r\n角度 0 为水平、90 为垂直。";
                    break;

                case "油画":
                    SetP1("笔触", 1, 6, 2);
                    SetP2("色阶", 4, 24, 12);
                    _note.Text = "用邻域内出现最多的颜色替代当前像素，\r\n模拟油画的块状笔触。笔触越大、色阶越少，风格越强。";
                    break;

                case "素描":
                    SetP1("强度", 0, 100, 80);
                    _note.Text = "提取明暗轮廓，模拟铅笔素描。\r\n强度 0 为原图，100 为纯线稿。";
                    break;

                case "浮雕":
                    SetP1("强度", 0, 100, 70);
                    _note.Text = "按光照方向做差分，得到立体浮雕效果。\r\n强度越大凹凸感越强。";
                    break;

                case "边缘检测":
                    SetP1("强度", 0, 100, 80);
                    _note.Text = "用 Sobel 算子提取边缘，得到白底黑线的线稿。\r\n强度 100 为纯边缘图。";
                    break;

                case "背景虚化":
                    SetP1("模糊", 1, 40, 14);
                    SetP2("清晰区", 0, 90, 45);
                    _note.Text = "画面中心保持清晰、四周逐渐模糊，模拟大光圈人像。\r\n“清晰区”为清晰的半径占比，越大清晰范围越大。";
                    break;

                case "纸张纹理":
                    SetP1("强度", 0, 100, 50);
                    _note.Text = "叠加柔和的噪点与暖色，模拟纸张/复古质感。\r\n强度越大颗粒与泛黄越明显。";
                    break;

                case "光晕":
                    SetP1("强度", 0, 100, 60);
                    _note.Text = "提取高光并向外扩散，形成柔美的光晕（Bloom）。\r\n适合提亮高光、营造梦幻氛围。";
                    break;

                case "圆角":
                    SetP1("半径", 0, 300, 60);
                    _note.Text = "为图片切出圆角（透明背景），半径单位为像素。\r\n保存为 PNG 可保留透明圆角。";
                    break;

                case "圆形头像":
                    _note.Text = "按较短边居中裁剪并套用圆形蒙版，生成头像方图。\r\n保存为 PNG 可保留透明背景。\r\n本特效无需参数。";
                    break;

                case "投影":
                    SetP1("偏移", 1, 40, 12);
                    SetP2("模糊", 0, 40, 12);
                    _note.Text = "在图片右下方向外扩展画布并添加柔和阴影。\r\n偏移控制距离、模糊控制阴影柔化程度。";
                    break;

                default:
                    _note.Text = string.Empty;
                    break;
            }
        }

        private string SelectedEffect()
        {
            return _effectBox.SelectedIndex < 0 ? string.Empty : (string)_effectBox.SelectedItem;
        }

        private Bitmap Render(Bitmap baseImage)
        {
            string name = SelectedEffect();
            Bitmap work = ImageFilters.Clone(baseImage);

            switch (name)
            {
                case "马赛克":
                    ImageFilters.Mosaic(work, _p1Bar.Value);
                    break;

                case "高斯模糊":
                    ImageFilters.GaussianBlur(work, _p1Bar.Value);
                    break;

                case "动感模糊":
                    ImageFilters.MotionBlur(work, _p1Bar.Value, _p2Bar.Value);
                    break;

                case "油画":
                    ImageFilters.OilPaint(work, _p1Bar.Value, _p2Bar.Value);
                    break;

                case "素描":
                    ImageFilters.Sketch(work, _p1Bar.Value / 100f);
                    break;

                case "浮雕":
                    ImageFilters.Emboss(work, _p1Bar.Value / 100f);
                    break;

                case "边缘检测":
                    ImageFilters.EdgeDetect(work, _p1Bar.Value / 100f);
                    break;

                case "背景虚化":
                    ImageFilters.BackgroundBlur(work, _p1Bar.Value, _p2Bar.Value / 100f);
                    break;

                case "纸张纹理":
                    ImageFilters.Paper(work, _p1Bar.Value / 100f, 20240607);
                    break;

                case "光晕":
                    ImageFilters.Glow(work, _p1Bar.Value / 100f);
                    break;

                case "圆角":
                    ImageFilters.RoundedCorners(work, _p1Bar.Value);
                    break;

                case "圆形头像":
                    {
                        Bitmap circle = ImageFilters.Circle(baseImage);
                        work.Dispose();
                        work = circle;
                    }
                    break;

                case "投影":
                    {
                        Bitmap shadow = ImageFilters.DropShadow(baseImage, _p1Bar.Value, _p1Bar.Value, _p2Bar.Value, 0.55f);
                        work.Dispose();
                        work = shadow;
                    }
                    break;
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
                _status.Text = "当前特效：" + SelectedEffect();
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
            if (_sourceImage == null)
            {
                _status.Text = "请先选择图片";
                return;
            }

            string sourcePath = _imageBox.Text;
            string dir = Path.GetDirectoryName(sourcePath);
            string name = Path.GetFileNameWithoutExtension(sourcePath) + "_" + SelectedEffect() + ".png";

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
