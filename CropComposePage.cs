using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class CropComposePage : ToolPage
    {
        private static readonly float[] Ratios = { 0f, 1f, 16f / 9f, 3f / 2f, 4f / 3f, 9f / 16f, 2f / 3f, 3f / 4f };

        private TextBox _imageBox;
        private ImageCanvas _canvas;

        private Bitmap _sourceImage;
        private Bitmap _currentImage;

        private ComboBox _ratioBox;
        private Label _selectionLabel;
        private TrackBar _angleBar;
        private Label _angleValue;
        private CheckBox _autoCropBox;
        private NumericUpDown _canvasWidthNum;
        private NumericUpDown _canvasHeightNum;
        private ComboBox _anchorBox;
        private Button _fillButton;
        private CheckBox _transparentBox;
        private Color _fillColor = Color.White;
        private Label _status;
        private Label _sizeLabel;

        public CropComposePage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1020, 700);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "裁剪构图"; }
        }

        public override void Shutdown()
        {
            if (_currentImage != null)
            {
                _currentImage.Dispose();
            }
            if (_sourceImage != null)
            {
                _sourceImage.Dispose();
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
            _imageBox.Size = new Size(300, 25);
            _imageBox.ReadOnly = true;
            Controls.Add(_imageBox);

            _canvas = new ImageCanvas();
            _canvas.Location = new Point(10, 78);
            _canvas.Size = new Size(630, 560);
            _canvas.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _canvas.SelectionChanged += delegate { UpdateSelectionLabel(); };
            Controls.Add(_canvas);

            BuildControls();

            _status = new Label();
            _status.Text = "请选择图片，然后拖动框选、旋转校正或扩展画布";
            _status.Location = new Point(10, 652);
            _status.Size = new Size(1000, 22);
            _status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_status);
        }

        private void BuildControls()
        {
            GroupBox cropGroup = new GroupBox();
            cropGroup.Text = "裁剪";
            cropGroup.Location = new Point(655, 78);
            cropGroup.Size = new Size(350, 122);
            cropGroup.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(cropGroup);

            AddLabel(cropGroup, "比例", 10, 26);
            _ratioBox = new ComboBox();
            _ratioBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _ratioBox.Location = new Point(80, 22);
            _ratioBox.Size = new Size(255, 25);
            _ratioBox.Items.Add("自由");
            _ratioBox.Items.Add("1:1");
            _ratioBox.Items.Add("16:9");
            _ratioBox.Items.Add("3:2");
            _ratioBox.Items.Add("4:3");
            _ratioBox.Items.Add("9:16");
            _ratioBox.Items.Add("2:3");
            _ratioBox.Items.Add("3:4");
            _ratioBox.SelectedIndex = 0;
            _ratioBox.SelectedIndexChanged += delegate { OnRatioChanged(); };
            cropGroup.Controls.Add(_ratioBox);

            _selectionLabel = new Label();
            _selectionLabel.Text = "选区：无";
            _selectionLabel.Location = new Point(10, 56);
            _selectionLabel.AutoSize = true;
            cropGroup.Controls.Add(_selectionLabel);

            Button applyCrop = new Button();
            applyCrop.Text = "应用裁剪";
            applyCrop.Location = new Point(10, 84);
            applyCrop.Size = new Size(120, 28);
            applyCrop.Click += delegate { ApplyCrop(); };
            cropGroup.Controls.Add(applyCrop);

            Button selectAll = new Button();
            selectAll.Text = "全选画面";
            selectAll.Location = new Point(140, 84);
            selectAll.Size = new Size(120, 28);
            selectAll.Click += delegate { SelectWholeImage(); };
            cropGroup.Controls.Add(selectAll);

            GroupBox rotateGroup = new GroupBox();
            rotateGroup.Text = "旋转校正";
            rotateGroup.Location = new Point(655, 208);
            rotateGroup.Size = new Size(350, 116);
            rotateGroup.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(rotateGroup);

            AddLabel(rotateGroup, "角度", 10, 26);
            _angleBar = new TrackBar();
            _angleBar.AutoSize = false;
            _angleBar.Minimum = -45;
            _angleBar.Maximum = 45;
            _angleBar.Value = 0;
            _angleBar.TickStyle = TickStyle.BottomRight;
            _angleBar.TickFrequency = 5;
            _angleBar.Location = new Point(70, 20);
            _angleBar.Size = new Size(210, 34);
            _angleBar.ValueChanged += delegate { _angleValue.Text = _angleBar.Value + "°"; };
            rotateGroup.Controls.Add(_angleBar);

            _angleValue = new Label();
            _angleValue.Text = "0°";
            _angleValue.Location = new Point(286, 28);
            _angleValue.Size = new Size(56, 20);
            _angleValue.TextAlign = ContentAlignment.MiddleRight;
            rotateGroup.Controls.Add(_angleValue);

            _autoCropBox = new CheckBox();
            _autoCropBox.Text = "自动裁去空白边";
            _autoCropBox.Location = new Point(10, 58);
            _autoCropBox.AutoSize = true;
            _autoCropBox.Checked = true;
            rotateGroup.Controls.Add(_autoCropBox);

            Button applyRotate = new Button();
            applyRotate.Text = "应用校正";
            applyRotate.Location = new Point(10, 82);
            applyRotate.Size = new Size(120, 28);
            applyRotate.Click += delegate { ApplyRotate(); };
            rotateGroup.Controls.Add(applyRotate);

            GroupBox canvasGroup = new GroupBox();
            canvasGroup.Text = "画布（扩展留白 / 压缩裁剪）";
            canvasGroup.Location = new Point(655, 332);
            canvasGroup.Size = new Size(350, 200);
            canvasGroup.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(canvasGroup);

            AddLabel(canvasGroup, "宽", 10, 28);
            _canvasWidthNum = MakeNumeric(canvasGroup, 45, 24, 80, 1, 200000, 800);
            AddLabel(canvasGroup, "高", 145, 28);
            _canvasHeightNum = MakeNumeric(canvasGroup, 180, 24, 80, 1, 200000, 600);

            AddLabel(canvasGroup, "锚点", 10, 62);
            _anchorBox = new ComboBox();
            _anchorBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _anchorBox.Location = new Point(80, 58);
            _anchorBox.Size = new Size(255, 25);
            _anchorBox.Items.Add("左上");
            _anchorBox.Items.Add("上中");
            _anchorBox.Items.Add("右上");
            _anchorBox.Items.Add("左中");
            _anchorBox.Items.Add("居中");
            _anchorBox.Items.Add("右中");
            _anchorBox.Items.Add("左下");
            _anchorBox.Items.Add("下中");
            _anchorBox.Items.Add("右下");
            _anchorBox.SelectedIndex = 4;
            canvasGroup.Controls.Add(_anchorBox);

            _fillButton = new Button();
            _fillButton.Text = "填充色";
            _fillButton.Location = new Point(10, 94);
            _fillButton.Size = new Size(90, 26);
            _fillButton.BackColor = _fillColor;
            _fillButton.Click += delegate { ChooseFillColor(); };
            canvasGroup.Controls.Add(_fillButton);

            _transparentBox = new CheckBox();
            _transparentBox.Text = "透明填充（仅 PNG 有效）";
            _transparentBox.Location = new Point(110, 97);
            _transparentBox.AutoSize = true;
            canvasGroup.Controls.Add(_transparentBox);

            Button fitCurrent = new Button();
            fitCurrent.Text = "按当前图片填尺寸";
            fitCurrent.Location = new Point(10, 130);
            fitCurrent.Size = new Size(140, 28);
            fitCurrent.Click += delegate { FillSizeFromCurrent(); };
            canvasGroup.Controls.Add(fitCurrent);

            Button applyCanvas = new Button();
            applyCanvas.Text = "应用画布";
            applyCanvas.Location = new Point(160, 130);
            applyCanvas.Size = new Size(120, 28);
            applyCanvas.Click += delegate { ApplyCanvas(); };
            canvasGroup.Controls.Add(applyCanvas);

            _sizeLabel = new Label();
            _sizeLabel.Text = "当前尺寸：无";
            _sizeLabel.Location = new Point(10, 166);
            _sizeLabel.AutoSize = true;
            canvasGroup.Controls.Add(_sizeLabel);

            Button reset = new Button();
            reset.Text = "重置";
            reset.Location = new Point(655, 542);
            reset.Size = new Size(110, 32);
            reset.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            reset.Click += delegate { ResetImage(); };
            Controls.Add(reset);

            Button save = new Button();
            save.Text = "保存为 PNG";
            save.Location = new Point(775, 542);
            save.Size = new Size(230, 32);
            save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            save.Click += delegate { SaveResult(); };
            Controls.Add(save);
        }

        private Label AddLabel(Control parent, string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.AutoSize = true;
            parent.Controls.Add(label);
            return label;
        }

        private NumericUpDown MakeNumeric(Control parent, int x, int y, int width, int min, int max, int value)
        {
            NumericUpDown num = new NumericUpDown();
            num.Location = new Point(x, y);
            num.Size = new Size(width, 25);
            num.Minimum = min;
            num.Maximum = max;
            num.Value = value;
            parent.Controls.Add(num);
            return num;
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

            try
            {
                Bitmap loaded = ImageUtil.LoadImage(dialog.FileName);
                if (_currentImage != null)
                {
                    _currentImage.Dispose();
                    _currentImage = null;
                }
                if (_sourceImage != null)
                {
                    _sourceImage.Dispose();
                }
                _sourceImage = loaded;
                _imageBox.Text = dialog.FileName;
                SetCurrent(ImageLayout.Clone(_sourceImage));
                _status.Text = "已加载，可框选裁剪或做校正";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetCurrent(Bitmap bitmap)
        {
            Bitmap old = _currentImage;
            _currentImage = bitmap;
            _canvas.SetImage(_currentImage);
            if (old != null)
            {
                old.Dispose();
            }
            _canvasWidthNum.Value = _currentImage.Width;
            _canvasHeightNum.Value = _currentImage.Height;
            _sizeLabel.Text = "当前尺寸：" + _currentImage.Width + " × " + _currentImage.Height;
            UpdateSelectionLabel();
        }

        private void FillSizeFromCurrent()
        {
            if (_currentImage == null)
            {
                return;
            }
            _canvasWidthNum.Value = _currentImage.Width;
            _canvasHeightNum.Value = _currentImage.Height;
        }

        private void OnRatioChanged()
        {
            int index = _ratioBox.SelectedIndex;
            if (index < 0)
            {
                index = 0;
            }
            _canvas.LockAspect = Ratios[index];
            _canvas.Selection = Rectangle.Empty;
            UpdateSelectionLabel();
            if (index == 0)
            {
                _status.Text = "裁剪比例：自由";
            }
            else
            {
                _status.Text = "裁剪比例：" + _ratioBox.Text;
            }
        }

        private void SelectWholeImage()
        {
            if (_currentImage == null)
            {
                _status.Text = "请先选择图片";
                return;
            }
            _canvas.Selection = new Rectangle(0, 0, _currentImage.Width, _currentImage.Height);
            UpdateSelectionLabel();
        }

        private void UpdateSelectionLabel()
        {
            Rectangle selection = _canvas.Selection;
            if (selection.Width < 1 || selection.Height < 1)
            {
                _selectionLabel.Text = "选区：无";
            }
            else
            {
                _selectionLabel.Text = "选区：" + selection.Width + " × " + selection.Height;
            }
        }

        private void ApplyCrop()
        {
            if (_currentImage == null)
            {
                _status.Text = "请先选择图片";
                return;
            }
            Rectangle selection = _canvas.Selection;
            if (selection.Width < 1 || selection.Height < 1)
            {
                _status.Text = "请先在图片上拖动框选区域";
                return;
            }
            SetCurrent(ImageLayout.Crop(_currentImage, selection));
            _status.Text = "已裁剪";
        }

        private void ApplyRotate()
        {
            if (_currentImage == null)
            {
                _status.Text = "请先选择图片";
                return;
            }
            if (_angleBar.Value == 0)
            {
                _status.Text = "角度为 0，无需校正";
                return;
            }
            Color background = _transparentBox.Checked ? Color.Transparent : _fillColor;
            SetCurrent(ImageLayout.Rotate(_currentImage, _angleBar.Value, background, _autoCropBox.Checked));
            _status.Text = "已旋转校正 " + _angleBar.Value + "°";
        }

        private void ApplyCanvas()
        {
            if (_currentImage == null)
            {
                _status.Text = "请先选择图片";
                return;
            }
            Color fill = _transparentBox.Checked ? Color.Transparent : _fillColor;
            SetCurrent(ImageLayout.ExtendCanvas(
                _currentImage,
                (int)_canvasWidthNum.Value,
                (int)_canvasHeightNum.Value,
                (ImageAnchor)_anchorBox.SelectedIndex,
                fill));
            _status.Text = "已应用画布";
        }

        private void ResetImage()
        {
            if (_sourceImage == null)
            {
                _status.Text = "请先选择图片";
                return;
            }
            SetCurrent(ImageLayout.Clone(_sourceImage));
            _angleBar.Value = 0;
            _status.Text = "已重置为原图";
        }

        private void ChooseFillColor()
        {
            ColorDialog dialog = new ColorDialog();
            dialog.Color = _fillColor;
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _fillColor = dialog.Color;
                _fillButton.BackColor = _fillColor;
            }
        }

        private void SaveResult()
        {
            if (_currentImage == null)
            {
                _status.Text = "请先选择图片";
                return;
            }

            string sourcePath = _imageBox.Text;
            string dir = "";
            string name = "结果.png";
            if (sourcePath.Length > 0)
            {
                dir = Path.GetDirectoryName(sourcePath);
                name = Path.GetFileNameWithoutExtension(sourcePath) + "_裁剪构图.png";
            }

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

            try
            {
                ImageUtil.SavePng(_currentImage, dialog.FileName);
                _status.Text = "已保存：" + dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
