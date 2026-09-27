using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class ImageCanvas : Control, IMessageFilter
    {
        private Image _image;
        private Rectangle _selection;
        private bool _readOnly;
        private bool _dragging;
        private Point _dragStart;
        private RectangleF _imageRect;
        private float _scale;
        private float _lockAspect;
        private bool _brushEnabled;
        private int _brushRadius;
        private bool _painting;
        private bool _hovering;
        private Point _hoverPoint;
        private bool _dragEnabled;
        private bool _movingImage;

        private float _zoom = 1f;
        private float _panX;
        private float _panY;
        private int _lastImageW;
        private int _lastImageH;
        private bool _spaceDown;
        private bool _panning;
        private bool _mouseOver;
        private bool _zoomEnabled = true;
        private Point _panStart;
        private float _panStartX;
        private float _panStartY;
        private const float MinZoom = 0.1f;
        private const float MaxZoom = 16f;

        public event EventHandler SelectionChanged;
        public event Action<Point> PixelClicked;
        public event Action<Point> BrushStarted;
        public event Action<Point> BrushMoved;
        public event Action BrushFinished;
        public event Action<Point> DragStarted;
        public event Action<Point> DragMoved;
        public event Action DragFinished;

        public ImageCanvas()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);
            TabStop = true;
            BackColor = Color.FromArgb(245, 245, 245);
        }

        // 画布是否允许缩放/平移（缩略图等只读小图可关闭）。
        public bool ZoomEnabled
        {
            get { return _zoomEnabled; }
            set { _zoomEnabled = value; }
        }

        // 当前缩放倍率（1 = 适应窗口）。
        public float Zoom
        {
            get { return _zoom; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Application.AddMessageFilter(this);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Application.RemoveMessageFilter(this);
            base.OnHandleDestroyed(e);
        }

        // 全局监听空格键：鼠标悬停在画布上时按住空格进入平移模式。
        bool IMessageFilter.PreFilterMessage(ref Message m)
        {
            if (!_zoomEnabled || !Visible || !Enabled || _image == null)
            {
                return false;
            }
            const int WM_KEYDOWN = 0x100;
            const int WM_KEYUP = 0x101;
            const int VK_SPACE = 0x20;
            if (m.Msg == WM_KEYDOWN && (int)m.WParam == VK_SPACE)
            {
                if (_mouseOver && !_spaceDown)
                {
                    _spaceDown = true;
                    UpdateCursor();
                    return true;
                }
            }
            else if (m.Msg == WM_KEYUP && (int)m.WParam == VK_SPACE)
            {
                if (_spaceDown)
                {
                    _spaceDown = false;
                    if (_panning) { EndPan(); }
                    UpdateCursor();
                    return true;
                }
            }
            return false;
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

        public bool BrushEnabled
        {
            get { return _brushEnabled; }
            set
            {
                _brushEnabled = value;
                Invalidate();
            }
        }

        public int BrushRadius
        {
            get { return _brushRadius; }
            set
            {
                _brushRadius = value < 1 ? 1 : value;
                Invalidate();
            }
        }

        // 拖动移动模式：鼠标按下拖动时报告图片像素坐标，用于移动当前图层。
        public bool DragEnabled
        {
            get { return _dragEnabled; }
            set
            {
                _dragEnabled = value;
                UpdateCursor();
                Invalidate();
            }
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
            SetImage(image, false);
        }

        // keepSelection=true 用于预览刷新：保持正在框选的选区，避免每帧被清空。
        public void SetImage(Image image, bool keepSelection)
        {
            // 首次载入或画布尺寸变化时复位缩放/平移；预览刷新（同尺寸）保留视图。
            // 注意：上一张显示图可能已被释放，所以用记录的上次尺寸比较，不能读 _image.Width。
            bool resetView = false;
            if (image == null) { resetView = true; }
            else if (_image == null) { resetView = true; }
            else if (_lastImageW != image.Width || _lastImageH != image.Height) { resetView = true; }
            _image = image;
            _lastImageW = (image == null) ? 0 : image.Width;
            _lastImageH = (image == null) ? 0 : image.Height;
            if (resetView)
            {
                _zoom = 1f;
                _panX = 0f;
                _panY = 0f;
                _panning = false;
            }
            if (!keepSelection)
            {
                _selection = Rectangle.Empty;
            }
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!_zoomEnabled || _image == null)
            {
                return;
            }

            ComputeLayout();
            float factor = e.Delta > 0 ? 1.25f : (1f / 1.25f);
            float newZoom = _zoom * factor;
            if (newZoom < MinZoom) { newZoom = MinZoom; }
            if (newZoom > MaxZoom) { newZoom = MaxZoom; }
            if (Math.Abs(newZoom - _zoom) < 0.0001f) { return; }

            // 以鼠标位置为锚点缩放：缩放前后该点在画布上的位置保持不变。
            float ix = (e.X - _imageRect.X) / _scale;
            float iy = (e.Y - _imageRect.Y) / _scale;
            _zoom = newZoom;
            ComputeLayout();
            float cx = (ClientSize.Width - _image.Width * _scale) / 2f;
            float cy = (ClientSize.Height - _image.Height * _scale) / 2f;
            _panX = (e.X - ix * _scale) - cx;
            _panY = (e.Y - iy * _scale) - cy;
            ComputeLayout();
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

            DrawChecker(e.Graphics, _imageRect);

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

            if (_brushEnabled && _hovering)
            {
                float radius = _brushRadius * _scale;
                PointF center = ImagePointToControl(_hoverPoint);
                using (Pen pen = new Pen(Color.FromArgb(220, 0, 0, 0), 2f))
                using (Pen pen2 = new Pen(Color.FromArgb(220, 255, 255, 255), 1f))
                {
                    e.Graphics.DrawEllipse(pen, center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
                    e.Graphics.DrawEllipse(pen2, center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
                }
            }
        }

        private static Bitmap _checker;

        private static Bitmap CheckerTile()
        {
            if (_checker == null)
            {
                int cell = 8;
                _checker = new Bitmap(cell * 2, cell * 2);
                using (Graphics g = Graphics.FromImage(_checker))
                {
                    g.Clear(Color.FromArgb(255, 255, 255, 255));
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, 205, 205, 205)))
                    {
                        g.FillRectangle(brush, 0, 0, cell, cell);
                        g.FillRectangle(brush, cell, cell, cell, cell);
                    }
                }
            }
            return _checker;
        }

        private static TextureBrush _checkerBrush;

        private static void DrawChecker(Graphics g, RectangleF rect)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
            {
                return;
            }
            if (_checkerBrush == null)
            {
                _checkerBrush = new TextureBrush(CheckerTile());
                _checkerBrush.WrapMode = WrapMode.Tile;
            }
            _checkerBrush.ResetTransform();
            _checkerBrush.TranslateTransform(rect.X, rect.Y);
            g.FillRectangle(_checkerBrush, rect);
        }

        private void ComputeLayout()
        {
            if (_image == null)
            {
                return;
            }

            float fit = Math.Min((float)ClientSize.Width / _image.Width, (float)ClientSize.Height / _image.Height);
            _scale = fit * _zoom;
            float w = _image.Width * _scale;
            float h = _image.Height * _scale;

            float baseX = (ClientSize.Width - w) / 2f;
            float baseY = (ClientSize.Height - h) / 2f;
            float x, y;
            if (w <= ClientSize.Width)
            {
                x = baseX;
                _panX = 0f;
            }
            else
            {
                x = baseX + _panX;
                float minX = ClientSize.Width - w;
                if (x > 0f) { x = 0f; }
                if (x < minX) { x = minX; }
                _panX = x - baseX;
            }
            if (h <= ClientSize.Height)
            {
                y = baseY;
                _panY = 0f;
            }
            else
            {
                y = baseY + _panY;
                float minY = ClientSize.Height - h;
                if (y > 0f) { y = 0f; }
                if (y < minY) { y = minY; }
                _panY = y - baseY;
            }
            _imageRect = new RectangleF(x, y, w, h);
        }

        private void BeginPan(Point p)
        {
            ComputeLayout();
            _panning = true;
            _panStart = p;
            _panStartX = _panX;
            _panStartY = _panY;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }

        private void EndPan()
        {
            _panning = false;
            Capture = false;
            UpdateCursor();
        }

        private void UpdateCursor()
        {
            if (_spaceDown || _panning || _dragEnabled)
            {
                Cursor = Cursors.SizeAll;
            }
            else
            {
                Cursor = Cursors.Default;
            }
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

        private PointF ImagePointToControl(Point point)
        {
            return new PointF(_imageRect.X + point.X * _scale, _imageRect.Y + point.Y * _scale);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_image == null)
            {
                return;
            }
            _mouseOver = true;

            if (_zoomEnabled && (_spaceDown || e.Button == MouseButtons.Middle))
            {
                BeginPan(e.Location);
                return;
            }

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            _hovering = true;
            _hoverPoint = ControlToImage(e.Location);

            if (_dragEnabled)
            {
                _movingImage = true;
                Capture = true;
                if (DragStarted != null)
                {
                    DragStarted(_hoverPoint);
                }
                return;
            }

            if (_brushEnabled)
            {
                if (_readOnly)
                {
                    Invalidate();
                    return;
                }
                _painting = true;
                Capture = true;
                Invalidate();
                if (BrushStarted != null)
                {
                    BrushStarted(_hoverPoint);
                }
                return;
            }

            if (_readOnly)
            {
                return;
            }

            _dragging = true;
            _dragStart = _hoverPoint;
            _selection = new Rectangle(_dragStart, Size.Empty);
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_image == null)
            {
                return;
            }

            if (_panning)
            {
                _panX = _panStartX + (e.X - _panStart.X);
                _panY = _panStartY + (e.Y - _panStart.Y);
                ComputeLayout();
                Invalidate();
                return;
            }

            _hovering = true;
            _hoverPoint = ControlToImage(e.Location);

            if (_dragEnabled)
            {
                if (_movingImage && DragMoved != null)
                {
                    DragMoved(_hoverPoint);
                }
                return;
            }

            if (_brushEnabled)
            {
                if (_painting && !_readOnly)
                {
                    Invalidate();
                    if (BrushMoved != null)
                    {
                        BrushMoved(_hoverPoint);
                    }
                    return;
                }
                Invalidate();
                return;
            }

            if (!_dragging)
            {
                return;
            }

            _selection = MakeRectangle(_dragStart, _hoverPoint, _lockAspect);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_panning)
            {
                EndPan();
                return;
            }

            if (_image == null)
            {
                return;
            }

            if (_dragEnabled)
            {
                if (_movingImage)
                {
                    _movingImage = false;
                    Capture = false;
                    if (DragFinished != null)
                    {
                        DragFinished();
                    }
                }
                return;
            }

            if (_brushEnabled)
            {
                if (!_painting)
                {
                    return;
                }
                _painting = false;
                Capture = false;
                Invalidate();
                if (BrushFinished != null)
                {
                    BrushFinished();
                }
                return;
            }

            if (e.Button == MouseButtons.Left && PixelClicked != null)
            {
                PixelClicked(ControlToImage(e.Location));
            }

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

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _mouseOver = true;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mouseOver = false;
            if (_hovering)
            {
                _hovering = false;
                Invalidate();
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
