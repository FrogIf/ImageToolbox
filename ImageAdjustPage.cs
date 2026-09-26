using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class ImageAdjustPage : ToolPage
    {
        private const int PreviewSize = 1200;

        private TextBox _imageBox;
        private ImageCanvas _canvas;
        private Label _status;
        private Timer _previewTimer;

        private Bitmap _sourceImage;
        private Bitmap _previewBase;
        private bool _ownPreview;
        private Bitmap _resultImage;
        private bool _loading;

        private HistogramView _histogram;
        private ComboBox _levelChannel;
        private TrackBar _blackBar;
        private TrackBar _whiteBar;
        private TrackBar _gammaBar;
        private Label _blackValue;
        private Label _whiteValue;
        private Label _gammaValue;
        private readonly int[] _levelBlack = { 0, 0, 0 };
        private readonly int[] _levelWhite = { 255, 255, 255 };
        private readonly float[] _levelGamma = { 1f, 1f, 1f };

        private ComboBox _curveChannel;
        private CurveEditor _curveEditor;
        private readonly PointF[][] _curves = new PointF[4][];
        private int _curveIndex;

        private CheckBox _wbPickBox;
        private Label _wbInfo;
        private readonly float[] _wbGain = { 1f, 1f, 1f };
        private TrackBar _hueBar;
        private TrackBar _saturationBar;
        private TrackBar _lightnessBar;

        private CheckBox _localEnable;
        private ComboBox _localType;
        private Button _pickCenterButton;
        private bool _pickingCenter;
        private PointF _localCenter = new PointF(0.5f, 0.5f);
        private TrackBar _radiusXBar;
        private TrackBar _radiusYBar;
        private TrackBar _featherBar;
        private TrackBar _angleBar;
        private TrackBar _localExposureBar;
        private TrackBar _localContrastBar;
        private TrackBar _localSaturationBar;

        private ComboBox _toneMode;
        private NumericUpDown _posterizeNum;
        private Button _duotoneAButton;
        private Button _duotoneBButton;
        private Color _duotoneA = Color.FromArgb(20, 30, 60);
        private Color _duotoneB = Color.FromArgb(245, 230, 200);
        private ComboBox _gradientBox;

        private CheckBox _lutEnable;
        private TextBox _lutPathBox;
        private Label _lutInfo;
        private TrackBar _lutStrengthBar;
        private int _lutSize;
        private float[] _lutData;

        public ImageAdjustPage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1180, 720);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "图像调整"; }
        }

        public override void Shutdown()
        {
            if (_resultImage != null)
            {
                _resultImage.Dispose();
            }
            if (_ownPreview && _previewBase != null)
            {
                _previewBase.Dispose();
            }
            if (_sourceImage != null)
            {
                _sourceImage.Dispose();
            }
        }

        private void BuildUi()
        {
            _loading = true;

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
            _imageBox.Size = new Size(320, 25);
            _imageBox.ReadOnly = true;
            Controls.Add(_imageBox);

            TableLayoutPanel content = new TableLayoutPanel();
            content.Location = new Point(10, 78);
            content.Size = new Size(1160, 586);
            content.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            content.ColumnCount = 2;
            content.RowCount = 1;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400f));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(content);

            _canvas = new ImageCanvas();
            _canvas.Dock = DockStyle.Fill;
            _canvas.Margin = new Padding(0, 0, 8, 0);
            _canvas.ReadOnly = true;
            _canvas.PixelClicked += OnCanvasPixelClicked;
            content.Controls.Add(_canvas, 0, 0);

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            content.Controls.Add(tabs, 1, 0);

            BuildLevelsTab(tabs);
            BuildCurveTab(tabs);
            BuildWhiteBalanceTab(tabs);
            BuildLocalTab(tabs);
            BuildToneTab(tabs);
            BuildLutTab(tabs);

            Button reset = new Button();
            reset.Text = "重置";
            reset.Location = new Point(10, 674);
            reset.Size = new Size(110, 30);
            reset.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            reset.Click += delegate { ResetAll(); };
            Controls.Add(reset);

            Button save = new Button();
            save.Text = "保存为 PNG";
            save.Location = new Point(130, 674);
            save.Size = new Size(200, 30);
            save.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            save.Click += delegate { SaveResult(); };
            Controls.Add(save);

            _status = new Label();
            _status.Text = "请选择图片，在右侧进行色阶 / 曲线 / 白平衡 / HSL / 局部 / 色调 / LUT 调整";
            _status.Location = new Point(340, 678);
            _status.Size = new Size(830, 22);
            _status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_status);

            _previewTimer = new Timer();
            _previewTimer.Interval = 180;
            _previewTimer.Tick += delegate
            {
                _previewTimer.Stop();
                UpdatePreview();
            };

            _loading = false;

            ResetLevels();
            ResetCurves();
            ResetWhiteBalance();
            ResetLocal();
            ResetTone();
            ResetLut();
        }

        private void BuildLevelsTab(TabControl tabs)
        {
            TabPage page = new TabPage("直方图/色阶");
            page.UseVisualStyleBackColor = true;
            tabs.TabPages.Add(page);

            _histogram = new HistogramView();
            _histogram.Location = new Point(8, 8);
            _histogram.Size = new Size(376, 150);
            page.Controls.Add(_histogram);

            AddLabel(page, "通道", 10, 170);
            _levelChannel = new ComboBox();
            _levelChannel.DropDownStyle = ComboBoxStyle.DropDownList;
            _levelChannel.Location = new Point(60, 166);
            _levelChannel.Size = new Size(150, 25);
            _levelChannel.Items.Add("RGB");
            _levelChannel.Items.Add("红");
            _levelChannel.Items.Add("绿");
            _levelChannel.Items.Add("蓝");
            _levelChannel.SelectedIndex = 0;
            _levelChannel.SelectedIndexChanged += delegate { RefreshLevelSliders(); };
            page.Controls.Add(_levelChannel);

            _blackBar = AddSlider(page, "黑场", 200, 0, 255, 0, out _blackValue, OnLevelChanged);
            _whiteBar = AddSlider(page, "白场", 234, 0, 255, 255, out _whiteValue, OnLevelChanged);
            _gammaBar = AddSlider(page, "伽马", 268, 10, 300, 100, out _gammaValue, OnLevelChanged);
            RefreshLevelSliders();

            Button auto = new Button();
            auto.Text = "自动色阶";
            auto.Location = new Point(10, 306);
            auto.Size = new Size(120, 30);
            auto.Click += delegate { AutoLevels(); };
            page.Controls.Add(auto);

            Button reset = new Button();
            reset.Text = "重置";
            reset.Location = new Point(140, 306);
            reset.Size = new Size(90, 30);
            reset.Click += delegate { ResetLevels(); };
            page.Controls.Add(reset);

            AddNotes(page, 344,
                "色阶用黑场 / 白场 / 伽马控制亮度分布：\r\n" +
                "• 黑场：低于该亮度的像素压成纯黑，数值越大暗部越深。\r\n" +
                "• 白场：高于该亮度的像素提成纯白，数值越小亮部越亮。\r\n" +
                "• 伽马：只影响中间调，>1 变亮，<1 变暗。\r\n\r\n" +
                "直方图高峰代表该亮度的像素多。若两端溢出、中间塌陷，\r\n" +
                "可拉黑白场重新分布；自动色阶会按两端各 0.5% 截断自动定黑白场。");
        }

        private void BuildCurveTab(TabControl tabs)
        {
            TabPage page = new TabPage("曲线");
            page.UseVisualStyleBackColor = true;
            tabs.TabPages.Add(page);

            AddLabel(page, "通道", 10, 16);
            _curveChannel = new ComboBox();
            _curveChannel.DropDownStyle = ComboBoxStyle.DropDownList;
            _curveChannel.Location = new Point(60, 12);
            _curveChannel.Size = new Size(150, 25);
            _curveChannel.Items.Add("RGB");
            _curveChannel.Items.Add("红");
            _curveChannel.Items.Add("绿");
            _curveChannel.Items.Add("蓝");
            _curveChannel.SelectedIndex = 0;
            _curveChannel.SelectedIndexChanged += delegate { SwitchCurveChannel(); };
            page.Controls.Add(_curveChannel);

            _curveEditor = new CurveEditor();
            _curveEditor.Location = new Point(8, 48);
            _curveEditor.Size = new Size(376, 210);
            _curveEditor.CurveChanged += delegate { OnCurveChanged(); };
            page.Controls.Add(_curveEditor);

            Label hint = new Label();
            hint.Text = "左键添加 / 拖动控制点，右键删除（首尾两点不可删）";
            hint.Location = new Point(10, 264);
            hint.AutoSize = true;
            page.Controls.Add(hint);

            Button reset = new Button();
            reset.Text = "重置当前通道";
            reset.Location = new Point(10, 288);
            reset.Size = new Size(140, 30);
            reset.Click += delegate { ResetCurrentCurve(); };
            page.Controls.Add(reset);

            AddNotes(page, 326,
                "• 曲线把“原亮度”映射为“新亮度”：横轴为原亮度，\r\n" +
                "  纵轴为调整后亮度，对角线表示不改变。\r\n" +
                "• 左键在空白处添加控制点，按住左键拖动可移动，\r\n" +
                "  右键删除；首尾两点不可删。\r\n" +
                "• 曲线上移 = 提亮，下移 = 压暗。\r\n" +
                "• 常用形状：\r\n" +
                "  – S 形：暗部更暗、亮部更亮，提高对比；\r\n" +
                "  – 反 S 形：降低对比；\r\n" +
                "  – 左下角上提：抬起阴影、去除灰雾。\r\n" +
                "• RGB 为总曲线；切到红 / 绿 / 蓝可分别偏色：\r\n" +
                "  该通道上提偏该色，下拉偏其补色（如蓝上提偏蓝、下拉偏黄）。\r\n" +
                "• 采用单调三次插值，曲线不会回折、不会过冲。");
        }

        private void BuildWhiteBalanceTab(TabControl tabs)
        {
            TabPage page = new TabPage("白平衡/HSL");
            page.UseVisualStyleBackColor = true;
            tabs.TabPages.Add(page);

            GroupBox wb = new GroupBox();
            wb.Text = "白平衡";
            wb.Location = new Point(8, 8);
            wb.Size = new Size(376, 150);
            page.Controls.Add(wb);

            _wbPickBox = new CheckBox();
            _wbPickBox.Text = "取点校正：点击图片上的中性灰区域";
            _wbPickBox.Location = new Point(12, 24);
            _wbPickBox.AutoSize = true;
            wb.Controls.Add(_wbPickBox);

            _wbInfo = new Label();
            _wbInfo.Text = "增益：R 1.00 / G 1.00 / B 1.00";
            _wbInfo.Location = new Point(12, 52);
            _wbInfo.Size = new Size(350, 20);
            wb.Controls.Add(_wbInfo);

            Button auto = new Button();
            auto.Text = "自动白平衡";
            auto.Location = new Point(12, 108);
            auto.Size = new Size(130, 30);
            auto.Click += delegate { AutoWhiteBalance(); };
            wb.Controls.Add(auto);

            Button reset = new Button();
            reset.Text = "重置";
            reset.Location = new Point(152, 108);
            reset.Size = new Size(90, 30);
            reset.Click += delegate { ResetWhiteBalance(); };
            wb.Controls.Add(reset);

            GroupBox hsl = new GroupBox();
            hsl.Text = "色相 / 饱和度 / 明度";
            hsl.Location = new Point(8, 166);
            hsl.Size = new Size(376, 210);
            page.Controls.Add(hsl);

            Label hueValue;
            Label satValue;
            Label lightValue;
            _hueBar = AddSlider(hsl, "色相", 28, -180, 180, 0, out hueValue, OnHslChanged);
            _saturationBar = AddSlider(hsl, "饱和度", 66, -100, 100, 0, out satValue, OnHslChanged);
            _lightnessBar = AddSlider(hsl, "明度", 104, -100, 100, 0, out lightValue, OnHslChanged);

            Button hslReset = new Button();
            hslReset.Text = "重置";
            hslReset.Location = new Point(12, 148);
            hslReset.Size = new Size(90, 30);
            hslReset.Click += delegate { ResetHsl(); };
            hsl.Controls.Add(hslReset);

            AddNotes(page, 384,
                "白平衡：\r\n" +
                "• 取点校正：点击画面中应为白 / 灰的区域，据此把三通道拉平。\r\n" +
                "• 自动白平衡：按“灰度世界”假设，令全图三通道均值相等。\r\n\r\n" +
                "HSL：\r\n" +
                "• 色相：整体旋转颜色（-180~180）。\r\n" +
                "• 饱和度：0 为灰，越小越淡、越大越艳。\r\n" +
                "• 明度：正值提亮、负值压暗。");
        }

        private void BuildLocalTab(TabControl tabs)
        {
            TabPage page = new TabPage("局部调整");
            page.UseVisualStyleBackColor = true;
            tabs.TabPages.Add(page);

            _localEnable = new CheckBox();
            _localEnable.Text = "启用局部调整";
            _localEnable.Location = new Point(10, 12);
            _localEnable.AutoSize = true;
            _localEnable.CheckedChanged += delegate { SchedulePreview(); };
            page.Controls.Add(_localEnable);

            AddLabel(page, "蒙版", 10, 48);
            _localType = new ComboBox();
            _localType.DropDownStyle = ComboBoxStyle.DropDownList;
            _localType.Location = new Point(60, 44);
            _localType.Size = new Size(130, 25);
            _localType.Items.Add("径向");
            _localType.Items.Add("线性");
            _localType.SelectedIndex = 0;
            _localType.SelectedIndexChanged += delegate { SchedulePreview(); };
            page.Controls.Add(_localType);

            _pickCenterButton = new Button();
            _pickCenterButton.Text = "在图上点选中心";
            _pickCenterButton.Location = new Point(206, 42);
            _pickCenterButton.Size = new Size(164, 28);
            _pickCenterButton.Click += delegate { StartPickCenter(); };
            page.Controls.Add(_pickCenterButton);

            Label v;
            _radiusXBar = AddSlider(page, "半径X", 82, 5, 100, 30, out v, OnLocalChanged);
            _radiusYBar = AddSlider(page, "半径Y", 114, 5, 100, 30, out v, OnLocalChanged);
            _featherBar = AddSlider(page, "羽化", 146, 0, 99, 50, out v, OnLocalChanged);
            _angleBar = AddSlider(page, "角度", 178, -90, 90, 0, out v, OnLocalChanged);
            _localExposureBar = AddSlider(page, "曝光", 210, -100, 100, 0, out v, OnLocalChanged);
            _localContrastBar = AddSlider(page, "对比度", 242, -100, 100, 0, out v, OnLocalChanged);
            _localSaturationBar = AddSlider(page, "饱和度", 274, -100, 100, 0, out v, OnLocalChanged);

            Button reset = new Button();
            reset.Text = "重置";
            reset.Location = new Point(10, 310);
            reset.Size = new Size(90, 30);
            reset.Click += delegate { ResetLocal(); };
            page.Controls.Add(reset);

            AddNotes(page, 350,
                "局部调整只在蒙版范围内生效，范围外用渐变过渡：\r\n" +
                "• 径向：以中心为圆心的椭圆，半径 X / Y 定范围，\r\n" +
                "  羽化控制边缘过渡的宽度。\r\n" +
                "• 线性：沿“角度”方向做一半亮、一半暗的渐变。\r\n" +
                "• 点选中心后调整曝光 / 对比度 / 饱和度，\r\n" +
                "  只有蒙版区域会变化。\r\n\r\n" +
                "适合单独提亮人脸、压暗天空、给主体加饱和等。");
        }

        private void BuildToneTab(TabControl tabs)
        {
            TabPage page = new TabPage("色调");
            page.UseVisualStyleBackColor = true;
            tabs.TabPages.Add(page);

            AddLabel(page, "模式", 10, 18);
            _toneMode = new ComboBox();
            _toneMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _toneMode.Location = new Point(70, 14);
            _toneMode.Size = new Size(180, 25);
            _toneMode.Items.Add("无");
            _toneMode.Items.Add("色调分离");
            _toneMode.Items.Add("双色调");
            _toneMode.Items.Add("渐变映射");
            _toneMode.SelectedIndex = 0;
            _toneMode.SelectedIndexChanged += delegate { SchedulePreview(); };
            page.Controls.Add(_toneMode);

            AddLabel(page, "色阶数", 10, 56);
            _posterizeNum = new NumericUpDown();
            _posterizeNum.Location = new Point(80, 52);
            _posterizeNum.Size = new Size(80, 25);
            _posterizeNum.Minimum = 2;
            _posterizeNum.Maximum = 32;
            _posterizeNum.Value = 6;
            _posterizeNum.ValueChanged += delegate { SchedulePreview(); };
            page.Controls.Add(_posterizeNum);

            Label duoLabel = new Label();
            duoLabel.Text = "双色调";
            duoLabel.Location = new Point(10, 94);
            duoLabel.AutoSize = true;
            page.Controls.Add(duoLabel);

            _duotoneAButton = new Button();
            _duotoneAButton.Text = "暗部色";
            _duotoneAButton.Location = new Point(80, 90);
            _duotoneAButton.Size = new Size(90, 28);
            _duotoneAButton.BackColor = _duotoneA;
            _duotoneAButton.Click += delegate { ChooseDuotone(true); };
            page.Controls.Add(_duotoneAButton);

            _duotoneBButton = new Button();
            _duotoneBButton.Text = "亮部色";
            _duotoneBButton.Location = new Point(180, 90);
            _duotoneBButton.Size = new Size(90, 28);
            _duotoneBButton.BackColor = _duotoneB;
            _duotoneBButton.Click += delegate { ChooseDuotone(false); };
            page.Controls.Add(_duotoneBButton);

            AddLabel(page, "渐变", 10, 138);
            _gradientBox = new ComboBox();
            _gradientBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _gradientBox.Location = new Point(80, 134);
            _gradientBox.Size = new Size(200, 25);
            for (int i = 0; i < ImageTuning.Gradients.Length; i++)
            {
                _gradientBox.Items.Add(ImageTuning.Gradients[i].Name);
            }
            _gradientBox.SelectedIndex = 0;
            _gradientBox.SelectedIndexChanged += delegate { SchedulePreview(); };
            page.Controls.Add(_gradientBox);

            Button reset = new Button();
            reset.Text = "重置";
            reset.Location = new Point(10, 176);
            reset.Size = new Size(90, 30);
            reset.Click += delegate { ResetTone(); };
            page.Controls.Add(reset);

            AddNotes(page, 214,
                "• 色调分离：把每个通道压缩成有限档，产生海报 / 波普风的色块。\r\n\r\n" +
                "• 双色调：按亮度在“暗部色”与“亮部色”之间插值，\r\n" +
                "  适合单色艺术照。\r\n\r\n" +
                "• 渐变映射：按亮度映射到多色渐变。\r\n" +
                "  预设：黑白 / 蓝橙 / 青品 / 暖阳 / 冷调 / 紫绿。\r\n\r\n" +
                "提示：双色调与渐变映射会重写颜色，建议先完成曝光与白平衡。");
        }

        private void BuildLutTab(TabControl tabs)
        {
            TabPage page = new TabPage("LUT");
            page.UseVisualStyleBackColor = true;
            tabs.TabPages.Add(page);

            _lutEnable = new CheckBox();
            _lutEnable.Text = "启用 LUT";
            _lutEnable.Location = new Point(10, 12);
            _lutEnable.AutoSize = true;
            _lutEnable.CheckedChanged += delegate { SchedulePreview(); };
            page.Controls.Add(_lutEnable);

            _lutPathBox = new TextBox();
            _lutPathBox.Location = new Point(10, 44);
            _lutPathBox.Size = new Size(270, 25);
            _lutPathBox.ReadOnly = true;
            page.Controls.Add(_lutPathBox);

            Button browse = new Button();
            browse.Text = "浏览...";
            browse.Location = new Point(288, 43);
            browse.Size = new Size(90, 27);
            browse.Click += delegate { BrowseLut(); };
            page.Controls.Add(browse);

            _lutInfo = new Label();
            _lutInfo.Text = "未加载 .cube 文件";
            _lutInfo.Location = new Point(10, 78);
            _lutInfo.Size = new Size(360, 20);
            page.Controls.Add(_lutInfo);

            Label strength;
            _lutStrengthBar = AddSlider(page, "强度", 108, 0, 100, 100, out strength, OnLutChanged);

            Button reset = new Button();
            reset.Text = "重置";
            reset.Location = new Point(10, 150);
            reset.Size = new Size(90, 30);
            reset.Click += delegate { ResetLut(); };
            page.Controls.Add(reset);

            AddNotes(page, 190,
                "LUT 是把某种调色预设以查找表的形式套用到画面。\r\n\r\n" +
                "• 支持 3D .cube 文件（16³ / 32³ / 64³），采用三线性插值。\r\n" +
                "• 加载后自动启用，可用“强度”在 0%~100% 控制叠加程度。\r\n" +
                "• 建议先做好基础曝光 / 白平衡，再用 LUT 定风格。\r\n\r\n" +
                "提示：请从可信来源获取 LUT 文件；.cube 中的 DOMAIN / TITLE 行会被忽略。");
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

        private TrackBar AddSlider(Control parent, string text, int y, int min, int max, int value, out Label valueLabel, EventHandler handler)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(10, y + 6);
            label.AutoSize = true;
            parent.Controls.Add(label);

            TrackBar bar = new TrackBar();
            bar.AutoSize = false;
            bar.Minimum = min;
            bar.Maximum = max;
            bar.TickStyle = TickStyle.None;
            bar.Location = new Point(70, y);
            bar.Size = new Size(240, 30);
            parent.Controls.Add(bar);

            Label valueLabelLocal = new Label();
            valueLabelLocal.Text = value.ToString();
            valueLabelLocal.Location = new Point(315, y + 6);
            valueLabelLocal.Size = new Size(60, 20);
            valueLabelLocal.TextAlign = ContentAlignment.MiddleRight;
            parent.Controls.Add(valueLabelLocal);
            valueLabel = valueLabelLocal;

            bar.Tag = valueLabelLocal;
            bar.ValueChanged += SliderValueChanged;
            if (handler != null)
            {
                bar.ValueChanged += handler;
            }

            bar.Value = value;
            return bar;
        }

        private void SliderValueChanged(object sender, EventArgs e)
        {
            TrackBar bar = sender as TrackBar;
            if (bar == null)
            {
                return;
            }
            Label label = bar.Tag as Label;
            if (label != null)
            {
                label.Text = bar.Value.ToString();
            }
        }

        private void AddNotes(Control parent, int y, string text)
        {
            GroupBox box = new GroupBox();
            box.Text = "说明";
            box.Location = new Point(8, y);
            box.Size = new Size(376, 546 - y);
            parent.Controls.Add(box);

            Panel panel = new Panel();
            panel.Location = new Point(8, 20);
            panel.Size = new Size(box.Width - 16, box.Height - 26);
            panel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            panel.AutoScroll = true;
            box.Controls.Add(panel);

            Label label = new Label();
            label.AutoSize = true;
            label.MaximumSize = new Size(box.Width - 36, 0);
            label.Text = text;
            label.Location = new Point(0, 0);
            label.ForeColor = Color.FromArgb(70, 70, 70);
            panel.Controls.Add(label);
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
                if (_resultImage != null)
                {
                    _resultImage.Dispose();
                    _resultImage = null;
                }
                if (_ownPreview && _previewBase != null)
                {
                    _previewBase.Dispose();
                    _previewBase = null;
                }
                if (_sourceImage != null)
                {
                    _sourceImage.Dispose();
                }
                _sourceImage = loaded;
                _imageBox.Text = dialog.FileName;

                _previewBase = ImageUtil.CreatePreview(_sourceImage, PreviewSize);
                _ownPreview = _previewBase != null;
                if (_previewBase == null)
                {
                    _previewBase = _sourceImage;
                }

                Bitmap previewSource = _previewBase;
                _histogram.SetData(ImageTuning.Histogram(previewSource));
                UpdatePreview();
                _status.Text = "已加载图片，可开始调整";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private TuningState BuildState()
        {
            TuningState state = new TuningState();
            state.LevelBlack = (int[])_levelBlack.Clone();
            state.LevelWhite = (int[])_levelWhite.Clone();
            state.LevelGamma = (float[])_levelGamma.Clone();
            state.CurvePoints = _curves;
            state.WhiteBalanceGain = _wbGain;
            state.Hue = _hueBar.Value;
            state.Saturation = _saturationBar.Value;
            state.Lightness = _lightnessBar.Value;
            state.ToneMode = _toneMode.SelectedIndex;
            state.PosterizeLevels = (int)_posterizeNum.Value;
            state.DuotoneA = _duotoneA;
            state.DuotoneB = _duotoneB;
            state.GradientPreset = _gradientBox.SelectedIndex;
            state.LutEnabled = _lutEnable.Checked;
            state.LutStrength = _lutStrengthBar.Value / 100f;
            state.LutSize = _lutSize;
            state.LutData = _lutData;
            state.LocalEnabled = _localEnable.Checked;
            state.LocalLinear = _localType.SelectedIndex == 1;
            state.LocalCenter = _localCenter;
            state.LocalRadiusX = _radiusXBar.Value / 100f;
            state.LocalRadiusY = _radiusYBar.Value / 100f;
            state.LocalFeather = _featherBar.Value / 100f;
            state.LocalAngle = _angleBar.Value;
            state.LocalExposure = _localExposureBar.Value;
            state.LocalContrast = _localContrastBar.Value;
            state.LocalSaturation = _localSaturationBar.Value;
            return state;
        }

        private void SchedulePreview()
        {
            if (_loading || _sourceImage == null)
            {
                return;
            }
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void UpdatePreview()
        {
            if (_sourceImage == null || _previewBase == null)
            {
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap work = ImageTuning.Apply(_previewBase, BuildState());
                Bitmap old = _resultImage;
                _resultImage = work;
                _canvas.SetImage(_resultImage);
                if (old != null)
                {
                    old.Dispose();
                }
                _status.Text = "预览 " + _resultImage.Width + " × " + _resultImage.Height;
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

        private void OnCanvasPixelClicked(Point point)
        {
            if (_sourceImage == null || _previewBase == null)
            {
                return;
            }

            if (_wbPickBox.Checked)
            {
                int x = Math.Max(0, Math.Min(_previewBase.Width - 1, point.X));
                int y = Math.Max(0, Math.Min(_previewBase.Height - 1, point.Y));
                Color sample = _previewBase.GetPixel(x, y);
                float[] gains = ImageTuning.WhiteBalanceFromColor(sample);
                _wbGain[0] = gains[0];
                _wbGain[1] = gains[1];
                _wbGain[2] = gains[2];
                _wbPickBox.Checked = false;
                UpdateWhiteBalanceInfo();
                UpdatePreview();
                return;
            }

            if (_pickingCenter)
            {
                float nx = _resultImage == null || _resultImage.Width <= 1 ? 0.5f : (float)point.X / (_resultImage.Width - 1);
                float ny = _resultImage == null || _resultImage.Height <= 1 ? 0.5f : (float)point.Y / (_resultImage.Height - 1);
                _localCenter = new PointF(Math.Max(0f, Math.Min(1f, nx)), Math.Max(0f, Math.Min(1f, ny)));
                _pickingCenter = false;
                _pickCenterButton.Text = "在图上点选中心";
                UpdatePreview();
            }
        }

        private void StartPickCenter()
        {
            if (_sourceImage == null)
            {
                _status.Text = "请先选择图片";
                return;
            }
            _pickingCenter = true;
            _pickCenterButton.Text = "请点击图片...";
            _status.Text = "请在预览图上点击设置局部中心";
        }

        private int CurrentLevelChannel()
        {
            int index = _levelChannel.SelectedIndex;
            if (index < 0)
            {
                index = 0;
            }
            return index;
        }

        private void OnLevelChanged(object sender, EventArgs e)
        {
            if (_blackBar == null || _whiteBar == null || _gammaBar == null)
            {
                return;
            }
            int channel = CurrentLevelChannel();
            if (channel == 0)
            {
                for (int c = 0; c < 3; c++)
                {
                    _levelBlack[c] = _blackBar.Value;
                    _levelWhite[c] = _whiteBar.Value;
                    _levelGamma[c] = _gammaBar.Value / 100f;
                }
            }
            else
            {
                int c = channel - 1;
                _levelBlack[c] = _blackBar.Value;
                _levelWhite[c] = _whiteBar.Value;
                _levelGamma[c] = _gammaBar.Value / 100f;
            }
            _blackValue.Text = _blackBar.Value.ToString();
            _whiteValue.Text = _whiteBar.Value.ToString();
            _gammaValue.Text = (_gammaBar.Value / 100f).ToString("0.00");
            SchedulePreview();
        }

        private void RefreshLevelSliders()
        {
            if (_blackBar == null || _whiteBar == null || _gammaBar == null)
            {
                return;
            }
            int channel = CurrentLevelChannel();
            int c = channel == 0 ? 0 : channel - 1;
            _blackBar.Value = Math.Max(_blackBar.Minimum, Math.Min(_blackBar.Maximum, _levelBlack[c]));
            _whiteBar.Value = Math.Max(_whiteBar.Minimum, Math.Min(_whiteBar.Maximum, _levelWhite[c]));
            _gammaBar.Value = Math.Max(_gammaBar.Minimum, Math.Min(_gammaBar.Maximum, (int)Math.Round(_levelGamma[c] * 100f)));
            _blackValue.Text = _blackBar.Value.ToString();
            _whiteValue.Text = _whiteBar.Value.ToString();
            _gammaValue.Text = (_gammaBar.Value / 100f).ToString("0.00");
        }

        private void AutoLevels()
        {
            if (_previewBase == null)
            {
                _status.Text = "请先选择图片";
                return;
            }
            int[] black;
            int[] white;
            ImageTuning.AutoLevels(_previewBase, out black, out white);
            for (int c = 0; c < 3; c++)
            {
                _levelBlack[c] = black[c];
                _levelWhite[c] = white[c];
            }
            RefreshLevelSliders();
            UpdatePreview();
            _status.Text = "已自动色阶";
        }

        private void ResetLevels()
        {
            for (int c = 0; c < 3; c++)
            {
                _levelBlack[c] = 0;
                _levelWhite[c] = 255;
                _levelGamma[c] = 1f;
            }
            RefreshLevelSliders();
            SchedulePreview();
        }

        private void SwitchCurveChannel()
        {
            if (_curveEditor == null)
            {
                return;
            }
            _curveIndex = _curveChannel.SelectedIndex < 0 ? 0 : _curveChannel.SelectedIndex;
            _curveEditor.LineColor = CurveColor(_curveIndex);
            PointF[] points = _curves[_curveIndex];
            _curveEditor.Points = points;
        }

        private static Color CurveColor(int index)
        {
            switch (index)
            {
                case 1: return Color.FromArgb(240, 80, 80);
                case 2: return Color.FromArgb(80, 220, 100);
                case 3: return Color.FromArgb(90, 140, 255);
                default: return Color.White;
            }
        }

        private void OnCurveChanged()
        {
            _curves[_curveIndex] = _curveEditor.Points;
            SchedulePreview();
        }

        private void ResetCurrentCurve()
        {
            _curves[_curveIndex] = null;
            _curveEditor.Points = null;
            SchedulePreview();
        }

        private void ResetCurves()
        {
            for (int i = 0; i < 4; i++)
            {
                _curves[i] = null;
            }
            if (_curveEditor != null)
            {
                _curveEditor.Points = null;
            }
        }

        private void AutoWhiteBalance()
        {
            if (_previewBase == null)
            {
                _status.Text = "请先选择图片";
                return;
            }
            float[] gains = ImageTuning.AutoWhiteBalance(_previewBase);
            _wbGain[0] = gains[0];
            _wbGain[1] = gains[1];
            _wbGain[2] = gains[2];
            UpdateWhiteBalanceInfo();
            UpdatePreview();
            _status.Text = "已自动白平衡";
        }

        private void UpdateWhiteBalanceInfo()
        {
            _wbInfo.Text = "增益：R " + _wbGain[0].ToString("0.00") + " / G " + _wbGain[1].ToString("0.00") + " / B " + _wbGain[2].ToString("0.00");
        }

        private void ResetWhiteBalance()
        {
            _wbGain[0] = 1f;
            _wbGain[1] = 1f;
            _wbGain[2] = 1f;
            if (_wbInfo != null)
            {
                UpdateWhiteBalanceInfo();
            }
            SchedulePreview();
        }

        private void OnHslChanged(object sender, EventArgs e)
        {
            SchedulePreview();
        }

        private void ResetHsl()
        {
            _hueBar.Value = 0;
            _saturationBar.Value = 0;
            _lightnessBar.Value = 0;
            SchedulePreview();
        }

        private void OnLocalChanged(object sender, EventArgs e)
        {
            SchedulePreview();
        }

        private void ResetLocal()
        {
            _localEnable.Checked = false;
            _localCenter = new PointF(0.5f, 0.5f);
            _localType.SelectedIndex = 0;
            _radiusXBar.Value = 30;
            _radiusYBar.Value = 30;
            _featherBar.Value = 50;
            _angleBar.Value = 0;
            _localExposureBar.Value = 0;
            _localContrastBar.Value = 0;
            _localSaturationBar.Value = 0;
            SchedulePreview();
        }

        private void ChooseDuotone(bool first)
        {
            ColorDialog dialog = new ColorDialog();
            dialog.Color = first ? _duotoneA : _duotoneB;
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                if (first)
                {
                    _duotoneA = dialog.Color;
                    _duotoneAButton.BackColor = _duotoneA;
                }
                else
                {
                    _duotoneB = dialog.Color;
                    _duotoneBButton.BackColor = _duotoneB;
                }
                SchedulePreview();
            }
        }

        private void ResetTone()
        {
            _toneMode.SelectedIndex = 0;
            _posterizeNum.Value = 6;
            _duotoneA = Color.FromArgb(20, 30, 60);
            _duotoneB = Color.FromArgb(245, 230, 200);
            _duotoneAButton.BackColor = _duotoneA;
            _duotoneBButton.BackColor = _duotoneB;
            _gradientBox.SelectedIndex = 0;
            SchedulePreview();
        }

        private void BrowseLut()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择 .cube LUT 文件";
            dialog.Filter = "Cube LUT|*.cube|所有文件|*.*";
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            int size;
            float[] data;
            if (ImageTuning.TryLoadCube(dialog.FileName, out size, out data))
            {
                _lutSize = size;
                _lutData = data;
                _lutPathBox.Text = dialog.FileName;
                _lutInfo.Text = "已加载：" + Path.GetFileName(dialog.FileName) + "（" + size + "³）";
                _lutEnable.Checked = true;
                UpdatePreview();
                _status.Text = "已加载 LUT";
            }
            else
            {
                MessageBox.Show(this, "无法解析该 .cube 文件", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnLutChanged(object sender, EventArgs e)
        {
            SchedulePreview();
        }

        private void ResetLut()
        {
            _lutEnable.Checked = false;
            _lutStrengthBar.Value = 100;
            SchedulePreview();
        }

        private void ResetAll()
        {
            ResetLevels();
            ResetCurves();
            ResetWhiteBalance();
            ResetHsl();
            ResetLocal();
            ResetTone();
            ResetLut();
            UpdatePreview();
        }

        private void SaveResult()
        {
            if (_sourceImage == null)
            {
                _status.Text = "请先选择图片";
                return;
            }

            string dir = Path.GetDirectoryName(_imageBox.Text);
            string name = Path.GetFileNameWithoutExtension(_imageBox.Text) + "_图像调整.png";

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
                Bitmap result = ImageTuning.Apply(_sourceImage, BuildState());
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
