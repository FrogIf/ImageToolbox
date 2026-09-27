using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class CurveOp : EditOpPanel
    {
        private ComboBox _channel;
        private CurveEditor _curve;
        private PointF[][] _curves = new PointF[4][];
        private bool _sync;

        public CurveOp()
        {
            EditOpUi.Title(this, "曲线", 10);
            EditOpUi.Caption(this, "通道", 44);
            _channel = EditOpUi.Combo(this, 64, new string[] { "RGB", "红", "绿", "蓝" }, 0);
            _channel.SelectedIndexChanged += delegate { LoadChannel(); };

            _curve = new CurveEditor();
            _curve.Location = new Point(10, 100);
            _curve.Size = new Size(250, 300);
            _curve.CurveChanged += delegate
            {
                if (_sync) { return; }
                _curves[_channel.SelectedIndex] = _curve.Points;
                RaisePreview();
            };
            Controls.Add(_curve);

            EditOpUi.Button(this, "重置当前通道", 10, 410, 130, delegate { ResetChannel(); });
            EditOpUi.Note(this, "左键添加/拖动控制点，右键删除；曲线向上提亮、向下压暗。", 450, 48);
        }

        private static PointF[] Diagonal()
        {
            return new PointF[] { new PointF(0f, 0f), new PointF(1f, 1f) };
        }

        private void LoadChannel()
        {
            _sync = true;
            _curve.Points = _curves[_channel.SelectedIndex] != null ? _curves[_channel.SelectedIndex] : Diagonal();
            _sync = false;
            RaisePreview();
        }

        private void ResetChannel()
        {
            _curves[_channel.SelectedIndex] = null;
            _sync = true;
            _curve.Points = Diagonal();
            _sync = false;
            RaisePreview();
        }

        protected override void OnResetState()
        {
            for (int i = 0; i < 4; i++) { _curves[i] = null; }
            _sync = true;
            _curve.Points = Diagonal();
            _sync = false;
        }

        private TuningState State()
        {
            TuningState s = new TuningState();
            s.CurvePoints = _curves;
            return s;
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null) { return null; }
            return ImageTuning.Apply(PreviewSource, State());
        }

        public override Bitmap BuildResult()
        {
            if (Source == null) { return null; }
            return ImageTuning.Apply(Source, State());
        }
    }

    public class LocalMaskOp : EditOpPanel
    {
        private ComboBox _mode;
        private TrackBar _rx, _ry, _feather, _angle, _exposure, _contrast, _sat;
        private Label _rxV, _ryV, _featherV, _angleV, _exposureV, _contrastV, _satV;
        private PointF _center = new PointF(0.5f, 0.5f);

        public LocalMaskOp()
        {
            EditOpUi.Title(this, "局部调整", 10);
            EditOpUi.Caption(this, "形状", 44);
            _mode = EditOpUi.Combo(this, 64, new string[] { "径向（椭圆）", "线性（渐变）" }, 0);
            _mode.SelectedIndexChanged += delegate { RaisePreview(); };

            _rx = EditOpUi.Slider(this, "半径X", 100, 5, 100, 30, out _rxV);
            _ry = EditOpUi.Slider(this, "半径Y", 136, 5, 100, 30, out _ryV);
            _feather = EditOpUi.Slider(this, "羽化", 172, 0, 99, 50, out _featherV);
            _angle = EditOpUi.Slider(this, "角度", 208, 0, 180, 0, out _angleV);
            _exposure = EditOpUi.Slider(this, "曝光", 244, -100, 100, 0, out _exposureV);
            _contrast = EditOpUi.Slider(this, "对比度", 280, -100, 100, 0, out _contrastV);
            _sat = EditOpUi.Slider(this, "饱和度", 316, -100, 100, 0, out _satV);
            _rx.ValueChanged += OnChange;
            _ry.ValueChanged += OnChange;
            _feather.ValueChanged += OnChange;
            _angle.ValueChanged += OnChange;
            _exposure.ValueChanged += OnChange;
            _contrast.ValueChanged += OnChange;
            _sat.ValueChanged += OnChange;

            EditOpUi.Button(this, "居中", 10, 354, 80, delegate { _center = new PointF(0.5f, 0.5f); RaisePreview(); });
            EditOpUi.Note(this, "在画布上点击设置中心（径向）或起点（线性）。", 396, 40);
        }

        protected override void OnActivate()
        {
            if (Canvas != null) { Canvas.ReadOnly = true; }
        }

        public override void OnCanvasClick(Point imagePoint)
        {
            if (Source == null) { return; }
            _center = new PointF(
                Math.Max(0f, Math.Min(1f, (float)imagePoint.X / Source.Width)),
                Math.Max(0f, Math.Min(1f, (float)imagePoint.Y / Source.Height)));
            RaisePreview();
        }

        private void OnChange(object sender, EventArgs e)
        {
            _rxV.Text = _rx.Value.ToString();
            _ryV.Text = _ry.Value.ToString();
            _featherV.Text = _feather.Value.ToString();
            _angleV.Text = _angle.Value.ToString();
            _exposureV.Text = _exposure.Value.ToString();
            _contrastV.Text = _contrast.Value.ToString();
            _satV.Text = _sat.Value.ToString();
            RaisePreview();
        }

        protected override void OnResetState()
        {
            _mode.SelectedIndex = 0;
            _rx.Value = 30;
            _ry.Value = 30;
            _feather.Value = 50;
            _angle.Value = 0;
            _exposure.Value = 0;
            _contrast.Value = 0;
            _sat.Value = 0;
            _center = new PointF(0.5f, 0.5f);
        }

        private TuningState State()
        {
            TuningState s = new TuningState();
            s.LocalEnabled = true;
            s.LocalLinear = _mode.SelectedIndex == 1;
            s.LocalCenter = _center;
            s.LocalRadiusX = _rx.Value / 100f;
            s.LocalRadiusY = _ry.Value / 100f;
            s.LocalFeather = _feather.Value / 100f;
            s.LocalAngle = _angle.Value;
            s.LocalExposure = _exposure.Value;
            s.LocalContrast = _contrast.Value;
            s.LocalSaturation = _sat.Value;
            return s;
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null) { return null; }
            return ImageTuning.Apply(PreviewSource, State());
        }

        public override Bitmap BuildResult()
        {
            if (Source == null) { return null; }
            return ImageTuning.Apply(Source, State());
        }
    }

    public class ToningOp : EditOpPanel
    {
        private ComboBox _mode;
        private TrackBar _posterize;
        private Label _posterizeV;
        private ComboBox _gradient;
        private Button _colorA, _colorB;
        private Color _duotoneA = Color.FromArgb(20, 30, 60);
        private Color _duotoneB = Color.FromArgb(245, 230, 200);

        public ToningOp()
        {
            EditOpUi.Title(this, "色调", 10);
            EditOpUi.Caption(this, "方式", 44);
            _mode = EditOpUi.Combo(this, 64, new string[] { "无", "色调分离", "双色调", "渐变映射" }, 0);
            _mode.SelectedIndexChanged += delegate { UpdateMode(); RaisePreview(); };

            _posterize = EditOpUi.Slider(this, "色阶数", 100, 2, 16, 6, out _posterizeV);
            _posterize.ValueChanged += delegate { _posterizeV.Text = _posterize.Value.ToString(); RaisePreview(); };

            _colorA = EditOpUi.Button(this, "暗部色", 10, 140, 130, delegate { PickColor(true); });
            _colorB = EditOpUi.Button(this, "亮部色", 150, 140, 130, delegate { PickColor(false); });

            EditOpUi.Caption(this, "渐变预设", 180);
            string[] names = new string[ImageTuning.Gradients.Length];
            for (int i = 0; i < names.Length; i++) { names[i] = ImageTuning.Gradients[i].Name; }
            _gradient = EditOpUi.Combo(this, 200, names, 0);
            _gradient.SelectedIndexChanged += delegate { RaisePreview(); };

            EditOpUi.Note(this, "色调分离产生色块；双色调/渐变映射按亮度重映射颜色。", 240, 48);
            UpdateMode();
        }

        private void UpdateMode()
        {
            int m = _mode.SelectedIndex;
            _posterizeV.Text = _posterize.Value.ToString();
            SetVisible(_posterize, m == 1);
            _posterizeV.Visible = m == 1;
            _colorA.Visible = _colorB.Visible = (m == 2);
            _gradient.Visible = (m == 3);
        }

        protected override void OnResetState()
        {
            _mode.SelectedIndex = 0;
            _posterize.Value = 6;
        }

        private void SetVisible(Control c, bool v)
        {
            c.Visible = v;
        }

        private void PickColor(bool a)
        {
            ColorDialog dialog = new ColorDialog();
            dialog.Color = a ? _duotoneA : _duotoneB;
            if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
            if (a) { _duotoneA = dialog.Color; } else { _duotoneB = dialog.Color; }
            RaisePreview();
        }

        private TuningState State()
        {
            TuningState s = new TuningState();
            s.ToneMode = _mode.SelectedIndex;
            s.PosterizeLevels = _posterize.Value;
            s.DuotoneA = _duotoneA;
            s.DuotoneB = _duotoneB;
            s.GradientPreset = Math.Max(0, _gradient.SelectedIndex);
            return s;
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null) { return null; }
            return ImageTuning.Apply(PreviewSource, State());
        }

        public override Bitmap BuildResult()
        {
            if (Source == null) { return null; }
            return ImageTuning.Apply(Source, State());
        }
    }

    public class LutOp : EditOpPanel
    {
        private Label _info;
        private TrackBar _strength;
        private Label _strengthV;
        private int _size;
        private float[] _data;

        public LutOp()
        {
            EditOpUi.Title(this, "LUT", 10);
            EditOpUi.Button(this, "加载 .cube 文件", 10, 40, 140, delegate { LoadCube(); });
            _info = EditOpUi.Note(this, "未加载（支持 16³ / 32³ / 64³ 3D LUT）", 78, 40);
            _strength = EditOpUi.Slider(this, "强度", 128, 0, 100, 100, out _strengthV);
            _strength.ValueChanged += delegate { _strengthV.Text = _strength.Value + "%"; RaisePreview(); };
            EditOpUi.Note(this, "加载后自动启用，强度控制叠加程度。", 170, 40);
        }

        private void LoadCube()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择 .cube 文件";
            dialog.Filter = "CUBE 文件|*.cube|所有文件|*.*";
            if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
            int size;
            float[] data;
            if (!ImageTuning.TryLoadCube(dialog.FileName, out size, out data))
            {
                MessageBox.Show(this, "无法解析该 .cube 文件");
                return;
            }
            _size = size;
            _data = data;
            _info.Text = "已加载：" + System.IO.Path.GetFileName(dialog.FileName) + "（" + size + "³）";
            RaisePreview();
        }

        protected override void OnResetState()
        {
            _strength.Value = 0;
        }

        private TuningState State()
        {
            TuningState s = new TuningState();
            s.LutEnabled = _data != null;
            s.LutData = _data;
            s.LutSize = _size;
            s.LutStrength = _strength.Value / 100f;
            return s;
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null) { return null; }
            return ImageTuning.Apply(PreviewSource, State());
        }

        public override Bitmap BuildResult()
        {
            if (Source == null) { return null; }
            return ImageTuning.Apply(Source, State());
        }
    }

    public class CanvasOp : EditOpPanel
    {
        private static readonly string[] Anchors = { "左上", "中上", "右上", "左中", "居中", "右中", "左下", "中下", "右下" };

        private ComboBox _mode;
        private TrackBar _angle, _width, _height;
        private Label _angleV, _widthV, _heightV;
        private CheckBox _autoCrop;
        private ComboBox _fill, _anchor;
        private static readonly Color[] FillColors = { Color.Transparent, Color.White, Color.Black, Color.FromArgb(240, 240, 240) };
        private bool _initSize;

        public override bool DocumentLevel
        {
            get { return true; }
        }

        public CanvasOp()
        {
            EditOpUi.Title(this, "画布 / 校正", 10);
            EditOpUi.Caption(this, "方式", 44);
            _mode = EditOpUi.Combo(this, 64, new string[] { "旋转拉直", "画布尺寸（扩展/裁切）" }, 0);
            _mode.SelectedIndexChanged += delegate { UpdateMode(); RaisePreview(); };

            _angle = EditOpUi.Slider(this, "角度", 100, -45, 45, 0, out _angleV);
            _autoCrop = new CheckBox();
            _autoCrop.Text = "自动裁掉黑边";
            _autoCrop.Location = new Point(10, 138);
            _autoCrop.AutoSize = true;
            _autoCrop.Checked = true;
            _autoCrop.CheckedChanged += delegate { RaisePreview(); };
            Controls.Add(_autoCrop);

            _width = EditOpUi.Slider(this, "宽", 168, 1, 6000, 800, out _widthV);
            _height = EditOpUi.Slider(this, "高", 204, 1, 6000, 600, out _heightV);
            _width.ValueChanged += delegate { _widthV.Text = _width.Value.ToString(); RaisePreview(); };
            _height.ValueChanged += delegate { _heightV.Text = _height.Value.ToString(); RaisePreview(); };
            EditOpUi.Caption(this, "锚点", 240);
            _anchor = EditOpUi.Combo(this, 260, Anchors, 4);
            _anchor.SelectedIndexChanged += delegate { RaisePreview(); };

            EditOpUi.Caption(this, "填充色", 296);
            _fill = EditOpUi.Combo(this, 316, new string[] { "透明", "白色", "黑色", "浅灰" }, 0);
            _fill.SelectedIndexChanged += delegate { RaisePreview(); };

            _angle.ValueChanged += delegate { _angleV.Text = _angle.Value + "°"; RaisePreview(); };
            EditOpUi.Note(this, "旋转拉直用于校正倾斜；画布尺寸可加边或裁切（锚点决定对齐）。", 356, 48);
            UpdateMode();
        }

        protected override void OnActivate()
        {
            if (Source != null && !_initSize)
            {
                _width.Value = Math.Max(1, Math.Min(6000, Source.Width));
                _height.Value = Math.Max(1, Math.Min(6000, Source.Height));
                _widthV.Text = _width.Value.ToString();
                _heightV.Text = _height.Value.ToString();
                _initSize = true;
            }
        }

        private void UpdateMode()
        {
            int m = _mode.SelectedIndex;
            _angleV.Text = _angle.Value + "°";
            SetVisible(_angle, m == 0);
            _angleV.Visible = m == 0;
            _autoCrop.Visible = m == 0;
            SetVisible(_width, m == 1);
            _widthV.Visible = m == 1;
            SetVisible(_height, m == 1);
            _heightV.Visible = m == 1;
            _anchor.Visible = m == 1;
            _fill.Visible = m == 1;
        }

        private void SetVisible(Control c, bool v) { c.Visible = v; }

        protected override void OnResetState()
        {
            _mode.SelectedIndex = 0;
            _angle.Value = 0;
            _autoCrop.Checked = false;
            if (Source != null)
            {
                _width.Value = Math.Max(1, Math.Min(6000, Source.Width));
                _height.Value = Math.Max(1, Math.Min(6000, Source.Height));
                _widthV.Text = _width.Value.ToString();
                _heightV.Text = _height.Value.ToString();
                _initSize = true;
            }
        }

        private Bitmap Build(Bitmap baseImage)
        {
            if (_mode.SelectedIndex == 0)
            {
                return ImageLayout.Rotate(baseImage, _angle.Value, FillColors[Math.Max(0, _fill.SelectedIndex)], _autoCrop.Checked);
            }
            return ImageLayout.ExtendCanvas(
                baseImage, _width.Value, _height.Value,
                (ImageAnchor)Math.Max(0, _anchor.SelectedIndex),
                FillColors[Math.Max(0, _fill.SelectedIndex)]);
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null) { return null; }
            return Build(PreviewSource);
        }

        public override Bitmap BuildResult()
        {
            if (Source == null) { return null; }
            return Build(Source);
        }
    }
}
