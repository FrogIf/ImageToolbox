using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    // 全局图层组件：编辑器的常驻面板，管理 EditSession 的图层栈。
    public class LayerPanel : UserControl
    {
        private EditSession _session;
        private ListBox _list;
        private ComboBox _mode;
        private TrackBar _opacity;
        private Label _opacityV;
        private CheckBox _visible;
        private Button _remove;
        private Button _up;
        private Button _down;
        private Button _merge;
        private bool _updating;

        public event EventHandler LayersChanged;

        public LayerPanel()
        {
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = SystemColors.Control;
            BuildUi();
        }

        public void Bind(EditSession session)
        {
            _session = session;
            Sync();
        }

        private void BuildUi()
        {
            Padding = new Padding(8, 0, 8, 0);

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 94;

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 104;

            _list = new ListBox();
            _list.Dock = DockStyle.Fill;
            _list.IntegralHeight = false;
            _list.SelectedIndexChanged += delegate { SelectLayer(); };

            Label title = new Label();
            title.Text = "图层";
            title.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            title.Dock = DockStyle.Top;
            title.Height = 24;
            title.TextAlign = ContentAlignment.MiddleLeft;

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.WrapContents = true;

            MakeBar(bar, "添加图片", delegate { AddImages(); });
            MakeBar(bar, "新建", delegate { Run(delegate { _session.AddBlankLayer("新图层"); }); });
            MakeBar(bar, "复制", delegate { Run(delegate { _session.DuplicateActive(); }); });
            _remove = MakeBar(bar, "删除", delegate { Run(delegate { _session.RemoveActive(); }); });
            _up = MakeBar(bar, "上移", delegate { Run(delegate { _session.MoveActive(1); }); });
            _down = MakeBar(bar, "下移", delegate { Run(delegate { _session.MoveActive(-1); }); });
            _merge = MakeBar(bar, "向下合并", delegate { Run(delegate { _session.MergeDown(); }); });
            MakeBar(bar, "盖印", delegate { Run(delegate { _session.StampVisible(); }); });
            MakeBar(bar, "拼合", delegate { Run(delegate { _session.Flatten(); }); });

            top.Controls.Add(bar);
            top.Controls.Add(title);

            _visible = new CheckBox();
            _visible.Text = "显示";
            _visible.Location = new Point(2, 4);
            _visible.AutoSize = true;
            _visible.CheckedChanged += delegate { ToggleVisible(); };
            bottom.Controls.Add(_visible);

            Label modeCaption = new Label();
            modeCaption.Text = "混合模式";
            modeCaption.Location = new Point(78, 7);
            modeCaption.AutoSize = true;
            bottom.Controls.Add(modeCaption);

            _mode = new ComboBox();
            _mode.DropDownStyle = ComboBoxStyle.DropDownList;
            _mode.Location = new Point(142, 4);
            _mode.Size = new Size(180, 25);
            for (int i = 0; i < ImageBlend.ModeNames.Length; i++)
            {
                _mode.Items.Add(ImageBlend.ModeNames[i]);
            }
            _mode.SelectedIndexChanged += delegate { ChangeMode(); };
            bottom.Controls.Add(_mode);

            Label opCaption = new Label();
            opCaption.Text = "不透明";
            opCaption.Location = new Point(2, 40);
            opCaption.AutoSize = true;
            bottom.Controls.Add(opCaption);

            _opacity = new TrackBar();
            _opacity.AutoSize = false;
            _opacity.TickStyle = TickStyle.None;
            _opacity.Minimum = 0;
            _opacity.Maximum = 100;
            _opacity.Value = 100;
            _opacity.Location = new Point(74, 34);
            _opacity.Size = new Size(180, 30);
            _opacity.ValueChanged += delegate { ChangeOpacity(); };
            bottom.Controls.Add(_opacity);

            _opacityV = new Label();
            _opacityV.Location = new Point(258, 40);
            _opacityV.Size = new Size(62, 20);
            _opacityV.TextAlign = ContentAlignment.MiddleRight;
            bottom.Controls.Add(_opacityV);

            Label hint = new Label();
            hint.Text = "操作作用于选中图层；画布显示所有图层的合成结果。\r\n拖动上方分隔条可调整本面板高度。";
            hint.Location = new Point(2, 72);
            hint.Size = new Size(318, 30);
            hint.ForeColor = Color.FromArgb(80, 80, 80);
            bottom.Controls.Add(hint);

            Controls.Add(_list);
            Controls.Add(top);
            Controls.Add(bottom);
        }

        private Button MakeBar(Control parent, string text, EventHandler onClick)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.MinimumSize = new Size(48, 28);
            button.Margin = new Padding(2);
            button.Click += onClick;
            parent.Controls.Add(button);
            return button;
        }

        private void Run(Action action)
        {
            if (_session == null || !_session.HasImage) { return; }
            action();
            Sync();
            Raise();
        }

        private void AddImages()
        {
            if (_session == null || !_session.HasImage) { return; }
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "添加图层图片";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            dialog.Multiselect = true;
            if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
            try
            {
                for (int i = 0; i < dialog.FileNames.Length; i++)
                {
                    Bitmap image = ImageUtil.LoadImage(dialog.FileNames[i]);
                    _session.AddImageLayer(image, Path.GetFileNameWithoutExtension(dialog.FileNames[i]));
                    image.Dispose();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图层：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Sync();
            Raise();
        }

        private void SelectLayer()
        {
            if (_updating || _session == null) { return; }
            int k = _list.SelectedIndex;
            if (k < 0) { return; }
            int index = _session.Layers.Count - 1 - k;
            if (index != _session.ActiveIndex)
            {
                _session.ActiveIndex = index;
            }
            SyncProps();
            UpdateButtons();
            Raise();
        }

        private void ToggleVisible()
        {
            if (_updating || _session == null) { return; }
            EditLayer layer = _session.ActiveLayer;
            if (layer == null) { return; }
            _session.SetVisible(layer, _visible.Checked);
            UpdateRowText();
            UpdateButtons();
            Raise();
        }

        private void ChangeMode()
        {
            if (_updating || _session == null) { return; }
            EditLayer layer = _session.ActiveLayer;
            if (layer == null) { return; }
            _session.SetMode(layer, (BlendMode)Math.Max(0, _mode.SelectedIndex));
            UpdateRowText();
            Raise();
        }

        private void ChangeOpacity()
        {
            if (_updating || _session == null) { return; }
            EditLayer layer = _session.ActiveLayer;
            if (layer == null) { return; }
            _session.SetOpacity(layer, _opacity.Value / 100f);
            _opacityV.Text = _opacity.Value + "%";
            UpdateRowText();
            Raise();
        }

        public void Sync()
        {
            _updating = true;
            _list.Items.Clear();
            if (_session != null && _session.HasImage)
            {
                for (int k = 0; k < _session.Layers.Count; k++)
                {
                    EditLayer layer = _session.Layers[_session.Layers.Count - 1 - k];
                    _list.Items.Add(Format(layer));
                }
                int sel = _session.Layers.Count - 1 - _session.ActiveIndex;
                if (sel >= 0 && sel < _list.Items.Count)
                {
                    _list.SelectedIndex = sel;
                }
            }
            _updating = false;
            SyncProps();
            UpdateButtons();
        }

        private void SyncProps()
        {
            EditLayer layer = (_session == null) ? null : _session.ActiveLayer;
            bool has = layer != null;
            _visible.Enabled = has;
            _mode.Enabled = has;
            _opacity.Enabled = has;
            if (!has)
            {
                _opacityV.Text = "";
                return;
            }
            _updating = true;
            _visible.Checked = layer.Visible;
            _mode.SelectedIndex = (int)layer.Mode;
            _opacity.Value = (int)Math.Round(layer.Opacity * 100f);
            _updating = false;
            _opacityV.Text = _opacity.Value + "%";
        }

        private void UpdateButtons()
        {
            if (_session == null)
            {
                _remove.Enabled = false;
                _up.Enabled = false;
                _down.Enabled = false;
                _merge.Enabled = false;
                return;
            }
            int i = _session.ActiveIndex;
            int n = _session.Layers.Count;
            _remove.Enabled = _session.CanRemoveActive;
            _up.Enabled = i >= 0 && i < n - 1;
            _down.Enabled = i > 0;
            _merge.Enabled = i > 0;
        }

        private void UpdateRowText()
        {
            if (_session == null) { return; }
            EditLayer layer = _session.ActiveLayer;
            if (layer == null) { return; }
            int k = _session.Layers.Count - 1 - _session.ActiveIndex;
            if (k < 0 || k >= _list.Items.Count) { return; }
            _updating = true;
            _list.Items[k] = Format(layer);
            _updating = false;
        }

        private static string Format(EditLayer layer)
        {
            return (layer.Visible ? "● " : "○ ") + layer.Name +
                "   [" + ImageBlend.ModeNames[(int)layer.Mode] + " " +
                (int)Math.Round(layer.Opacity * 100f) + "%]";
        }

        private void Raise()
        {
            if (LayersChanged != null)
            {
                LayersChanged(this, EventArgs.Empty);
            }
        }
    }
}
