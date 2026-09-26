using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class CurveEditor : Control
    {
        private readonly List<PointF> _points = new List<PointF>();
        private int _dragIndex = -1;
        private Color _lineColor = Color.White;

        public event EventHandler CurveChanged;

        public CurveEditor()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Color.FromArgb(32, 32, 36);
            ResetPoints();
        }

        public Color LineColor
        {
            get { return _lineColor; }
            set { _lineColor = value; Invalidate(); }
        }

        public PointF[] Points
        {
            get { return _points.ToArray(); }
            set
            {
                _points.Clear();
                if (value == null || value.Length < 2)
                {
                    _points.Add(new PointF(0f, 0f));
                    _points.Add(new PointF(1f, 1f));
                }
                else
                {
                    for (int i = 0; i < value.Length; i++)
                    {
                        _points.Add(new PointF(Clamp01(value[i].X), Clamp01(value[i].Y)));
                    }
                    _points.Sort(delegate (PointF a, PointF b) { return a.X.CompareTo(b.X); });
                }
                Invalidate();
            }
        }

        public void ResetPoints()
        {
            _points.Clear();
            _points.Add(new PointF(0f, 0f));
            _points.Add(new PointF(1f, 1f));
            Invalidate();
            RaiseChanged();
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private PointF ToNorm(Point p)
        {
            float x = ClientSize.Width <= 1 ? 0f : (float)p.X / (ClientSize.Width - 1);
            float y = ClientSize.Height <= 1 ? 0f : 1f - (float)p.Y / (ClientSize.Height - 1);
            return new PointF(Clamp01(x), Clamp01(y));
        }

        private PointF ToControl(PointF n)
        {
            return new PointF(n.X * (ClientSize.Width - 1), (1f - n.Y) * (ClientSize.Height - 1));
        }

        private void RaiseChanged()
        {
            if (CurveChanged != null)
            {
                CurveChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

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
            using (Pen diagonal = new Pen(Color.FromArgb(80, 80, 90)))
            {
                diagonal.DashStyle = DashStyle.Dash;
                g.DrawLine(diagonal, 0, height - 1, width - 1, 0);
            }

            byte[] lut = ImageTuning.BuildCurveLut(_points.ToArray());
            PointF[] line = new PointF[256];
            for (int i = 0; i < 256; i++)
            {
                line[i] = new PointF(i / 255f * (width - 1), (1f - lut[i] / 255f) * (height - 1));
            }
            using (Pen pen = new Pen(_lineColor, 2f))
            {
                g.DrawLines(pen, line);
            }

            for (int i = 0; i < _points.Count; i++)
            {
                PointF p = ToControl(_points[i]);
                g.FillEllipse(Brushes.White, p.X - 4f, p.Y - 4f, 8f, 8f);
                g.DrawEllipse(Pens.Black, p.X - 4f, p.Y - 4f, 8f, 8f);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            PointF norm = ToNorm(e.Location);

            if (e.Button == MouseButtons.Right)
            {
                int index = FindPoint(e.Location);
                if (index > 0 && index < _points.Count - 1)
                {
                    _points.RemoveAt(index);
                    Invalidate();
                    RaiseChanged();
                }
                return;
            }

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            int hit = FindPoint(e.Location);
            if (hit >= 0)
            {
                _dragIndex = hit;
            }
            else
            {
                _points.Add(norm);
                _points.Sort(delegate (PointF a, PointF b) { return a.X.CompareTo(b.X); });
                _dragIndex = _points.IndexOf(norm);
                RaiseChanged();
            }
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragIndex < 0)
            {
                return;
            }
            PointF norm = ToNorm(e.Location);
            if (_dragIndex > 0 && _dragIndex < _points.Count - 1)
            {
                float left = _points[_dragIndex - 1].X + 0.01f;
                float right = _points[_dragIndex + 1].X - 0.01f;
                norm.X = Math.Max(left, Math.Min(right, norm.X));
            }
            else if (_dragIndex == 0)
            {
                norm.X = 0f;
            }
            else
            {
                norm.X = 1f;
            }
            _points[_dragIndex] = new PointF(norm.X, norm.Y);
            Invalidate();
            RaiseChanged();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragIndex = -1;
            Capture = false;
        }

        private int FindPoint(Point location)
        {
            for (int i = 0; i < _points.Count; i++)
            {
                PointF p = ToControl(_points[i]);
                float dx = p.X - location.X;
                float dy = p.Y - location.Y;
                if (dx * dx + dy * dy <= 100f)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
