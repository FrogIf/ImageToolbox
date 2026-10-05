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
        private TrackBar _scaleX, _scaleY, _angle;
        private Label _scaleXV, _scaleYV, _angleV, _offset;
        private int _dx, _dy;
        private int _grab = -1;              // 0..3 四角；4..7 四边中点；8 旋转柄；-1 移动
        private int _dragBaseX, _dragBaseY;
        private Point _dragStart;
        private float _baseAngle;
        private float _baseScaleX = 1f, _baseScaleY = 1f;  // 拖动起始时的缩放比
        private PointF _anchor;              // 缩放时固定的对角/对边点
        private PointF _rotCenter;           // 旋转中心
        private double _grabAngle;           // 旋转开始时指针相对中心的角度
        private bool _syncing;               // 程序化同步滑杆时抑制重复预览
        private bool _dragging;
        private float _previewScale = 1f;
        private Bitmap _preview;             // 复用的预览位图（避免每帧分配整图）
        private Rectangle _content;          // 源坐标下实际内容包围盒（去掉透明边）
        private Bitmap _contentFor;          // _content 对应的 Source，用于缓存

        public TransformOp()
        {
            EditOpUi.Title(this, "变换", 10);
            EditOpUi.Note(this, "画布上拖动移动图层；拖四角等比缩放（锚定对角）、拖四边中点单向拉伸、拖顶部圆柄旋转。放大或旋转超出画布的部分会被裁掉。", 40, 66);

            _scaleX = EditOpUi.Slider(this, "水平", 112, 10, 400, 100, out _scaleXV);
            _scaleX.ValueChanged += delegate
            {
                _scaleXV.Text = _scaleX.Value + "%";
                if (!_syncing) { RaisePreview(); }
            };

            _scaleY = EditOpUi.Slider(this, "垂直", 142, 10, 400, 100, out _scaleYV);
            _scaleY.ValueChanged += delegate
            {
                _scaleYV.Text = _scaleY.Value + "%";
                if (!_syncing) { RaisePreview(); }
            };

            _angle = EditOpUi.Slider(this, "旋转", 182, -180, 180, 0, out _angleV);
            _angle.ValueChanged += delegate
            {
                _angleV.Text = _angle.Value + "°";
                if (!_syncing) { RaisePreview(); }
            };

            _offset = new Label();
            _offset.Location = new Point(10, 222);
            _offset.Size = new Size(250, 20);
            _offset.Text = "位移：0, 0";
            Controls.Add(_offset);

            EditOpUi.Button(this, "重置", 10, 252, 80, delegate { Reset(); });
            _scaleXV.Text = "100%";
            _scaleYV.Text = "100%";
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
            EnsureContent();
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

        // 只在确实存在位移/缩放/旋转时才算“有未应用结果”，避免单击画布也触发提示。
        public override bool HasPendingResult
        {
            get { return !IsIdentity(); }
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
            _scaleX.Value = 100;
            _scaleY.Value = 100;
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
            return _dx == 0 && _dy == 0 && _scaleX.Value == 100 && _scaleY.Value == 100 && _angle.Value == 0;
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
                _baseScaleX = _scaleX.Value / 100f;
                _baseScaleY = _scaleY.Value / 100f;
                if (_grab >= 0 && _grab < 8)
                {
                    PointF uo = U(AnchorIndex(_grab));
                    double a = _baseAngle * Math.PI / 180.0;
                    float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
                    float rxo = cos * (_baseScaleX * uo.X) - sin * (_baseScaleY * uo.Y);
                    float ryo = sin * (_baseScaleX * uo.X) + cos * (_baseScaleY * uo.Y);
                    _anchor = new PointF(PivotX() + _dx + rxo, PivotY() + _dy + ryo);
                }
                else if (_grab == 8)
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
                if (_grab >= 0 && _grab < 8)
                {
                    ApplyScaleDrag(imagePoint);
                }
                else if (_grab == 8)
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
                // 结束事件：仅在确有变换时刷新，避免单击画布把操作标记为“已修改”。
                if (!IsIdentity()) { RaisePreview(); }
            }
        }

        // 在图层坐标里命中手柄：0..3 角，4 旋转，-1 无。
        private int HitHandle(Point p)
        {
            if (Source == null || Canvas == null) { return -1; }
            float factor = _previewScale * Canvas.ViewScale;
            if (factor <= 0.0001f) { return -1; }
            float tol = 8f / factor;
            for (int i = 0; i < 8; i++)
            {
                if (Distance(p, HandleLayer(i)) <= tol) { return i; }
            }
            // 旋转柄按客户区几何计算（放大后会在顶边内侧），再换算回图层坐标比较。
            PointF origin = Canvas.ImageToClient(new PointF(0f, 0f));
            PointF rotClient = RotationHandleClient(QuadClient());
            PointF rotLayer = new PointF(
                (rotClient.X - origin.X) / factor, (rotClient.Y - origin.Y) / factor);
            if (Distance(p, rotLayer) <= tol) { return 8; }
            return -1;
        }

        // 缩放拖动：锚定对角（四角）或对边（四边中点）不动。
        // 四角等比缩放；四边中点只改变对应的 X / Y 方向，实现单向拉伸/压缩。
        private void ApplyScaleDrag(Point p)
        {
            double a = _baseAngle * Math.PI / 180.0;
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);

            PointF ui = U(_grab);
            PointF uo = U(AnchorIndex(_grab));
            float dux = ui.X - uo.X, duy = ui.Y - uo.Y;
            float px = p.X - _anchor.X;
            float py = p.Y - _anchor.Y;
            // 转到未旋转坐标系。
            float lx = cos * px + sin * py;
            float ly = -sin * px + cos * py;

            float sx, sy;
            if (_grab < 4)
            {
                // 四角：相对起始比例等比缩放。
                float denom = _baseScaleX * dux * dux + _baseScaleY * duy * duy;
                if (denom < 0.0001f) { return; }
                float k = (lx * dux + ly * duy) / denom;
                sx = _baseScaleX * k;
                sy = _baseScaleY * k;
            }
            else
            {
                sx = (Math.Abs(dux) > 0.0001f) ? lx / dux : _baseScaleX;
                sy = (Math.Abs(duy) > 0.0001f) ? ly / duy : _baseScaleY;
            }
            if (sx < 0.1f) { sx = 0.1f; }
            if (sx > 4f) { sx = 4f; }
            if (sy < 0.1f) { sy = 0.1f; }
            if (sy > 4f) { sy = 4f; }

            // 缩放后保持锚点不动：中心 = 锚点 - R*(S*对边偏移)。
            float rxo = cos * (sx * uo.X) - sin * (sy * uo.Y);
            float ryo = sin * (sx * uo.X) + cos * (sy * uo.Y);
            _dx = (int)Math.Round(_anchor.X - rxo - PivotX());
            _dy = (int)Math.Round(_anchor.Y - ryo - PivotY());
            SetScaleX((int)Math.Round(sx * 100f));
            SetScaleY((int)Math.Round(sy * 100f));
        }

        // 0<->2、1<->3（对角），4<->6、5<->7（对边）。
        private static int AnchorIndex(int i)
        {
            if (i == 0) { return 2; }
            if (i == 1) { return 3; }
            if (i == 2) { return 0; }
            if (i == 3) { return 1; }
            if (i == 4) { return 6; }
            if (i == 5) { return 7; }
            if (i == 6) { return 4; }
            return 5;
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

        private void SetScaleX(int percent)
        {
            if (percent < _scaleX.Minimum) { percent = _scaleX.Minimum; }
            if (percent > _scaleX.Maximum) { percent = _scaleX.Maximum; }
            _syncing = true;
            _scaleX.Value = percent;
            _syncing = false;
            _scaleXV.Text = percent + "%";
        }

        private void SetScaleY(int percent)
        {
            if (percent < _scaleY.Minimum) { percent = _scaleY.Minimum; }
            if (percent > _scaleY.Maximum) { percent = _scaleY.Maximum; }
            _syncing = true;
            _scaleY.Value = percent;
            _syncing = false;
            _scaleYV.Text = percent + "%";
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

        // 计算并缓存实际内容包围盒（去掉透明区域）；全透明时退回整层。
        private void EnsureContent()
        {
            if (Source == null)
            {
                _content = Rectangle.Empty;
                _contentFor = null;
                return;
            }
            if (_contentFor == Source && _content.Width > 0 && _content.Height > 0) { return; }
            Rectangle r = ImageFilters.ContentBounds(Source);
            if (r.Width <= 0 || r.Height <= 0) { r = new Rectangle(0, 0, Source.Width, Source.Height); }
            _content = r;
            _contentFor = Source;
        }

        // 变换基准点（内容包围盒中心）在源坐标里的位置。
        private float PivotX()
        {
            return (_content.Width > 0) ? _content.X + _content.Width / 2f : Source.Width / 2f;
        }

        private float PivotY()
        {
            return (_content.Height > 0) ? _content.Y + _content.Height / 2f : Source.Height / 2f;
        }

        private PointF Center()
        {
            return new PointF(PivotX() + _dx, PivotY() + _dy);
        }

        // 手柄相对内容中心的偏移（未缩放、未旋转）：
        // 0..3 = 左上/右上/右下/左下；4..7 = 上/右/下/左 边中点。
        private PointF U(int i)
        {
            float hw = _content.Width / 2f;
            float hh = _content.Height / 2f;
            if (i == 0) { return new PointF(-hw, -hh); }
            if (i == 1) { return new PointF(hw, -hh); }
            if (i == 2) { return new PointF(hw, hh); }
            if (i == 3) { return new PointF(-hw, hh); }
            if (i == 4) { return new PointF(0f, -hh); }
            if (i == 5) { return new PointF(hw, 0f); }
            if (i == 6) { return new PointF(0f, hh); }
            return new PointF(-hw, 0f);
        }

        // 手柄在图层坐标里的当前位置（含缩放、旋转、位移）。
        private PointF HandleLayer(int i)
        {
            PointF u = U(i);
            double a = _angle.Value * Math.PI / 180.0;
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
            float sx = _scaleX.Value / 100f, sy = _scaleY.Value / 100f;
            float mx = PivotX() + _dx, my = PivotY() + _dy;
            return new PointF(
                mx + (cos * (sx * u.X) - sin * (sy * u.Y)),
                my + (sin * (sx * u.X) + cos * (sy * u.Y)));
        }

        private PointF[] QuadLayer()
        {
            PointF[] pts = new PointF[4];
            for (int i = 0; i < 4; i++) { pts[i] = HandleLayer(i); }
            return pts;
        }

        // 四角在客户区里的位置（按图层坐标 -> 预览坐标 -> 客户区映射）。
        private PointF[] QuadClient()
        {
            PointF[] pts = new PointF[4];
            for (int i = 0; i < 4; i++)
            {
                PointF h = HandleLayer(i);
                pts[i] = Canvas.ImageToClient(new PointF(h.X * _previewScale, h.Y * _previewScale));
            }
            return pts;
        }

        // 旋转柄在客户区里的位置：顶边中点沿外法线外移 22px。
        // 关键：外移量是否放得下要按**顶边**相对画布控件的位置判断，而不是整张图的上边距——
        // 图片放大后图层顶边可能在控件外，但实际内容包围盒的顶边仍在控件内。
        // 外侧放不下时把柄移到顶边内侧，避免它正好压在包围盒边线上（放大时曾出现该问题）。
        private PointF RotationHandleClient(PointF[] c)
        {
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

            const float desired = 22f;
            const float pad = 7f;   // 柄半径 5 + 余量
            float w = (Canvas != null) ? Canvas.ClientSize.Width : 0f;
            float h = (Canvas != null) ? Canvas.ClientSize.Height : 0f;

            // 从顶边中点沿外法线走到控件边界的最大距离。
            float tMax = float.MaxValue;
            if (nx > 0.0001f) { tMax = Math.Min(tMax, (w - pad - tx) / nx); }
            else if (nx < -0.0001f) { tMax = Math.Min(tMax, (pad - tx) / nx); }
            if (ny > 0.0001f) { tMax = Math.Min(tMax, (h - pad - ty) / ny); }
            else if (ny < -0.0001f) { tMax = Math.Min(tMax, (pad - ty) / ny); }

            float off;
            if (tMax >= desired) { off = desired; }
            else if (tMax >= 12f) { off = tMax; }
            else { off = -desired; }   // 外侧没有空间：放到顶边内侧，保证不与边线重叠且可抓取

            return new PointF(tx + nx * off, ty + ny * off);
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
                    PointF rot = RotationHandleClient(c);
                    g.DrawLine(pen, tx, ty, rot.X, rot.Y);

                    for (int i = 0; i < 8; i++)
                    {
                        PointF hc = layerToClient(HandleLayer(i));
                        g.FillRectangle(fill, hc.X - 4f, hc.Y - 4f, 8f, 8f);
                        g.DrawRectangle(pen, hc.X - 4f, hc.Y - 4f, 8f, 8f);
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
            if (h == 8) { return Cursors.Hand; }
            if (h >= 0 && h < 8)
            {
                PointF hp = HandleLayer(h);
                PointF[] q = QuadLayer();
                float cx = (q[0].X + q[1].X + q[2].X + q[3].X) / 4f;
                float cy = (q[0].Y + q[1].Y + q[2].Y + q[3].Y) / 4f;
                double ang = Math.Atan2(hp.Y - cy, hp.X - cx) * 180.0 / Math.PI;
                double m = ((ang % 180.0) + 180.0) % 180.0;
                if (m < 22.5 || m >= 157.5) { return Cursors.SizeWE; }
                if (m < 67.5) { return Cursors.SizeNWSE; }
                if (m < 112.5) { return Cursors.SizeNS; }
                return Cursors.SizeNESW;
            }
            return Cursors.SizeAll;
        }

        // ---- 结果 ----

        private static void DrawTransformInto(Graphics g, Bitmap source, float pivotX, float pivotY, int dx, int dy, float scaleX, float scaleY, float angle, InterpolationMode mode)
        {
            g.InterpolationMode = mode;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TranslateTransform(pivotX + dx, pivotY + dy);
            g.RotateTransform(angle);
            g.ScaleTransform(scaleX, scaleY);
            g.TranslateTransform(-pivotX, -pivotY);
            g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
        }

        private Bitmap Transform(Bitmap source, int dx, int dy, float scaleX, float scaleY, float angle)
        {
            Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                DrawTransformInto(g, source, PivotX(), PivotY(), dx, dy, scaleX, scaleY, angle, InterpolationMode.HighQualityBicubic);
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
                DrawTransformInto(g, PreviewSource, PivotX() * _previewScale, PivotY() * _previewScale, dx, dy, _scaleX.Value / 100f, _scaleY.Value / 100f, _angle.Value, InterpolationMode.HighQualityBilinear);
            }
            return _preview;
        }

        public override Bitmap BuildResult()
        {
            if (Source == null || IsIdentity()) { return null; }
            return Transform(Source, _dx, _dy, _scaleX.Value / 100f, _scaleY.Value / 100f, _angle.Value);
        }
    }
}
