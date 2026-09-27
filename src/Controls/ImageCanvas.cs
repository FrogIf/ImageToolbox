using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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

        private bool _editingSelection;      // 正在拖动/缩放已有选区
        private int _selHandle = -1;         // 0..3 角，4..7 边中点，8 选区内移动
        private Rectangle _selStartRect;
        private Point _selStartPt;
        private Point _hoverClient;

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
        public event Action<Point> PointerMoved;
        public event Action<Point> PointerDoubleClicked;
        public event Action ViewChanged;

        // 叠加绘制（图像之上，客户区坐标）：编辑器转发给当前操作绘制变换框等。
        public Action<Graphics> OverlayPainter;
        // 悬停光标：输入显示图坐标，返回 null 用默认光标（如变换框手柄）。
        public Func<Point, Cursor> CursorProvider;

        // 文字在画布上就地编辑时置 true：空格键交给输入框，不触发平移。
        public bool TextEditing { get; set; }

        public ImageCanvas()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable |
                ControlStyles.StandardClick |
                ControlStyles.StandardDoubleClick,
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
            if (!_zoomEnabled || TextEditing || !Visible || !Enabled || _image == null)
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

        // 是否允许拖动/缩放已有选区（裁剪、局部覆盖等可交互框选的操作）。
        private bool SelectionEditable
        {
            get { return !_readOnly && !_brushEnabled && !_dragEnabled; }
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
            RaiseViewChanged();
        }

        private const int WM_CTLCOLOREDIT = 0x0133;
        private const int WM_CTLCOLORLISTBOX = 0x0134;
        private const int WM_CTLCOLORSTATIC = 0x0138;
        private const int TRANSPARENT = 1;
        private const int NULL_BRUSH = 5;

        [DllImport("gdi32.dll")]
        private static extern int SetBkMode(IntPtr hdc, int mode);

        [DllImport("gdi32.dll")]
        private static extern uint SetTextColor(IntPtr hdc, int color);

        [DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(int fnObject);

        // 编辑控件的背景色查询会发给父控件（本画布）：返回空画刷（不填充底色）并设置
        // 透明文字模式，使就地文字输入框透出画布图像；同时保留子控件的前景色。
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CTLCOLOREDIT || m.Msg == WM_CTLCOLORSTATIC || m.Msg == WM_CTLCOLORLISTBOX)
            {
                Control child = Control.FromHandle(m.LParam);
                Color fore = (child != null) ? child.ForeColor : Color.Black;
                SetTextColor(m.WParam, ColorTranslator.ToWin32(fore));
                SetBkMode(m.WParam, TRANSPARENT);
                m.Result = GetStockObject(NULL_BRUSH);
                return;
            }
            base.WndProc(ref m);
        }

        // 按本控件的坐标绘制背景（棋盘格 + 图像）。透明子控件也可调用它把自己
        // 所在区域画成图像，从而“透出”画布内容。
        public void PaintView(Graphics g)
        {
            g.Clear(BackColor);

            if (_image == null)
            {
                using (Pen border = new Pen(Color.FromArgb(200, 200, 200)))
                {
                    g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                }
                return;
            }

            ComputeLayout();
            DrawChecker(g, _imageRect);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // 平铺翻转采样：缩放绘制时不让插值核采到图像外的“透明”像素，否则图像四周会
            // 出现一圈半透明边（与棋盘格混出白边）。
            using (ImageAttributes wrap = new ImageAttributes())
            {
                wrap.SetWrapMode(WrapMode.TileFlipXY);
                PointF[] dst =
                {
                    new PointF(_imageRect.Left, _imageRect.Top),
                    new PointF(_imageRect.Right, _imageRect.Top),
                    new PointF(_imageRect.Left, _imageRect.Bottom)
                };
                g.DrawImage(_image, dst, new RectangleF(0f, 0f, _image.Width, _image.Height), GraphicsUnit.Pixel, wrap);
            }
        }

        // 图像画在背景层：这样带 WS_EX_TRANSPARENT 的透明子控件（就地文字输入框）
        // 请求父控件绘制背景时能透出图像。
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            PaintView(e.Graphics);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_image == null)
            {
                return;
            }

            ComputeLayout();

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

                // 可交互选区：画出 8 个手柄，提示可拖动/缩放。
                if (SelectionEditable && !_dragging)
                {
                    PointF[] pts = SelectionHandlePoints();
                    using (Pen pen = new Pen(Color.FromArgb(0, 174, 255), 1f))
                    using (Brush fill = new SolidBrush(Color.White))
                    {
                        for (int i = 0; i < pts.Length; i++)
                        {
                            e.Graphics.FillRectangle(fill, pts[i].X - 4f, pts[i].Y - 4f, 8f, 8f);
                            e.Graphics.DrawRectangle(pen, pts[i].X - 4f, pts[i].Y - 4f, 8f, 8f);
                        }
                    }
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

            if (OverlayPainter != null)
            {
                OverlayPainter(e.Graphics);
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

        // 悬停光标：优先交给 CursorProvider（如变换框手柄），否则回退到默认/拖动光标。
        private void ApplyHoverCursor()
        {
            if (_spaceDown || _panning)
            {
                Cursor = Cursors.SizeAll;
                return;
            }
            if (CursorProvider != null)
            {
                Cursor provided = CursorProvider(_hoverPoint);
                Cursor = (provided != null) ? provided : Cursors.Default;
                return;
            }
            if (SelectionEditable)
            {
                Cursor sc = SelectionCursor();
                if (sc != null) { Cursor = sc; return; }
            }
            UpdateCursor();
        }

        // 悬停时按选区手柄/内部返回光标；null 表示不在选区内。
        private Cursor SelectionCursor()
        {
            if (_selection.Width <= 0 || _selection.Height <= 0) { return null; }
            int h = HitSelectionHandle(_hoverClient);
            if (h >= 0) { return HandleCursor(h); }
            if (_selection.Contains(_hoverPoint)) { return Cursors.SizeAll; }
            return null;
        }

        // 显示图坐标 -> 控件坐标（供文字就地编辑等叠加控件定位）。
        public PointF ImageToClient(PointF imagePoint)
        {
            ComputeLayout();
            return new PointF(_imageRect.X + imagePoint.X * _scale, _imageRect.Y + imagePoint.Y * _scale);
        }

        public float ViewScale
        {
            get { ComputeLayout(); return _scale; }
        }

        private void RaiseViewChanged()
        {
            if (ViewChanged != null) { ViewChanged(); }
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

        // 不裁剪到图像边界的显示图坐标：变换框的手柄（如旋转柄）可能落在图像外的留白区，
        // 仍要能命中，所以拖动/悬停用这个版本。
        private Point ControlToImageRaw(Point point)
        {
            if (_image == null || _scale <= 0f)
            {
                return Point.Empty;
            }

            return new Point(
                (int)Math.Round((point.X - _imageRect.X) / _scale),
                (int)Math.Round((point.Y - _imageRect.Y) / _scale));
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

        // 选区手柄（客户区坐标）：0..3 角，4..7 边中点。
        private PointF[] SelectionHandlePoints()
        {
            RectangleF r = ImageToControl(_selection);
            return new PointF[]
            {
                new PointF(r.Left, r.Top),
                new PointF(r.Right, r.Top),
                new PointF(r.Right, r.Bottom),
                new PointF(r.Left, r.Bottom),
                new PointF((r.Left + r.Right) / 2f, r.Top),
                new PointF(r.Right, (r.Top + r.Bottom) / 2f),
                new PointF((r.Left + r.Right) / 2f, r.Bottom),
                new PointF(r.Left, (r.Top + r.Bottom) / 2f)
            };
        }

        private int HitSelectionHandle(Point client)
        {
            if (_selection.Width <= 0 || _selection.Height <= 0) { return -1; }
            const float tol = 6f;
            PointF[] pts = SelectionHandlePoints();
            for (int i = 0; i < pts.Length; i++)
            {
                if (Math.Abs(client.X - pts[i].X) <= tol && Math.Abs(client.Y - pts[i].Y) <= tol)
                {
                    return i;
                }
            }
            return -1;
        }

        private static Cursor HandleCursor(int h)
        {
            if (h == 0 || h == 2) { return Cursors.SizeNWSE; }
            if (h == 1 || h == 3) { return Cursors.SizeNESW; }
            if (h == 4 || h == 6) { return Cursors.SizeNS; }
            if (h == 5 || h == 7) { return Cursors.SizeWE; }
            return Cursors.SizeAll;
        }

        // 按拖动的手柄更新选区：8 整体移动，0..7 调整对应边（限制在图像内，最小 1px）。
        private void UpdateSelectionEdit(Point img)
        {
            if (_image == null) { return; }
            int w = _image.Width, hgt = _image.Height;

            if (_selHandle == 8)
            {
                int dx = img.X - _selStartPt.X;
                int dy = img.Y - _selStartPt.Y;
                int x = Clamp(_selStartRect.X + dx, 0, w - _selStartRect.Width);
                int y = Clamp(_selStartRect.Y + dy, 0, hgt - _selStartRect.Height);
                _selection = new Rectangle(x, y, _selStartRect.Width, _selStartRect.Height);
                return;
            }

            int left = _selStartRect.Left, top = _selStartRect.Top;
            int right = _selStartRect.Right, bottom = _selStartRect.Bottom;
            bool west = (_selHandle == 0 || _selHandle == 3 || _selHandle == 7);
            bool east = (_selHandle == 1 || _selHandle == 2 || _selHandle == 5);
            bool north = (_selHandle == 0 || _selHandle == 1 || _selHandle == 4);
            bool south = (_selHandle == 2 || _selHandle == 3 || _selHandle == 6);
            if (west) { left = img.X; }
            else if (east) { right = img.X; }
            if (north) { top = img.Y; }
            else if (south) { bottom = img.Y; }

            left = Clamp(left, 0, w);
            right = Clamp(right, 0, w);
            top = Clamp(top, 0, hgt);
            bottom = Clamp(bottom, 0, hgt);
            if (right < left) { int t = left; left = right; right = t; }
            if (bottom < top) { int t = top; top = bottom; bottom = t; }
            if (right - left < 1) { right = Math.Min(w, left + 1); }
            if (bottom - top < 1) { bottom = Math.Min(hgt, top + 1); }
            _selection = new Rectangle(left, top, right - left, bottom - top);
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) { return min; }
            if (v > max) { return max; }
            return v;
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
            _hoverClient = e.Location;
            // 拖动模式（如变换框）需要留白区的真实坐标，不能被裁剪到图像边界。
            _hoverPoint = _dragEnabled ? ControlToImageRaw(e.Location) : ControlToImage(e.Location);

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

            // 已有选区：命中手柄则缩放，点在选区内则整体移动；否则重新框选。
            if (SelectionEditable && _selection.Width > 0 && _selection.Height > 0)
            {
                int hh = HitSelectionHandle(e.Location);
                if (hh >= 0 || _selection.Contains(_hoverPoint))
                {
                    _editingSelection = true;
                    _selHandle = (hh >= 0) ? hh : 8;
                    _selStartRect = _selection;
                    _selStartPt = _hoverPoint;
                    Capture = true;
                    Invalidate();
                    return;
                }
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
                RaiseViewChanged();
                return;
            }

            _hovering = true;
            _hoverClient = e.Location;
            _hoverPoint = _dragEnabled ? ControlToImageRaw(e.Location) : ControlToImage(e.Location);
            ApplyHoverCursor();
            if (PointerMoved != null)
            {
                PointerMoved(_hoverPoint);
            }

            if (_editingSelection)
            {
                UpdateSelectionEdit(_hoverPoint);
                Invalidate();
                return;
            }

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

            if (_editingSelection)
            {
                _editingSelection = false;
                _selHandle = -1;
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

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (_image == null || e.Button != MouseButtons.Left || PointerDoubleClicked == null)
            {
                return;
            }
            PointerDoubleClicked(_dragEnabled ? ControlToImageRaw(e.Location) : ControlToImage(e.Location));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mouseOver = false;
            if (_hovering)
            {
                _hovering = false;
                Cursor = Cursors.Default;
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
