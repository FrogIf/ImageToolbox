using System;
using System.Drawing;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class ToolHostPage : ToolPage
    {
        private readonly string _title;
        private readonly ToolPage[] _tools;
        private ListBox _list;
        private Panel _content;

        public ToolHostPage(string title, ToolPage[] tools)
        {
            _title = title;
            _tools = tools;

            AutoScaleMode = AutoScaleMode.Inherit;
            Font = new Font("Microsoft YaHei UI", 9F);

            BuildUi();
        }

        public override string ToolName
        {
            get { return _title; }
        }

        public override void Shutdown()
        {
            if (_tools == null)
            {
                return;
            }
            for (int i = 0; i < _tools.Length; i++)
            {
                if (_tools[i] != null)
                {
                    _tools[i].Shutdown();
                }
            }
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(root);

            TableLayoutPanel leftGrid = new TableLayoutPanel();
            leftGrid.Dock = DockStyle.Fill;
            leftGrid.ColumnCount = 1;
            leftGrid.RowCount = 2;
            leftGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            leftGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            leftGrid.Margin = new Padding(3, 3, 3, 3);
            root.Controls.Add(leftGrid, 0, 0);

            Label header = new Label();
            header.Text = "  " + _title;
            header.Dock = DockStyle.Fill;
            header.TextAlign = ContentAlignment.MiddleLeft;
            header.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            header.BackColor = Color.FromArgb(238, 238, 238);
            leftGrid.Controls.Add(header, 0, 0);

            _list = new ListBox();
            _list.Dock = DockStyle.Fill;
            _list.IntegralHeight = false;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.ItemHeight = 24;
            _list.SelectedIndexChanged += delegate { ShowSelected(); };
            leftGrid.Controls.Add(_list, 0, 1);

            _content = new Panel();
            _content.Dock = DockStyle.Fill;
            _content.Margin = new Padding(3, 3, 3, 3);
            root.Controls.Add(_content, 1, 0);

            for (int i = 0; i < _tools.Length; i++)
            {
                ToolPage tool = _tools[i];
                tool.Dock = DockStyle.Fill;
                tool.Visible = false;
                _content.Controls.Add(tool);
                _list.Items.Add(tool.ToolName);
            }

            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = 0;
            }
        }

        private void ShowSelected()
        {
            int index = _list.SelectedIndex;
            for (int i = 0; i < _tools.Length; i++)
            {
                _tools[i].Visible = (i == index);
            }
            if (index >= 0 && index < _tools.Length)
            {
                _tools[index].BringToFront();
            }
        }
    }
}
