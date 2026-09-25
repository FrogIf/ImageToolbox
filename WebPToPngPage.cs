using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class WebPToPngPage : ToolPage
    {
        private const int ThumbSize = 96;
        private const string PlaceholderKey = "_placeholder";

        private ListView _listView;
        private ImageList _imageList;
        private TextBox _outBox;
        private CheckBox _overwriteBox;
        private CheckBox _recursiveBox;
        private ProgressBar _progress;
        private Label _status;
        private Button _convertButton;
        private BackgroundWorker _worker;
        private BackgroundWorker _thumbWorker;

        private readonly Dictionary<string, ListViewItem> _itemByPath = new Dictionary<string, ListViewItem>();
        private readonly List<string> _thumbQueue = new List<string>();
        private readonly HashSet<string> _thumbQueued = new HashSet<string>();
        private readonly HashSet<string> _watchedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _closing;
        private bool _suppressStatus;

        private string _currentOutDir = "";
        private bool _currentOverwrite;

        public WebPToPngPage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(720, 540);
            AllowDrop = true;

            BuildUi();

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
        }

        public override string ToolName
        {
            get { return "WebP 转 PNG"; }
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
            Button addFiles = MakeButton("添加文件", 10, 10, 90);
            addFiles.Click += delegate { AddFilesDialog(); };
            Controls.Add(addFiles);

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

            Button refresh = MakeButton("刷新", 555, 10, 70);
            refresh.Click += delegate { RefreshFromDirs(); };
            Controls.Add(refresh);

            _imageList = new ImageList();
            _imageList.ImageSize = new Size(ThumbSize, ThumbSize);
            _imageList.ColorDepth = ColorDepth.Depth32Bit;
            _imageList.Images.Add(PlaceholderKey, CreatePlaceholder());

            _listView = new ListView();
            _listView.Location = new Point(10, 48);
            _listView.Size = new Size(700, 292);
            _listView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
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

            GroupBox outGroup = new GroupBox();
            outGroup.Text = "输出目录（留空则保存到源文件所在目录）";
            outGroup.Location = new Point(10, 348);
            outGroup.Size = new Size(700, 56);
            outGroup.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(outGroup);

            _outBox = new TextBox();
            _outBox.Location = new Point(12, 22);
            _outBox.Size = new Size(486, 25);
            _outBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            outGroup.Controls.Add(_outBox);

            Button browse = new Button();
            browse.Text = "浏览...";
            browse.Location = new Point(504, 21);
            browse.Size = new Size(80, 26);
            browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browse.Click += delegate { ChooseOutDir(); };
            outGroup.Controls.Add(browse);

            Button useSource = new Button();
            useSource.Text = "使用源目录";
            useSource.Location = new Point(590, 21);
            useSource.Size = new Size(96, 26);
            useSource.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            useSource.Click += delegate { _outBox.Text = ""; };
            outGroup.Controls.Add(useSource);

            _overwriteBox = new CheckBox();
            _overwriteBox.Text = "覆盖同名文件";
            _overwriteBox.Location = new Point(10, 414);
            _overwriteBox.AutoSize = true;
            _overwriteBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(_overwriteBox);

            _recursiveBox = new CheckBox();
            _recursiveBox.Text = "添加文件夹时递归子目录";
            _recursiveBox.Location = new Point(150, 414);
            _recursiveBox.AutoSize = true;
            _recursiveBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _recursiveBox.Checked = true;
            Controls.Add(_recursiveBox);

            _progress = new ProgressBar();
            _progress.Location = new Point(10, 446);
            _progress.Size = new Size(700, 20);
            _progress.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_progress);

            _status = new Label();
            _status.Text = "就绪";
            _status.Location = new Point(10, 474);
            _status.Size = new Size(560, 22);
            _status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(_status);

            _convertButton = new Button();
            _convertButton.Text = "开始转换";
            _convertButton.Location = new Point(600, 472);
            _convertButton.Size = new Size(110, 30);
            _convertButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _convertButton.Click += delegate { StartConvert(); };
            Controls.Add(_convertButton);

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

        private Button MakeButton(string text, int x, int y, int width)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(width, 28);
            return b;
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

        private void AddFilesDialog()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择 WebP 文件";
            dialog.Filter = "WebP 图片|*.webp|所有文件|*.*";
            dialog.Multiselect = true;
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                AddFiles(dialog.FileNames);
            }
        }

        private void AddFolderDialog()
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "选择包含 WebP 文件的文件夹";
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                RememberDir(dialog.SelectedPath);
                List<string> found = FindWebPFiles(dialog.SelectedPath, _recursiveBox.Checked);
                if (found.Count == 0)
                {
                    MessageBox.Show(this, "该文件夹中未找到 .webp 文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                AddFiles(found);
            }
        }

        private static List<string> FindWebPFiles(string folder, bool recursive)
        {
            List<string> result = new List<string>();
            SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            try
            {
                foreach (string path in Directory.GetFiles(folder, "*.webp", option))
                {
                    result.Add(path);
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

                if (!full.ToLowerInvariant().EndsWith(".webp"))
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
                        AddFileEntries(FindWebPFiles(dir, _recursiveBox.Checked));
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

        private void StartConvert()
        {
            if (_worker.IsBusy)
            {
                return;
            }

            if (_listView.Items.Count == 0)
            {
                MessageBox.Show(this, "请先添加要转换的 WebP 文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                MessageBox.Show(this, "请至少勾选一张要转换的图片", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string outDir = _outBox.Text.Trim();
            if (outDir.Length > 0 && !Directory.Exists(outDir))
            {
                MessageBox.Show(this, "输出目录不存在", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _currentOutDir = outDir;
            _currentOverwrite = _overwriteBox.Checked;

            _progress.Minimum = 0;
            _progress.Maximum = files.Count;
            _progress.Value = 0;
            _convertButton.Enabled = false;
            _status.Text = "转换中...";

            _worker.RunWorkerAsync(files);
        }

        private void WorkerDoWork(object sender, DoWorkEventArgs e)
        {
            List<string> files = (List<string>)e.Argument;
            int ok = 0;
            int fail = 0;
            List<string> errors = new List<string>();

            for (int i = 0; i < files.Count; i++)
            {
                if (_worker.CancellationPending)
                {
                    e.Cancel = true;
                    break;
                }

                try
                {
                    WebPConverter.Convert(files[i], _currentOutDir, _currentOverwrite);
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    errors.Add(Path.GetFileName(files[i]) + "：" + ex.Message);
                }
                _worker.ReportProgress(i + 1);
            }

            e.Result = new ConvertResult(ok, fail, files.Count, errors);
        }

        private void WorkerProgress(object sender, ProgressChangedEventArgs e)
        {
            _progress.Value = e.ProgressPercentage;
        }

        private void WorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            _convertButton.Enabled = true;

            if (e.Error != null)
            {
                _status.Text = "转换失败：" + e.Error.Message;
                MessageBox.Show(this, e.Error.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (e.Cancelled)
            {
                _status.Text = "已取消";
                return;
            }

            ConvertResult result = (ConvertResult)e.Result;
            _status.Text = "完成：成功 " + result.Ok + " / " + result.Total + "，失败 " + result.Fail;

            string message = "成功 " + result.Ok + " 个，失败 " + result.Fail + " 个，共 " + result.Total + " 个。";
            if (result.Errors.Count > 0)
            {
                message += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, result.Errors.ToArray());
            }
            MessageBox.Show(this, message, "转换完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    collected.AddRange(FindWebPFiles(path, _recursiveBox.Checked));
                }
                else if (File.Exists(path) && path.ToLowerInvariant().EndsWith(".webp"))
                {
                    collected.Add(path);
                }
            }

            if (collected.Count == 0)
            {
                _status.Text = "未找到 .webp 文件";
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

    public class ConvertResult
    {
        public int Ok;
        public int Fail;
        public int Total;
        public List<string> Errors;

        public ConvertResult(int ok, int fail, int total, List<string> errors)
        {
            Ok = ok;
            Fail = fail;
            Total = total;
            Errors = errors;
        }
    }
}
