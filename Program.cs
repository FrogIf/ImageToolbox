using System;
using System.Drawing;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class MainForm : Form
    {
        private TabControl _tabs;

        public MainForm()
        {
            Text = "图片工具箱";
            ClientSize = new Size(1180, 760);
            MinimumSize = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            try
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
            }

            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            Controls.Add(_tabs);

            AddTool(new BatchProcessPage());
            AddTool(new CropComposePage());
            AddTool(new SliceCollagePage());
            AddTool(new IdPhotoPage());
            AddTool(new LocalOverlayPage());
            AddTool(new StyleAdjustPage());
            AddTool(new EffectsPage());
            AddTool(new BrushBlurPage());
            AddTool(new ImageAdjustPage());
            AddTool(new ColorMatchPage());
            AddTool(new ImageInfoPage());
            AddTool(new ImageComparePage());
            AddTool(new LayerComposePage());
            AddTool(new MattingPage());
            AddTool(new ColorToolPage());
            AddTool(new MultiSizeExportPage());
        }

        public void AddTool(ToolPage tool)
        {
            TabPage page = new TabPage(tool.ToolName);
            page.UseVisualStyleBackColor = true;
            tool.Dock = DockStyle.Fill;
            page.Controls.Add(tool);
            _tabs.TabPages.Add(page);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            foreach (TabPage page in _tabs.TabPages)
            {
                foreach (Control control in page.Controls)
                {
                    ToolPage tool = control as ToolPage;
                    if (tool != null)
                    {
                        tool.Shutdown();
                    }
                }
            }
            base.OnFormClosing(e);
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
