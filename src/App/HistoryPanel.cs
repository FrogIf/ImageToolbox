using System;
using System.Drawing;
using System.Windows.Forms;

namespace ImageToolbox
{
    // 历史记录面板：把 EditSession 的撤销栈显示成可点击的时间线。
    // 第 0 条是初始状态（打开图片/新建），其余为每次操作；点击任意一条即
    // 连续撤销/重做到该步。列表只读，不改变撤销/重做的语义。
    public class HistoryPanel : UserControl
    {
        private EditSession _session;
        private ListBox _list;
        private bool _updating;

        // 用户点击某一步时触发（参数为时间线索引）。编辑器负责跳转后刷新画布/图层。
        public event Action<int> JumpRequested;

        public HistoryPanel()
        {
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = SystemColors.Control;

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 30;

            Button root = new Button();
            root.Text = "回到最初";
            root.Location = new Point(8, 4);
            root.Size = new Size(88, 22);
            root.Click += delegate { if (JumpRequested != null) { JumpRequested(0); } };
            bar.Controls.Add(root);

            Label hint = new Label();
            hint.Text = "点击任意一步回退/前进";
            hint.Location = new Point(104, 7);
            hint.AutoSize = true;
            hint.ForeColor = Color.FromArgb(90, 90, 90);
            bar.Controls.Add(hint);

            _list = new ListBox();
            _list.Dock = DockStyle.Fill;
            _list.IntegralHeight = false;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = 22;
            _list.DrawItem += DrawItem;
            _list.SelectedIndexChanged += delegate
            {
                if (_updating || _session == null) { return; }
                int i = _list.SelectedIndex;
                if (i >= 0 && JumpRequested != null) { JumpRequested(i); }
            };

            Controls.Add(_list);
            Controls.Add(bar);
        }

        public void Bind(EditSession session)
        {
            if (_session != null) { _session.Changed -= OnChanged; }
            _session = session;
            if (_session != null) { _session.Changed += OnChanged; }
            Sync();
        }

        private void OnChanged(object sender, EventArgs e)
        {
            Sync();
        }

        public void Sync()
        {
            _updating = true;
            try
            {
                int count = (_session != null) ? _session.HistoryCount : 0;
                while (_list.Items.Count > count) { _list.Items.RemoveAt(_list.Items.Count - 1); }
                while (_list.Items.Count < count) { _list.Items.Add(""); }

                int cur = (_session != null) ? _session.CurrentHistoryIndex : -1;
                _list.SelectedIndex = (cur >= 0 && cur < _list.Items.Count) ? cur : -1;
            }
            finally
            {
                _updating = false;
            }
            _list.Invalidate();
        }

        private void DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || _session == null || e.Index >= _session.HistoryCount) { return; }

            bool current = (e.Index == _session.CurrentHistoryIndex);
            string kind = (e.Index == 0) ? "初始" : e.Index.ToString();
            string text = (current ? "▶ " : "   ") + kind + "  " + _session.HistoryLabel(e.Index);

            Color color = ((e.State & DrawItemState.Selected) != 0)
                ? SystemColors.HighlightText : SystemColors.ControlText;
            Rectangle r = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 8), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, Font, r, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _session != null) { _session.Changed -= OnChanged; }
            base.Dispose(disposing);
        }
    }
}
