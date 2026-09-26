using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class ImageComparePage : ToolPage
    {
        private TextBox _aBox;
        private TextBox _bBox;
        private ComboBox _modeBox;
        private TrackBar _sliderBar;
        private Label _sliderValue;
        private TrackBar _diffBar;
        private Label _diffValue;
        private Label _sliderLabel;
        private Label _diffLabel;
        private ImageCanvas _canvas;
        private Label _status;
        private Timer _debounce;

        private Bitmap _imageA;
        private Bitmap _imageB;
        private Bitmap _resultImage;
        private string _pathA;
        private string _pathB;

        public ImageComparePage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1120, 680);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "图像对比"; }
        }

        public override void Shutdown()
        {
            if (_imageA != null) { _imageA.Dispose(); }
            if (_imageB != null) { _imageB.Dispose(); }
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
            Controls.Add(root);

            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.AutoScroll = true;
            left.Margin = new Padding(3, 3, 3, 3);
            root.Controls.Add(left, 0, 0);

            Label aLabel = new Label();
            aLabel.Text = "图片 A";
            aLabel.Location = new Point(10, 14);
            aLabel.AutoSize = true;
            left.Controls.Add(aLabel);

            Button aBrowse = new Button();
            aBrowse.Text = "浏览...";
            aBrowse.Location = new Point(200, 8);
            aBrowse.Size = new Size(80, 28);
            aBrowse.Click += delegate { Browse(true); };
            left.Controls.Add(aBrowse);

            _aBox = new TextBox();
            _aBox.Location = new Point(10, 42);
            _aBox.Size = new Size(270, 25);
            _aBox.ReadOnly = true;
            left.Controls.Add(_aBox);

            Label bLabel = new Label();
            bLabel.Text = "图片 B";
            bLabel.Location = new Point(10, 78);
            bLabel.AutoSize = true;
            left.Controls.Add(bLabel);

            Button bBrowse = new Button();
            bBrowse.Text = "浏览...";
            bBrowse.Location = new Point(200, 72);
            bBrowse.Size = new Size(80, 28);
            bBrowse.Click += delegate { Browse(false); };
            left.Controls.Add(bBrowse);

            _bBox = new TextBox();
            _bBox.Location = new Point(10, 106);
            _bBox.Size = new Size(270, 25);
            _bBox.ReadOnly = true;
            left.Controls.Add(_bBox);

            Label modeLabel = new Label();
            modeLabel.Text = "对比方式";
            modeLabel.Location = new Point(10, 142);
            modeLabel.AutoSize = true;
            left.Controls.Add(modeLabel);

            _modeBox = new ComboBox();
            _modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _modeBox.Location = new Point(10, 162);
            _modeBox.Size = new Size(270, 25);
            _modeBox.Items.Add("左右并排");
            _modeBox.Items.Add("上下并排");
            _modeBox.Items.Add("滑块对比");
            _modeBox.Items.Add("差异高亮");
            _modeBox.SelectedIndexChanged += delegate { UpdateControls(); SchedulePreview(); };
            left.Controls.Add(_modeBox);

            _sliderLabel = new Label();
            _sliderLabel.Text = "滑块位置";
            _sliderLabel.Location = new Point(10, 196);
            _sliderLabel.AutoSize = true;
            left.Controls.Add(_sliderLabel);

            _sliderBar = new TrackBar();
            _sliderBar.AutoSize = false;
            _sliderBar.TickStyle = TickStyle.None;
            _sliderBar.Minimum = 0;
            _sliderBar.Maximum = 100;
            _sliderBar.Value = 50;
            _sliderBar.Location = new Point(10, 214);
            _sliderBar.Size = new Size(200, 30);
            _sliderBar.ValueChanged += delegate { _sliderValue.Text = _sliderBar.Value + "%"; SchedulePreview(); };
            left.Controls.Add(_sliderBar);

            _sliderValue = new Label();
            _sliderValue.Text = "50%";
            _sliderValue.Location = new Point(216, 220);
            _sliderValue.Size = new Size(64, 20);
            _sliderValue.TextAlign = ContentAlignment.MiddleRight;
            left.Controls.Add(_sliderValue);

            _diffLabel = new Label();
            _diffLabel.Text = "差异强度";
            _diffLabel.Location = new Point(10, 254);
            _diffLabel.AutoSize = true;
            left.Controls.Add(_diffLabel);

            _diffBar = new TrackBar();
            _diffBar.AutoSize = false;
            _diffBar.TickStyle = TickStyle.None;
            _diffBar.Minimum = 50;
            _diffBar.Maximum = 400;
            _diffBar.Value = 150;
            _diffBar.Location = new Point(10, 272);
            _diffBar.Size = new Size(200, 30);
            _diffBar.ValueChanged += delegate { _diffValue.Text = _diffBar.Value + "%"; SchedulePreview(); };
            left.Controls.Add(_diffBar);

            _diffValue = new Label();
            _diffValue.Text = "150%";
            _diffValue.Location = new Point(216, 278);
            _diffValue.Size = new Size(64, 20);
            _diffValue.TextAlign = ContentAlignment.MiddleRight;
            left.Controls.Add(_diffValue);

            Label note = new Label();
            note.Location = new Point(10, 314);
            note.Size = new Size(268, 220);
            note.ForeColor = Color.FromArgb(70, 70, 70);
            note.Text =
                "说明：\r\n" +
                "• 左右/上下并排：两张图按较短边对齐缩放后拼接。\r\n" +
                "• 滑块对比：左边显示图片 B、右边显示图片 A，\r\n" +
                "  拖动滑块改变分界。\r\n" +
                "• 差异高亮：把两图逐像素求差并放大，\r\n" +
                "  差异越大越亮（适合找修改点）。\r\n" +
                "• 图片 B 会自动缩放到与图片 A 相同尺寸。";
            left.Controls.Add(note);

            _canvas = new ImageCanvas();
            _canvas.Dock = DockStyle.Fill;
            _canvas.Margin = new Padding(3, 3, 3, 3);
            _canvas.ReadOnly = true;
            root.Controls.Add(_canvas, 1, 0);

            Button save = new Button();
            save.Text = "保存对比图";
            save.Location = new Point(10, 8);
            save.Size = new Size(130, 32);
            save.Click += delegate { SaveResult(); };
            root.Controls.Add(save, 0, 1);

            _status = new Label();
            _status.Text = "请选择两张图片进行对比";
            _status.Location = new Point(150, 14);
            _status.Size = new Size(600, 22);
            _status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            root.Controls.Add(_status, 1, 1);

            _debounce = new Timer();
            _debounce.Interval = 120;
            _debounce.Tick += delegate { _debounce.Stop(); UpdatePreview(); };

            _modeBox.SelectedIndex = 0;
            UpdateControls();
        }

        private void UpdateControls()
        {
            bool slider = _modeBox.SelectedIndex == 2;
            bool diff = _modeBox.SelectedIndex == 3;
            _sliderBar.Visible = slider;
            _sliderValue.Visible = slider;
            _sliderLabel.Visible = slider;
            _diffBar.Visible = diff;
            _diffValue.Visible = diff;
            _diffLabel.Visible = diff;
        }

        private void Browse(bool isA)
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
                if (isA)
                {
                    if (_imageA != null) { _imageA.Dispose(); }
                    _imageA = bitmap;
                    _pathA = dialog.FileName;
                    _aBox.Text = dialog.FileName;
                }
                else
                {
                    if (_imageB != null) { _imageB.Dispose(); }
                    _imageB = bitmap;
                    _pathB = dialog.FileName;
                    _bBox.Text = dialog.FileName;
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

        private void SchedulePreview()
        {
            if (_imageA == null || _imageB == null)
            {
                return;
            }
            _debounce.Stop();
            _debounce.Start();
        }

        private void UpdatePreview()
        {
            if (_imageA == null || _imageB == null)
            {
                _status.Text = "请选择两张图片（A 与 B）";
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap result = BuildCompare();
                if (_resultImage != null)
                {
                    _resultImage.Dispose();
                }
                _resultImage = result;
                _canvas.SetImage(_resultImage);
                _status.Text = "对比方式：" + _modeBox.Text + "  （A " + _imageA.Width + "x" + _imageA.Height + " / B " + _imageB.Width + "x" + _imageB.Height + "）";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "对比失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private Bitmap BuildCompare()
        {
            switch (_modeBox.SelectedIndex)
            {
                case 1:
                    return ImageCompare.SideBySide(_imageA, _imageB, true, 8);
                case 2:
                    return ImageCompare.Slider(_imageA, _imageB, _sliderBar.Value / 100f);
                case 3:
                    return ImageCompare.Difference(_imageA, _imageB, _diffBar.Value / 100f);
                default:
                    return ImageCompare.SideBySide(_imageA, _imageB, false, 8);
            }
        }

        private void SaveResult()
        {
            if (_imageA == null || _imageB == null)
            {
                _status.Text = "请先选择两张图片";
                return;
            }

            string dir = Path.GetDirectoryName(_pathA);
            string name = Path.GetFileNameWithoutExtension(_pathA) + "_对比.png";

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "保存对比图";
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
                Bitmap result = BuildCompare();
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
