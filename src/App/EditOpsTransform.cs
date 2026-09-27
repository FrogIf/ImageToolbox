using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ImageToolbox
{
    // 变换：对当前图层做移动 / 缩放 / 旋转（以图层中心为基准）。
    // 画布上叠加一个带手柄的变换框：拖框内移动、拖四角缩放（锚定对角）、拖顶部圆柄旋转；
    // 右侧滑杆与手柄双向联动。放大或旋转超出画布的部分会被裁掉。
    public class TransformOp : EditOpPanel
    {
        private TrackBar _scale, _angle;
        private Label _scaleV, _angleV, _offset;
        private int _dx, _dy;
        private int _grab = -1;              // 0..3 = 四角手柄；4 = 旋转柄；-1 = 移动
        private int _dragBaseX, _dragBaseY;
        private Point _dragStart;
        private float _baseAngle;
        private PointF _anchor;              // 缩放时固定的对角点
        private PointF _rotCenter;           // 旋转中心
        private double _grabAngle;           // 旋转开始时指针相对中心的角度
        private bool _syncing;               // 程序化同步滑杆时抑制重复预览
        private bool _dragging;
        private float _previewScale = 1f;
        private Bitmap _preview;             // 复用的预览位图（避免每帧分配整图）

        public TransformOp()
        {
            EditOpUi.Title(this, "变换", 10);
            EditOpUi.Note(this, "画布上拖动移动图层；拖四角缩放（锚定对角）、拖顶部圆柄旋转。放大或旋转超出画布的部分会被裁掉。", 40, 66);

            _scale = EditOpUi.Slider(this, "缩放", 112, 10, 400, 100, out _scaleV);
            _scale.ValueChanged += delegate
            {
                _scaleV.Text = _scale.Value + "%";
                if (!_syncing) { RaisePreview(); }
            };

            _angle = EditOpUi.Slider(this, "旋转", 152, -180, 180, 0, out _angleV);
            _angle.ValueChanged += delegate
            {
                _angleV.Text = _angle.Value + "°";
                if (!_syncing) { RaisePreview(); }
            };

            _offset = new Label();
            _offset.Location = new Point(10, 192);
            _offset.Size = new Size(250, 20);
            _offset.Text = "位移：0, 0";
            Controls.Add(_offset);

            EditOpUi.Button(this, "重置", 10, 222, 80, delegate { Reset(); });
            _scaleV.Text = "100%";
            _angleV.Text = "0°";
        }

        protected override void OnActivate()
        {
            if (Canvas != null)
            {
                Canvas.ReadOnly = false;
                Canvas.BrushEnabled = false;
            }
            _previewScale = (PreviewSource != null && Source != null) ? (float)PreviewSource.Width / Source.Width : 1f;
            _dragging = false;
            _grab = -1;
            // 预览尺寸变了才重建复用缓冲。
            if (_preview != null && (PreviewSource == null ||
                _preview.Width != PreviewSource.Width || _preview.Height != PreviewSource.Height))
            {
                _preview.Dispose();
                _preview = null;
            }
        }

        public override void DisposeResources()
        {
            if (_preview != null) { _preview.Dispose(); _preview = null; }
        }

        // 预览位图由本操作复用持有（编辑器直接显示、不拷贝/释放），避免拖动时每帧分配整图。
        public override bool ReusablePreview
        {
            get { return true; }
        }

        public override bool WantsCanvasDrag
        {
            get { return true; }
        }

        public override bool WantsTransformBox
        {
            get { return true; }
        }

        // 手柄拖动需要即时重绘变换框，关闭预览防抖。
        public override bool LivePreview
        {
            get { return true; }
        }

        protected override void OnResetState()
        {
            Reset();
        }

        private void Reset()
        {
            _dx = 0;
            _dy = 0;
            _dragging = false;
            _grab = -1;
            _syncing = true;
            _scale.Value = 100;
            _angle.Value = 0;
            _syncing = false;
            UpdateOffsetLabel();
            RaisePreview();
        }

        private void UpdateOffsetLabel()
        {
            if (_offset != null) { _offset.Text = "位移：" + _dx + ", " + _dy; }
        }

        private bool IsIdentity()
        {
            return _dx == 0 && _dy == 0 && _scale.Value == 100 && _angle.Value == 0;
        }

        // ---- 画布交互 ----

        public override void OnCanvasDrag(Point imagePoint, int action)
        {
            if (action == 0)
            {
                _dragging = true;
                _grab = HitHandle(imagePoint);
                _dragStart = imagePoint;
                _dragBaseX = _dx;
                _dragBaseY = _dy;
                _baseAngle = _angle.Value;
                if (_grab >= 0 && _grab < 4)
                {
                    _anchor = QuadLayer()[(_grab + 2) % 4];
                }
                else if (_grab == 4)
                {
                    _rotCenter = Center();
                    _grabAngle = Math.Atan2(imagePoint.Y - _rotCenter.Y, imagePoint.X - _rotCenter.X);
                }
            }
            else if (action == 1)
            {
                if (!_dragging)
                {
                    // 兜底：没有起点时按移动处理。
                    _dragging = true;
                    _grab = -1;
                    _dragStart = imagePoint;
                    _dragBaseX = _dx;
                    _dragBaseY = _dy;
                }
                if (_grab >= 0 && _grab < 4)
                {
                    ApplyScaleDrag(imagePoint);
                }
                else if (_grab == 4)
                {
                    ApplyRotateDrag(imagePoint);
                }
                else
                {
                    _dx = _dragBaseX + (imagePoint.X - _dragStart.X);
                    _dy = _dragBaseY + (imagePoint.Y - _dragStart.Y);
                }
                UpdateOffsetLabel();
                RaisePreview();
            }
            else
            {
                _dragging = false;
                _grab = -1;
                UpdateOffsetLabel();
                RaisePreview();
            }
        }

        // 在图层坐标里命中手柄：0..3 角，4 旋转，-1 无。
        private int HitHandle(Point p)
        {
            if (Source == null || Canvas == null) { return -1; }
            float factor = _previewScale * Canvas.ViewScale;
            if (factor <= 0.0001f) { return -1; }
            float tol = 8f / factor;
            PointF[] q = QuadLayer();
            for (int i = 0; i < 4; i++)
            {
                if (Distance(p, q[i]) <= tol) { return i; }
            }
            if (Distance(p, RotationHandle(q, factor)) <= tol) { return 4; }
            return -1;
        }

        // 拖角缩放：锚定对角不动，沿对角方向解出缩放比。
        private void ApplyScaleDrag(Point p)
        {
            float cx = Source.Width / 2f;
            float cy = Source.Height / 2f;
            double a = _baseAngle * Math.PI / 180.0;
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);

            PointF ui = U(_grab);
            PointF uo = U((_grab + 2) % 4);
            float dix = ui.X - uo.X, diy = ui.Y - uo.Y;
            float len2 = dix * dix + diy * diy;
            if (len2 < 0.0001f) { return; }

            float rx = cos * dix - sin * diy;
            float ry = sin * dix + cos * diy;
            float px = p.X - _anchor.X;
            float py = p.Y - _anchor.Y;
            float s = (px * rx + py * ry) / len2;
            if (s < 0.1f) { s = 0.1f; }
            if (s > 4f) { s = 4f; }

            // 缩放后保持锚点不动：中心 = 锚点 - R*缩放*对角偏移。
            float mrx = cos * uo.X - sin * uo.Y;
            float mry = sin * uo.X + cos * uo.Y;
            _dx = (int)Math.Round(_anchor.X - s * mrx - cx);
            _dy = (int)Math.Round(_anchor.Y - s * mry - cy);
            SetScale((int)Math.Round(s * 100f));
        }

        // 拖旋转柄：绕中心旋转，保持中心不动。
        private void ApplyRotateDrag(Point p)
        {
            double now = Math.Atan2(p.Y - _rotCenter.Y, p.X - _rotCenter.X);
            double deg = _baseAngle + (now - _grabAngle) * 180.0 / Math.PI;
            while (deg > 180.0) { deg -= 360.0; }
            while (deg < -180.0) { deg += 360.0; }
            SetAngle((int)Math.Round(deg));
        }

        private void SetScale(int percent)
        {
            if (percent < _scale.Minimum) { percent = _scale.Minimum; }
            if (percent > _scale.Maximum) { percent = _scale.Maximum; }
            _syncing = true;
            _scale.Value = percent;
            _syncing = false;
        }

        private void SetAngle(int degrees)
        {
            if (degrees < _angle.Minimum) { degrees = _angle.Minimum; }
            if (degrees > _angle.Maximum) { degrees = _angle.Maximum; }
            _syncing = true;
            _angle.Value = degrees;
            _syncing = false;
        }

        // ---- 变换框几何（图层坐标）----

        private PointF Center()
        {
            return new PointF(Source.Width / 2f + _dx, Source.Height / 2f + _dy);
        }

        // 左上 / 右上 / 右下 / 左下（相对中心的偏移）。
        private PointF U(int i)
        {
            float hw = Source.Width / 2f;
            float hh = Source.Height / 2f;
            if (i == 0) { return new PointF(-hw, -hh); }
            if (i == 1) { return new PointF(hw, -hh); }
            if (i == 2) { return new PointF(hw, hh); }
            return new PointF(-hw, hh);
        }

        private PointF[] QuadLayer()
        {
            float cx = Source.Width / 2f;
            float cy = Source.Height / 2f;
            double a = _angle.Value * Math.PI / 180.0;
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
            float s = _scale.Value / 100f;
            float mx = cx + _dx, my = cy + _dy;
            PointF[] pts = new PointF[4];
            for (int i = 0; i < 4; i++)
            {
                PointF u = U(i);
                pts[i] = new PointF(mx + (cos * u.X - sin * u.Y) * s, my + (sin * u.X + cos * u.Y) * s);
            }
            return pts;
        }

        // 顶边中点向外偏移 22 个客户区像素处的旋转柄位置。
        private PointF RotationHandle(PointF[] q, float factor)
        {
            float tx = (q[0].X + q[1].X) / 2f;
            float ty = (q[0].Y + q[1].Y) / 2f;
            float dx = q[1].X - q[0].X;
            float dy = q[1].Y - q[0].Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            float nx = 0f, ny = -1f;
            if (len > 0.0001f)
            {
                nx = -dy / len;
                ny = dx / len;
                float cx = (q[0].X + q[1].X + q[2].X + q[3].X) / 4f;
                float cy = (q[0].Y + q[1].Y + q[2].Y + q[3].Y) / 4f;
                if (nx * (tx - cx) + ny * (ty - cy) < 0f) { nx = -nx; ny = -ny; }
            }
            float off = RotationOffsetClient() / factor;
            return new PointF(tx + nx * off, ty + ny * off);
        }

        // 旋转柄相对顶边的外移量（客户区像素）。留白不足时收进来，
        // 避免柄落到画布控件之外而无法抓取；绘制与命中都用它以保证一致。
        private float RotationOffsetClient()
        {
            if (Canvas == null) { return 22f; }
            float margin = Canvas.ImageToClient(new PointF(0f, 0f)).Y;
            return (margin < 26f) ? Math.Max(0f, margin - 4f) : 22f;
        }

        private static float Distance(Point p, PointF f)
        {
            float dx = p.X - f.X, dy = p.Y - f.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        // ---- 叠加绘制 / 悬停光标 ----

        public override void PaintCanvasOverlay(Graphics g, Func<PointF, PointF> layerToClient)
        {
            if (Source == null || layerToClient == null) { return; }
            PointF[] q = QuadLayer();
            PointF[] c = new PointF[4];
            for (int i = 0; i < 4; i++) { c[i] = layerToClient(q[i]); }

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (Pen pen = new Pen(Color.FromArgb(0, 174, 255), 1.5f))
                using (Brush fill = new SolidBrush(Color.White))
                {
                    g.DrawPolygon(pen, c);

                    float tx = (c[0].X + c[1].X) / 2f;
                    float ty = (c[0].Y + c[1].Y) / 2f;
                    float dx = c[1].X - c[0].X;
                    float dy = c[1].Y - c[0].Y;
                    float len = (float)Math.Sqrt(dx * dx + dy * dy);
                    float nx = 0f, ny = -1f;
                    if (len > 0.0001f)
                    {
                        nx = -dy / len;
                        ny = dx / len;
                        float cx = (c[0].X + c[1].X + c[2].X + c[3].X) / 4f;
                        float cy = (c[0].Y + c[1].Y + c[2].Y + c[3].Y) / 4f;
                        if (nx * (tx - cx) + ny * (ty - cy) < 0f) { nx = -nx; ny = -ny; }
                    }
                    float off = RotationOffsetClient();
                    PointF rot = new PointF(tx + nx * off, ty + ny * off);
                    g.DrawLine(pen, tx, ty, rot.X, rot.Y);

                    for (int i = 0; i < 4; i++)
                    {
                        g.FillRectangle(fill, c[i].X - 4f, c[i].Y - 4f, 8f, 8f);
                        g.DrawRectangle(pen, c[i].X - 4f, c[i].Y - 4f, 8f, 8f);
                    }
                    g.FillEllipse(fill, rot.X - 5f, rot.Y - 5f, 10f, 10f);
                    g.DrawEllipse(pen, rot.X - 5f, rot.Y - 5f, 10f, 10f);
                }
            }
            finally
            {
                g.SmoothingMode = old;
            }
        }

        public override Cursor TransformCursor(Point layerPoint)
        {
            if (Source == null || Canvas == null) { return null; }
            int h = HitHandle(layerPoint);
            if (h == 4) { return Cursors.Hand; }
            if (h >= 0 && h < 4)
            {
                PointF[] q = QuadLayer();
                float cx = (q[0].X + q[1].X + q[2].X + q[3].X) / 4f;
                float cy = (q[0].Y + q[1].Y + q[2].Y + q[3].Y) / 4f;
                double ang = Math.Atan2(q[h].Y - cy, q[h].X - cx) * 180.0 / Math.PI;
                double m = ((ang % 180.0) + 180.0) % 180.0;
                if (m < 22.5 || m >= 157.5) { return Cursors.SizeWE; }
                if (m < 67.5) { return Cursors.SizeNWSE; }
                if (m < 112.5) { return Cursors.SizeNS; }
                return Cursors.SizeNESW;
            }
            return Cursors.SizeAll;
        }

        // ---- 结果 ----

        private static void DrawTransformInto(Graphics g, Bitmap source, int dx, int dy, float scale, float angle, InterpolationMode mode)
        {
            g.InterpolationMode = mode;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float cx = source.Width / 2f;
            float cy = source.Height / 2f;
            g.TranslateTransform(cx + dx, cy + dy);
            g.RotateTransform(angle);
            g.ScaleTransform(scale, scale);
            g.TranslateTransform(-cx, -cy);
            g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
        }

        private Bitmap Transform(Bitmap source, int dx, int dy, float scale, float angle)
        {
            Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                DrawTransformInto(g, source, dx, dy, scale, angle, InterpolationMode.HighQualityBicubic);
            }
            return result;
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null || IsIdentity()) { return null; }
            if (_preview == null || _preview.Width != PreviewSource.Width || _preview.Height != PreviewSource.Height)
            {
                if (_preview != null) { _preview.Dispose(); }
                _preview = new Bitmap(PreviewSource.Width, PreviewSource.Height, PixelFormat.Format32bppArgb);
            }
            int dx = (int)Math.Round(_dx * _previewScale);
            int dy = (int)Math.Round(_dy * _previewScale);
            using (Graphics g = Graphics.FromImage(_preview))
            {
                // 预览随后还会被画布缩小显示，拖动时用双线性即可，快于双三次。
                g.Clear(Color.Transparent);
                DrawTransformInto(g, PreviewSource, dx, dy, _scale.Value / 100f, _angle.Value, InterpolationMode.HighQualityBilinear);
            }
            return _preview;
        }

        public override Bitmap BuildResult()
        {
            if (Source == null || IsIdentity()) { return null; }
            return Transform(Source, _dx, _dy, _scale.Value / 100f, _angle.Value);
        }
    }
}
