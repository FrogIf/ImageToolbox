using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ImageToolbox
{
    // 通用“压紧”布局：把容器里绝对定位（Dock=None）的子控件按 Top 分组（同一行视为一组），
    // 再把各组按 gap 依次贴紧排列，组内保持原来的相对偏移。用于减小控件高度后消除多余空白。
    internal static class LayoutCompact
    {
        public static void Compact(Control container, int gap)
        {
            List<Control> kids = new List<Control>();
            for (int i = 0; i < container.Controls.Count; i++)
            {
                Control c = container.Controls[i];
                if (c.Dock == DockStyle.None) { kids.Add(c); }
            }
            if (kids.Count == 0) { return; }

            kids.Sort(delegate(Control a, Control b)
            {
                int r = a.Top.CompareTo(b.Top);
                if (r != 0) { return r; }
                return a.Left.CompareTo(b.Left);
            });

            const int tol = 8;
            List<List<Control>> groups = new List<List<Control>>();
            List<int> groupTop = new List<int>();
            for (int i = 0; i < kids.Count; i++)
            {
                Control c = kids[i];
                int last = groups.Count - 1;
                if (last >= 0 && Math.Abs(c.Top - groupTop[last]) <= tol)
                {
                    groups[last].Add(c);
                }
                else
                {
                    groups.Add(new List<Control>());
                    groups[groups.Count - 1].Add(c);
                    groupTop.Add(c.Top);
                }
            }

            container.SuspendLayout();
            try
            {
                int y = groupTop[0];
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    List<Control> g = groups[gi];
                    int baseTop = groupTop[gi];
                    int height = 0;
                    for (int i = 0; i < g.Count; i++)
                    {
                        Control c = g[i];
                        int offset = c.Top - baseTop;
                        c.Top = y + offset;
                        if (offset + c.Height > height) { height = offset + c.Height; }
                    }
                    y += height + gap;
                }
            }
            finally
            {
                container.ResumeLayout(true);
            }
        }

        // 先压紧，再把容器高度收缩到刚好包住子控件（用于 GroupBox）。
        public static void CompactAndFit(Control container, int gap, int bottomPad)
        {
            Compact(container, gap);
            int maxBottom = 0;
            for (int i = 0; i < container.Controls.Count; i++)
            {
                Control c = container.Controls[i];
                if (c.Dock != DockStyle.None) { continue; }
                if (c.Bottom > maxBottom) { maxBottom = c.Bottom; }
            }
            if (maxBottom > 0)
            {
                container.Height = maxBottom + bottomPad;
            }
        }
    }
}
