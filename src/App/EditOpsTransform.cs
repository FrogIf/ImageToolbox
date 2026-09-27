using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ImageToolbox
{
    // 变换：对当前图层做移动 / 缩放 / 旋转（以图层中心为基准）。
    // 在画布上按住左键拖动即移动；缩放/旋转用右侧滑杆。
    public class TransformOp : EditOpPanel
    {
        private TrackBar _scale, _angle;
        private Label _scaleV, _angleV, _offset;
        private int _dx, _dy;
        private bool _dragging;
        private Point _dragStart;
        private int _dragBaseX, _dragBaseY;
        private float _previewScale = 1f;

        public TransformOp()
        {
            EditOpUi.Title(this, "变换", 10);
            EditOpUi.Note(this, "在画布上按住左键拖动可移动图层；缩放 / 旋转以图层中心为基准。放大或旋转超出画布的部分会被裁掉。", 40, 66);

            _scale = EditOpUi.Slider(this, "缩放", 112, 10, 400, 100, out _scaleV);
            _scale.ValueChanged += delegate { _scaleV.Text = _scale.Value + "%"; RaisePreview(); };

            _angle = EditOpUi.Slider(this, "旋转", 152, -180, 180, 0, out _angleV);
            _angle.ValueChanged += delegate { _angleV.Text = _angle.Value + "°"; RaisePreview(); };

            _offset = new Label();
            _offset.Location = new Point(10, 192);
            _offset.Size = new Size(300, 20);
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
        }

        public override bool WantsCanvasDrag
        {
            get { return true; }
        }

        public override void OnCanvasDrag(Point imagePoint, int action)
        {
            if (action == 0)
            {
                _dragging = true;
                _dragStart = imagePoint;
                _dragBaseX = _dx;
                _dragBaseY = _dy;
            }
            else if (action == 1 && _dragging)
            {
                _dx = _dragBaseX + (imagePoint.X - _dragStart.X);
                _dy = _dragBaseY + (imagePoint.Y - _dragStart.Y);
                UpdateOffsetLabel();
                RaisePreview();
            }
            else if (action == 2)
            {
                _dragging = false;
                UpdateOffsetLabel();
                RaisePreview();
            }
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
            _scale.Value = 100;
            _angle.Value = 0;
            _scaleV.Text = "100%";
            _angleV.Text = "0°";
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

        private Bitmap Transform(Bitmap source, int dx, int dy, float scale, float angle)
        {
            Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float cx = source.Width / 2f;
                float cy = source.Height / 2f;
                g.TranslateTransform(cx + dx, cy + dy);
                g.RotateTransform(angle);
                g.ScaleTransform(scale, scale);
                g.TranslateTransform(-cx, -cy);
                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
            }
            return result;
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null || IsIdentity()) { return null; }
            int dx = (int)Math.Round(_dx * _previewScale);
            int dy = (int)Math.Round(_dy * _previewScale);
            return Transform(PreviewSource, dx, dy, _scale.Value / 100f, _angle.Value);
        }

        public override Bitmap BuildResult()
        {
            if (Source == null || IsIdentity()) { return null; }
            return Transform(Source, _dx, _dy, _scale.Value / 100f, _angle.Value);
        }
    }
}
