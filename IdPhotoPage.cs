using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class IdPhotoPage : ToolPage
    {
        private static readonly string[] SizeNames = { "一寸 25×35mm", "小一寸 22×32mm", "大一寸 33×48mm", "二寸 35×49mm", "小二寸 35×45mm", "三寸 55×84mm", "护照 33×48mm", "美国签证 51×51mm" };
        private static readonly double[] SizeWidthsMm = { 25, 22, 33, 35, 35, 55, 33, 51 };
        private static readonly double[] SizeHeightsMm = { 35, 32, 48, 49, 45, 84, 48, 51 };

        private static readonly string[] PaperNames = { "6 寸 (152×102mm)", "5 寸 (127×89mm)", "A4 (297×210mm)" };
        private static readonly double[] PaperWidthsMm = { 152, 127, 297 };
        private static readonly double[] PaperHeightsMm = { 102, 89, 210 };

        private TextBox _imageBox;
        private Bitmap _sourceImage;
        private Bitmap _singlePhoto;
        private Bitmap _sheetImage;

        private ComboBox _sizeBox;
        private NumericUpDown _dpiNum;
        private ComboBox _modeBox;
        private Button _backgroundButton;
        private Color _background = Color.White;
        private Label _singleInfo;

        private ComboBox _paperBox;
        private NumericUpDown _columnsNum;
        private NumericUpDown _countNum;
        private NumericUpDown _spacingNum;
        private NumericUpDown _marginNum;
        private CheckBox _cutLinesBox;
        private Label _sheetInfo;

        private ImageCanvas _singleCanvas;
        private ImageCanvas _sheetCanvas;
        private Label _status;

        public IdPhotoPage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1100, 700);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "证件照"; }
        }

        public override void Shutdown()
        {
            if (_singlePhoto != null)
            {
                _singlePhoto.Dispose();
            }
            if (_sheetImage != null)
            {
                _sheetImage.Dispose();
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
            browse.Location = new Point(240, 8);
            browse.Size = new Size(90, 28);
            browse.Click += delegate { BrowseImage(); };
            Controls.Add(browse);

            _imageBox = new TextBox();
            _imageBox.Location = new Point(10, 42);
            _imageBox.Size = new Size(320, 25);
            _imageBox.ReadOnly = true;
            Controls.Add(_imageBox);

            BuildSingleGroup();
            BuildSheetGroup();

            Button saveSingle = new Button();
            saveSingle.Text = "保存单张";
            saveSingle.Location = new Point(10, 550);
            saveSingle.Size = new Size(150, 32);
            saveSingle.Click += delegate { SaveSingle(); };
            Controls.Add(saveSingle);

            Button saveSheet = new Button();
            saveSheet.Text = "保存排版";
            saveSheet.Location = new Point(170, 550);
            saveSheet.Size = new Size(150, 32);
            saveSheet.Click += delegate { SaveSheet(); };
            Controls.Add(saveSheet);

            Label singleLabel = new Label();
            singleLabel.Text = "单张证件照";
            singleLabel.Location = new Point(350, 12);
            singleLabel.AutoSize = true;
            Controls.Add(singleLabel);

            _singleCanvas = new ImageCanvas();
            _singleCanvas.Location = new Point(350, 38);
            _singleCanvas.Size = new Size(360, 600);
            _singleCanvas.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            _singleCanvas.ReadOnly = true;
            Controls.Add(_singleCanvas);

            Label sheetLabel = new Label();
            sheetLabel.Text = "排版预览";
            sheetLabel.Location = new Point(720, 12);
            sheetLabel.AutoSize = true;
            Controls.Add(sheetLabel);

            _sheetCanvas = new ImageCanvas();
            _sheetCanvas.Location = new Point(720, 38);
            _sheetCanvas.Size = new Size(360, 600);
            _sheetCanvas.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _sheetCanvas.ReadOnly = true;
            Controls.Add(_sheetCanvas);

            _status = new Label();
            _status.Text = "请选择图片，设置尺寸后生成证件照与排版";
            _status.Location = new Point(10, 640);
            _status.Size = new Size(1070, 42);
            _status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_status);
        }

        private void BuildSingleGroup()
        {
            GroupBox group = new GroupBox();
            group.Text = "证件照尺寸";
            group.Location = new Point(10, 80);
            group.Size = new Size(320, 202);
            Controls.Add(group);

            AddLabel(group, "尺寸", 10, 30);
            _sizeBox = new ComboBox();
            _sizeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _sizeBox.Location = new Point(80, 26);
            _sizeBox.Size = new Size(230, 25);
            for (int i = 0; i < SizeNames.Length; i++)
            {
                _sizeBox.Items.Add(SizeNames[i]);
            }
            _sizeBox.SelectedIndex = 0;
            group.Controls.Add(_sizeBox);

            AddLabel(group, "分辨率", 10, 62);
            _dpiNum = MakeNumeric(group, 80, 58, 80, 72, 1200, 300);

            AddLabel(group, "模式", 10, 94);
            _modeBox = new ComboBox();
            _modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _modeBox.Location = new Point(80, 90);
            _modeBox.Size = new Size(230, 25);
            _modeBox.Items.Add("裁剪填满");
            _modeBox.Items.Add("完整留白");
            _modeBox.SelectedIndex = 0;
            group.Controls.Add(_modeBox);

            _backgroundButton = new Button();
            _backgroundButton.Text = "背景色";
            _backgroundButton.Location = new Point(10, 124);
            _backgroundButton.Size = new Size(90, 26);
            _backgroundButton.BackColor = _background;
            _backgroundButton.Click += delegate { ChooseBackground(); };
            group.Controls.Add(_backgroundButton);

            Button build = new Button();
            build.Text = "生成单张";
            build.Location = new Point(110, 122);
            build.Size = new Size(200, 30);
            build.Click += delegate { BuildSingle(); };
            group.Controls.Add(build);

            _singleInfo = new Label();
            _singleInfo.Text = "尚未生成";
            _singleInfo.Location = new Point(10, 162);
            _singleInfo.Size = new Size(300, 32);
            group.Controls.Add(_singleInfo);
        }

        private void BuildSheetGroup()
        {
            GroupBox group = new GroupBox();
            group.Text = "排版到相纸";
            group.Location = new Point(10, 290);
            group.Size = new Size(320, 248);
            Controls.Add(group);

            AddLabel(group, "相纸", 10, 30);
            _paperBox = new ComboBox();
            _paperBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _paperBox.Location = new Point(80, 26);
            _paperBox.Size = new Size(230, 25);
            for (int i = 0; i < PaperNames.Length; i++)
            {
                _paperBox.Items.Add(PaperNames[i]);
            }
            _paperBox.SelectedIndex = 0;
            group.Controls.Add(_paperBox);

            AddLabel(group, "每行", 10, 62);
            _columnsNum = MakeNumeric(group, 80, 58, 60, 1, 20, 2);
            AddLabel(group, "张数", 170, 62);
            _countNum = MakeNumeric(group, 240, 58, 70, 0, 200, 0);

            AddLabel(group, "间距", 10, 94);
            _spacingNum = MakeNumeric(group, 80, 90, 60, 0, 200, 6);
            AddLabel(group, "边距", 170, 94);
            _marginNum = MakeNumeric(group, 240, 90, 70, 0, 200, 8);

            _cutLinesBox = new CheckBox();
            _cutLinesBox.Text = "绘制裁剪线";
            _cutLinesBox.Location = new Point(10, 124);
            _cutLinesBox.AutoSize = true;
            _cutLinesBox.Checked = true;
            group.Controls.Add(_cutLinesBox);

            Button build = new Button();
            build.Text = "生成排版";
            build.Location = new Point(10, 154);
            build.Size = new Size(300, 32);
            build.Click += delegate { BuildSheet(); };
            group.Controls.Add(build);

            Label hint = new Label();
            hint.Text = "“张数”填 0 表示自动铺满相纸";
            hint.Location = new Point(10, 194);
            hint.AutoSize = true;
            group.Controls.Add(hint);

            _sheetInfo = new Label();
            _sheetInfo.Text = "尚未生成";
            _sheetInfo.Location = new Point(10, 216);
            _sheetInfo.Size = new Size(300, 26);
            group.Controls.Add(_sheetInfo);
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
            dialog.Title = "选择照片";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                Bitmap loaded = ImageUtil.LoadImage(dialog.FileName);
                if (_sourceImage != null)
                {
                    _sourceImage.Dispose();
                }
                _sourceImage = loaded;
                _imageBox.Text = dialog.FileName;
                _status.Text = "已加载照片，请设置尺寸后点“生成单张”";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private int MmToPx(double mm, int dpi)
        {
            return Math.Max(1, (int)Math.Round(mm / 25.4 * dpi));
        }

        private void ChooseBackground()
        {
            ColorDialog dialog = new ColorDialog();
            dialog.Color = _background;
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _background = dialog.Color;
                _backgroundButton.BackColor = _background;
            }
        }

        private int CurrentPhotoWidth()
        {
            int index = Math.Max(0, _sizeBox.SelectedIndex);
            return MmToPx(SizeWidthsMm[index], (int)_dpiNum.Value);
        }

        private int CurrentPhotoHeight()
        {
            int index = Math.Max(0, _sizeBox.SelectedIndex);
            return MmToPx(SizeHeightsMm[index], (int)_dpiNum.Value);
        }

        private void BuildSingle()
        {
            if (_sourceImage == null)
            {
                MessageBox.Show(this, "请先选择照片", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int width = CurrentPhotoWidth();
            int height = CurrentPhotoHeight();
            bool fill = _modeBox.SelectedIndex == 0;

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap photo = ImageLayout.BuildIdPhoto(_sourceImage, width, height, fill, _background);
                Bitmap old = _singlePhoto;
                _singlePhoto = photo;
                _singleCanvas.SetImage(_singlePhoto);
                if (old != null)
                {
                    old.Dispose();
                }
                _singleInfo.Text = width + " × " + height + " 像素（" + (int)_dpiNum.Value + " DPI）";
                _status.Text = "已生成单张证件照";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "生成失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private void BuildSheet()
        {
            if (_singlePhoto == null)
            {
                if (_sourceImage == null)
                {
                    MessageBox.Show(this, "请先选择照片", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                BuildSingle();
                if (_singlePhoto == null)
                {
                    return;
                }
            }

            int dpi = (int)_dpiNum.Value;
            int paperIndex = Math.Max(0, _paperBox.SelectedIndex);
            int paperWidth = MmToPx(PaperWidthsMm[paperIndex], dpi);
            int paperHeight = MmToPx(PaperHeightsMm[paperIndex], dpi);

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap sheet = ImageLayout.BuildSheet(
                    _singlePhoto,
                    paperWidth,
                    paperHeight,
                    (int)_columnsNum.Value,
                    (int)_countNum.Value,
                    (int)_spacingNum.Value,
                    (int)_marginNum.Value,
                    _background,
                    _cutLinesBox.Checked);
                Bitmap old = _sheetImage;
                _sheetImage = sheet;
                _sheetCanvas.SetImage(_sheetImage);
                if (old != null)
                {
                    old.Dispose();
                }
                _sheetInfo.Text = "相纸 " + paperWidth + " × " + paperHeight + " 像素";
                _status.Text = "已生成排版";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "排版失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private void SaveSingle()
        {
            if (_singlePhoto == null)
            {
                _status.Text = "请先生成单张证件照";
                return;
            }
            SaveBitmap(_singlePhoto, "证件照.png");
        }

        private void SaveSheet()
        {
            if (_sheetImage == null)
            {
                _status.Text = "请先生成排版";
                return;
            }
            SaveBitmap(_sheetImage, "证件照排版.png");
        }

        private void SaveBitmap(Bitmap bitmap, string defaultName)
        {
            string dir = "";
            if (_imageBox.Text.Length > 0)
            {
                dir = Path.GetDirectoryName(_imageBox.Text);
            }

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "保存";
            dialog.Filter = "PNG 图片|*.png";
            dialog.FileName = defaultName;
            if (!string.IsNullOrEmpty(dir))
            {
                dialog.InitialDirectory = dir;
            }
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                ImageUtil.SavePng(bitmap, dialog.FileName);
                _status.Text = "已保存：" + dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
