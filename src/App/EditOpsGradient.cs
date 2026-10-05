using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ImageToolbox
{
    // 渐变：在画布上按住拖动确定渐变的方向与范围，起点色 ->（中间色…）-> 终点色 生成渐变叠加到当前图层。
    // 支持 线性 / 径向 / 角度 / 对称 / 菱形，可选混合模式与不透明度，可反转。
    // 中间色可按位置百分比增删/改色。与图层锁配合：锁定透明像素时只在已有像素上生效，锁定图像像素时被拦截。
    public class GradientOp : EditOpPanel
    {
        // 一个中间色标：位置 0..1（不含端点）+ 颜色。
        private class GradStop
        {
            public float Pos;
            public Color Color;
            public GradStop(float pos, Color color) { Pos = pos; Color = color; }
        }

        private ComboBox _type;
        private ComboBox _mode;
        private TrackBar _opacity;
        private Label _opacityV;
        private CheckBox _invert;
        private Panel _swatchA, _swatchB;

        private ListBox _midList;
        private TrackBar _stopPos;
        private Label _stopPosV;
        private readonly List<GradStop> _midStops = new List<GradStop>();
        private bool _updatingStops;

        private Color _colorA = Color.Black;
        private Color _colorB = Color.White;

        private bool _hasGradient;
        private Point _start;
        private Point _end;

        private float _previewScale = 1f;
        private Bitmap _preview;   // 复用的预览位图，避免拖动时每帧分配

        public GradientOp()
        {
            EditOpUi.Title(this, "渐变", 10);
            EditOpUi.Caption(this, "类型", 44);
            _type = EditOpUi.Combo(this, 64, new string[] { "线性", "径向", "角度", "对称", "菱形" }, 0);
            _type.SelectedIndexChanged += delegate { RaisePreview(); };

            EditOpUi.Caption(this, "颜色", 100);
            _swatchA = MakeSwatch(10, 120, _colorA);
            EditOpUi.Button(this, "起点色", 46, 121, 70, delegate { PickColor(true); });
            _swatchB = MakeSwatch(128, 120, _colorB);
            EditOpUi.Button(this, "终点色", 164, 121, 70, delegate { PickColor(false); });

            _invert = new CheckBox();
            _invert.Text = "反转";
            _invert.Location = new Point(10, 150);
            _invert.AutoSize = true;
            _invert.CheckedChanged += delegate { RaisePreview(); };
            Controls.Add(_invert);

            EditOpUi.Caption(this, "中间色", 176);
            _midList = new ListBox();
            _midList.Location = new Point(10, 196);
            _midList.Size = new Size(250, 62);
            _midList.IntegralHeight = false;
            _midList.SelectedIndexChanged += delegate { SyncStopControls(); };
            Controls.Add(_midList);
            EditOpUi.Button(this, "添加", 10, 264, 78, delegate { AddStop(); });
            EditOpUi.Button(this, "改色", 94, 264, 78, delegate { EditStop(); });
            EditOpUi.Button(this, "删除", 178, 264, 78, delegate { RemoveStop(); });

            _stopPos = EditOpUi.Slider(this, "位置", 294, 1, 99, 50, out _stopPosV);
            _stopPos.ValueChanged += delegate { ChangeStopPos(); };

            _opacity = EditOpUi.Slider(this, "不透明度", 328, 0, 100, 100, out _opacityV);
            _opacity.ValueChanged += delegate { _opacityV.Text = _opacity.Value + "%"; RaisePreview(); };

            EditOpUi.Caption(this, "混合模式", 358);
            _mode = EditOpUi.Combo(this, 378, ImageBlend.ModeNames, 0);
            _mode.SelectedIndexChanged += delegate { RaisePreview(); };

            EditOpUi.Note(this, "在画布上按住拖动确定渐变方向与范围。中间色用「添加」按最大间隔插入，选中后可改色、拖「位置」调整；「反转」颠倒整条色带。", 412, 60);
            SyncStopControls();
        }

        private Panel MakeSwatch(int x, int y, Color color)
        {
            Panel p = new Panel();
            p.Location = new Point(x, y);
            p.Size = new Size(30, 24);
            p.BorderStyle = BorderStyle.FixedSingle;
            p.BackColor = color;
            Controls.Add(p);
            return p;
        }

        protected override void OnActivate()
        {
            if (Canvas != null)
            {
                Canvas.ReadOnly = false;
                Canvas.BrushEnabled = false;
            }
            _previewScale = (PreviewSource != null && Source != null) ? (float)PreviewSource.Width / Source.Width : 1f;
        }

        public override void DisposeResources()
        {
            if (_preview != null) { _preview.Dispose(); _preview = null; }
        }

        public override bool ReusablePreview
        {
            get { return true; }
        }

        public override bool WantsCanvasDrag
        {
            get { return true; }
        }

        public override bool LivePreview
        {
            get { return true; }
        }

        // 只有拖出过渐变才算“有未应用结果”。
        public override bool HasPendingResult
        {
            get { return _hasGradient; }
        }

        protected override void OnResetState()
        {
            // 只清掉“已拖出的渐变”，保留用户调好的颜色/中间色，方便连续使用。
            _hasGradient = false;
            RaisePreview();
        }

        public override void OnCanvasDrag(Point imagePoint, int action)
        {
            if (action == 0)
            {
                _start = imagePoint;
                _end = imagePoint;
                _hasGradient = false;
            }
            else if (action == 1)
            {
                _end = imagePoint;
                _hasGradient = true;
            }
            // action==2 是“松开”，编辑器派发的是 Point.Empty（不是真实坐标）：
            // 这里必须保留 action 1 记下的终点，否则一松手渐变范围就跳回左上角。
            RaisePreview();
        }

        private void PickColor(bool start)
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = start ? _colorA : _colorB;
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
                if (start) { _colorA = dialog.Color; _swatchA.BackColor = _colorA; }
                else { _colorB = dialog.Color; _swatchB.BackColor = _colorB; }
                RaisePreview();
            }
        }

        // ---- 中间色管理 ----

        private void AddStop()
        {
            // 在最宽的一段里插一个中间色，默认取该位置的插值色。
            float pos = 0.5f;
            float bestGap = -1f;
            float prev = 0f;
            for (int k = 0; k <= _midStops.Count; k++)
            {
                float cur = (k < _midStops.Count) ? _midStops[k].Pos : 1f;
                if (cur - prev > bestGap) { bestGap = cur - prev; pos = (prev + cur) / 2f; }
                prev = cur;
            }
            if (pos < 0.01f) { pos = 0.01f; }
            if (pos > 0.99f) { pos = 0.99f; }
            _midStops.Add(new GradStop(pos, SampleColor(pos)));
            SortStops();
            RefreshMidList(pos);
            RaisePreview();
        }

        private void EditStop()
        {
            int i = _midList.SelectedIndex;
            if (i < 0 || i >= _midStops.Count) { return; }
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = _midStops[i].Color;
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
                _midStops[i].Color = dialog.Color;
                RefreshMidList(_midStops[i].Pos);
                RaisePreview();
            }
        }

        private void RemoveStop()
        {
            int i = _midList.SelectedIndex;
            if (i < 0 || i >= _midStops.Count) { return; }
            _midStops.RemoveAt(i);
            RefreshMidList(-1f);
            RaisePreview();
        }

        private void ChangeStopPos()
        {
            if (_updatingStops) { return; }
            int i = _midList.SelectedIndex;
            if (i < 0 || i >= _midStops.Count) { return; }
            _stopPosV.Text = _stopPos.Value + "%";
            GradStop moved = _midStops[i];
            moved.Pos = _stopPos.Value / 100f;
            SortStops();
            RefreshMidList(moved.Pos);
            RaisePreview();
        }

        private void SortStops()
        {
            _midStops.Sort(delegate(GradStop a, GradStop b) { return a.Pos.CompareTo(b.Pos); });
        }

        private void RefreshMidList(float selectPos)
        {
            _updatingStops = true;
            _midList.Items.Clear();
            int sel = -1;
            for (int i = 0; i < _midStops.Count; i++)
            {
                _midList.Items.Add(FormatStop(_midStops[i]));
                if (sel < 0 && Math.Abs(_midStops[i].Pos - selectPos) < 0.005f) { sel = i; }
            }
            if (sel < 0 && _midStops.Count > 0) { sel = 0; }
            _midList.SelectedIndex = sel;
            _updatingStops = false;
            SyncStopControls();
        }

        private void SyncStopControls()
        {
            int i = _midList.SelectedIndex;
            bool has = i >= 0 && i < _midStops.Count;
            _stopPos.Enabled = has;
            _updatingStops = true;
            if (has)
            {
                int v = (int)Math.Round(_midStops[i].Pos * 100f);
                if (v < _stopPos.Minimum) { v = _stopPos.Minimum; }
                if (v > _stopPos.Maximum) { v = _stopPos.Maximum; }
                _stopPos.Value = v;
                _stopPosV.Text = v + "%";
            }
            else
            {
                _stopPosV.Text = "-";
            }
            _updatingStops = false;
        }

        private static string FormatStop(GradStop s)
        {
            return ((int)Math.Round(s.Pos * 100f)) + "%   " + ColorTranslator.ToHtml(s.Color);
        }

        // 在位置 t(0..1) 处按 起点色 -> 中间色… -> 终点色 取插值色。
        private Color SampleColor(float t)
        {
            if (t <= 0f) { return _colorA; }
            if (t >= 1f) { return _colorB; }
            float prevPos = 0f;
            Color prevCol = _colorA;
            for (int i = 0; i < _midStops.Count; i++)
            {
                GradStop s = _midStops[i];
                if (t <= s.Pos)
                {
                    float f = (s.Pos > prevPos) ? (t - prevPos) / (s.Pos - prevPos) : 0f;
                    return Lerp(prevCol, s.Color, f);
                }
                prevPos = s.Pos;
                prevCol = s.Color;
            }
            float ff = (1f > prevPos) ? (t - prevPos) / (1f - prevPos) : 0f;
            return Lerp(prevCol, _colorB, ff);
        }

        private static Color Lerp(Color a, Color b, float f)
        {
            if (f < 0f) { f = 0f; }
            if (f > 1f) { f = 1f; }
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * f + 0.5f),
                (int)(a.R + (b.R - a.R) * f + 0.5f),
                (int)(a.G + (b.G - a.G) * f + 0.5f),
                (int)(a.B + (b.B - a.B) * f + 0.5f));
        }

        // ---- 结果 ----

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null || !_hasGradient) { return null; }
            return Build(PreviewSource, true);
        }

        public override Bitmap BuildResult()
        {
            if (Source == null || !_hasGradient) { return null; }
            return Build(Source, false);
        }

        private Bitmap Build(Bitmap baseImage, bool preview)
        {
            float s = preview ? _previewScale : 1f;
            int w = baseImage.Width, h = baseImage.Height;

            Bitmap acc;
            if (preview)
            {
                if (_preview == null || _preview.Width != w || _preview.Height != h)
                {
                    if (_preview != null) { _preview.Dispose(); }
                    _preview = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                }
                acc = _preview;
            }
            else
            {
                acc = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            }
            CopyInto(acc, baseImage);

            using (Bitmap grad = BuildGradientBitmap(w, h,
                _start.X * s, _start.Y * s, _end.X * s, _end.Y * s,
                _invert.Checked, Math.Max(0, _type.SelectedIndex)))
            {
                ImageBlend.CompositeInto(acc, grad, (BlendMode)Math.Max(0, _mode.SelectedIndex), _opacity.Value / 100f, 0, 0);
            }
            return acc;
        }

        private static void CopyInto(Bitmap dst, Bitmap src)
        {
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImageUnscaled(src, 0, 0);
            }
        }

        // 逐像素生成渐变覆盖图（alpha=覆盖度 255），叠加阶段再乘不透明度与混合模式。
        // 颜色沿 起点色 -> 中间色… -> 终点色 插值；反转即按 1-t 取色。
        private Bitmap BuildGradientBitmap(int w, int h, float x0, float y0, float x1, float y1,
            bool invert, int type)
        {
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = d.Stride;
                byte[] buf = new byte[stride * h];

                float dxv = x1 - x0, dyv = y1 - y0;
                float len2 = dxv * dxv + dyv * dyv;
                if (len2 < 0.0001f) { len2 = 0.0001f; }
                float len = (float)Math.Sqrt(len2);
                float lenL1 = Math.Abs(dxv) + Math.Abs(dyv);
                if (lenL1 < 0.0001f) { lenL1 = 0.0001f; }
                double baseAng = Math.Atan2(dyv, dxv);
                const double TwoPi = Math.PI * 2.0;

                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    float dy = y - y0;
                    for (int x = 0; x < w; x++)
                    {
                        float dx = x - x0;
                        float t;
                        if (type == 1)          // 径向
                        {
                            t = (float)Math.Sqrt(dx * dx + dy * dy) / len;
                        }
                        else if (type == 2)     // 角度
                        {
                            double a = Math.Atan2(dy, dx) - baseAng;
                            while (a < 0.0) { a += TwoPi; }
                            while (a >= TwoPi) { a -= TwoPi; }
                            t = (float)(a / TwoPi);
                        }
                        else if (type == 3)     // 对称
                        {
                            float lin = (dx * dxv + dy * dyv) / len2;
                            t = 1f - Math.Abs(2f * lin - 1f);
                        }
                        else if (type == 4)     // 菱形
                        {
                            t = (Math.Abs(dx) + Math.Abs(dy)) / lenL1;
                        }
                        else                    // 线性
                        {
                            t = (dx * dxv + dy * dyv) / len2;
                        }
                        if (t < 0f) { t = 0f; }
                        if (t > 1f) { t = 1f; }
                        if (invert) { t = 1f - t; }

                        Color c = SampleColor(t);
                        int i = row + x * 4;
                        buf[i] = c.B;
                        buf[i + 1] = c.G;
                        buf[i + 2] = c.R;
                        buf[i + 3] = 255;
                    }
                }
                Marshal.Copy(buf, 0, d.Scan0, buf.Length);
            }
            finally
            {
                bmp.UnlockBits(d);
            }
            return bmp;
        }
    }
}
