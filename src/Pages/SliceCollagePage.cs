using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class SliceCollagePage : ToolPage
    {
        private TabControl _tabs;

        private Bitmap _sliceSource;
        private Bitmap _slicePreview;
        private TextBox _sliceImageBox;
        private NumericUpDown _sliceRows;
        private NumericUpDown _sliceCols;
        private TextBox _sliceOutBox;
        private ImageCanvas _sliceCanvas;
        private Label _sliceStatus;

        private ListBox _collageList;
        private ComboBox _collageDirection;
        private NumericUpDown _collageColumns;
        private NumericUpDown _collageSpacing;
        private NumericUpDown _collageMargin;
        private NumericUpDown _collageCellWidth;
        private NumericUpDown _collageCellHeight;
        private Button _collageBackgroundButton;
        private Color _collageBackground = Color.White;
        private CheckBox _collageFill;
        private NumericUpDown _collageLongEdge;
        private ImageCanvas _collageCanvas;
        private Bitmap _collagePreview;
        private Label _collageStatus;

        public SliceCollagePage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1020, 700);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "切图拼图"; }
        }

        public override void Shutdown()
        {
            if (_slicePreview != null)
            {
                _slicePreview.Dispose();
            }
            if (_sliceSource != null)
            {
                _sliceSource.Dispose();
            }
            if (_collagePreview != null)
            {
                _collagePreview.Dispose();
            }
        }

        private void BuildUi()
        {
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.Size = new Size(1012, 668);
            Controls.Add(_tabs);

            TabPage sliceTab = new TabPage("九宫格切图");
            sliceTab.UseVisualStyleBackColor = true;
            _tabs.TabPages.Add(sliceTab);
            BuildSliceTab(sliceTab);

            TabPage collageTab = new TabPage("拼图 / 长图");
            collageTab.UseVisualStyleBackColor = true;
            _tabs.TabPages.Add(collageTab);
            BuildCollageTab(collageTab);
        }

        private static TableLayoutPanel MakeTabLayout(Control parent, ImageCanvas canvas)
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.RowCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 344f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            parent.Controls.Add(layout);

            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.AutoScroll = true;
            layout.Controls.Add(left, 0, 0);

            canvas.Dock = DockStyle.Fill;
            canvas.Margin = new Padding(4);
            layout.Controls.Add(canvas, 1, 0);
            return layout;
        }

        private void BuildSliceTab(Control parent)
        {
            _sliceCanvas = new ImageCanvas();
            _sliceCanvas.ReadOnly = true;
            TableLayoutPanel layout = MakeTabLayout(parent, _sliceCanvas);
            Control left = layout.GetControlFromPosition(0, 0);

            Button browse = new Button();
            browse.Text = "浏览...";
            browse.Location = new Point(240, 8);
            browse.Size = new Size(90, 28);
            browse.Click += delegate { BrowseSliceImage(); };
            left.Controls.Add(browse);

            _sliceImageBox = new TextBox();
            _sliceImageBox.Location = new Point(10, 42);
            _sliceImageBox.Size = new Size(320, 25);
            _sliceImageBox.ReadOnly = true;
            left.Controls.Add(_sliceImageBox);

            GroupBox group = new GroupBox();
            group.Text = "切分设置";
            group.Location = new Point(10, 80);
            group.Size = new Size(320, 200);
            left.Controls.Add(group);

            AddLabel(group, "行数", 10, 30);
            _sliceRows = MakeNumeric(group, 80, 26, 70, 1, 100, 3);
            _sliceRows.ValueChanged += delegate { UpdateSlicePreview(); };

            AddLabel(group, "列数", 170, 30);
            _sliceCols = MakeNumeric(group, 240, 26, 70, 1, 100, 3);
            _sliceCols.ValueChanged += delegate { UpdateSlicePreview(); };

            AddLabel(group, "输出目录", 10, 64);
            _sliceOutBox = new TextBox();
            _sliceOutBox.Location = new Point(10, 84);
            _sliceOutBox.Size = new Size(220, 25);
            group.Controls.Add(_sliceOutBox);

            Button outBrowse = new Button();
            outBrowse.Text = "浏览...";
            outBrowse.Location = new Point(240, 83);
            outBrowse.Size = new Size(70, 27);
            outBrowse.Click += delegate { ChooseSliceOutDir(); };
            group.Controls.Add(outBrowse);

            Label hint = new Label();
            hint.Text = "留空则保存到源图所在目录，命名：原名_r行_c列.png";
            hint.Location = new Point(10, 116);
            hint.AutoSize = true;
            group.Controls.Add(hint);

            Button start = new Button();
            start.Text = "开始切图";
            start.Location = new Point(10, 150);
            start.Size = new Size(300, 34);
            start.Click += delegate { StartSlice(); };
            group.Controls.Add(start);

            _sliceStatus = new Label();
            _sliceStatus.Text = "请选择要切分的图片";
            _sliceStatus.Location = new Point(10, 300);
            _sliceStatus.Size = new Size(330, 60);
            left.Controls.Add(_sliceStatus);
        }

        private void BuildCollageTab(Control parent)
        {
            _collageCanvas = new ImageCanvas();
            _collageCanvas.ReadOnly = true;
            TableLayoutPanel layout = MakeTabLayout(parent, _collageCanvas);
            Control left = layout.GetControlFromPosition(0, 0);

            Label listLabel = new Label();
            listLabel.Text = "图片列表（按顺序拼接）";
            listLabel.Location = new Point(10, 10);
            listLabel.AutoSize = true;
            left.Controls.Add(listLabel);

            _collageList = new ListBox();
            _collageList.Location = new Point(10, 34);
            _collageList.Size = new Size(320, 210);
            _collageList.SelectionMode = SelectionMode.One;
            _collageList.AllowDrop = true;
            _collageList.DragEnter += CollageListDragEnter;
            _collageList.DragDrop += CollageListDragDrop;
            left.Controls.Add(_collageList);

            Button add = new Button();
            add.Text = "添加";
            add.Location = new Point(10, 250);
            add.Size = new Size(74, 28);
            add.Click += delegate { AddCollageImages(); };
            left.Controls.Add(add);

            Button remove = new Button();
            remove.Text = "移除";
            remove.Location = new Point(89, 250);
            remove.Size = new Size(74, 28);
            remove.Click += delegate { RemoveCollageImage(); };
            left.Controls.Add(remove);

            Button up = new Button();
            up.Text = "上移";
            up.Location = new Point(168, 250);
            up.Size = new Size(74, 28);
            up.Click += delegate { MoveCollageImage(-1); };
            left.Controls.Add(up);

            Button down = new Button();
            down.Text = "下移";
            down.Location = new Point(247, 250);
            down.Size = new Size(83, 28);
            down.Click += delegate { MoveCollageImage(1); };
            left.Controls.Add(down);

            GroupBox group = new GroupBox();
            group.Text = "拼图设置";
            group.Location = new Point(10, 286);
            group.Size = new Size(320, 270);
            left.Controls.Add(group);

            AddLabel(group, "方向", 10, 28);
            _collageDirection = new ComboBox();
            _collageDirection.DropDownStyle = ComboBoxStyle.DropDownList;
            _collageDirection.Location = new Point(70, 24);
            _collageDirection.Size = new Size(240, 25);
            _collageDirection.Items.Add("横向");
            _collageDirection.Items.Add("纵向");
            _collageDirection.Items.Add("网格");
            _collageDirection.SelectedIndex = 0;
            group.Controls.Add(_collageDirection);

            AddLabel(group, "列数", 10, 60);
            _collageColumns = MakeNumeric(group, 70, 56, 60, 1, 20, 2);

            AddLabel(group, "间距", 10, 92);
            _collageSpacing = MakeNumeric(group, 70, 88, 60, 0, 500, 8);
            AddLabel(group, "边距", 170, 92);
            _collageMargin = MakeNumeric(group, 230, 88, 60, 0, 500, 8);

            AddLabel(group, "格宽", 10, 124);
            _collageCellWidth = MakeNumeric(group, 70, 120, 60, 0, 20000, 0);
            AddLabel(group, "格高", 170, 124);
            _collageCellHeight = MakeNumeric(group, 230, 120, 60, 0, 20000, 0);

            _collageBackgroundButton = new Button();
            _collageBackgroundButton.Text = "背景色";
            _collageBackgroundButton.Location = new Point(10, 152);
            _collageBackgroundButton.Size = new Size(90, 26);
            _collageBackgroundButton.BackColor = _collageBackground;
            _collageBackgroundButton.Click += delegate { ChooseCollageBackground(); };
            group.Controls.Add(_collageBackgroundButton);

            _collageFill = new CheckBox();
            _collageFill.Text = "网格单元格裁剪填满";
            _collageFill.Location = new Point(110, 155);
            _collageFill.AutoSize = true;
            group.Controls.Add(_collageFill);

            AddLabel(group, "输出长边", 10, 194);
            _collageLongEdge = MakeNumeric(group, 80, 190, 80, 0, 100000, 0);
            Label leHint = new Label();
            leHint.Text = "0 = 不限制";
            leHint.Location = new Point(170, 194);
            leHint.AutoSize = true;
            group.Controls.Add(leHint);

            Button preview = new Button();
            preview.Text = "预览";
            preview.Location = new Point(10, 224);
            preview.Size = new Size(140, 32);
            preview.Click += delegate { PreviewCollage(); };
            group.Controls.Add(preview);

            Button save = new Button();
            save.Text = "保存为 PNG";
            save.Location = new Point(160, 224);
            save.Size = new Size(150, 32);
            save.Click += delegate { SaveCollage(); };
            group.Controls.Add(save);

            _collageStatus = new Label();
            _collageStatus.Text = "添加多张图片后可预览或保存";
            _collageStatus.Location = new Point(10, 566);
            _collageStatus.Size = new Size(330, 40);
            left.Controls.Add(_collageStatus);
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

        private void BrowseSliceImage()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择要切分的图片";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                Bitmap loaded = ImageUtil.LoadImage(dialog.FileName);
                if (_sliceSource != null)
                {
                    _sliceSource.Dispose();
                }
                _sliceSource = loaded;
                _sliceImageBox.Text = dialog.FileName;
                UpdateSlicePreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateSlicePreview()
        {
            if (_sliceSource == null)
            {
                return;
            }

            int rows = (int)_sliceRows.Value;
            int cols = (int)_sliceCols.Value;
            Bitmap preview = ImageLayout.ResizeLongEdge(_sliceSource, 1000);
            using (Graphics g = Graphics.FromImage(preview))
            using (Pen pen = new Pen(Color.FromArgb(0, 174, 255), 2f))
            {
                for (int c = 1; c < cols; c++)
                {
                    int x = (int)Math.Round((double)preview.Width * c / cols);
                    g.DrawLine(pen, x, 0, x, preview.Height);
                }
                for (int r = 1; r < rows; r++)
                {
                    int y = (int)Math.Round((double)preview.Height * r / rows);
                    g.DrawLine(pen, 0, y, preview.Width, y);
                }
            }

            Bitmap old = _slicePreview;
            _slicePreview = preview;
            _sliceCanvas.SetImage(_slicePreview);
            if (old != null)
            {
                old.Dispose();
            }

            _sliceStatus.Text = "将切分为 " + (rows * cols) + " 张（" + rows + " 行 × " + cols + " 列）";
        }

        private void ChooseSliceOutDir()
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "选择输出目录";
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _sliceOutBox.Text = dialog.SelectedPath;
            }
        }

        private void StartSlice()
        {
            if (_sliceSource == null)
            {
                MessageBox.Show(this, "请先选择要切分的图片", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string outDir = _sliceOutBox.Text.Trim();
            if (outDir.Length == 0)
            {
                outDir = Path.GetDirectoryName(Path.GetFullPath(_sliceImageBox.Text));
            }
            if (!Directory.Exists(outDir))
            {
                MessageBox.Show(this, "输出目录不存在", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int rows = (int)_sliceRows.Value;
            int cols = (int)_sliceCols.Value;
            string baseName = Path.GetFileNameWithoutExtension(_sliceImageBox.Text);

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            int ok = 0;
            List<string> errors = new List<string>();
            try
            {
                List<Bitmap> tiles = ImageLayout.Slice(_sliceSource, cols, rows);
                try
                {
                    int index = 0;
                    for (int r = 0; r < rows; r++)
                    {
                        for (int c = 0; c < cols; c++)
                        {
                            Bitmap tile = tiles[index++];
                            string target = ResolveTilePath(outDir, baseName, r + 1, c + 1);
                            try
                            {
                                ImageUtil.SavePng(tile, target);
                                ok++;
                            }
                            catch (Exception ex)
                            {
                                errors.Add(Path.GetFileName(target) + "：" + ex.Message);
                            }
                        }
                    }
                }
                finally
                {
                    for (int i = 0; i < tiles.Count; i++)
                    {
                        tiles[i].Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "切图失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                this.Cursor = previous;
            }

            _sliceStatus.Text = "完成：成功 " + ok + " 张，失败 " + errors.Count + " 张";
            if (errors.Count > 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, errors.ToArray()), "部分失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string ResolveTilePath(string dir, string baseName, int row, int col)
        {
            string name = baseName + "_r" + row + "_c" + col;
            string path = Path.Combine(dir, name + ".png");
            int n = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(dir, name + "_" + n + ".png");
                n++;
            }
            return path;
        }

        private void AddCollageImages()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择图片（可多选）";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            dialog.Multiselect = true;
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }
            AddCollagePaths(dialog.FileNames);
        }

        private void AddCollagePaths(string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                if (ImageBatch.IsSupported(paths[i]))
                {
                    _collageList.Items.Add(paths[i]);
                }
            }
            _collageStatus.Text = "共 " + _collageList.Items.Count + " 张图片";
        }

        private void RemoveCollageImage()
        {
            int index = _collageList.SelectedIndex;
            if (index >= 0)
            {
                _collageList.Items.RemoveAt(index);
            }
        }

        private void MoveCollageImage(int delta)
        {
            int index = _collageList.SelectedIndex;
            int target = index + delta;
            if (index < 0 || target < 0 || target >= _collageList.Items.Count)
            {
                return;
            }
            object item = _collageList.Items[index];
            _collageList.Items.RemoveAt(index);
            _collageList.Items.Insert(target, item);
            _collageList.SelectedIndex = target;
        }

        private void CollageListDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void CollageListDragDrop(object sender, DragEventArgs e)
        {
            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            AddCollagePaths(paths);
        }

        private void ChooseCollageBackground()
        {
            ColorDialog dialog = new ColorDialog();
            dialog.Color = _collageBackground;
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _collageBackground = dialog.Color;
                _collageBackgroundButton.BackColor = _collageBackground;
            }
        }

        private CollageOptions BuildCollageOptions()
        {
            CollageOptions options = new CollageOptions();
            options.Direction = (CollageDirection)_collageDirection.SelectedIndex;
            options.Columns = (int)_collageColumns.Value;
            options.Spacing = (int)_collageSpacing.Value;
            options.Margin = (int)_collageMargin.Value;
            options.CellWidth = (int)_collageCellWidth.Value;
            options.CellHeight = (int)_collageCellHeight.Value;
            options.Background = _collageBackground;
            options.Fill = _collageFill.Checked;
            options.LongEdge = (int)_collageLongEdge.Value;
            return options;
        }

        private Bitmap RenderCollage()
        {
            if (_collageList.Items.Count == 0)
            {
                MessageBox.Show(this, "请先添加图片", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            List<string> paths = new List<string>();
            foreach (object item in _collageList.Items)
            {
                paths.Add((string)item);
            }

            List<Bitmap> images = new List<Bitmap>();
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    images.Add(ImageUtil.LoadImage(paths[i]));
                }
                return ImageLayout.Collage(images, BuildCollageOptions());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "拼图失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            finally
            {
                for (int i = 0; i < images.Count; i++)
                {
                    images[i].Dispose();
                }
            }
        }

        private void PreviewCollage()
        {
            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap result = RenderCollage();
                if (result == null)
                {
                    return;
                }
                Bitmap old = _collagePreview;
                _collagePreview = result;
                _collageCanvas.SetImage(_collagePreview);
                if (old != null)
                {
                    old.Dispose();
                }
                _collageStatus.Text = "预览尺寸：" + _collagePreview.Width + " × " + _collagePreview.Height;
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private void SaveCollage()
        {
            if (_collageList.Items.Count == 0)
            {
                MessageBox.Show(this, "请先添加图片", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "保存拼图";
            dialog.Filter = "PNG 图片|*.png";
            dialog.FileName = "拼图.png";
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap result = RenderCollage();
                if (result == null)
                {
                    return;
                }
                try
                {
                    ImageUtil.SavePng(result, dialog.FileName);
                    _collageStatus.Text = "已保存：" + dialog.FileName;
                }
                finally
                {
                    result.Dispose();
                }
            }
            finally
            {
                this.Cursor = previous;
            }
        }
    }
}
