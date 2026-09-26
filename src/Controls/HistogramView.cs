using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class HistogramView : Control
    {
        private int[][] _data;

        public HistogramView()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Color.FromArgb(24, 24, 28);
        }

        public void SetData(int[][] data)
        {
            _data = data;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            int width = ClientSize.Width;
            int height = ClientSize.Height;
            if (width <= 1 || height <= 1)
            {
                return;
            }

            using (Pen grid = new Pen(Color.FromArgb(60, 60, 70)))
            {
                for (int i = 1; i < 4; i++)
                {
                    int x = width * i / 4;
                    int y = height * i / 4;
                    g.DrawLine(grid, x, 0, x, height);
                    g.DrawLine(grid, 0, y, width, y);
                }
            }

            if (_data == null || _data.Length < 3)
            {
                return;
            }

            int max = 1;
            for (int c = 0; c < 3; c++)
            {
                for (int i = 0; i < 256; i++)
                {
                    if (_data[c][i] > max)
                    {
                        max = _data[c][i];
                    }
                }
            }
            double scale = 1.0 / Math.Sqrt(max);

            Color[] colors = { Color.FromArgb(200, 60, 60), Color.FromArgb(60, 200, 90), Color.FromArgb(70, 120, 240) };
            for (int c = 0; c < 3; c++)
            {
                PointF[] points = new PointF[258];
                points[0] = new PointF(0, height - 1);
                for (int i = 0; i < 256; i++)
                {
                    double value = Math.Sqrt(_data[c][i]) * scale;
                    float x = (float)(i / 255.0 * (width - 1));
                    float y = (float)(height - value * (height - 1));
                    points[i + 1] = new PointF(x, y);
                }
                points[257] = new PointF(width - 1, height - 1);
                using (Brush brush = new SolidBrush(Color.FromArgb(150, colors[c])))
                {
                    g.FillPolygon(brush, points);
                }
            }
        }
    }
}
