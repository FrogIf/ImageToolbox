using System;
using System.Drawing;
using System.Windows.Forms;

namespace ImageToolbox
{
    // 手动 DPI 缩放：顶层窗口按 实际DPI/96 缩放整棵控件树的“位置/尺寸/内边距/边距”，
    // 但**不**缩放字体（DPI 感知下 9pt 已经按设备 DPI 渲染，字体自会变大）。
    // 这样在 150%/200% 等非标准缩放下也不会出现“字体变大而控件没变大”的裁切/重叠。
    internal static class DpiScaler
    {
        public static float Factor(Control root)
        {
            float dpi = 96f;
            try
            {
                using (Graphics g = root.CreateGraphics())
                {
                    dpi = g.DpiX;
                }
            }
            catch (Exception)
            {
                dpi = 96f;
            }
            if (dpi <= 0f) { dpi = 96f; }
            float f = dpi / 96f;
            if (f < 1f) { f = 1f; } // 不做缩小，避免高 DPI 上下文里被压小
            return f;
        }

        public static void Apply(Control root, bool scaleRootSize)
        {
            ApplyFactor(root, Factor(root), scaleRootSize);
        }

        public static void ApplyFactor(Control root, float factor, bool scaleRootSize)
        {
            if (factor <= 0f)
            {
                factor = 1f;
            }
            root.SuspendLayout();
            try
            {
                Walk(root, factor, true, scaleRootSize);
            }
            finally
            {
                root.ResumeLayout(true);
            }
        }

        private static void Walk(Control c, float f, bool isRoot, bool scaleRootSize)
        {
            // 操作面板：无论 DPI 是否缩放都先按设计坐标“压紧”一次。
            EditOpPanel op = c as EditOpPanel;
            if (op != null)
            {
                op.CompactLayout(6);
            }

            if (Math.Abs(f - 1f) >= 0.01f)
            {
                ScaleOne(c, f, isRoot, scaleRootSize);
            }

            for (int i = 0; i < c.Controls.Count; i++)
            {
                Walk(c.Controls[i], f, false, false);
            }
        }

        private static void ScaleOne(Control c, float f, bool isRoot, bool scaleRootSize)
        {
            c.Padding = ScalePadding(c.Padding, f);
            c.Margin = ScalePadding(c.Margin, f);
            if (!c.MinimumSize.IsEmpty)
            {
                c.MinimumSize = new Size(Scale(c.MinimumSize.Width, f), Scale(c.MinimumSize.Height, f));
            }

            if (isRoot)
            {
                if (scaleRootSize && !c.AutoSize)
                {
                    c.Size = new Size(Scale(c.Width, f), Scale(c.Height, f));
                }
            }
            else if (c.Dock == DockStyle.None)
            {
                c.Location = new Point(Scale(c.Left, f), Scale(c.Top, f));
                if (!c.AutoSize)
                {
                    c.Size = new Size(Scale(c.Width, f), Scale(c.Height, f));
                }
            }
            else
            {
                // 停靠控件由布局管理，只放大“厚度”
                if (c.Dock == DockStyle.Top || c.Dock == DockStyle.Bottom)
                {
                    c.Height = Scale(c.Height, f);
                }
                else if (c.Dock == DockStyle.Left || c.Dock == DockStyle.Right)
                {
                    c.Width = Scale(c.Width, f);
                }
            }

            TableLayoutPanel tlp = c as TableLayoutPanel;
            if (tlp != null)
            {
                for (int i = 0; i < tlp.ColumnStyles.Count; i++)
                {
                    if (tlp.ColumnStyles[i].SizeType == SizeType.Absolute)
                    {
                        tlp.ColumnStyles[i].Width = tlp.ColumnStyles[i].Width * f;
                    }
                }
                for (int i = 0; i < tlp.RowStyles.Count; i++)
                {
                    if (tlp.RowStyles[i].SizeType == SizeType.Absolute)
                    {
                        tlp.RowStyles[i].Height = tlp.RowStyles[i].Height * f;
                    }
                }
            }

            Splitter splitter = c as Splitter;
            if (splitter != null)
            {
                splitter.MinExtra = Scale(splitter.MinExtra, f);
                splitter.MinSize = Scale(splitter.MinSize, f);
            }
        }

        private static int Scale(int value, float f)
        {
            return (int)Math.Round(value * f);
        }

        private static Padding ScalePadding(Padding p, float f)
        {
            return new Padding(Scale(p.Left, f), Scale(p.Top, f), Scale(p.Right, f), Scale(p.Bottom, f));
        }
    }
}
