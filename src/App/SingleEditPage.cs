using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class SingleEditPage : ToolPage
    {
        private const int PreviewSize = 1000;

        private readonly EditSession _session = new EditSession();
        private readonly EditOpPanel[] _ops;
        private ListBox _list;
        private Panel _opHost;
        private LayerPanel _layerPanel;
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

        private int _active = -1;
        private string _sourcePath;
        private Bitmap _previewSource;
        private bool _ownPreviewSource;
        private Bitmap _opSource;
        private bool _ownOpSource;
        private Bitmap _shownPreview;
        private Bitmap _displayImage;
        private Bitmap _entrySnapshot;

        private bool _opDirty;
        private bool _suppressDirty;
        private bool _switching;

        public SingleEditPage()
        {
            _ops = new EditOpPanel[]
            {
                new BasicAdjustOp(),
                new LevelsOp(),
                new CurveOp(),
                new WhiteBalanceOp(),
                new HslOp(),
                new LocalMaskOp(),
                new ToningOp(),
                new LutOp(),
                new StyleOp(),
                new EffectsOp(),
                new DrawOp(),
                new TransformOp(),
                new MattingOp(),
                new CropOp(),
                new CanvasOp(),
                new IdPhotoOp(),
                new SliceCollageOp(),
                new ColorMatchOp(),
                new LocalOverlayOp(),
                new ColorToolOp(),
                new CompareOp(),
                new InfoOp()
            };

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1180, 720);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "单图编辑"; }
        }

        public override void Shutdown()
        {
            if (_canvas != null) { _canvas.SetImage(null); }
            _displayImage = null;
            for (int i = 0; i < _ops.Length; i++)
            {
                _ops[i].Detach();
                _ops[i].DisposeResources();
            }
            if (_shownPreview != null) { _shownPreview.Dispose(); _shownPreview = null; }
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
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
            _status.Location = new Point(582, 11);
            _status.Size = new Size(500, 22);
            _status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _status.Text = "打开一张图片，然后在左侧选择操作；调好后点「应用到图片」";
            toolbar.Controls.Add(_status);

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 3;
            body.RowCount = 1;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156f));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340f));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            body.Margin = new Padding(3, 3, 3, 3);
            root.Controls.Add(body, 0, 1);

            _list = new ListBox();
            _list.Dock = DockStyle.Fill;
            _list.IntegralHeight = false;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.ItemHeight = 26;
            _list.Margin = new Padding(3, 3, 3, 3);
            _list.SelectedIndexChanged += delegate { OnOpSelected(); };
            body.Controls.Add(_list, 0, 0);

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
            _layerPanel.Dock = DockStyle.Bottom;
            _layerPanel.Height = 318;
            _layerPanel.MinimumSize = new Size(0, 220);
            _layerPanel.LayersChanged += delegate { OnLayersChanged(); };
            _layerPanel.PropsChanged += delegate { SchedulePreview(); };
            right.Controls.Add(_layerPanel);
            _layerPanel.Bind(_session);

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
                _opHost.Controls.Add(op);
            }

            string[] names =
            {
                "基础调整", "色阶", "曲线", "白平衡", "HSL", "局部调整", "色调", "LUT",
                "风格预设", "特效", "绘画标注", "变换", "抠图", "裁剪 / 旋转", "画布 / 校正",
                "证件照", "切图拼图", "取色配色", "局部覆盖",
                "颜色工具", "图像对比", "图片信息"
            };
            for (int i = 0; i < names.Length; i++)
            {
                _list.Items.Add(names[i]);
            }

            _previewTimer = new Timer();
            _previewTimer.Interval = 120;
            _previewTimer.Tick += delegate { _previewTimer.Stop(); ComputePreview(); };

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
            button.Location = new Point(x, 6);
            button.Size = new Size(width, 28);
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
                _session.SetOriginal(loaded);
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
                _session.SetOriginal(blank);
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
            _session.CommitDocument(restore);
            ReloadAll();
            _layerPanel.Sync();
            _status.Text = "已重置到进入该操作时的图片状态";
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
            if (_active >= 0 && _active < _ops.Length && _opDirty && _ops[_active].CanApply)
            {
                string name = (_active < _list.Items.Count) ? _list.Items[_active].ToString() : "当前操作";
                DialogResult answer = MessageBox.Show(this,
                    "「" + name + "」的结果还没有应用到图片。\r\n是否先应用到图片？",
                    "未应用的修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel)
                {
                    _switching = true;
                    _list.SelectedIndex = _active;
                    _switching = false;
                    return;
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
                    _previewTimer.Stop();
                    ComputePreview();
                }
                else
                {
                    _canvas.SetImage(null);
                }
                UpdateButtons();
            }
            finally
            {
                _suppressDirty = false;
            }
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

            if (_ops[_active].DocumentLevel)
            {
                Bitmap full = _session.Composite();
                _opSource = full;
                _ownOpSource = full != null;
                if (full == null)
                {
                    return;
                }
                _previewSource = ImageUtil.CreatePreview(full, PreviewSize);
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
                _previewSource = _session.PreviewOf(layer, PreviewSize);
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
                ComputePreview();
                return;
            }
            _previewTimer.Start();
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
                        display = _session.CompositePreview(_session.ActiveIndex, _previewSource, PreviewSize);
                        ownDisplay = true;
                    }
                }
                else if (_session.IsSoloNormalActive)
                {
                    display = (opPreview != null) ? opPreview : _previewSource;
                    ownDisplay = disposeOp;
                }
                else
                {
                    Bitmap activePreview = (opPreview != null) ? opPreview : _previewSource;
                    display = _session.CompositePreview(_session.ActiveIndex, activePreview, PreviewSize);
                    ownDisplay = true;
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

        private void OnLayersChanged()
        {
            _previewTimer.Stop();
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
                Bitmap result = _ops[_active].BuildResult();
                if (result == null)
                {
                    _status.Text = "当前操作没有可应用的结果（如未取样/未框选，或为查看类）";
                    return false;
                }
                if (_ops[_active].DocumentLevel)
                {
                    _session.CommitDocument(result);
                }
                else
                {
                    _session.CommitToActive(result);
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
            Text = "新建图片";
            ClientSize = new Size(300, 158);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            Label wl = new Label();
            wl.Text = "宽度 (px)";
            wl.Location = new Point(16, 22);
            wl.AutoSize = true;
            Controls.Add(wl);

            _width = new NumericUpDown();
            _width.Location = new Point(110, 18);
            _width.Size = new Size(160, 24);
            _width.Minimum = 1;
            _width.Maximum = 20000;
            _width.Value = 1920;
            Controls.Add(_width);

            Label hl = new Label();
            hl.Text = "高度 (px)";
            hl.Location = new Point(16, 56);
            hl.AutoSize = true;
            Controls.Add(hl);

            _height = new NumericUpDown();
            _height.Location = new Point(110, 52);
            _height.Size = new Size(160, 24);
            _height.Minimum = 1;
            _height.Maximum = 20000;
            _height.Value = 1080;
            Controls.Add(_height);

            Label bl = new Label();
            bl.Text = "背景";
            bl.Location = new Point(16, 90);
            bl.AutoSize = true;
            Controls.Add(bl);

            _bg = new ComboBox();
            _bg.DropDownStyle = ComboBoxStyle.DropDownList;
            _bg.Location = new Point(110, 86);
            _bg.Size = new Size(160, 24);
            _bg.Items.Add("白色");
            _bg.Items.Add("透明");
            _bg.SelectedIndex = 0;
            Controls.Add(_bg);

            Button ok = new Button();
            ok.Text = "确定";
            ok.Location = new Point(110, 120);
            ok.Size = new Size(76, 28);
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(194, 120);
            cancel.Size = new Size(76, 28);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
