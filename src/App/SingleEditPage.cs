using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class SingleEditPage : ToolPage
    {
        // 预览（画布显示用）的长边像素：至少 1000，并按画布尺寸自适应，
        // 保证适应窗口显示时预览不会被放大而发糊；上限避免内存/CPU 过大。
        private int _previewSize = 1000;

        private readonly EditSession _session = new EditSession();
        private readonly EditOpPanel[] _ops;
        private SelectionOp _selectionOp;   // 选区（用于跨功能保持选区轮廓）
        private ListBox _list;
        private Panel _opHost;
        private LayerPanel _layerPanel;
        private HistoryPanel _historyPanel;
        private ImageCanvas _canvas;
        private Button _openButton;
        private Button _newButton;
        private Button _saveButton;
        private Button _applyButton;
        private Button _undoButton;
        private Button _redoButton;
        private Button _resetButton;
        private Label _status;
        private Timer _previewTimer;
        private Timer _liveTimer;
        private Timer _antsTimer;   // 选区蚂蚁线动画（跨功能持续）
        private bool _livePending;

        private int _active = -1;
        private string _sourcePath;
        private Bitmap _previewSource;
        private bool _ownPreviewSource;
        private Bitmap _opSource;
        private bool _ownOpSource;
        private Bitmap _shownPreview;
        private Bitmap _displayImage;
        private Bitmap _entrySnapshot;
        private Bitmap _composeBuffer;   // 单图编辑：多图层预览的复用合成缓冲（避免每帧新建整图）
        private bool _composeValid;      // 缓冲当前是否对应当前状态（否则下一帧整图重合成）

        private bool _opDirty;
        private bool _suppressDirty;
        private bool _switching;

        public SingleEditPage()
        {
            _ops = new EditOpPanel[]
            {
                new InfoOp(),
                new DrawOp(),
                new TransformOp(),
                new MattingOp(),
                new SelectionOp(),
                new CropOp(),
                new LocalOverlayOp(),
                new ColorGradeOp(),
                new LocalMaskOp(),
                new StyleOp(),
                new EffectsOp(),
                new GradientOp(),
                new ColorMatchOp(),
                new ColorToolOp(),
                new IdPhotoOp(),
                new SliceCollageOp(),
                new CompareOp()
            };
            for (int i = 0; i < _ops.Length; i++)
            {
                SelectionOp so = _ops[i] as SelectionOp;
                if (so != null) { _selectionOp = so; break; }
            }

            // 作为 MainForm 的子控件：用 Inherit，由顶层窗体的 DPI 缩放统一处理，
            // 自身再设 Dpi 会在高 DPI 下被缩放两次（布局错乱）。
            AutoScaleMode = AutoScaleMode.Inherit;
            Font = new Font("Microsoft YaHei UI", 9F);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "单图编辑"; }
        }

        public override void Shutdown()
        {
            if (_antsTimer != null) { _antsTimer.Stop(); _antsTimer.Dispose(); _antsTimer = null; }
            if (_canvas != null) { _canvas.SetImage(null); }
            _displayImage = null;
            for (int i = 0; i < _ops.Length; i++)
            {
                _ops[i].Detach();
                _ops[i].DisposeResources();
            }
            if (_shownPreview != null) { _shownPreview.Dispose(); _shownPreview = null; }
            if (_composeBuffer != null) { _composeBuffer.Dispose(); _composeBuffer = null; }
            if (_ownPreviewSource && _previewSource != null) { _previewSource.Dispose(); }
            _previewSource = null;
            if (_ownOpSource && _opSource != null) { _opSource.Dispose(); }
            _opSource = null;
            if (_entrySnapshot != null) { _entrySnapshot.Dispose(); _entrySnapshot = null; }
            _session.DisposeAll();
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(root);

            Panel toolbar = new Panel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.Margin = new Padding(3, 3, 3, 0);
            root.Controls.Add(toolbar, 0, 0);

            _openButton = MakeButton(toolbar, "打开图片", 8, 90);
            _openButton.Click += delegate { OpenImage(); };

            _newButton = MakeButton(toolbar, "新建", 102, 62);
            _newButton.Click += delegate { NewImage(); };

            _saveButton = MakeButton(toolbar, "保存为 PNG", 168, 100);
            _saveButton.Click += delegate { SaveImage(); };

            _applyButton = MakeButton(toolbar, "应用到图片", 272, 100);
            _applyButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _applyButton.Click += delegate { ApplyActive(); };

            _undoButton = MakeButton(toolbar, "撤销", 376, 62);
            _undoButton.Click += delegate { _session.Undo(); ReloadAll(); _layerPanel.Sync(); };

            _redoButton = MakeButton(toolbar, "重做", 442, 62);
            _redoButton.Click += delegate { _session.Redo(); ReloadAll(); _layerPanel.Sync(); };

            _resetButton = MakeButton(toolbar, "复位", 508, 62);
            _resetButton.Click += delegate { _session.ResetToOriginal(); ReloadAll(); _layerPanel.Sync(); };

            _status = new Label();
            _status.Location = new Point(582, 6);
            _status.Size = new Size(500, 18);
            _status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _status.Text = "打开一张图片，然后在左侧选择操作；调好后点「应用到图片」";
            toolbar.Controls.Add(_status);

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 3;
            body.RowCount = 1;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142f));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300f));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            body.Margin = new Padding(3, 3, 3, 3);
            root.Controls.Add(body, 0, 1);

            BufferedListHost listHost = new BufferedListHost();
            listHost.Dock = DockStyle.Fill;
            listHost.Margin = new Padding(3, 3, 3, 3);
            body.Controls.Add(listHost, 0, 0);

            _list = new ListBox();
            _list.Dock = DockStyle.Fill;
            _list.IntegralHeight = false;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.ItemHeight = 26;
            _list.SelectedIndexChanged += delegate { OnOpSelected(); };
            listHost.Controls.Add(_list);

            _canvas = new ImageCanvas();
            _canvas.Dock = DockStyle.Fill;
            _canvas.Margin = new Padding(3, 3, 3, 3);
            _canvas.ReadOnly = true;
            _canvas.PixelClicked += delegate(Point p) { DispatchClick(p); };
            _canvas.SelectionChanged += delegate { DispatchSelection(); };
            _canvas.BrushStarted += delegate(Point p) { DispatchBrush(p, 0); };
            _canvas.BrushMoved += delegate(Point p) { DispatchBrush(p, 1); };
            _canvas.BrushFinished += delegate { DispatchBrush(Point.Empty, 2); };
            _canvas.DragStarted += delegate(Point p) { DispatchDrag(p, 0); };
            _canvas.DragMoved += delegate(Point p) { DispatchDrag(p, 1); };
            _canvas.DragFinished += delegate { DispatchDrag(Point.Empty, 2); };
            _canvas.PointerMoved += delegate(Point p) { DispatchHover(p); };
            _canvas.PointerDoubleClicked += delegate(Point p) { DispatchDoubleClick(p); };
            body.Controls.Add(_canvas, 1, 0);

            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.Margin = new Padding(0);
            body.Controls.Add(right, 2, 0);

            _opHost = new Panel();
            _opHost.Dock = DockStyle.Fill;
            _opHost.AutoScroll = true;
            _opHost.Padding = new Padding(3, 3, 3, 3);
            right.Controls.Add(_opHost);

            Splitter splitter = new Splitter();
            splitter.Dock = DockStyle.Bottom;
            splitter.Height = 6;
            splitter.MinExtra = 140;
            splitter.MinSize = 220;
            splitter.BackColor = SystemColors.ControlDark;
            right.Controls.Add(splitter);

            _layerPanel = new LayerPanel();
            _layerPanel.Dock = DockStyle.Fill;
            _layerPanel.LayersChanged += delegate { OnLayersChanged(); };
            // 切换/增删图层前，若当前操作有未应用的结果，先询问应用/放弃/取消。
            _layerPanel.ActiveLayerChanging += delegate(object s, LayerChangingEventArgs e)
            {
                // 绘画标注在新建图层时保持旧交互：未应用的笔迹自动跟随到新图层，不提示。
                if (e.AddsLayer && _active >= 0 && _active < _ops.Length && _ops[_active].CarriesOverToAddedLayer)
                {
                    return;
                }
                e.Cancel = !ResolvePendingEdits();
            };
            // 图层属性（显示/混合模式/不透明度）变化会让背景合成失效，必须整图重合成。
            _layerPanel.PropsChanged += delegate { _composeValid = false; SchedulePreview(); };

            _historyPanel = new HistoryPanel();
            _historyPanel.Dock = DockStyle.Fill;
            _historyPanel.JumpRequested += delegate(int index)
            {
                _session.JumpTo(index);
                ReloadAll();
                _layerPanel.Sync();
            };

            // 底部「图层 / 历史」两个 Tab，共用原图层面板的高度与可拖动分隔条。
            TabControl bottom = new TabControl();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 318;
            TabPage layerTab = new TabPage("图层");
            layerTab.UseVisualStyleBackColor = true;
            layerTab.Controls.Add(_layerPanel);
            bottom.TabPages.Add(layerTab);
            TabPage historyTab = new TabPage("历史");
            historyTab.UseVisualStyleBackColor = true;
            historyTab.Controls.Add(_historyPanel);
            bottom.TabPages.Add(historyTab);
            right.Controls.Add(bottom);

            _layerPanel.Bind(_session);
            _historyPanel.Bind(_session);

            for (int i = 0; i < _ops.Length; i++)
            {
                EditOpPanel op = _ops[i];
                op.Visible = false;
                op.PreviewInvalidated += delegate { OnOpPreviewInvalidated(op); };
                op.ApplyRequested += delegate
                {
                    if (_ops[_active] == op) { ApplyActive(); }
                };
                op.ResetRequested += delegate
                {
                    if (_ops[_active] == op) { ResetToEntry(); }
                };
                // 组合操作切换子页面前，同样先询问未应用的修改。
                op.SubOpChanging += delegate(object s, CancelEventArgs e)
                {
                    if (_ops[_active] == op) { e.Cancel = !ResolvePendingEdits(); }
                };
                _opHost.Controls.Add(op);
            }

            string[] names =
            {
                "图片信息", "绘画标注", "变换", "抠图", "选区", "裁剪 / 旋转",
                "局部覆盖", "调色", "局部调整", "风格预设", "特效", "渐变",
                "取色配色", "颜色工具", "证件照", "切图拼图", "图像对比"
            };
            for (int i = 0; i < names.Length; i++)
            {
                _list.Items.Add(names[i]);
            }

            _previewTimer = new Timer();
            _previewTimer.Interval = 120;
            _previewTimer.Tick += delegate { _previewTimer.Stop(); ComputePreview(); };

            // 实时操作（画笔/变换）的节流器：把同一帧内的多次失效合并成一次预览，
            // 避免高频鼠标事件每来一个就整图重算+重绘，把 UI 线程压满而卡顿。
            _liveTimer = new Timer();
            _liveTimer.Interval = 15;
            _liveTimer.Tick += delegate { _liveTimer.Stop(); _livePending = false; ComputePreview(); };

            // 选区蚂蚁线：110ms 推进一次相位并重绘画布；只要存在选区就一直流动
            // （即使当前不是「选区」功能，画布叠加层也会补画选区轮廓）。
            _antsTimer = new Timer();
            _antsTimer.Interval = 110;
            _antsTimer.Tick += delegate { OnAntsTick(); };
            _antsTimer.Start();

            UpdateButtons();

            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = 0;
            }
        }

        private static Button MakeButton(Control parent, string text, int x, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, 3);
            button.Size = new Size(width, 22);
            parent.Controls.Add(button);
            return button;
        }

        private void OpenImage()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "打开图片";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap loaded = ImageUtil.LoadImage(dialog.FileName);
                ClearCanvasDisplay();
                _sourcePath = dialog.FileName;
                _session.SetOriginal(loaded, "打开图片");
                loaded.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                this.Cursor = previous;
            }

            CaptureEntrySnapshot();
            ReloadAll();
            _layerPanel.Sync();
            _status.Text = "已载入：" + Path.GetFileName(_sourcePath) + "  （" + _session.Width + "x" + _session.Height + "）";
        }

        private void NewImage()
        {
            int width, height;
            Color background;
            using (NewImageDialog dialog = new NewImageDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                width = dialog.ImageWidth;
                height = dialog.ImageHeight;
                background = dialog.Background;
            }

            ApplyNewImage(width, height, background);
        }

        private void ApplyNewImage(int width, int height, Color background)
        {
            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap blank = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(blank))
                {
                    g.Clear(background);
                }
                ClearCanvasDisplay();
                _sourcePath = null;
                _session.SetOriginal(blank, "新建图片");
                blank.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "新建失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                this.Cursor = previous;
            }

            CaptureEntrySnapshot();
            ReloadAll();
            _layerPanel.Sync();
            _status.Text = "已新建：" + _session.Width + "x" + _session.Height +
                (background.A == 0 ? "（透明背景）" : "（白色背景）");
        }

        private void ClearCanvasDisplay()
        {
            _canvas.SetImage(null);
            if (_shownPreview != null)
            {
                _shownPreview.Dispose();
                _shownPreview = null;
            }
            _displayImage = null;
            _opDirty = false;
            if (_selectionOp != null) { _selectionOp.ClearSelection(); }
        }

        private void CaptureEntrySnapshot()
        {
            if (_entrySnapshot != null) { _entrySnapshot.Dispose(); _entrySnapshot = null; }
            if (_active >= 0 && _active < _ops.Length && _ops[_active].WantsEntrySnapshot && _session.HasImage)
            {
                _entrySnapshot = _session.Composite();
            }
        }

        private void ResetToEntry()
        {
            if (_entrySnapshot == null || !_session.HasImage)
            {
                _status.Text = "没有可重置的基准状态";
                return;
            }
            Bitmap restore = ImageFilters.Clone(_entrySnapshot);
            _session.CommitDocument(restore, "重置");
            ReloadAll();
            _layerPanel.Sync();
            _status.Text = "已重置到进入该操作时的图片状态";
        }

        // 当前操作有未应用的修改时询问：应用 / 放弃 / 取消。
        // 返回 false 表示用户选择取消（应停留在原状态）。
        private bool ResolvePendingEdits()
        {
            if (_active < 0 || _active >= _ops.Length || !_opDirty || !_ops[_active].CanApply ||
                !_ops[_active].HasPendingResult)
            {
                return true;
            }
            string name = (_active < _list.Items.Count) ? _list.Items[_active].ToString() : "当前操作";
            DialogResult answer = MessageBox.Show(this,
                "「" + name + "」的结果还没有应用到图片。\r\n是否先应用到图片？",
                "未应用的修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
            {
                return false;
            }
            if (answer == DialogResult.Yes)
            {
                ApplyActive();
            }
            else
            {
                // 放弃未应用的修改：把当前操作恢复到中性状态。
                _ops[_active].ResetState();
                _opDirty = false;
            }
            return true;
        }

        private void OnOpSelected()
        {
            if (_switching)
            {
                return;
            }
            int index = _list.SelectedIndex;
            if (index == _active)
            {
                return;
            }

            // 当前操作有未应用的预览结果时，切换前先询问。
            if (!ResolvePendingEdits())
            {
                _switching = true;
                _list.SelectedIndex = _active;
                _switching = false;
                return;
            }

            if (_active >= 0 && _active < _ops.Length)
            {
                _ops[_active].Detach();
            }
            _active = index;
            for (int i = 0; i < _ops.Length; i++)
            {
                _ops[i].Visible = (i == index);
            }
            if (index >= 0 && index < _ops.Length)
            {
                _ops[index].BringToFront();
            }
            _opDirty = false;
            CaptureEntrySnapshot();
            ReloadAll();
        }

        private void ReloadAll()
        {
            if (_active < 0 || _active >= _ops.Length)
            {
                UpdateButtons();
                return;
            }
            _suppressDirty = true;
            try
            {
                if (_session.HasImage)
                {
                    RefreshPreviewSource();
                    for (int i = 0; i < _ops.Length; i++)
                    {
                        _ops[i].Visible = (i == _active);
                    }
                    _canvas.ReadOnly = true;
                    _canvas.BrushEnabled = false;
                    _canvas.DragEnabled = false;
                    _canvas.LockAspect = 0f;
                    _canvas.Selection = Rectangle.Empty;
                    _ops[_active].Attach(_opSource, _previewSource, _canvas);
                    if (_ops[_active].WantsCanvasDrag)
                    {
                        _canvas.ReadOnly = false;
                        _canvas.BrushEnabled = false;
                        _canvas.LockAspect = 0f;
                        _canvas.Selection = Rectangle.Empty;
                        _canvas.DragEnabled = true;
                    }
                    // 交互式变换框：把叠加绘制与悬停光标交给当前操作。
                    Action<Graphics> activeOverlay = null;
                    _canvas.CursorProvider = null;
                    if (_ops[_active].WantsTransformBox)
                    {
                        activeOverlay = delegate(Graphics g)
                        {
                            if (_active >= 0 && _active < _ops.Length) { _ops[_active].PaintCanvasOverlay(g, LayerToClient); }
                        };
                        _canvas.CursorProvider = delegate(Point disp)
                        {
                            if (_active < 0 || _active >= _ops.Length) { return null; }
                            return _ops[_active].TransformCursor(ToLayer(ToSession(disp)));
                        };
                    }
                    // 选区跨功能保留：当前操作不是选区时，由编辑器补画选区轮廓（蚂蚁线）。
                    Action<Graphics> overlay = activeOverlay;
                    _canvas.OverlayPainter = delegate(Graphics g)
                    {
                        if (overlay != null) { overlay(g); }
                        if (_selectionOp != null && _active >= 0 && _active < _ops.Length &&
                            _ops[_active] != _selectionOp && _session.HasImage)
                        {
                            _selectionOp.PaintSelection(g, LayerToClient, _session.Width, _session.Height);
                        }
                    };
                    _previewTimer.Stop();
                    _liveTimer.Stop();
                    _livePending = false;
                    _composeValid = false;
                    ComputePreview();
                }
                else
                {
                    _canvas.OverlayPainter = null;
                    _canvas.CursorProvider = null;
                    _canvas.SetImage(null);
                }
                UpdateButtons();
            }
            finally
            {
                _suppressDirty = false;
            }
        }

        // 预览长边 = 画布可显示范围（适应窗口时长边不超过画布），限制在 [1000, 2000]，
        // 这样适应窗口/1× 显示时预览不会被放大，画质接近原图查看器。
        private void UpdatePreviewSize()
        {
            int w = _canvas.ClientSize.Width;
            int h = _canvas.ClientSize.Height;
            int s = Math.Max(w, h);
            if (s < 1000) { s = 1000; }
            if (s > 2000) { s = 2000; }
            _previewSize = s;
        }

        private void RefreshPreviewSource()
        {
            if (_ownPreviewSource && _previewSource != null)
            {
                _previewSource.Dispose();
            }
            _previewSource = null;
            _ownPreviewSource = false;
            if (_ownOpSource && _opSource != null)
            {
                _opSource.Dispose();
            }
            _opSource = null;
            _ownOpSource = false;
            if (!_session.HasImage)
            {
                return;
            }

            UpdatePreviewSize();

            if (_ops[_active].DocumentLevel)
            {
                Bitmap full = _session.Composite();
                _opSource = full;
                _ownOpSource = full != null;
                if (full == null)
                {
                    return;
                }
                _previewSource = ImageUtil.CreatePreview(full, _previewSize);
                _ownPreviewSource = _previewSource != null;
                if (!_ownPreviewSource)
                {
                    _previewSource = full;
                }
            }
            else
            {
                EditLayer layer = _session.ActiveLayer;
                _opSource = (layer == null) ? null : layer.Image;
                if (_opSource == null)
                {
                    return;
                }
                // 借用会话里按图层缓存的缩小图，避免每次刷新都重复缩放整图。
                _previewSource = _session.PreviewOf(layer, _previewSize);
                _ownPreviewSource = false;
            }
        }

        private void OnOpPreviewInvalidated(EditOpPanel op)
        {
            if (!_suppressDirty && _active >= 0 && _active < _ops.Length && op == _ops[_active])
            {
                _opDirty = true;
            }
            SchedulePreview();
        }

        private void SchedulePreview()
        {
            _previewTimer.Stop();
            if (_active >= 0 && _active < _ops.Length && _ops[_active].LivePreview)
            {
                if (_ops[_active].ImmediatePreview)
                {
                    _liveTimer.Stop();
                    _livePending = false;
                    ComputePreview();
                    return;
                }
                // 节流（不是防抖）：计时器已在跑时忽略新的失效，但到期总会用最新状态重算一次，
                // 因此连续拖动不会因为不断重置计时器而迟迟不刷新。
                if (!_livePending)
                {
                    _livePending = true;
                    _liveTimer.Start();
                }
                return;
            }
            _previewTimer.Start();
        }

        // 蚂蚁线动画：仅在存在选区时推进相位并重绘画布。
        private void OnAntsTick()
        {
            if (_canvas == null || _selectionOp == null || !_selectionOp.HasSelection || !_session.HasImage)
            {
                return;
            }
            _selectionOp.AdvanceAnts();
            _canvas.Invalidate();
        }

        private void ComputePreview()
        {
            if (_active < 0 || _active >= _ops.Length || !_session.HasImage)
            {
                return;
            }

            bool live = _ops[_active].LivePreview;
            Cursor previous = this.Cursor;
            if (!live)
            {
                this.Cursor = Cursors.WaitCursor;
            }
            try
            {
                Bitmap opPreview = _ops[_active].RenderPreview();
                Bitmap display;
                bool ownDisplay;
                bool documentLevel = _ops[_active].DocumentLevel;

                // 图层锁：让预览也遵守锁定，做到「看到什么就应用什么」。
                //   锁定图像像素 -> 不显示该操作的改动（直接看原样/整图合成）；
                //   锁定透明像素 -> 预览 alpha 采用原图层 alpha（透明处保持透明）。
                EditLayer act = _session.ActiveLayer;
                if (!documentLevel && act != null && opPreview != null)
                {
                    if (act.LockImage)
                    {
                        if (!_ops[_active].ReusablePreview) { opPreview.Dispose(); }
                        opPreview = null;
                    }
                    else if (act.LockTransparent && _previewSource != null &&
                        opPreview.Width == _previewSource.Width && opPreview.Height == _previewSource.Height)
                    {
                        EditSession.ApplyAlphaLock(opPreview, _previewSource);
                    }
                }

                // 选区约束：这些操作只在选区内生效（选区外用原图层像素）。
                if (!documentLevel && _ops[_active].RespectsSelection && opPreview != null && _previewSource != null &&
                    _selectionOp != null && opPreview.Width == _previewSource.Width && opPreview.Height == _previewSource.Height)
                {
                    byte[] selMask = _selectionOp.PreviewMask(opPreview.Width, opPreview.Height);
                    if (selMask != null) { ImageSelection.BlendMasked(opPreview, _previewSource, selMask); }
                }

                bool disposeOp = opPreview != null && !_ops[_active].ReusablePreview;
                if (documentLevel || !_ops[_active].CanApply)
                {
                    if (opPreview != null)
                    {
                        display = opPreview;
                        ownDisplay = disposeOp;
                    }
                    else if (documentLevel)
                    {
                        display = _previewSource;
                        ownDisplay = false;
                    }
                    else
                    {
                        display = FlatCompositePreview(out ownDisplay);
                    }
                }
                else if (_session.IsSoloNormalActive)
                {
                    display = (opPreview != null) ? opPreview : _previewSource;
                    ownDisplay = disposeOp;
                }
                else if (opPreview == null || !_ops[_active].HasPendingResult)
                {
                    // 该操作当前没有实际像素改动（例如变换处在 identity、调色各参数为默认）。
                    // 画面就是整图合成：直接显示「整图按真实图层顺序合成后再缩小」，与盖印/导出
                    // 完全一致；若走逐图层缩小再叠加，含透明图层（如贴入到新图层的内容）的边缘
                    // 会混出一圈白边。
                    if (opPreview != null && disposeOp)
                    {
                        opPreview.Dispose();
                    }
                    display = FlatCompositePreview(out ownDisplay);
                }
                else
                {
                    Bitmap activePreview = opPreview;
                    if (_session.CanReuseComposite(_session.ActiveIndex))
                    {
                        display = ComposeLayerPreview(activePreview);
                        ownDisplay = false;
                    }
                    else
                    {
                        // 当前图层上方有混合模式/半透明的图层：无法预合并，按真实堆叠顺序整图合成。
                        display = _session.CompositePreview(_session.ActiveIndex, activePreview, _previewSize);
                        ownDisplay = true;
                        _composeValid = false;
                    }
                    if (disposeOp)
                    {
                        opPreview.Dispose();
                    }
                }

                if (display == null)
                {
                    display = _previewSource;
                    ownDisplay = false;
                }
                // 先让画布切换到新图，再释放旧的显示图，避免画布短暂持有已释放的位图。
                Bitmap oldShown = _shownPreview;
                _shownPreview = ownDisplay ? display : null;
                SetDisplay(display);
                if (oldShown != null && !object.ReferenceEquals(oldShown, display))
                {
                    oldShown.Dispose();
                }
                UpdateBrushCursor();
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

        // 整图按真实图层顺序合成后缩小。用于「操作当前不修改像素」的预览（查看类操作、
        // 变换在 identity 等）：结果与盖印可见图层 / 导出图片逐像素一致，含透明图层的边缘
        // 不会像逐图层缩小再叠加那样和背景混出白边。own 表示返回的位图由调用方释放。
        private Bitmap FlatCompositePreview(out bool own)
        {
            own = true;
            Bitmap flatComposite = _session.Composite();
            Bitmap scaled = ImageUtil.CreatePreview(flatComposite, _previewSize);
            if (scaled == null)
            {
                return flatComposite;
            }
            flatComposite.Dispose();
            return scaled;
        }

        // 多图层预览：把当前图层内容合成到复用的缓冲上（背景层已缓存）。
        // 当前操作能报告「只有某块脏区变化」时只重合成该块，实时绘制每帧只处理笔触范围。
        private Bitmap ComposeLayerPreview(Bitmap activePreview)
        {
            int w = activePreview.Width, h = activePreview.Height;
            if (_composeBuffer == null || _composeBuffer.Width != w || _composeBuffer.Height != h)
            {
                if (_composeBuffer != null) { _composeBuffer.Dispose(); }
                _composeBuffer = new Bitmap(w, h);
                _composeValid = false;
            }
            Rectangle dirty;
            bool opDirty = _ops[_active].TryGetPreviewDirtyRect(out dirty);
            bool whole = !_composeValid || !opDirty;
            if (!whole && dirty.IsEmpty)
            {
                return _composeBuffer;   // 本帧当前图层内容没有变化
            }
            _session.CompositePreviewOver(_composeBuffer, _session.ActiveIndex, activePreview, _previewSize, dirty, whole);
            _composeValid = true;
            return _composeBuffer;
        }

        private void OnLayersChanged()
        {
            _previewTimer.Stop();
            _liveTimer.Stop();
            _livePending = false;
            ReloadAll();
        }

        private void SetDisplay(Bitmap bmp)
        {
            _displayImage = bmp;
            _canvas.SetImage(bmp, true);
        }

        private void UpdateBrushCursor()
        {
            if (_displayImage == null || !_session.HasImage)
            {
                return;
            }
            int radius = _ops[_active].BrushRadiusSession;
            float scale = (float)_displayImage.Width / _session.Width;
            _canvas.BrushRadius = Math.Max(1, (int)Math.Round(radius * scale));
        }

        private float DisplayScale()
        {
            if (_displayImage == null || !_session.HasImage)
            {
                return 1f;
            }
            return (float)_session.Width / _displayImage.Width;
        }

        private Point ToSession(Point p)
        {
            float s = DisplayScale();
            return new Point((int)Math.Round(p.X * s), (int)Math.Round(p.Y * s));
        }

        private Rectangle ToSession(Rectangle r)
        {
            float s = DisplayScale();
            return new Rectangle(
                (int)Math.Round(r.X * s), (int)Math.Round(r.Y * s),
                (int)Math.Round(r.Width * s), (int)Math.Round(r.Height * s));
        }

        private Point ToLayer(Point sessionPoint)
        {
            if (_active < 0 || _ops[_active].DocumentLevel) { return sessionPoint; }
            EditLayer layer = _session.ActiveLayer;
            if (layer == null) { return sessionPoint; }
            return new Point(sessionPoint.X - layer.Offset.X, sessionPoint.Y - layer.Offset.Y);
        }

        // 图层坐标 -> 画布客户区坐标（供变换框叠加绘制）。
        private PointF LayerToClient(PointF layerPoint)
        {
            float s = 1f / DisplayScale();
            float ox = 0f, oy = 0f;
            if (_active >= 0 && _active < _ops.Length && !_ops[_active].DocumentLevel)
            {
                EditLayer layer = _session.ActiveLayer;
                if (layer != null) { ox = layer.Offset.X; oy = layer.Offset.Y; }
            }
            return _canvas.ImageToClient(new PointF((layerPoint.X + ox) * s, (layerPoint.Y + oy) * s));
        }

        private void DispatchClick(Point p)
        {
            if (_active >= 0) { _ops[_active].OnCanvasClick(ToLayer(ToSession(p))); }
        }

        private void DispatchSelection()
        {
            if (_active < 0) { return; }
            Rectangle r = ToSession(_canvas.Selection);
            if (!_ops[_active].DocumentLevel)
            {
                EditLayer layer = _session.ActiveLayer;
                if (layer != null)
                {
                    r = new Rectangle(r.X - layer.Offset.X, r.Y - layer.Offset.Y, r.Width, r.Height);
                }
            }
            _ops[_active].OnCanvasSelection(r);
        }

        private void DispatchBrush(Point p, int action)
        {
            if (_active >= 0) { _ops[_active].OnBrushPoint(action == 2 ? Point.Empty : ToLayer(ToSession(p)), action); }
        }

        // 画布拖动：派发给需要拖动的当前操作（如“变换”），坐标为图层空间。
        private void DispatchDrag(Point p, int action)
        {
            if (_active < 0 || _active >= _ops.Length) { return; }
            if (!_ops[_active].WantsCanvasDrag) { return; }
            _ops[_active].OnCanvasDrag(action == 2 ? Point.Empty : ToLayer(ToSession(p)), action);
        }

        // 鼠标悬停 / 双击：按图层空间派发给当前操作（如多边形套索）。
        private void DispatchHover(Point p)
        {
            if (_active >= 0 && _active < _ops.Length) { _ops[_active].OnCanvasHover(ToLayer(ToSession(p))); }
        }

        private void DispatchDoubleClick(Point p)
        {
            if (_active >= 0 && _active < _ops.Length) { _ops[_active].OnCanvasDoubleClick(ToLayer(ToSession(p))); }
        }

        private bool ApplyActive()
        {
            if (_active < 0 || _active >= _ops.Length || !_session.HasImage)
            {
                return false;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                // 历史名称 / 新图层标志都要在 BuildResult 之前取（BuildResult 会清掉操作内部的一次性状态）。
                string label = _ops[_active].HistoryLabel;
                if (string.IsNullOrEmpty(label))
                {
                    label = (_active < _list.Items.Count) ? _list.Items[_active].ToString() : "操作";
                }
                bool newLayer = _ops[_active].ResultIsNewLayer;
                // 锁定图像像素：不改当前图层的像素，直接拦截（文档级/新建图层不受影响）。
                if (!_ops[_active].DocumentLevel && !newLayer)
                {
                    EditLayer locked = _session.ActiveLayer;
                    if (locked != null && locked.LockImage)
                    {
                        _status.Text = "当前图层已锁定图像像素，无法应用像素修改";
                        return false;
                    }
                }
                Bitmap result = _ops[_active].BuildResult();
                if (result == null)
                {
                    _status.Text = "当前操作没有可应用的结果（如未取样/未框选，或为查看类）";
                    return false;
                }
                // 选区约束：只在选区内生效，选区外保持原图层像素。
                if (!_ops[_active].DocumentLevel && !newLayer && _ops[_active].RespectsSelection && _selectionOp != null)
                {
                    EditLayer layer = _session.ActiveLayer;
                    byte[] selMask = _selectionOp.EffectiveMask();
                    if (layer != null && layer.Image != null && selMask != null)
                    {
                        ImageSelection.BlendMasked(result, layer.Image, selMask);
                    }
                }
                if (_ops[_active].DocumentLevel)
                {
                    _session.CommitDocument(result, label);
                }
                else if (newLayer)
                {
                    _session.AddImageLayer(result, "选区复制", label);
                }
                else
                {
                    _session.CommitToActive(result, label);
                }
                _ops[_active].ResetState();
                _opDirty = false;
                ReloadAll();
                _layerPanel.Sync();
                _status.Text = "已应用  （当前 " + _session.Width + "x" + _session.Height + "）";
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "应用失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private void SaveImage()
        {
            if (!_session.HasImage)
            {
                _status.Text = "请先打开图片";
                return;
            }

            // 有未应用的操作时先询问（是=应用后保存 / 否=不应用直接保存 / 取消=不保存）。
            if (!ResolvePendingEdits())
            {
                return;
            }

            string dir = string.IsNullOrEmpty(_sourcePath) ? null : Path.GetDirectoryName(_sourcePath);
            string name = string.IsNullOrEmpty(_sourcePath)
                ? "编辑结果.png"
                : Path.GetFileNameWithoutExtension(_sourcePath) + "_编辑.png";

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
                using (Bitmap flat = _session.Composite())
                {
                    ImageUtil.SavePng(flat, dialog.FileName);
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

        private void UpdateButtons()
        {
            bool has = _session.HasImage;
            _saveButton.Enabled = has;
            _undoButton.Enabled = has && _session.CanUndo;
            _redoButton.Enabled = has && _session.CanRedo;
            _resetButton.Enabled = has && _session.CanUndo;
            bool canApply = has && _active >= 0 && _active < _ops.Length && _ops[_active].CanApply;
            _applyButton.Enabled = canApply;
        }
    }

    // 新建图片对话框：输入宽高并选择背景（白色 / 透明）。
    internal class NewImageDialog : Form
    {
        private NumericUpDown _width;
        private NumericUpDown _height;
        private ComboBox _bg;

        public int ImageWidth { get { return (int)_width.Value; } }
        public int ImageHeight { get { return (int)_height.Value; } }
        public Color Background
        {
            get { return (_bg.SelectedIndex == 1) ? Color.Transparent : Color.White; }
        }

        public NewImageDialog()
        {
            // 顶层对话框，自行按 DPI 缩放（与 MainForm 同样的手动缩放）。
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9F);
            Text = "新建图片";
            ClientSize = new Size(260, 140);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            Label wl = new Label();
            wl.Text = "宽度 (px)";
            wl.Location = new Point(16, 20);
            wl.AutoSize = true;
            Controls.Add(wl);

            _width = new NumericUpDown();
            _width.Location = new Point(86, 16);
            _width.Size = new Size(130, 22);
            _width.Minimum = 1;
            _width.Maximum = 20000;
            _width.Value = 1920;
            Controls.Add(_width);

            Label hl = new Label();
            hl.Text = "高度 (px)";
            hl.Location = new Point(16, 50);
            hl.AutoSize = true;
            Controls.Add(hl);

            _height = new NumericUpDown();
            _height.Location = new Point(86, 46);
            _height.Size = new Size(130, 22);
            _height.Minimum = 1;
            _height.Maximum = 20000;
            _height.Value = 1080;
            Controls.Add(_height);

            Label bl = new Label();
            bl.Text = "背景";
            bl.Location = new Point(16, 80);
            bl.AutoSize = true;
            Controls.Add(bl);

            _bg = new ComboBox();
            _bg.DropDownStyle = ComboBoxStyle.DropDownList;
            _bg.Location = new Point(86, 76);
            _bg.Size = new Size(130, 22);
            _bg.Items.Add("白色");
            _bg.Items.Add("透明");
            _bg.SelectedIndex = 0;
            Controls.Add(_bg);

            Button ok = new Button();
            ok.Text = "确定";
            ok.Location = new Point(86, 104);
            ok.Size = new Size(72, 22);
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(164, 104);
            cancel.Size = new Size(72, 22);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            DpiScaler.Apply(this, true);
        }
    }
}
