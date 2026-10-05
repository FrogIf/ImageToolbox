using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    // 图层变更事件参数：AddsLayer 标记本次变更是“新增图层”。
    public class LayerChangingEventArgs : CancelEventArgs
    {
        public readonly bool AddsLayer;

        public LayerChangingEventArgs(bool addsLayer)
        {
            AddsLayer = addsLayer;
        }
    }

    // 全局图层组件：编辑器的常驻面板，管理 EditSession 的图层栈。
    public class LayerPanel : UserControl
    {
        private EditSession _session;
        private ListBox _list;
        private ComboBox _mode;
        private TrackBar _opacity;
        private Label _opacityV;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _miVisible;
        private ToolStripMenuItem _miRename;
        private Button _remove;
        private Button _up;
        private Button _down;
        private Button _merge;
        private bool _updating;

        public event EventHandler LayersChanged;
        public event EventHandler PropsChanged;
        // 切换/删除/新增等会改变当前图层之前触发；e.Cancel=true 表示取消该操作（留在原图层）。
        // e.AddsLayer=true 表示该操作是“新增图层”，编辑器可据此保持旧交互（如绘画笔迹跟随新图层）。
        public event EventHandler<LayerChangingEventArgs> ActiveLayerChanging;

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
            top.Height = 80;

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 86;

            _list = new ListBox();
            _list.Dock = DockStyle.Fill;
            _list.IntegralHeight = false;
            _list.SelectedIndexChanged += delegate { SelectLayer(); };
            _list.MouseDown += ListMouseDown;
            _list.DrawMode = DrawMode.OwnerDrawVariable;
            _list.MeasureItem += delegate(object s, MeasureItemEventArgs e) { e.ItemHeight = _list.Font.Height + 7; };
            _list.DrawItem += DrawLayerItem;

            // 图层的显示/隐藏与重命名放在列表的右键菜单里。
            _menu = new ContextMenuStrip();
            _miVisible = new ToolStripMenuItem("隐藏图层", null, MenuToggleVisible);
            _miRename = new ToolStripMenuItem("重命名…", null, MenuRename);
            _menu.Items.Add(_miVisible);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miRename);
            _menu.Opening += delegate { UpdateMenuText(); };
            _list.ContextMenuStrip = _menu;

            Panel titleBar = new Panel();
            titleBar.Dock = DockStyle.Top;
            titleBar.Height = 24;

            Label title = new Label();
            title.Text = "图层";
            title.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            title.Location = new Point(2, 2);
            title.AutoSize = true;
            titleBar.Controls.Add(title);

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.WrapContents = true;

            MakeBar(bar, "添加图片", delegate { AddImages(); });
            MakeBar(bar, "新建", delegate { Run(delegate { _session.AddBlankLayer("新图层", "新建图层"); }, true); });
            MakeBar(bar, "复制", delegate { Run(delegate { _session.DuplicateActive("复制图层"); }); });
            _remove = MakeBar(bar, "删除", delegate { Run(delegate { _session.RemoveActive("删除图层"); }); });
            _up = MakeBar(bar, "上移", delegate { Run(delegate { _session.MoveActive(1, "上移图层"); }); });
            _down = MakeBar(bar, "下移", delegate { Run(delegate { _session.MoveActive(-1, "下移图层"); }); });
            _merge = MakeBar(bar, "向下合并", delegate { Run(delegate { _session.MergeDown("向下合并"); }); });
            MakeBar(bar, "盖印", delegate { Run(delegate { _session.StampVisible("盖印"); }); });
            MakeBar(bar, "拼合", delegate { Run(delegate { _session.Flatten("拼合"); }); });

            top.Controls.Add(bar);
            top.Controls.Add(titleBar);

            Label modeCaption = new Label();
            modeCaption.Text = "混合模式";
            modeCaption.Location = new Point(2, 6);
            modeCaption.AutoSize = true;
            bottom.Controls.Add(modeCaption);

            _mode = new ComboBox();
            _mode.DropDownStyle = ComboBoxStyle.DropDownList;
            _mode.Location = new Point(62, 3);
            _mode.Size = new Size(172, 22);
            for (int i = 0; i < ImageBlend.ModeNames.Length; i++)
            {
                _mode.Items.Add(ImageBlend.ModeNames[i]);
            }
            _mode.SelectedIndexChanged += delegate { ChangeMode(); };
            bottom.Controls.Add(_mode);

            Label opCaption = new Label();
            opCaption.Text = "不透明";
            opCaption.Location = new Point(2, 31);
            opCaption.AutoSize = true;
            bottom.Controls.Add(opCaption);

            _opacity = new TrackBar();
            _opacity.AutoSize = false;
            _opacity.TickStyle = TickStyle.None;
            _opacity.Minimum = 0;
            _opacity.Maximum = 100;
            _opacity.Value = 100;
            _opacity.Location = new Point(74, 26);
            _opacity.Size = new Size(150, 22);
            _opacity.ValueChanged += delegate { ChangeOpacity(); };
            bottom.Controls.Add(_opacity);

            _opacityV = new Label();
            _opacityV.Location = new Point(228, 30);
            _opacityV.Size = new Size(52, 18);
            _opacityV.TextAlign = ContentAlignment.MiddleRight;
            bottom.Controls.Add(_opacityV);

            Label hint = new Label();
            hint.Text = "操作作用于选中图层；画布显示所有图层的合成结果。\r\n右键图层可显示/隐藏、重命名；拖动上方分隔条调整高度。";
            hint.Location = new Point(2, 56);
            hint.Size = new Size(280, 28);
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
            button.MinimumSize = new Size(48, 22);
            button.Margin = new Padding(2);
            button.Click += onClick;
            parent.Controls.Add(button);
            return button;
        }

        private void Run(Action action)
        {
            Run(action, false);
        }

        private void Run(Action action, bool addsLayer)
        {
            if (_session == null || !_session.HasImage) { return; }
            if (!ConfirmLayerChange(addsLayer)) { return; }
            action();
            Sync();
            Raise();
        }

        // 询问编辑器当前操作是否有未应用的修改；返回 false 表示取消本次图层变更。
        private bool ConfirmLayerChange(bool addsLayer)
        {
            if (ActiveLayerChanging == null) { return true; }
            LayerChangingEventArgs e = new LayerChangingEventArgs(addsLayer);
            ActiveLayerChanging(this, e);
            return !e.Cancel;
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
                    _session.AddImageLayer(image, Path.GetFileNameWithoutExtension(dialog.FileNames[i]), "添加图片");
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
                if (!ConfirmLayerChange(false))
                {
                    Sync();   // 恢复列表选中到当前图层
                    return;
                }
                _session.ActiveIndex = index;
            }
            Sync();
            Raise();
        }

        // 右键先选中光标下的图层，菜单命令再作用于当前图层。
        private void ListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || _updating || _session == null) { return; }
            int i = _list.IndexFromPoint(e.Location);
            if (i >= 0 && i != _list.SelectedIndex) { _list.SelectedIndex = i; }
        }

        private void UpdateMenuText()
        {
            EditLayer layer = (_session == null) ? null : _session.ActiveLayer;
            bool has = layer != null;
            _miVisible.Enabled = has;
            _miRename.Enabled = has;
            _miVisible.Text = (has && !layer.Visible) ? "显示图层" : "隐藏图层";
        }

        private void MenuToggleVisible(object sender, EventArgs e)
        {
            if (_session == null) { return; }
            EditLayer layer = _session.ActiveLayer;
            if (layer == null) { return; }
            _session.SetVisible(layer, !layer.Visible, layer.Visible ? "隐藏图层" : "显示图层");
            UpdateRowText();
            UpdateButtons();
            RaiseProps();
        }

        private void MenuRename(object sender, EventArgs e)
        {
            if (_session == null) { return; }
            EditLayer layer = _session.ActiveLayer;
            if (layer == null) { return; }
            string name = RenameLayerDialog.Prompt(this, layer.Name);
            if (name == null) { return; }
            name = name.Trim();
            if (name.Length == 0) { return; }
            _session.Rename(layer, name, "重命名图层");
            Sync();
        }

        private void ChangeMode()
        {
            if (_updating || _session == null) { return; }
            EditLayer layer = _session.ActiveLayer;
            if (layer == null) { return; }
            _session.SetMode(layer, (BlendMode)Math.Max(0, _mode.SelectedIndex), "混合模式");
            UpdateRowText();
            RaiseProps();
        }

        private void ChangeOpacity()
        {
            if (_updating || _session == null) { return; }
            EditLayer layer = _session.ActiveLayer;
            if (layer == null) { return; }
            _session.SetOpacity(layer, _opacity.Value / 100f, "不透明度");
            _opacityV.Text = _opacity.Value + "%";
            UpdateRowText();
            RaiseProps();
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
            _mode.Enabled = has;
            _opacity.Enabled = has;
            if (!has)
            {
                _opacityV.Text = "";
                return;
            }
            _updating = true;
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
            // 文本里不含可见性，单靠 Items[k] 赋值在“显示/隐藏”时不变化、不会重绘，需强制刷新。
            _list.Invalidate();
        }

        // 列表自绘，这里的文本只用于无障碍/测试（名字 + 模式/不透明度）；显示状态用左侧方框表示。
        private static string Format(EditLayer layer)
        {
            return layer.Name + "   [" + ImageBlend.ModeNames[(int)layer.Mode] + " " +
                (int)Math.Round(layer.Opacity * 100f) + "%]";
        }

        // 自绘图层行：左侧是「可见性方框」（可见=蓝色勾选，隐藏=空框加斜杠），
        // 隐藏图层名用灰色，选中行仍能清楚区分。
        private void DrawLayerItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || _session == null || e.Index >= _session.Layers.Count)
            {
                return;
            }
            EditLayer layer = _session.Layers[_session.Layers.Count - 1 - e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool visible = layer.Visible;
            Rectangle b = e.Bounds;

            int box = Math.Max(12, _list.Font.Height - 3);
            int bx = b.Left + 4;
            int by = b.Top + (b.Height - box) / 2;
            Rectangle r = new Rectangle(bx, by, box, box);

            Color line = selected ? SystemColors.HighlightText : Color.FromArgb(110, 110, 110);
            Point[] checkPts = new Point[]
            {
                new Point(r.X + box / 4, r.Y + box / 2),
                new Point(r.X + box * 2 / 5, r.Y + box * 3 / 4),
                new Point(r.X + box * 4 / 5, r.Y + box / 4)
            };
            if (visible)
            {
                // 未选中：填充蓝色方框 + 白勾；选中（蓝底）：方框描边 + 勾，避免白底白勾看不见。
                if (!selected)
                {
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(0, 120, 215)))
                    {
                        e.Graphics.FillRectangle(fill, r.X + 1, r.Y + 1, r.Width - 1, r.Height - 1);
                    }
                }
                using (Pen pen = new Pen(line)) { e.Graphics.DrawRectangle(pen, r); }
                using (Pen check = new Pen(selected ? SystemColors.HighlightText : Color.White, Math.Max(1.5f, box / 6f)))
                {
                    check.StartCap = LineCap.Round;
                    check.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(check, checkPts);
                }
            }
            else
            {
                using (Pen pen = new Pen(line))
                {
                    e.Graphics.DrawRectangle(pen, r);
                    e.Graphics.DrawLine(pen, r.X + 2, r.Bottom - 2, r.Right - 2, r.Y + 2);
                }
            }

            Color textColor = selected ? SystemColors.HighlightText : (visible ? SystemColors.ControlText : SystemColors.GrayText);
            Rectangle textRect = new Rectangle(r.Right + 6, b.Top, Math.Max(0, b.Right - r.Right - 8), b.Height);
            TextRenderer.DrawText(e.Graphics, Format(layer), _list.Font, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (selected)
            {
                e.DrawFocusRectangle();
            }
        }

        private void Raise()
        {
            if (LayersChanged != null)
            {
                LayersChanged(this, EventArgs.Empty);
            }
        }

        private void RaiseProps()
        {
            if (PropsChanged != null)
            {
                PropsChanged(this, EventArgs.Empty);
            }
        }
    }

    // 图层重命名对话框（单行输入）。
    internal class RenameLayerDialog : Form
    {
        private TextBox _box;

        public static string Prompt(IWin32Window owner, string initial)
        {
            using (RenameLayerDialog dialog = new RenameLayerDialog(initial))
            {
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._box.Text : null;
            }
        }

        public RenameLayerDialog(string initial)
        {
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9F);
            Text = "重命名图层";
            ClientSize = new Size(280, 96);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            Label label = new Label();
            label.Text = "图层名称：";
            label.Location = new Point(14, 16);
            label.AutoSize = true;
            Controls.Add(label);

            _box = new TextBox();
            _box.Text = initial;
            _box.Location = new Point(14, 40);
            _box.Size = new Size(252, 22);
            _box.SelectAll();
            Controls.Add(_box);

            Button ok = new Button();
            ok.Text = "确定";
            ok.Location = new Point(116, 70);
            ok.Size = new Size(72, 22);
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(194, 70);
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
            _box.Focus();
        }
    }
}
