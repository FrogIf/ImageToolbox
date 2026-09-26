using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class MultiSizeExportPage : ToolPage
    {
        private ListBox _files;
        private TextBox _outDirBox;
        private ComboBox _formatBox;
        private CheckBox _keepAspectBox;
        private CheckBox _icoBox;
        private CheckBox[] _sizeChecks;
        private int[] _sizeValues = { 16, 24, 32, 48, 64, 96, 128, 256, 512 };
        private ProgressBar _progress;
        private Label _status;

        public MultiSizeExportPage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1120, 680);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "批量导出"; }
        }

        public override void Shutdown()
        {
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
            Controls.Add(root);

            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.AutoScroll = true;
            left.Margin = new Padding(3, 3, 3, 3);
            root.Controls.Add(left, 0, 0);

            GroupBox fileGroup = new GroupBox();
            fileGroup.Text = "文件";
            fileGroup.Location = new Point(10, 8);
            fileGroup.Size = new Size(320, 92);
            left.Controls.Add(fileGroup);

            Button addFiles = new Button();
            addFiles.Text = "添加文件";
            addFiles.Location = new Point(10, 22);
            addFiles.Size = new Size(94, 28);
            addFiles.Click += delegate { AddFiles(); };
            fileGroup.Controls.Add(addFiles);

            Button addFolder = new Button();
            addFolder.Text = "添加文件夹";
            addFolder.Location = new Point(110, 22);
            addFolder.Size = new Size(104, 28);
            addFolder.Click += delegate { AddFolder(); };
            fileGroup.Controls.Add(addFolder);

            Button remove = new Button();
            remove.Text = "移除";
            remove.Location = new Point(220, 22);
            remove.Size = new Size(94, 28);
            remove.Click += delegate { RemoveSelected(); };
            fileGroup.Controls.Add(remove);

            Button clear = new Button();
            clear.Text = "清空列表";
            clear.Location = new Point(10, 56);
            clear.Size = new Size(94, 28);
            clear.Click += delegate { _files.Items.Clear(); UpdateStatus(); };
            fileGroup.Controls.Add(clear);

            Label fileHint = new Label();
            fileHint.Text = "支持 PNG/JPG/BMP/GIF/WebP/TIFF";
            fileHint.Location = new Point(112, 62);
            fileHint.AutoSize = true;
            fileHint.ForeColor = Color.FromArgb(90, 90, 90);
            fileGroup.Controls.Add(fileHint);

            GroupBox sizeGroup = new GroupBox();
            sizeGroup.Text = "导出尺寸（像素，按方框适配）";
            sizeGroup.Location = new Point(10, 108);
            sizeGroup.Size = new Size(320, 116);
            left.Controls.Add(sizeGroup);

            _sizeChecks = new CheckBox[_sizeValues.Length];
            for (int i = 0; i < _sizeValues.Length; i++)
            {
                CheckBox box = new CheckBox();
                box.Text = _sizeValues[i].ToString();
                box.AutoSize = true;
                box.Location = new Point(12 + (i % 3) * 100, 24 + (i / 3) * 28);
                box.Checked = _sizeValues[i] >= 48 && _sizeValues[i] <= 256;
                sizeGroup.Controls.Add(box);
                _sizeChecks[i] = box;
            }

            GroupBox optionGroup = new GroupBox();
            optionGroup.Text = "选项";
            optionGroup.Location = new Point(10, 232);
            optionGroup.Size = new Size(320, 92);
            left.Controls.Add(optionGroup);

            Label formatLabel = new Label();
            formatLabel.Text = "输出格式";
            formatLabel.Location = new Point(12, 24);
            formatLabel.AutoSize = true;
            optionGroup.Controls.Add(formatLabel);

            _formatBox = new ComboBox();
            _formatBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _formatBox.Location = new Point(92, 20);
            _formatBox.Size = new Size(100, 25);
            _formatBox.Items.Add("PNG");
            _formatBox.Items.Add("JPG");
            _formatBox.Items.Add("BMP");
            _formatBox.SelectedIndex = 0;
            optionGroup.Controls.Add(_formatBox);

            _icoBox = new CheckBox();
            _icoBox.Text = "同时生成 .ico 图标";
            _icoBox.Location = new Point(210, 22);
            _icoBox.AutoSize = true;
            _icoBox.Checked = true;
            optionGroup.Controls.Add(_icoBox);

            _keepAspectBox = new CheckBox();
            _keepAspectBox.Text = "保持宽高比（不勾选则拉伸为正方形）";
            _keepAspectBox.Location = new Point(12, 54);
            _keepAspectBox.AutoSize = true;
            _keepAspectBox.Checked = true;
            optionGroup.Controls.Add(_keepAspectBox);

            Label outLabel = new Label();
            outLabel.Text = "输出目录";
            outLabel.Location = new Point(10, 336);
            outLabel.AutoSize = true;
            left.Controls.Add(outLabel);

            _outDirBox = new TextBox();
            _outDirBox.Location = new Point(10, 356);
            _outDirBox.Size = new Size(230, 25);
            left.Controls.Add(_outDirBox);

            Button outBrowse = new Button();
            outBrowse.Text = "浏览...";
            outBrowse.Location = new Point(246, 355);
            outBrowse.Size = new Size(84, 27);
            outBrowse.Click += delegate { BrowseOutDir(); };
            left.Controls.Add(outBrowse);

            Label note = new Label();
            note.Location = new Point(10, 392);
            note.Size = new Size(320, 130);
            note.ForeColor = Color.FromArgb(70, 70, 70);
            note.Text =
                "说明：\r\n" +
                "• 每个文件按所选尺寸各导出一张，命名 原名_宽x高。\r\n" +
                "• 勾选“同时生成 .ico”会把所有所选尺寸打包成一个\r\n" +
                "  多尺寸图标 原名.ico（PNG 压缩，支持 Win7+）。\r\n" +
                "• 输出目录留空时，导出到原文件旁的“导出”子目录。";
            left.Controls.Add(note);

            _files = new ListBox();
            _files.Dock = DockStyle.Fill;
            _files.Margin = new Padding(3, 3, 3, 3);
            _files.SelectionMode = SelectionMode.MultiExtended;
            _files.AllowDrop = true;
            _files.DragEnter += delegate(object s, DragEventArgs e) { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            _files.DragDrop += delegate(object s, DragEventArgs e) { AddPaths((string[])e.Data.GetData(DataFormats.FileDrop)); };
            root.Controls.Add(_files, 1, 0);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Fill;
            bottom.Margin = new Padding(3, 0, 3, 3);
            root.Controls.Add(bottom, 0, 1);
            root.SetColumnSpan(bottom, 2);

            Button start = new Button();
            start.Text = "开始导出";
            start.Location = new Point(10, 8);
            start.Size = new Size(130, 34);
            start.Click += delegate { StartExport(); };
            bottom.Controls.Add(start);

            _progress = new ProgressBar();
            _progress.Location = new Point(150, 12);
            _progress.Size = new Size(360, 26);
            _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            bottom.Controls.Add(_progress);

            _status = new Label();
            _status.Text = "请添加文件，选择尺寸后点「开始导出」";
            _status.Location = new Point(522, 16);
            _status.Size = new Size(560, 22);
            _status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            bottom.Controls.Add(_status);
        }

        private void AddFiles()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择图片";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            dialog.Multiselect = true;
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                AddPaths(dialog.FileNames);
            }
        }

        private void AddFolder()
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "选择包含图片的文件夹";
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }
            string[] files = Directory.GetFiles(dialog.SelectedPath);
            AddPaths(files);
        }

        private void AddPaths(string[] paths)
        {
            if (paths == null)
            {
                return;
            }
            for (int i = 0; i < paths.Length; i++)
            {
                if (!ImageBatch.IsSupported(paths[i]))
                {
                    continue;
                }
                if (!_files.Items.Contains(paths[i]))
                {
                    _files.Items.Add(paths[i]);
                }
            }
            UpdateStatus();
        }

        private void RemoveSelected()
        {
            while (_files.SelectedIndices.Count > 0)
            {
                _files.Items.RemoveAt(_files.SelectedIndices[0]);
            }
            UpdateStatus();
        }

        private void BrowseOutDir()
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "选择输出目录";
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _outDirBox.Text = dialog.SelectedPath;
            }
        }

        private void UpdateStatus()
        {
            _status.Text = "已选 " + _files.Items.Count + " 个文件";
        }

        private List<int> SelectedSizes()
        {
            List<int> sizes = new List<int>();
            for (int i = 0; i < _sizeValues.Length; i++)
            {
                if (_sizeChecks[i].Checked)
                {
                    sizes.Add(_sizeValues[i]);
                }
            }
            return sizes;
        }

        private BatchOutputFormat SelectedFormat()
        {
            switch (_formatBox.SelectedIndex)
            {
                case 1:
                    return BatchOutputFormat.Jpeg;
                case 2:
                    return BatchOutputFormat.Bmp;
                default:
                    return BatchOutputFormat.Png;
            }
        }

        private void StartExport()
        {
            if (_files.Items.Count == 0)
            {
                _status.Text = "请先添加文件";
                return;
            }
            List<int> sizes = SelectedSizes();
            if (sizes.Count == 0)
            {
                _status.Text = "请至少选择一个尺寸";
                return;
            }

            string[] files = new string[_files.Items.Count];
            for (int i = 0; i < files.Length; i++)
            {
                files[i] = (string)_files.Items[i];
            }

            string outDir = _outDirBox.Text.Trim();
            bool keepAspect = _keepAspectBox.Checked;
            bool makeIco = _icoBox.Checked;
            BatchOutputFormat format = SelectedFormat();
            string ext = ImageBatch.FormatExtension(format);

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            _progress.Minimum = 0;
            _progress.Maximum = files.Length;
            _progress.Value = 0;

            int ok = 0;
            int fail = 0;
            try
            {
                for (int f = 0; f < files.Length; f++)
                {
                    _status.Text = "处理中：" + Path.GetFileName(files[f]) + "（" + (f + 1) + "/" + files.Length + "）";
                    Application.DoEvents();

                    Bitmap source = null;
                    List<Bitmap> icoImages = new List<Bitmap>();
                    try
                    {
                        source = ImageUtil.LoadImage(files[f]);
                        string dir = string.IsNullOrEmpty(outDir)
                            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(files[f])), "导出")
                            : outDir;
                        Directory.CreateDirectory(dir);
                        string baseName = Path.GetFileNameWithoutExtension(files[f]);

                        for (int s = 0; s < sizes.Count; s++)
                        {
                            Size size = MultiSizeExport.ComputeSize(source.Width, source.Height, sizes[s], sizes[s], keepAspect);
                            Bitmap resized = MultiSizeExport.ResizeTo(source, size.Width, size.Height);
                            string target = Path.Combine(dir, baseName + "_" + size.Width + "x" + size.Height + ext);
                            ImageBatch.Save(resized, target, format, 92, Color.White);
                            if (makeIco)
                            {
                                icoImages.Add(resized);
                            }
                            else
                            {
                                resized.Dispose();
                            }
                        }

                        if (makeIco && icoImages.Count > 0)
                        {
                            MultiSizeExport.WriteIco(icoImages, Path.Combine(dir, baseName + ".ico"));
                        }
                        ok++;
                    }
                    catch (Exception)
                    {
                        fail++;
                    }
                    finally
                    {
                        for (int i = 0; i < icoImages.Count; i++)
                        {
                            icoImages[i].Dispose();
                        }
                        if (source != null)
                        {
                            source.Dispose();
                        }
                    }

                    _progress.Value = f + 1;
                    Application.DoEvents();
                }

                _status.Text = "完成：成功 " + ok + " 个，失败 " + fail + " 个";
            }
            finally
            {
                this.Cursor = previous;
            }
        }
    }
}
