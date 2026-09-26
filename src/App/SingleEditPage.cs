using System;
using System.Drawing;
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
        private ImageCanvas _canvas;
        private Button _openButton;
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
        private Bitmap _shownPreview;
        private Bitmap _displayImage;
        private Bitmap _entrySnapshot;

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
                new BrushBlurOp(),
                new MattingOp(),
                new CropOp(),
                new CanvasOp(),
                new IdPhotoOp(),
                new SliceCollageOp(),
                new ColorMatchOp(),
                new LocalOverlayOp(),
                new LayerComposeOp(),
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

            _saveButton = MakeButton(toolbar, "保存为 PNG", 102, 100);
            _saveButton.Click += delegate { SaveImage(); };

            _applyButton = MakeButton(toolbar, "应用到图片", 208, 100);
            _applyButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _applyButton.Click += delegate { ApplyActive(); };

            _undoButton = MakeButton(toolbar, "撤销", 314, 62);
            _undoButton.Click += delegate { _session.Undo(); ReloadAll(); };

            _redoButton = MakeButton(toolbar, "重做", 380, 62);
            _redoButton.Click += delegate { _session.Redo(); ReloadAll(); };

            _resetButton = MakeButton(toolbar, "复位", 446, 62);
            _resetButton.Click += delegate { _session.ResetToOriginal(); ReloadAll(); };

            _status = new Label();
            _status.Location = new Point(520, 11);
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
            body.Controls.Add(_canvas, 1, 0);

            _opHost = new Panel();
            _opHost.Dock = DockStyle.Fill;
            _opHost.AutoScroll = true;
            _opHost.Margin = new Padding(3, 3, 3, 3);
            body.Controls.Add(_opHost, 2, 0);

            for (int i = 0; i < _ops.Length; i++)
            {
                EditOpPanel op = _ops[i];
                op.Visible = false;
                op.PreviewInvalidated += delegate { SchedulePreview(); };
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
                "风格预设", "特效", "画笔打码", "抠图", "裁剪 / 旋转", "画布 / 校正",
                "证件照", "切图拼图", "取色配色", "局部覆盖", "图层合成",
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
            _status.Text = "已载入：" + Path.GetFileName(_sourcePath) + "  （" + _session.Current.Width + "x" + _session.Current.Height + "）";
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
        }

        private void CaptureEntrySnapshot()
        {
            if (_entrySnapshot != null) { _entrySnapshot.Dispose(); _entrySnapshot = null; }
            if (_active >= 0 && _active < _ops.Length && _ops[_active].WantsEntrySnapshot && _session.HasImage)
            {
                _entrySnapshot = ImageFilters.Clone(_session.Current);
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
            _session.Commit(restore);
            ReloadAll();
            _status.Text = "已重置到进入该操作时的图片状态";
        }

        private void OnOpSelected()
        {
            int index = _list.SelectedIndex;
            if (index == _active)
            {
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
            if (_session.HasImage)
            {
                RefreshPreviewSource();
                for (int i = 0; i < _ops.Length; i++)
                {
                    _ops[i].Visible = (i == _active);
                }
                _canvas.ReadOnly = true;
                _canvas.BrushEnabled = false;
                _canvas.LockAspect = 0f;
                _canvas.Selection = Rectangle.Empty;
                _ops[_active].Attach(_session.Current, _previewSource, _canvas);
                _previewTimer.Stop();
                ComputePreview();
            }
            else
            {
                _canvas.SetImage(null);
            }
            UpdateButtons();
        }

        private void RefreshPreviewSource()
        {
            if (_ownPreviewSource && _previewSource != null)
            {
                _previewSource.Dispose();
            }
            _previewSource = ImageUtil.CreatePreview(_session.Current, PreviewSize);
            _ownPreviewSource = _previewSource != null;
            if (!_ownPreviewSource)
            {
                _previewSource = _session.Current;
            }
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
                Bitmap preview = _ops[_active].RenderPreview();
                if (_shownPreview != null)
                {
                    _shownPreview.Dispose();
                    _shownPreview = null;
                }
                if (preview != null)
                {
                    _shownPreview = preview;
                    SetDisplay(preview);
                }
                else
                {
                    SetDisplay(_session.Current);
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

        private void SetDisplay(Bitmap bmp)
        {
            _displayImage = bmp;
            _canvas.SetImage(bmp);
        }

        private void UpdateBrushCursor()
        {
            if (_displayImage == null || _session.Current == null)
            {
                return;
            }
            int radius = _ops[_active].BrushRadiusSession;
            float scale = (float)_displayImage.Width / _session.Current.Width;
            _canvas.BrushRadius = Math.Max(1, (int)Math.Round(radius * scale));
        }

        private float DisplayScale()
        {
            if (_displayImage == null || _session.Current == null)
            {
                return 1f;
            }
            return (float)_session.Current.Width / _displayImage.Width;
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

        private void DispatchClick(Point p)
        {
            if (_active >= 0) { _ops[_active].OnCanvasClick(ToSession(p)); }
        }

        private void DispatchSelection()
        {
            if (_active >= 0) { _ops[_active].OnCanvasSelection(ToSession(_canvas.Selection)); }
        }

        private void DispatchBrush(Point p, int action)
        {
            if (_active >= 0) { _ops[_active].OnBrushPoint(action == 2 ? Point.Empty : ToSession(p), action); }
        }

        private void ApplyActive()
        {
            if (_active < 0 || _active >= _ops.Length || !_session.HasImage)
            {
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap result = _ops[_active].BuildResult();
                if (result == null)
                {
                    _status.Text = "当前操作没有可应用的结果（如未取样/未框选，或为查看类）";
                    return;
                }
                _session.Commit(result);
                _ops[_active].ResetState();
                ReloadAll();
                _status.Text = "已应用  （当前 " + _session.Current.Width + "x" + _session.Current.Height + "）";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "应用失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                ImageUtil.SavePng(_session.Current, dialog.FileName);
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
}
