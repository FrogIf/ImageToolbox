using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class ImageCanvas : Control
    {
        private Image _image;
        private Rectangle _selection;
        private bool _readOnly;
        private bool _dragging;
        private Point _dragStart;
        private RectangleF _imageRect;
        private float _scale;
        private float _lockAspect;

        public event EventHandler SelectionChanged;

        public ImageCanvas()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Color.FromArgb(245, 245, 245);
        }

        public bool ReadOnly
        {
            get { return _readOnly; }
            set { _readOnly = value; }
        }

        public float LockAspect
        {
            get { return _lockAspect; }
            set { _lockAspect = value; }
        }

        public Rectangle Selection
        {
            get { return _selection; }
            set
            {
                _selection = value;
                Invalidate();
            }
        }

        public void SetImage(Image image)
        {
            _image = image;
            _selection = Rectangle.Empty;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);

            if (_image == null)
            {
                using (Pen border = new Pen(Color.FromArgb(200, 200, 200)))
                {
                    e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                }
                return;
            }

            ComputeLayout();

            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(_image, _imageRect);

            if (_selection.Width > 0 && _selection.Height > 0)
            {
                RectangleF rect = ImageToControl(_selection);

                using (Region outside = new Region(ClientRectangle))
                {
                    outside.Exclude(rect);
                    using (Brush dim = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                    {
                        e.Graphics.FillRegion(dim, outside);
                    }
                }

                using (Pen pen = new Pen(Color.FromArgb(0, 174, 255), 2f))
                {
                    e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }
        }

        private void ComputeLayout()
        {
            if (_image == null)
            {
                return;
            }

            float sx = (float)ClientSize.Width / _image.Width;
            float sy = (float)ClientSize.Height / _image.Height;
            _scale = Math.Min(sx, sy);
            float w = _image.Width * _scale;
            float h = _image.Height * _scale;
            _imageRect = new RectangleF((ClientSize.Width - w) / 2f, (ClientSize.Height - h) / 2f, w, h);
        }

        private Point ControlToImage(Point point)
        {
            if (_image == null || _scale <= 0f)
            {
                return Point.Empty;
            }

            int x = (int)Math.Round((point.X - _imageRect.X) / _scale);
            int y = (int)Math.Round((point.Y - _imageRect.Y) / _scale);
            x = Math.Max(0, Math.Min(_image.Width, x));
            y = Math.Max(0, Math.Min(_image.Height, y));
            return new Point(x, y);
        }

        private RectangleF ImageToControl(Rectangle rect)
        {
            return new RectangleF(
                _imageRect.X + rect.X * _scale,
                _imageRect.Y + rect.Y * _scale,
                rect.Width * _scale,
                rect.Height * _scale);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_readOnly || _image == null || e.Button != MouseButtons.Left)
            {
                return;
            }

            _dragging = true;
            _dragStart = ControlToImage(e.Location);
            _selection = new Rectangle(_dragStart, Size.Empty);
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging)
            {
                return;
            }

            Point current = ControlToImage(e.Location);
            _selection = MakeRectangle(_dragStart, current, _lockAspect);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            Capture = false;

            if (_selection.Width < 1 || _selection.Height < 1)
            {
                _selection = Rectangle.Empty;
            }

            Invalidate();
            if (SelectionChanged != null)
            {
                SelectionChanged(this, EventArgs.Empty);
            }
        }

        private static Rectangle MakeRectangle(Point a, Point b, float aspect)
        {
            int dx = b.X - a.X;
            int dy = b.Y - a.Y;

            if (aspect > 0f)
            {
                int w = Math.Abs(dx);
                int h = Math.Abs(dy);
                if (h == 0 || (w > 0 && w / (float)h > aspect))
                {
                    h = Math.Max(1, (int)Math.Round(w / aspect));
                }
                else
                {
                    w = Math.Max(1, (int)Math.Round(h * aspect));
                }
                dx = dx < 0 ? -w : w;
                dy = dy < 0 ? -h : h;
            }

            int x = Math.Min(a.X, a.X + dx);
            int y = Math.Min(a.Y, a.Y + dy);
            int width = Math.Abs(dx);
            int height = Math.Abs(dy);
            return new Rectangle(x, y, width, height);
        }
    }
}
