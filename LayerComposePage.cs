using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class LayerComposePage : ToolPage
    {
        private TextBox _baseBox;
        private ListBox _layerList;
        private ComboBox _modeBox;
        private TrackBar _opacityBar;
        private Label _opacityValue;
        private ImageCanvas _canvas;
        private Label _status;
        private Timer _debounce;
        private bool _syncing;

        private Bitmap _baseImage;
        private Bitmap _resultImage;
        private string _basePath;
        private List<Layer> _layers = new List<Layer>();

        private class Layer
        {
            public string Path = "";
            public Bitmap Image;
            public BlendMode Mode = BlendMode.Normal;
            public float Opacity = 1f;
        }

        public LayerComposePage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(1120, 680);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "图层合成"; }
        }

        public override void Shutdown()
        {
            if (_baseImage != null) { _baseImage.Dispose(); }
            if (_resultImage != null) { _resultImage.Dispose(); }
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].Image != null) { _layers[i].Image.Dispose(); }
            }
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
            Controls.Add(root);

            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.AutoScroll = true;
            left.Margin = new Padding(3, 3, 3, 3);
            root.Controls.Add(left, 0, 0);

            Label baseLabel = new Label();
            baseLabel.Text = "底图";
            baseLabel.Location = new Point(10, 14);
            baseLabel.AutoSize = true;
            left.Controls.Add(baseLabel);

            Button baseBrowse = new Button();
            baseBrowse.Text = "浏览...";
            baseBrowse.Location = new Point(220, 8);
            baseBrowse.Size = new Size(80, 28);
            baseBrowse.Click += delegate { BrowseBase(); };
            left.Controls.Add(baseBrowse);

            _baseBox = new TextBox();
            _baseBox.Location = new Point(10, 42);
            _baseBox.Size = new Size(290, 25);
            _baseBox.ReadOnly = true;
            left.Controls.Add(_baseBox);

            Label layerLabel = new Label();
            layerLabel.Text = "图层（自下而上叠加）";
            layerLabel.Location = new Point(10, 78);
            layerLabel.AutoSize = true;
            left.Controls.Add(layerLabel);

            _layerList = new ListBox();
            _layerList.Location = new Point(10, 100);
            _layerList.Size = new Size(290, 150);
            _layerList.IntegralHeight = false;
            _layerList.SelectedIndexChanged += delegate { OnLayerSelected(); };
            left.Controls.Add(_layerList);

            Button add = new Button();
            add.Text = "添加图层";
            add.Location = new Point(10, 258);
            add.Size = new Size(90, 30);
            add.Click += delegate { AddLayer(); };
            left.Controls.Add(add);

            Button remove = new Button();
            remove.Text = "移除";
            remove.Location = new Point(106, 258);
            remove.Size = new Size(90, 30);
            remove.Click += delegate { RemoveLayer(); };
            left.Controls.Add(remove);

            Button up = new Button();
            up.Text = "上移";
            up.Location = new Point(202, 258);
            up.Size = new Size(46, 30);
            up.Click += delegate { MoveLayer(-1); };
            left.Controls.Add(up);

            Button down = new Button();
            down.Text = "下移";
            down.Location = new Point(254, 258);
            down.Size = new Size(46, 30);
            down.Click += delegate { MoveLayer(1); };
            left.Controls.Add(down);

            Label modeLabel = new Label();
            modeLabel.Text = "混合模式";
            modeLabel.Location = new Point(10, 300);
            modeLabel.AutoSize = true;
            left.Controls.Add(modeLabel);

            _modeBox = new ComboBox();
            _modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _modeBox.Location = new Point(10, 320);
            _modeBox.Size = new Size(290, 25);
            for (int i = 0; i < ImageBlend.ModeNames.Length; i++)
            {
                _modeBox.Items.Add(ImageBlend.ModeNames[i]);
            }
            _modeBox.SelectedIndexChanged += delegate { OnLayerPropertyChanged(); };
            left.Controls.Add(_modeBox);

            Label opacityLabel = new Label();
            opacityLabel.Text = "不透明度";
            opacityLabel.Location = new Point(10, 354);
            opacityLabel.AutoSize = true;
            left.Controls.Add(opacityLabel);

            _opacityBar = new TrackBar();
            _opacityBar.AutoSize = false;
            _opacityBar.TickStyle = TickStyle.None;
            _opacityBar.Minimum = 0;
            _opacityBar.Maximum = 100;
            _opacityBar.Value = 100;
            _opacityBar.Location = new Point(10, 372);
            _opacityBar.Size = new Size(230, 30);
            _opacityBar.ValueChanged += delegate { _opacityValue.Text = _opacityBar.Value + "%"; OnLayerPropertyChanged(); };
            left.Controls.Add(_opacityBar);

            _opacityValue = new Label();
            _opacityValue.Text = "100%";
            _opacityValue.Location = new Point(246, 378);
            _opacityValue.Size = new Size(54, 20);
            _opacityValue.TextAlign = ContentAlignment.MiddleRight;
            left.Controls.Add(_opacityValue);

            Label note = new Label();
            note.Location = new Point(10, 414);
            note.Size = new Size(290, 130);
            note.ForeColor = Color.FromArgb(70, 70, 70);
            note.Text =
                "说明：\r\n" +
                "• 底图在下方，图层按列表顺序自下而上叠加。\r\n" +
                "• 图层会自动缩放到与底图相同尺寸。\r\n" +
                "• 选中某个图层后，可改它的混合模式与不透明度。";
            left.Controls.Add(note);

            _canvas = new ImageCanvas();
            _canvas.Dock = DockStyle.Fill;
            _canvas.Margin = new Padding(3, 3, 3, 3);
            _canvas.ReadOnly = true;
            root.Controls.Add(_canvas, 1, 0);

            Button save = new Button();
            save.Text = "保存为 PNG";
            save.Location = new Point(10, 8);
            save.Size = new Size(120, 32);
            save.Click += delegate { SaveResult(); };
            root.Controls.Add(save, 0, 1);

            _status = new Label();
            _status.Text = "请选择底图，再添加图层";
            _status.Location = new Point(140, 14);
            _status.Size = new Size(600, 22);
            _status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            root.Controls.Add(_status, 1, 1);

            _debounce = new Timer();
            _debounce.Interval = 120;
            _debounce.Tick += delegate { _debounce.Stop(); UpdatePreview(); };

            _syncing = true;
            _modeBox.SelectedIndex = 0;
            _syncing = false;
            UpdateLayerControls(false);
        }

        private void BrowseBase()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择底图";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap bitmap = ImageUtil.LoadImage(dialog.FileName);
                if (_baseImage != null) { _baseImage.Dispose(); }
                _baseImage = bitmap;
                _basePath = dialog.FileName;
                _baseBox.Text = dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                this.Cursor = previous;
            }

            UpdatePreview();
        }

        private void AddLayer()
        {
            if (_baseImage == null)
            {
                _status.Text = "请先选择底图";
                return;
            }

            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择图层图片";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            dialog.Multiselect = true;
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                for (int i = 0; i < dialog.FileNames.Length; i++)
                {
                    Layer layer = new Layer();
                    layer.Path = dialog.FileNames[i];
                    layer.Image = ImageUtil.LoadImage(dialog.FileNames[i]);
                    layer.Mode = BlendMode.Normal;
                    layer.Opacity = 1f;
                    _layers.Add(layer);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图层：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }

            RefreshLayerList(_layers.Count - 1);
            UpdatePreview();
        }

        private void RemoveLayer()
        {
            int index = _layerList.SelectedIndex;
            if (index < 0 || index >= _layers.Count)
            {
                return;
            }
            if (_layers[index].Image != null)
            {
                _layers[index].Image.Dispose();
            }
            _layers.RemoveAt(index);
            int next = Math.Min(index, _layers.Count - 1);
            RefreshLayerList(next);
            UpdatePreview();
        }

        private void MoveLayer(int delta)
        {
            int index = _layerList.SelectedIndex;
            int target = index + delta;
            if (index < 0 || target < 0 || target >= _layers.Count)
            {
                return;
            }
            Layer tmp = _layers[index];
            _layers[index] = _layers[target];
            _layers[target] = tmp;
            RefreshLayerList(target);
            UpdatePreview();
        }

        private void RefreshLayerList(int selectIndex)
        {
            _syncing = true;
            _layerList.Items.Clear();
            for (int i = 0; i < _layers.Count; i++)
            {
                _layerList.Items.Add((i + 1) + ". " + Path.GetFileName(_layers[i].Path));
            }
            if (selectIndex >= 0 && selectIndex < _layers.Count)
            {
                _layerList.SelectedIndex = selectIndex;
            }
            _syncing = false;
            OnLayerSelected();
        }

        private void OnLayerSelected()
        {
            if (_syncing)
            {
                return;
            }
            UpdateLayerControls(true);
        }

        private void UpdateLayerControls(bool applyFromLayer)
        {
            int index = _layerList.SelectedIndex;
            bool has = index >= 0 && index < _layers.Count;
            _modeBox.Enabled = has;
            _opacityBar.Enabled = has;
            if (!has)
            {
                return;
            }
            if (applyFromLayer)
            {
                _syncing = true;
                _modeBox.SelectedIndex = (int)_layers[index].Mode;
                _opacityBar.Value = (int)Math.Round(_layers[index].Opacity * 100f);
                _opacityValue.Text = _opacityBar.Value + "%";
                _syncing = false;
            }
        }

        private void OnLayerPropertyChanged()
        {
            if (_syncing)
            {
                return;
            }
            int index = _layerList.SelectedIndex;
            if (index < 0 || index >= _layers.Count)
            {
                return;
            }
            _layers[index].Mode = (BlendMode)Math.Max(0, _modeBox.SelectedIndex);
            _layers[index].Opacity = _opacityBar.Value / 100f;
            SchedulePreview();
        }

        private void SchedulePreview()
        {
            if (_baseImage == null)
            {
                return;
            }
            _debounce.Stop();
            _debounce.Start();
        }

        private void UpdatePreview()
        {
            if (_baseImage == null)
            {
                _status.Text = "请先选择底图";
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap result = BuildComposite();
                if (_resultImage != null)
                {
                    _resultImage.Dispose();
                }
                _resultImage = result;
                _canvas.SetImage(_resultImage);
                _status.Text = "底图 " + _baseImage.Width + "x" + _baseImage.Height + "，共 " + _layers.Count + " 个图层";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "合成失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private Bitmap BuildComposite()
        {
            Bitmap current = ImageFilters.Clone(_baseImage);
            for (int i = 0; i < _layers.Count; i++)
            {
                Bitmap next = ImageBlend.Blend(current, _layers[i].Image, _layers[i].Mode, _layers[i].Opacity);
                current.Dispose();
                current = next;
            }
            return current;
        }

        private void SaveResult()
        {
            if (_baseImage == null)
            {
                _status.Text = "请先选择底图";
                return;
            }

            string dir = Path.GetDirectoryName(_basePath);
            string name = Path.GetFileNameWithoutExtension(_basePath) + "_图层合成.png";

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
                Bitmap result = BuildComposite();
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
