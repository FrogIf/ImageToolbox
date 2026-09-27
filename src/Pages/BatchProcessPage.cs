using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class BatchProcessPage : ToolPage
    {
        private const int ThumbSize = 88;
        private const string PlaceholderKey = "_placeholder";

        private ListView _listView;
        private ImageList _imageList;
        private Panel _optionsPanel;
        private GroupBox _outGroup;
        private TextBox _outBox;
        private Button _outBrowse;
        private Button _useSource;
        private Button _addFiles;
        private Button[] _topButtons;
        private CheckBox _overwriteBox;
        private CheckBox _recursiveBox;
        private ProgressBar _progress;
        private Label _status;
        private Button _startButton;
        private bool _laying;
        private BackgroundWorker _worker;
        private BackgroundWorker _thumbWorker;

        private ComboBox _formatBox;
        private TrackBar _qualityBar;
        private Label _qualityValue;
        private Button _matteButton;
        private Color _matteColor = Color.White;

        private ComboBox _resizeModeBox;
        private NumericUpDown _longEdgeNum;
        private NumericUpDown _percentNum;
        private NumericUpDown _widthNum;
        private NumericUpDown _heightNum;
        private CheckBox _keepAspectBox;
        private CheckBox _allowUpscaleBox;

        private CheckBox _autoOrientBox;
        private ComboBox _rotateBox;
        private CheckBox _flipHBox;
        private CheckBox _flipVBox;

        private RadioButton _keepNameRadio;
        private RadioButton _customNameRadio;
        private TextBox _patternBox;
        private TextBox _findBox;
        private TextBox _replaceBox;
        private NumericUpDown _startIndexNum;
        private NumericUpDown _indexDigitsNum;

        private CheckBox _wmEnableBox;
        private RadioButton _wmTextRadio;
        private RadioButton _wmImageRadio;
        private TextBox _wmTextBox;
        private Button _wmColorButton;
        private Color _wmColor = Color.White;
        private NumericUpDown _wmFontSizeNum;
        private NumericUpDown _wmAngleNum;
        private TrackBar _wmOpacityBar;
        private Label _wmOpacityValue;
        private ComboBox _wmPosBox;
        private NumericUpDown _wmMarginNum;
        private CheckBox _wmTileBox;
        private TextBox _wmImageBox;
        private Button _wmImageBrowse;
        private NumericUpDown _wmScaleNum;

        private readonly Dictionary<string, ListViewItem> _itemByPath = new Dictionary<string, ListViewItem>();
        private readonly List<string> _thumbQueue = new List<string>();
        private readonly HashSet<string> _thumbQueued = new HashSet<string>();
        private readonly HashSet<string> _watchedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _closing;
        private bool _suppressStatus;

        public BatchProcessPage()
        {
            AutoScaleMode = AutoScaleMode.Inherit;
            Font = new Font("Microsoft YaHei UI", 9F);
            AllowDrop = true;

            // 本页大量使用 Anchor 绝对布局：必须在添加子控件前把客户区设成设计尺寸，
            // 否则 Anchor 会按默认的 150x150 记录边距，窗口一大控件就被拉伸/推到画外
            // （如文件列表撑成 1800px、右侧选项面板跑到不可见处）。
            ClientSize = new Size(990, 700);

            BuildUi();

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
        }

        public override string ToolName
        {
            get { return "批量处理"; }
        }

        public override void Shutdown()
        {
            _closing = true;
            if (_thumbWorker.IsBusy)
            {
                _thumbWorker.CancelAsync();
            }
            if (_worker.IsBusy)
            {
                _worker.CancelAsync();
            }
        }

        private void BuildUi()
        {
            _addFiles = MakeButton("添加文件", 10, 10, 90);
            _addFiles.Click += delegate { AddFilesDialog(); };
            Controls.Add(_addFiles);

            Button addFolder = MakeButton("添加文件夹", 105, 10, 100);
            addFolder.Click += delegate { AddFolderDialog(); };
            Controls.Add(addFolder);

            Button remove = MakeButton("移除选中", 210, 10, 90);
            remove.Click += delegate { RemoveSelected(); };
            Controls.Add(remove);

            Button clear = MakeButton("清空列表", 305, 10, 90);
            clear.Click += delegate { ClearFiles(); };
            Controls.Add(clear);

            Button checkAll = MakeButton("全选", 400, 10, 70);
            checkAll.Click += delegate { SetAllChecked(true); };
            Controls.Add(checkAll);

            Button uncheckAll = MakeButton("全不选", 475, 10, 70);
            uncheckAll.Click += delegate { SetAllChecked(false); };
            Controls.Add(uncheckAll);

            Button refresh = MakeButton("刷新", 555, 10, 80);
            refresh.Click += delegate { RefreshFromDirs(); };
            Controls.Add(refresh);

            _topButtons = new Button[] { _addFiles, addFolder, remove, clear, checkAll, uncheckAll, refresh };

            _imageList = new ImageList();
            _imageList.ImageSize = new Size(ThumbSize, ThumbSize);
            _imageList.ColorDepth = ColorDepth.Depth32Bit;
            _imageList.Images.Add(PlaceholderKey, CreatePlaceholder());

            _listView = new ListView();
            _listView.Location = new Point(10, 48);
            _listView.Size = new Size(630, 464);
            _listView.View = View.LargeIcon;
            _listView.LargeImageList = _imageList;
            _listView.MultiSelect = true;
            _listView.CheckBoxes = true;
            _listView.ShowItemToolTips = true;
            _listView.HideSelection = false;
            _listView.LabelWrap = true;
            _listView.BorderStyle = BorderStyle.FixedSingle;
            _listView.ItemChecked += delegate { UpdateStatus(); };
            Controls.Add(_listView);

            BuildOptionsPanel();

            _outGroup = new GroupBox();
            _outGroup.Text = "输出目录（留空则保存到源文件所在目录）";
            _outGroup.Location = new Point(10, 520);
            _outGroup.Size = new Size(970, 44);
            Controls.Add(_outGroup);

            _outBox = new TextBox();
            _outBox.Location = new Point(12, 16);
            _outBox.Size = new Size(756, 20);
            _outGroup.Controls.Add(_outBox);

            _outBrowse = new Button();
            _outBrowse.Text = "浏览...";
            _outBrowse.Location = new Point(774, 16);
            _outBrowse.Size = new Size(80, 20);
            _outBrowse.Click += delegate { ChooseOutDir(); };
            _outGroup.Controls.Add(_outBrowse);

            _useSource = new Button();
            _useSource.Text = "使用源目录";
            _useSource.Location = new Point(860, 16);
            _useSource.Size = new Size(96, 20);
            _useSource.Click += delegate { _outBox.Text = ""; };
            _outGroup.Controls.Add(_useSource);

            _overwriteBox = new CheckBox();
            _overwriteBox.Text = "覆盖同名文件";
            _overwriteBox.Location = new Point(10, 584);
            _overwriteBox.AutoSize = true;
            Controls.Add(_overwriteBox);

            _recursiveBox = new CheckBox();
            _recursiveBox.Text = "添加文件夹时递归子目录";
            _recursiveBox.Location = new Point(150, 584);
            _recursiveBox.AutoSize = true;
            _recursiveBox.Checked = true;
            Controls.Add(_recursiveBox);

            _progress = new ProgressBar();
            _progress.Location = new Point(10, 612);
            _progress.Size = new Size(970, 20);
            Controls.Add(_progress);

            _status = new Label();
            _status.Text = "就绪";
            _status.Location = new Point(10, 640);
            _status.Size = new Size(820, 22);
            Controls.Add(_status);

            _startButton = new Button();
            _startButton.Text = "开始处理";
            _startButton.Location = new Point(850, 642);
            _startButton.Size = new Size(130, 24);
            _startButton.Click += delegate { StartProcess(); };
            Controls.Add(_startButton);

            _worker = new BackgroundWorker();
            _worker.WorkerReportsProgress = true;
            _worker.WorkerSupportsCancellation = true;
            _worker.DoWork += WorkerDoWork;
            _worker.ProgressChanged += WorkerProgress;
            _worker.RunWorkerCompleted += WorkerCompleted;

            _thumbWorker = new BackgroundWorker();
            _thumbWorker.WorkerReportsProgress = true;
            _thumbWorker.WorkerSupportsCancellation = true;
            _thumbWorker.DoWork += ThumbDoWork;
            _thumbWorker.ProgressChanged += ThumbProgress;
            _thumbWorker.RunWorkerCompleted += ThumbCompleted;
        }

        // 本页以前用 Anchor 绝对布局，但 DpiScaler 会按 DPI 放大每个控件的坐标/尺寸，
        // 与 Anchor 的边距叠加后在高 DPI 下会把右侧面板/底部控件推到画外。改成每次布局
        // 都按当前客户区重新摆放（比例取顶部按钮的缩放后高度），与 DPI 无关且稳定。
        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            RelayoutBatch();
        }

        private void RelayoutBatch()
        {
            if (_laying || _addFiles == null || _topButtons == null || _listView == null || _optionsPanel == null ||
                _outGroup == null || _outBox == null || _outBrowse == null || _useSource == null ||
                _progress == null || _status == null || _startButton == null) { return; }
            _laying = true;
            try
            {
                float s = _addFiles.Height / 22f;
                if (s <= 0f) { s = 1f; }

                int w = ClientSize.Width;
                int h = ClientSize.Height;
                int pad = S(10, s);
                int gap = S(8, s);

                int startH = _startButton.Height > 0 ? _startButton.Height : S(24, s);
                int statusH = S(22, s);
                int progressH = S(20, s);
                int cbH = S(21, s);
                int outH = S(44, s);

                int startBottom = h - S(34, s);
                int startTop = startBottom - startH;
                int progressTop = startTop - gap - progressH;
                int cbTop = progressTop - S(10, s) - cbH;
                int outTop = cbTop - S(18, s) - outH;

                // 顶部按钮：从左到右排，放不下就换行（高 DPI/窄窗口下也不会溢出）。
                int bx = pad;
                int by = S(10, s);
                int rowH = 0;
                for (int i = 0; i < _topButtons.Length; i++)
                {
                    Button b = _topButtons[i];
                    if (bx > pad && bx + b.Width > w - pad)
                    {
                        bx = pad;
                        by += rowH + S(6, s);
                        rowH = 0;
                    }
                    if (b.Height > rowH) { rowH = b.Height; }
                    b.Location = new Point(bx, by);
                    bx += b.Width + S(5, s);
                }

                int contentTop = by + rowH + S(8, s);
                int contentBottom = outTop - gap;
                int contentH = contentBottom - contentTop;
                if (contentH < S(60, s)) { contentH = S(60, s); }

                int panelW = S(280, s);
                int panelX = w - pad - panelW;
                _optionsPanel.SetBounds(panelX, contentTop, panelW, contentH);
                int listW = panelX - gap - pad;
                if (listW < S(120, s)) { listW = S(120, s); }
                _listView.SetBounds(pad, contentTop, listW, contentH);

                _outGroup.SetBounds(pad, outTop, w - 2 * pad, outH);
                int ogW = _outGroup.ClientSize.Width > 0 ? _outGroup.ClientSize.Width : w - 2 * pad;
                int innerY = S(16, s);
                int h20 = S(20, s);
                int useW = S(96, s);
                _useSource.SetBounds(ogW - S(12, s) - useW, innerY, useW, h20);
                _outBrowse.SetBounds(_useSource.Left - gap - S(80, s), innerY, S(80, s), h20);
                int boxW = _outBrowse.Left - gap - S(12, s);
                if (boxW < S(60, s)) { boxW = S(60, s); }
                _outBox.SetBounds(S(12, s), innerY, boxW, h20);

                _overwriteBox.Location = new Point(pad, cbTop);
                _recursiveBox.Location = new Point(pad + S(140, s), cbTop);
                _progress.SetBounds(pad, progressTop, w - 2 * pad, progressH);
                _startButton.SetBounds(w - pad - S(130, s), startTop, S(130, s), startH);
                _status.SetBounds(pad, startBottom - statusH, _startButton.Left - gap - pad, statusH);
            }
            finally
            {
                _laying = false;
            }
        }

        private static int S(int value, float scale)
        {
            return (int)Math.Round(value * scale);
        }

        private void BuildOptionsPanel()
        {
            Panel panel = new Panel();
            panel.Location = new Point(700, 48);
            panel.Size = new Size(280, 464);
            panel.AutoScroll = true;
            panel.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(panel);
            _optionsPanel = panel;

            GroupBox formatGroup = new GroupBox();
            formatGroup.Text = "输出格式";
            formatGroup.Location = new Point(4, 4);
            formatGroup.Size = new Size(256, 120);
            panel.Controls.Add(formatGroup);

            AddLabel(formatGroup, "格式", 10, 26);
            _formatBox = new ComboBox();
            _formatBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _formatBox.Location = new Point(80, 22);
            _formatBox.Size = new Size(160, 22);
            _formatBox.Items.Add("保持原格式（WebP 源转 PNG）");
            _formatBox.Items.Add("PNG（保留透明）");
            _formatBox.Items.Add("JPG");
            _formatBox.Items.Add("BMP");
            _formatBox.Items.Add("GIF");
            _formatBox.Items.Add("TIFF");
            _formatBox.SelectedIndex = 0;
            _formatBox.SelectedIndexChanged += delegate { UpdateOptionStates(); };
            formatGroup.Controls.Add(_formatBox);

            AddLabel(formatGroup, "JPG 质量", 10, 58);
            _qualityBar = MakeTrackBar(formatGroup, 80, 54, 150);
            _qualityBar.Minimum = 1;
            _qualityBar.Maximum = 100;
            _qualityBar.Value = 90;
            _qualityBar.ValueChanged += delegate
            {
                _qualityValue.Text = _qualityBar.Value + "%";
            };
            _qualityValue = new Label();
            _qualityValue.Text = "90%";
            _qualityValue.Location = new Point(196, 60);
            _qualityValue.Size = new Size(48, 20);
            _qualityValue.TextAlign = ContentAlignment.MiddleRight;
            formatGroup.Controls.Add(_qualityValue);

            AddLabel(formatGroup, "透明填充", 10, 90);
            _matteButton = new Button();
            _matteButton.Text = "白色";
            _matteButton.Location = new Point(80, 86);
            _matteButton.Size = new Size(80, 20);
            _matteButton.BackColor = _matteColor;
            _matteButton.Click += delegate { ChooseMatteColor(); };
            formatGroup.Controls.Add(_matteButton);

            GroupBox resizeGroup = new GroupBox();
            resizeGroup.Text = "尺寸";
            resizeGroup.Location = new Point(4, 128);
            resizeGroup.Size = new Size(256, 176);
            panel.Controls.Add(resizeGroup);

            AddLabel(resizeGroup, "缩放模式", 10, 26);
            _resizeModeBox = new ComboBox();
            _resizeModeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _resizeModeBox.Location = new Point(80, 22);
            _resizeModeBox.Size = new Size(160, 22);
            _resizeModeBox.Items.Add("不缩放");
            _resizeModeBox.Items.Add("按长边");
            _resizeModeBox.Items.Add("按百分比");
            _resizeModeBox.Items.Add("指定宽高");
            _resizeModeBox.SelectedIndex = 0;
            _resizeModeBox.SelectedIndexChanged += delegate { UpdateOptionStates(); };
            resizeGroup.Controls.Add(_resizeModeBox);

            AddLabel(resizeGroup, "长边 px", 10, 58);
            _longEdgeNum = MakeNumeric(resizeGroup, 80, 54, 100, 1, 100000, 1920);

            AddLabel(resizeGroup, "百分比", 10, 90);
            _percentNum = MakeNumeric(resizeGroup, 80, 86, 100, 1, 4000, 100);

            AddLabel(resizeGroup, "宽 × 高", 10, 122);
            _widthNum = MakeNumeric(resizeGroup, 80, 118, 70, 1, 100000, 800);
            Label times = new Label();
            times.Text = "×";
            times.Location = new Point(154, 122);
            times.AutoSize = true;
            resizeGroup.Controls.Add(times);
            _heightNum = MakeNumeric(resizeGroup, 172, 118, 70, 1, 100000, 600);

            _keepAspectBox = new CheckBox();
            _keepAspectBox.Text = "保持宽高比";
            _keepAspectBox.Location = new Point(10, 148);
            _keepAspectBox.AutoSize = true;
            _keepAspectBox.Checked = true;
            resizeGroup.Controls.Add(_keepAspectBox);

            _allowUpscaleBox = new CheckBox();
            _allowUpscaleBox.Text = "允许放大";
            _allowUpscaleBox.Location = new Point(130, 148);
            _allowUpscaleBox.AutoSize = true;
            _allowUpscaleBox.Checked = true;
            resizeGroup.Controls.Add(_allowUpscaleBox);

            GroupBox transformGroup = new GroupBox();
            transformGroup.Text = "旋转 / 翻转";
            transformGroup.Location = new Point(4, 310);
            transformGroup.Size = new Size(256, 116);
            panel.Controls.Add(transformGroup);

            AddLabel(transformGroup, "旋转", 10, 26);
            _rotateBox = new ComboBox();
            _rotateBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _rotateBox.Location = new Point(80, 22);
            _rotateBox.Size = new Size(160, 22);
            _rotateBox.Items.Add("不旋转");
            _rotateBox.Items.Add("顺时针 90°");
            _rotateBox.Items.Add("180°");
            _rotateBox.Items.Add("逆时针 90°");
            _rotateBox.SelectedIndex = 0;
            transformGroup.Controls.Add(_rotateBox);

            _flipHBox = new CheckBox();
            _flipHBox.Text = "水平翻转";
            _flipHBox.Location = new Point(10, 56);
            _flipHBox.AutoSize = true;
            transformGroup.Controls.Add(_flipHBox);

            _flipVBox = new CheckBox();
            _flipVBox.Text = "垂直翻转";
            _flipVBox.Location = new Point(120, 56);
            _flipVBox.AutoSize = true;
            transformGroup.Controls.Add(_flipVBox);

            _autoOrientBox = new CheckBox();
            _autoOrientBox.Text = "按 EXIF 自动校正方向";
            _autoOrientBox.Location = new Point(10, 84);
            _autoOrientBox.AutoSize = true;
            _autoOrientBox.Checked = true;
            transformGroup.Controls.Add(_autoOrientBox);

            GroupBox nameGroup = new GroupBox();
            nameGroup.Text = "重命名";
            nameGroup.Location = new Point(4, 432);
            nameGroup.Size = new Size(256, 168);
            panel.Controls.Add(nameGroup);

            _keepNameRadio = new RadioButton();
            _keepNameRadio.Text = "保持原名";
            _keepNameRadio.Location = new Point(10, 22);
            _keepNameRadio.AutoSize = true;
            _keepNameRadio.Checked = true;
            _keepNameRadio.CheckedChanged += delegate { UpdateOptionStates(); };
            nameGroup.Controls.Add(_keepNameRadio);

            _customNameRadio = new RadioButton();
            _customNameRadio.Text = "自定义";
            _customNameRadio.Location = new Point(100, 22);
            _customNameRadio.AutoSize = true;
            _customNameRadio.CheckedChanged += delegate { UpdateOptionStates(); };
            nameGroup.Controls.Add(_customNameRadio);

            _patternBox = new TextBox();
            _patternBox.Location = new Point(10, 48);
            _patternBox.Size = new Size(236, 22);
            _patternBox.Text = "{name}_{nnn}";
            nameGroup.Controls.Add(_patternBox);

            Label hint = new Label();
            hint.Text = "可用：{name} {n} {nnn} {date} {time}";
            hint.Location = new Point(10, 76);
            hint.AutoSize = true;
            nameGroup.Controls.Add(hint);

            AddLabel(nameGroup, "起始序号", 10, 106);
            _startIndexNum = MakeNumeric(nameGroup, 80, 102, 70, 0, 1000000, 1);
            AddLabel(nameGroup, "位数", 158, 106);
            _indexDigitsNum = MakeNumeric(nameGroup, 196, 102, 50, 1, 8, 3);

            AddLabel(nameGroup, "查找", 10, 134);
            _findBox = new TextBox();
            _findBox.Location = new Point(50, 130);
            _findBox.Size = new Size(80, 22);
            nameGroup.Controls.Add(_findBox);
            AddLabel(nameGroup, "替换", 136, 134);
            _replaceBox = new TextBox();
            _replaceBox.Location = new Point(176, 130);
            _replaceBox.Size = new Size(70, 22);
            nameGroup.Controls.Add(_replaceBox);

            GroupBox wmGroup = new GroupBox();
            wmGroup.Text = "水印";
            wmGroup.Location = new Point(4, 606);
            wmGroup.Size = new Size(256, 306);
            panel.Controls.Add(wmGroup);

            _wmEnableBox = new CheckBox();
            _wmEnableBox.Text = "启用水印";
            _wmEnableBox.Location = new Point(10, 22);
            _wmEnableBox.AutoSize = true;
            _wmEnableBox.CheckedChanged += delegate { UpdateOptionStates(); };
            wmGroup.Controls.Add(_wmEnableBox);

            _wmTextRadio = new RadioButton();
            _wmTextRadio.Text = "文字";
            _wmTextRadio.Location = new Point(10, 48);
            _wmTextRadio.AutoSize = true;
            _wmTextRadio.Checked = true;
            _wmTextRadio.CheckedChanged += delegate { UpdateOptionStates(); };
            wmGroup.Controls.Add(_wmTextRadio);

            _wmImageRadio = new RadioButton();
            _wmImageRadio.Text = "图片";
            _wmImageRadio.Location = new Point(90, 48);
            _wmImageRadio.AutoSize = true;
            _wmImageRadio.CheckedChanged += delegate { UpdateOptionStates(); };
            wmGroup.Controls.Add(_wmImageRadio);

            _wmTextBox = new TextBox();
            _wmTextBox.Location = new Point(10, 74);
            _wmTextBox.Size = new Size(150, 22);
            _wmTextBox.Text = "水印";
            wmGroup.Controls.Add(_wmTextBox);

            _wmColorButton = new Button();
            _wmColorButton.Text = "颜色";
            _wmColorButton.Location = new Point(168, 73);
            _wmColorButton.Size = new Size(78, 20);
            _wmColorButton.BackColor = _wmColor;
            _wmColorButton.Click += delegate { ChooseWatermarkColor(); };
            wmGroup.Controls.Add(_wmColorButton);

            AddLabel(wmGroup, "字号", 10, 108);
            _wmFontSizeNum = MakeNumeric(wmGroup, 54, 104, 60, 6, 500, 36);
            AddLabel(wmGroup, "角度", 150, 108);
            _wmAngleNum = MakeNumeric(wmGroup, 190, 104, 56, -180, 180, 0);

            AddLabel(wmGroup, "不透明度", 10, 140);
            _wmOpacityBar = MakeTrackBar(wmGroup, 80, 136, 150);
            _wmOpacityBar.Minimum = 0;
            _wmOpacityBar.Maximum = 100;
            _wmOpacityBar.Value = 50;
            _wmOpacityBar.ValueChanged += delegate
            {
                _wmOpacityValue.Text = _wmOpacityBar.Value + "%";
            };
            _wmOpacityValue = new Label();
            _wmOpacityValue.Text = "50%";
            _wmOpacityValue.Location = new Point(196, 142);
            _wmOpacityValue.Size = new Size(48, 20);
            _wmOpacityValue.TextAlign = ContentAlignment.MiddleRight;
            wmGroup.Controls.Add(_wmOpacityValue);

            AddLabel(wmGroup, "位置", 10, 174);
            _wmPosBox = new ComboBox();
            _wmPosBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _wmPosBox.Location = new Point(80, 170);
            _wmPosBox.Size = new Size(160, 22);
            _wmPosBox.Items.Add("左上");
            _wmPosBox.Items.Add("上中");
            _wmPosBox.Items.Add("右上");
            _wmPosBox.Items.Add("左中");
            _wmPosBox.Items.Add("居中");
            _wmPosBox.Items.Add("右中");
            _wmPosBox.Items.Add("左下");
            _wmPosBox.Items.Add("下中");
            _wmPosBox.Items.Add("右下");
            _wmPosBox.SelectedIndex = 8;
            _wmPosBox.SelectedIndexChanged += delegate { UpdateOptionStates(); };
            wmGroup.Controls.Add(_wmPosBox);

            AddLabel(wmGroup, "边距", 10, 206);
            _wmMarginNum = MakeNumeric(wmGroup, 54, 202, 60, 0, 2000, 16);
            _wmTileBox = new CheckBox();
            _wmTileBox.Text = "平铺";
            _wmTileBox.Location = new Point(130, 204);
            _wmTileBox.AutoSize = true;
            _wmTileBox.CheckedChanged += delegate { UpdateOptionStates(); };
            wmGroup.Controls.Add(_wmTileBox);

            AddLabel(wmGroup, "水印图", 10, 238);
            _wmImageBox = new TextBox();
            _wmImageBox.Location = new Point(80, 234);
            _wmImageBox.Size = new Size(110, 22);
            wmGroup.Controls.Add(_wmImageBox);
            _wmImageBrowse = new Button();
            _wmImageBrowse.Text = "浏览...";
            _wmImageBrowse.Location = new Point(196, 233);
            _wmImageBrowse.Size = new Size(54, 20);
            _wmImageBrowse.Click += delegate { ChooseWatermarkImage(); };
            wmGroup.Controls.Add(_wmImageBrowse);

            AddLabel(wmGroup, "缩放%", 10, 270);
            _wmScaleNum = MakeNumeric(wmGroup, 80, 266, 60, 1, 100, 20);

            // 收紧：先让每个分组贴合内容，再把分组依次压紧排列。
            LayoutCompact.CompactAndFit(formatGroup, 6, 12);
            LayoutCompact.CompactAndFit(resizeGroup, 6, 12);
            LayoutCompact.CompactAndFit(transformGroup, 6, 12);
            LayoutCompact.CompactAndFit(nameGroup, 6, 12);
            LayoutCompact.CompactAndFit(wmGroup, 6, 12);
            LayoutCompact.Compact(panel, 6);

            UpdateOptionStates();
        }

        private Button MakeButton(string text, int x, int y, int width)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(width, 22);
            return b;
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
            num.Size = new Size(width, 20);
            num.Minimum = min;
            num.Maximum = max;
            num.Value = value;
            parent.Controls.Add(num);
            return num;
        }

        private TrackBar MakeTrackBar(Control parent, int x, int y, int width)
        {
            TrackBar bar = new TrackBar();
            bar.AutoSize = false;
            bar.Minimum = 0;
            bar.Maximum = 100;
            bar.TickStyle = TickStyle.None;
            bar.Location = new Point(x, y);
            bar.Size = new Size(width, 20);
            parent.Controls.Add(bar);
            return bar;
        }

        private Image CreatePlaceholder()
        {
            Bitmap bmp = new Bitmap(ThumbSize, ThumbSize);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(245, 245, 245));
                using (Pen pen = new Pen(Color.FromArgb(210, 210, 210)))
                {
                    g.DrawRectangle(pen, 0, 0, ThumbSize - 1, ThumbSize - 1);
                }
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    using (Brush brush = new SolidBrush(Color.FromArgb(160, 160, 160)))
                    {
                        g.DrawString("载入中", Font, brush, new RectangleF(0, 0, ThumbSize, ThumbSize), format);
                    }
                }
            }
            return bmp;
        }

        private void UpdateOptionStates()
        {
            bool jpeg = _formatBox.SelectedIndex == (int)BatchOutputFormat.Jpeg;
            _qualityBar.Enabled = jpeg;
            _qualityValue.Enabled = jpeg;
            _matteButton.Enabled = _formatBox.SelectedIndex == (int)BatchOutputFormat.Jpeg
                || _formatBox.SelectedIndex == (int)BatchOutputFormat.Bmp;

            int resizeMode = _resizeModeBox.SelectedIndex;
            _longEdgeNum.Enabled = resizeMode == (int)BatchResizeMode.LongEdge;
            _allowUpscaleBox.Enabled = resizeMode == (int)BatchResizeMode.LongEdge;
            _percentNum.Enabled = resizeMode == (int)BatchResizeMode.Percent;
            _widthNum.Enabled = resizeMode == (int)BatchResizeMode.WidthHeight;
            _heightNum.Enabled = resizeMode == (int)BatchResizeMode.WidthHeight;
            _keepAspectBox.Enabled = resizeMode == (int)BatchResizeMode.WidthHeight;

            bool custom = _customNameRadio.Checked;
            _patternBox.Enabled = custom;
            _findBox.Enabled = custom;
            _replaceBox.Enabled = custom;
            _startIndexNum.Enabled = custom;
            _indexDigitsNum.Enabled = custom;

            bool wm = _wmEnableBox.Checked;
            bool text = wm && _wmTextRadio.Checked;
            bool tile = _wmTileBox.Checked;

            _wmTextRadio.Enabled = wm;
            _wmImageRadio.Enabled = wm;
            _wmTextBox.Enabled = text;
            _wmColorButton.Enabled = text;
            _wmFontSizeNum.Enabled = text;
            _wmAngleNum.Enabled = text;
            _wmOpacityBar.Enabled = wm;
            _wmOpacityValue.Enabled = wm;
            _wmPosBox.Enabled = wm && !tile;
            _wmMarginNum.Enabled = wm && !tile;
            _wmTileBox.Enabled = wm;
            _wmImageBox.Enabled = wm && !text;
            _wmImageBrowse.Enabled = wm && !text;
            _wmScaleNum.Enabled = wm && !text;
        }

        private void ChooseMatteColor()
        {
            ColorDialog dialog = new ColorDialog();
            dialog.Color = _matteColor;
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _matteColor = dialog.Color;
                _matteButton.BackColor = _matteColor;
            }
        }

        private void ChooseWatermarkColor()
        {
            ColorDialog dialog = new ColorDialog();
            dialog.Color = _wmColor;
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _wmColor = dialog.Color;
                _wmColorButton.BackColor = _wmColor;
            }
        }

        private void ChooseWatermarkImage()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择水印图片";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _wmImageBox.Text = dialog.FileName;
            }
        }

        private void AddFilesDialog()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择图片";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.gif;*.tif;*.tiff;*.webp|所有文件|*.*";
            dialog.Multiselect = true;
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                AddFiles(dialog.FileNames);
            }
        }

        private void AddFolderDialog()
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "选择包含图片的文件夹";
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                RememberDir(dialog.SelectedPath);
                List<string> found = FindImageFiles(dialog.SelectedPath, _recursiveBox.Checked);
                if (found.Count == 0)
                {
                    MessageBox.Show(this, "该文件夹中未找到支持的图片", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                AddFiles(found);
            }
        }

        private static List<string> FindImageFiles(string folder, bool recursive)
        {
            List<string> result = new List<string>();
            SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            try
            {
                foreach (string path in Directory.GetFiles(folder, "*.*", option))
                {
                    if (ImageBatch.IsSupported(path))
                    {
                        result.Add(path);
                    }
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        private void AddFiles(IEnumerable<string> paths)
        {
            _suppressStatus = true;
            int added = AddFileEntries(paths);
            _suppressStatus = false;

            if (added > 0)
            {
                _status.Text = "已添加 " + added + " 个文件";
            }
            UpdateStatus();
            StartThumbWorker();
        }

        private int AddFileEntries(IEnumerable<string> paths)
        {
            int added = 0;
            foreach (string path in paths)
            {
                string full;
                try
                {
                    full = Path.GetFullPath(path);
                }
                catch (Exception)
                {
                    continue;
                }

                if (!ImageBatch.IsSupported(full))
                {
                    continue;
                }
                if (_itemByPath.ContainsKey(full))
                {
                    continue;
                }

                string display = Path.GetFileName(full);
                if (display.Length > 24)
                {
                    display = display.Substring(0, 12) + "…" + display.Substring(display.Length - 9);
                }

                ListViewItem item = new ListViewItem(display);
                item.Tag = full;
                item.ToolTipText = full;
                item.ImageIndex = 0;
                item.Checked = true;
                _listView.Items.Add(item);
                _itemByPath[full] = item;
                added++;

                if (_thumbQueued.Add(full))
                {
                    _thumbQueue.Add(full);
                }
            }

            return added;
        }

        private void UpdateStatus()
        {
            if (_suppressStatus)
            {
                return;
            }

            int total = _listView.Items.Count;
            int checkedCount = 0;
            foreach (ListViewItem item in _listView.Items)
            {
                if (item.Checked)
                {
                    checkedCount++;
                }
            }
            _status.Text = "共 " + total + " 个文件，已勾选 " + checkedCount + " 个";
        }

        private void SetAllChecked(bool value)
        {
            _suppressStatus = true;
            foreach (ListViewItem item in _listView.Items)
            {
                item.Checked = value;
            }
            _suppressStatus = false;
            UpdateStatus();
        }

        private void StartThumbWorker()
        {
            if (_closing || _thumbWorker.IsBusy || _thumbQueue.Count == 0)
            {
                return;
            }

            List<string> batch = new List<string>(_thumbQueue);
            _thumbQueue.Clear();
            _thumbWorker.RunWorkerAsync(batch);
        }

        private void ThumbDoWork(object sender, DoWorkEventArgs e)
        {
            List<string> batch = (List<string>)e.Argument;
            foreach (string path in batch)
            {
                if (_thumbWorker.CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }

                Image thumb = null;
                try
                {
                    thumb = ImageUtil.LoadThumbnail(path, ThumbSize);
                }
                catch (Exception)
                {
                }

                if (thumb != null)
                {
                    _thumbWorker.ReportProgress(0, new ThumbResult(path, thumb));
                }
            }
        }

        private void ThumbProgress(object sender, ProgressChangedEventArgs e)
        {
            if (_closing)
            {
                return;
            }

            ThumbResult result = (ThumbResult)e.UserState;
            if (_imageList.Images.ContainsKey(result.Path))
            {
                _imageList.Images.RemoveByKey(result.Path);
            }
            _imageList.Images.Add(result.Path, result.Image);

            ListViewItem item;
            if (_itemByPath.TryGetValue(result.Path, out item))
            {
                item.ImageKey = result.Path;
                _listView.Invalidate();
            }
        }

        private void ThumbCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (!_closing && _thumbQueue.Count > 0)
            {
                StartThumbWorker();
            }
        }

        private void RemoveSelected()
        {
            if (_listView.SelectedItems.Count == 0)
            {
                return;
            }

            List<ListViewItem> selected = new List<ListViewItem>();
            foreach (ListViewItem item in _listView.SelectedItems)
            {
                selected.Add(item);
            }

            foreach (ListViewItem item in selected)
            {
                RemoveItem(item);
            }

            UpdateStatus();
        }

        private void RemoveItem(ListViewItem item)
        {
            string path = (string)item.Tag;
            _listView.Items.Remove(item);
            _itemByPath.Remove(path);
            _thumbQueued.Remove(path);
            if (_imageList.Images.ContainsKey(path))
            {
                _imageList.Images.RemoveByKey(path);
            }
        }

        private void RememberDir(string dir)
        {
            string full;
            try
            {
                full = Path.GetFullPath(dir);
            }
            catch (Exception)
            {
                return;
            }
            _watchedDirs.Add(full);
        }

        private void RefreshFromDirs()
        {
            if (_watchedDirs.Count == 0)
            {
                _status.Text = "请先添加文件夹，再点刷新";
                return;
            }

            Cursor previousCursor = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            _suppressStatus = true;
            try
            {
                for (int i = _listView.Items.Count - 1; i >= 0; i--)
                {
                    ListViewItem item = _listView.Items[i];
                    string path = (string)item.Tag;
                    if (!File.Exists(path))
                    {
                        RemoveItem(item);
                    }
                }

                foreach (string dir in _watchedDirs)
                {
                    if (Directory.Exists(dir))
                    {
                        AddFileEntries(FindImageFiles(dir, _recursiveBox.Checked));
                    }
                }
            }
            finally
            {
                _suppressStatus = false;
                this.Cursor = previousCursor;
            }

            UpdateStatus();
            StartThumbWorker();
        }

        private void ClearFiles()
        {
            _listView.Items.Clear();
            _itemByPath.Clear();
            _thumbQueue.Clear();
            _thumbQueued.Clear();
            _watchedDirs.Clear();
            _imageList.Images.Clear();
            _imageList.Images.Add(PlaceholderKey, CreatePlaceholder());
            UpdateStatus();
        }

        private void ChooseOutDir()
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "选择输出目录";
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _outBox.Text = dialog.SelectedPath;
            }
        }

        private BatchOptions BuildOptions()
        {
            BatchOptions options = new BatchOptions();
            options.OutputDir = _outBox.Text.Trim();
            options.Overwrite = _overwriteBox.Checked;
            options.Format = (BatchOutputFormat)_formatBox.SelectedIndex;
            options.JpegQuality = _qualityBar.Value;
            options.MatteColor = _matteColor;

            options.ResizeMode = (BatchResizeMode)_resizeModeBox.SelectedIndex;
            options.LongEdge = (int)_longEdgeNum.Value;
            options.Percent = (int)_percentNum.Value;
            options.TargetWidth = (int)_widthNum.Value;
            options.TargetHeight = (int)_heightNum.Value;
            options.KeepAspect = _keepAspectBox.Checked;
            options.AllowUpscale = _allowUpscaleBox.Checked;

            options.AutoOrient = _autoOrientBox.Checked;
            options.Rotation = _rotateBox.SelectedIndex * 90;
            options.FlipHorizontal = _flipHBox.Checked;
            options.FlipVertical = _flipVBox.Checked;

            options.NamePattern = _customNameRadio.Checked ? _patternBox.Text : "{name}";
            options.StartIndex = (int)_startIndexNum.Value;
            options.IndexDigits = (int)_indexDigitsNum.Value;
            options.FindText = _customNameRadio.Checked ? _findBox.Text : "";
            options.ReplaceText = _replaceBox.Text;

            options.WatermarkEnabled = _wmEnableBox.Checked;
            options.WatermarkIsImage = _wmImageRadio.Checked;
            options.WatermarkText = _wmTextBox.Text;
            options.WatermarkFontSize = (float)_wmFontSizeNum.Value;
            options.WatermarkColor = _wmColor;
            options.WatermarkImagePath = _wmImageBox.Text.Trim();
            options.WatermarkScale = (int)_wmScaleNum.Value;
            options.WatermarkAngle = (float)_wmAngleNum.Value;
            options.WatermarkPosition = (WatermarkPosition)_wmPosBox.SelectedIndex;
            options.WatermarkOpacity = _wmOpacityBar.Value / 100f;
            options.WatermarkTile = _wmTileBox.Checked;
            options.WatermarkMargin = (int)_wmMarginNum.Value;
            return options;
        }

        private void StartProcess()
        {
            if (_worker.IsBusy)
            {
                return;
            }

            if (_listView.Items.Count == 0)
            {
                MessageBox.Show(this, "请先添加图片", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<string> files = new List<string>();
            foreach (ListViewItem item in _listView.Items)
            {
                if (item.Checked)
                {
                    files.Add((string)item.Tag);
                }
            }

            if (files.Count == 0)
            {
                MessageBox.Show(this, "请至少勾选一张图片", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            BatchOptions options = BuildOptions();
            if (options.OutputDir.Length > 0 && !Directory.Exists(options.OutputDir))
            {
                MessageBox.Show(this, "输出目录不存在", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Bitmap watermarkImage = null;
            if (options.WatermarkEnabled && options.WatermarkIsImage)
            {
                if (options.WatermarkImagePath.Length == 0 || !File.Exists(options.WatermarkImagePath))
                {
                    MessageBox.Show(this, "请选择有效的水印图片", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                try
                {
                    watermarkImage = ImageUtil.LoadImage(options.WatermarkImagePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "无法加载水印图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            BatchWork work = new BatchWork();
            work.Files = files;
            work.Options = options;
            work.WatermarkImage = watermarkImage;

            _progress.Minimum = 0;
            _progress.Maximum = files.Count;
            _progress.Value = 0;
            _startButton.Enabled = false;
            _status.Text = "处理中...";

            _worker.RunWorkerAsync(work);
        }

        private void WorkerDoWork(object sender, DoWorkEventArgs e)
        {
            BatchWork work = (BatchWork)e.Argument;
            int ok = 0;
            int fail = 0;
            List<string> errors = new List<string>();

            try
            {
                for (int i = 0; i < work.Files.Count; i++)
                {
                    if (_worker.CancellationPending)
                    {
                        e.Cancel = true;
                        break;
                    }

                    int sequence = work.Options.StartIndex + i;
                    try
                    {
                        ImageBatch.Run(work.Files[i], work.Options, sequence, work.WatermarkImage);
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        errors.Add(Path.GetFileName(work.Files[i]) + "：" + ex.Message);
                    }
                    _worker.ReportProgress(i + 1);
                }
            }
            finally
            {
                if (work.WatermarkImage != null)
                {
                    work.WatermarkImage.Dispose();
                }
            }

            e.Result = new BatchResult(ok, fail, work.Files.Count, errors);
        }

        private void WorkerProgress(object sender, ProgressChangedEventArgs e)
        {
            _progress.Value = e.ProgressPercentage;
        }

        private void WorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            _startButton.Enabled = true;

            if (e.Error != null)
            {
                _status.Text = "处理失败：" + e.Error.Message;
                MessageBox.Show(this, e.Error.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (e.Cancelled)
            {
                _status.Text = "已取消";
                return;
            }

            BatchResult result = (BatchResult)e.Result;
            _status.Text = "完成：成功 " + result.Ok + " / " + result.Total + "，失败 " + result.Fail;

            string message = "成功 " + result.Ok + " 个，失败 " + result.Fail + " 个，共 " + result.Total + " 个。";
            if (result.Errors.Count > 0)
            {
                message += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, result.Errors.ToArray());
            }
            MessageBox.Show(this, message, "处理完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            List<string> collected = new List<string>();

            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                {
                    RememberDir(path);
                    collected.AddRange(FindImageFiles(path, _recursiveBox.Checked));
                }
                else if (File.Exists(path) && ImageBatch.IsSupported(path))
                {
                    collected.Add(path);
                }
            }

            if (collected.Count == 0)
            {
                _status.Text = "未找到支持的图片";
                return;
            }
            AddFiles(collected);
        }
    }

    public class ThumbResult
    {
        public string Path;
        public Image Image;

        public ThumbResult(string path, Image image)
        {
            Path = path;
            Image = image;
        }
    }

    public class BatchResult
    {
        public int Ok;
        public int Fail;
        public int Total;
        public List<string> Errors;

        public BatchResult(int ok, int fail, int total, List<string> errors)
        {
            Ok = ok;
            Fail = fail;
            Total = total;
            Errors = errors;
        }
    }

    public class BatchWork
    {
        public List<string> Files;
        public BatchOptions Options;
        public Bitmap WatermarkImage;
    }
}
