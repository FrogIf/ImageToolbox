using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class BasicAdjustOp : EditOpPanel
    {
        private TrackBar _bright, _contrast, _sat, _temp, _tint;
        private Label _brightV, _contrastV, _satV, _tempV, _tintV;

        public BasicAdjustOp()
        {
            EditOpUi.Title(this, "基础", 10);
            _bright = EditOpUi.Slider(this, "亮度", 46, -100, 100, 0, out _brightV);
            _contrast = EditOpUi.Slider(this, "对比度", 82, -100, 100, 0, out _contrastV);
            _sat = EditOpUi.Slider(this, "饱和度", 118, 0, 200, 100, out _satV);
            _temp = EditOpUi.Slider(this, "色温", 154, -100, 100, 0, out _tempV);
            _tint = EditOpUi.Slider(this, "色调", 190, -100, 100, 0, out _tintV);
            EditOpUi.Button(this, "重置", 10, 232, 90, delegate { Reset(); });
            EditOpUi.Note(this, "拖动滑块实时预览，满意后点顶部「应用到图片」。", 274, 40);

            _bright.ValueChanged += OnChange;
            _contrast.ValueChanged += OnChange;
            _sat.ValueChanged += OnChange;
            _temp.ValueChanged += OnChange;
            _tint.ValueChanged += OnChange;
        }

        private void Reset()
        {
            _bright.Value = 0;
            _contrast.Value = 0;
            _sat.Value = 100;
            _temp.Value = 0;
            _tint.Value = 0;
        }

        protected override void OnResetState()
        {
            Reset();
        }

        public override bool HasPendingResult
        {
            get
            {
                return _bright.Value != 0 || _contrast.Value != 0 || _sat.Value != 100 ||
                    _temp.Value != 0 || _tint.Value != 0;
            }
        }

        private void OnChange(object sender, EventArgs e)
        {
            _brightV.Text = _bright.Value.ToString();
            _contrastV.Text = _contrast.Value.ToString();
            _satV.Text = _sat.Value.ToString();
            _tempV.Text = _temp.Value.ToString();
            _tintV.Text = _tint.Value.ToString();
            RaisePreview();
        }

        private ColorMatrix Matrix()
        {
            ColorMatrix m = ImageEffects.Saturation(_sat.Value / 100f);
            m = ImageEffects.Multiply(m, ImageEffects.Contrast(_contrast.Value / 100f));
            m = ImageEffects.Multiply(m, ImageEffects.Brightness(_bright.Value / 100f));
            m = ImageEffects.Multiply(m, ImageEffects.Temperature(_temp.Value / 100f));
            m = ImageEffects.Multiply(m, ImageEffects.Tint(_tint.Value / 100f));
            return m;
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null) { return null; }
            return ImageUtil.ApplyColorMatrix(PreviewSource, Matrix());
        }

        public override Bitmap BuildResult()
        {
            if (Source == null) { return null; }
            return ImageUtil.ApplyColorMatrix(Source, Matrix());
        }
    }

    public class LevelsOp : EditOpPanel
    {
        private HistogramView _hist;
        private TrackBar _black, _white, _gamma;
        private Label _blackV, _whiteV, _gammaV;
        private Bitmap _histSource;

        public LevelsOp()
        {
            EditOpUi.Title(this, "色阶", 10);
            _hist = new HistogramView();
            _hist.Location = new Point(10, 40);
            _hist.Size = new Size(250, 130);
            Controls.Add(_hist);

            _black = EditOpUi.Slider(this, "黑场", 182, 0, 254, 0, out _blackV);
            _white = EditOpUi.Slider(this, "白场", 218, 1, 255, 255, out _whiteV);
            _gamma = EditOpUi.Slider(this, "伽马", 254, 10, 300, 100, out _gammaV);
            EditOpUi.Button(this, "自动色阶", 10, 296, 100, delegate { Auto(); });
            EditOpUi.Button(this, "重置", 118, 296, 80, delegate { Reset(); });
            EditOpUi.Note(this, "黑场以下压黑、白场以上提白、伽马调中间调。", 336, 40);

            _black.ValueChanged += OnChange;
            _white.ValueChanged += OnChange;
            _gamma.ValueChanged += OnChange;
        }

        protected override void OnActivate()
        {
            if (Source != null && !object.ReferenceEquals(_histSource, Source))
            {
                _histSource = Source;
                _hist.SetData(ImageTuning.Histogram(Source));
            }
        }

        private void Auto()
        {
            if (Source == null) { return; }
            int[] black;
            int[] white;
            ImageTuning.AutoLevels(Source, out black, out white);
            _black.Value = Math.Max(0, black[0]);
            _white.Value = Math.Min(255, white[0]);
            RaisePreview();
        }

        private void Reset()
        {
            _black.Value = 0;
            _white.Value = 255;
            _gamma.Value = 100;
        }

        protected override void OnResetState()
        {
            Reset();
        }

        public override bool HasPendingResult
        {
            get { return _black.Value != 0 || _white.Value != 255 || _gamma.Value != 100; }
        }

        private void OnChange(object sender, EventArgs e)
        {
            _blackV.Text = _black.Value.ToString();
            _whiteV.Text = _white.Value.ToString();
            _gammaV.Text = (_gamma.Value / 100f).ToString("0.00");
            RaisePreview();
        }

        private TuningState State()
        {
            TuningState s = new TuningState();
            int b = _black.Value;
            int w = _white.Value;
            float g = _gamma.Value / 100f;
            s.LevelBlack = new int[] { b, b, b };
            s.LevelWhite = new int[] { w, w, w };
            s.LevelGamma = new float[] { g, g, g };
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

    public class WhiteBalanceOp : EditOpPanel
    {
        private float[] _gain = { 1f, 1f, 1f };
        private Label _info;

        public WhiteBalanceOp()
        {
            EditOpUi.Title(this, "白平衡", 10);
            _info = EditOpUi.Note(this, "在左侧图片上点击应为白 / 灰的位置取点校正；或点「自动」。", 46, 60);
            EditOpUi.Button(this, "自动白平衡", 10, 116, 120, delegate { Auto(); });
            EditOpUi.Button(this, "重置", 138, 116, 80, delegate { _gain = new float[] { 1f, 1f, 1f }; _info.Text = "已重置"; RaisePreview(); });
        }

        protected override void OnActivate()
        {
            if (Canvas != null)
            {
                Canvas.ReadOnly = true;
            }
        }

        public override void OnCanvasClick(Point imagePoint)
        {
            if (Source == null) { return; }
            int x = Math.Max(0, Math.Min(Source.Width - 1, imagePoint.X));
            int y = Math.Max(0, Math.Min(Source.Height - 1, imagePoint.Y));
            Color color = Source.GetPixel(x, y);
            _gain = ImageTuning.WhiteBalanceFromColor(color);
            _info.Text = "取样 RGB(" + color.R + "," + color.G + "," + color.B + ")";
            RaisePreview();
        }

        protected override void OnResetState()
        {
            _gain = new float[] { 1f, 1f, 1f };
            _info.Text = "已重置，请在图片上取点或点自动";
        }

        public override bool HasPendingResult
        {
            get
            {
                return Math.Abs(_gain[0] - 1f) > 0.0001f ||
                    Math.Abs(_gain[1] - 1f) > 0.0001f ||
                    Math.Abs(_gain[2] - 1f) > 0.0001f;
            }
        }

        private void Auto()
        {
            if (Source == null) { return; }
            _gain = ImageTuning.AutoWhiteBalance(Source);
            _info.Text = "已自动白平衡";
            RaisePreview();
        }

        private TuningState State()
        {
            TuningState s = new TuningState();
            s.WhiteBalanceGain = _gain;
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

    public class HslOp : EditOpPanel
    {
        private TrackBar _hue, _sat, _light;
        private Label _hueV, _satV, _lightV;

        public HslOp()
        {
            EditOpUi.Title(this, "HSL", 10);
            _hue = EditOpUi.Slider(this, "色相", 46, -180, 180, 0, out _hueV);
            _sat = EditOpUi.Slider(this, "饱和度", 82, -100, 100, 0, out _satV);
            _light = EditOpUi.Slider(this, "明度", 118, -100, 100, 0, out _lightV);
            EditOpUi.Button(this, "重置", 10, 160, 80, delegate { _hue.Value = 0; _sat.Value = 0; _light.Value = 0; });
            EditOpUi.Note(this, "整体旋转色相、增减饱和度与明度。", 202, 40);

            _hue.ValueChanged += OnChange;
            _sat.ValueChanged += OnChange;
            _light.ValueChanged += OnChange;
        }

        private void OnChange(object sender, EventArgs e)
        {
            _hueV.Text = _hue.Value.ToString();
            _satV.Text = _sat.Value.ToString();
            _lightV.Text = _light.Value.ToString();
            RaisePreview();
        }

        protected override void OnResetState()
        {
            _hue.Value = 0;
            _sat.Value = 0;
            _light.Value = 0;
        }

        public override bool HasPendingResult
        {
            get { return _hue.Value != 0 || _sat.Value != 0 || _light.Value != 0; }
        }

        private TuningState State()
        {
            TuningState s = new TuningState();
            s.Hue = _hue.Value;
            s.Saturation = _sat.Value;
            s.Lightness = _light.Value;
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

    public class StyleOp : EditOpPanel
    {
        private static readonly string[] Presets =
        {
            "原图", "冷白明亮", "暖阳", "清新自然", "日系通透", "高级灰", "复古胶片", "青橙",
            "黑白", "高对比黑白", "鲜艳增强", "冷调夜景", "蓝调忧郁", "怀旧泛黄"
        };

        private ComboBox _preset;
        private TrackBar _strength;
        private Label _strengthV;

        public StyleOp()
        {
            EditOpUi.Title(this, "风格预设", 10);
            EditOpUi.Caption(this, "风格", 44);
            _preset = EditOpUi.Combo(this, 64, Presets, 0);
            _preset.SelectedIndexChanged += delegate { RaisePreview(); };
            _strength = EditOpUi.Slider(this, "强度", 100, 0, 100, 100, out _strengthV);
            _strength.ValueChanged += delegate { _strengthV.Text = _strength.Value + "%"; RaisePreview(); };
            EditOpUi.Note(this, "内置 13 种风格滤镜，强度控制叠加程度。", 148, 40);
        }

        protected override void OnResetState()
        {
            _preset.SelectedIndex = 0;
            _strength.Value = 100;
        }

        // 「原图」或强度为 0 时没有实际改动。
        public override bool HasPendingResult
        {
            get { return ((string)_preset.SelectedItem) != "原图" && _strength.Value > 0; }
        }

        // 有选区时只在选区内生效。
        public override bool RespectsSelection
        {
            get { return true; }
        }

        private ColorMatrix Matrix()
        {
            string name = (string)_preset.SelectedItem;
            ColorMatrix baseMatrix = name == "原图" ? ImageEffects.Identity() : ImageEffects.PresetMatrix(name);
            return ImageEffects.Lerp(ImageEffects.Identity(), baseMatrix, _strength.Value / 100f);
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null) { return null; }
            return ImageUtil.ApplyColorMatrix(PreviewSource, Matrix());
        }

        public override Bitmap BuildResult()
        {
            if (Source == null) { return null; }
            return ImageUtil.ApplyColorMatrix(Source, Matrix());
        }
    }

    public class EffectsOp : EditOpPanel
    {
        private static readonly string[] Effects =
        {
            "无", "马赛克", "高斯模糊", "动感模糊", "油画", "素描", "浮雕", "边缘检测",
            "背景虚化", "纸张纹理", "光晕", "圆角", "圆形头像", "投影"
        };

        private ComboBox _effect;
        private Label _p1Label, _p2Label;
        private TrackBar _p1, _p2;
        private Label _p1V, _p2V;

        public EffectsOp()
        {
            EditOpUi.Title(this, "特效", 10);
            EditOpUi.Caption(this, "效果", 44);
            _effect = EditOpUi.Combo(this, 64, Effects, 0);
            _effect.SelectedIndexChanged += delegate { Configure(); RaisePreview(); };

            _p1Label = EditOpUi.Caption(this, "参数", 100);
            _p1 = EditOpUi.Slider(this, "", 118, 1, 80, 8, out _p1V);
            _p2Label = EditOpUi.Caption(this, "", 154);
            _p2 = EditOpUi.Slider(this, "", 172, 0, 180, 0, out _p2V);
            _p1.ValueChanged += delegate { _p1V.Text = _p1.Value.ToString(); RaisePreview(); };
            _p2.ValueChanged += delegate { _p2V.Text = _p2.Value.ToString(); RaisePreview(); };

            EditOpUi.Note(this, "对整张图应用像素级特效，圆形头像/投影会改变画布尺寸。", 214, 48);
            Configure();
        }

        protected override void OnResetState()
        {
            _effect.SelectedIndex = 0;
        }

        // 效果为「无」时没有实际改动（编辑器据此显示整图合成预览，避免透明边缘白边）。
        public override bool HasPendingResult
        {
            get { return _effect.SelectedIndex != 0; }
        }

        // 有选区时只在选区内生效。
        public override bool RespectsSelection
        {
            get { return true; }
        }

        private void Configure()
        {
            string name = (string)_effect.SelectedItem;
            bool none = name == "无";
            _p1Label.Visible = _p1.Visible = _p1V.Visible = !none;
            _p2Label.Visible = _p2.Visible = _p2V.Visible = false;
            if (none)
            {
                _p1Label.Text = "无需参数";
                return;
            }
            switch (name)
            {
                case "马赛克": _p1Label.Text = "块大小"; SetRange(3, 60, 12); break;
                case "高斯模糊": _p1Label.Text = "半径"; SetRange(1, 40, 8); break;
                case "动感模糊": _p1Label.Text = "长度"; SetRange(2, 60, 16); _p2Label.Text = "角度"; ShowP2(0, 180, 0); break;
                case "油画": _p1Label.Text = "笔触"; SetRange(1, 6, 2); _p2Label.Text = "色阶"; ShowP2(4, 24, 12); break;
                case "素描": _p1Label.Text = "强度"; SetRange(0, 100, 80); break;
                case "浮雕": _p1Label.Text = "强度"; SetRange(0, 100, 70); break;
                case "边缘检测": _p1Label.Text = "强度"; SetRange(0, 100, 80); break;
                case "背景虚化": _p1Label.Text = "模糊"; SetRange(1, 40, 14); _p2Label.Text = "清晰区"; ShowP2(0, 90, 45); break;
                case "纸张纹理": _p1Label.Text = "强度"; SetRange(0, 100, 50); break;
                case "光晕": _p1Label.Text = "强度"; SetRange(0, 100, 60); break;
                case "圆角": _p1Label.Text = "半径"; SetRange(0, 300, 60); break;
                case "圆形头像": _p1Label.Text = "无需参数"; break;
                case "投影": _p1Label.Text = "偏移"; SetRange(1, 40, 12); _p2Label.Text = "模糊"; ShowP2(0, 40, 12); break;
            }
        }

        private void SetRange(int min, int max, int value)
        {
            _p1.Minimum = min;
            _p1.Maximum = max;
            _p1.Value = value;
            _p1V.Text = value.ToString();
        }

        private void ShowP2(int min, int max, int value)
        {
            _p2Label.Visible = _p2.Visible = _p2V.Visible = true;
            _p2.Minimum = min;
            _p2.Maximum = max;
            _p2.Value = value;
            _p2V.Text = value.ToString();
        }

        private Bitmap Render(Bitmap baseImage)
        {
            string name = (string)_effect.SelectedItem;
            Bitmap work = ImageFilters.Clone(baseImage);
            switch (name)
            {
                case "马赛克": ImageFilters.Mosaic(work, _p1.Value); break;
                case "高斯模糊": ImageFilters.GaussianBlur(work, _p1.Value); break;
                case "动感模糊": ImageFilters.MotionBlur(work, _p1.Value, _p2.Value); break;
                case "油画": ImageFilters.OilPaint(work, _p1.Value, _p2.Value); break;
                case "素描": ImageFilters.Sketch(work, _p1.Value / 100f); break;
                case "浮雕": ImageFilters.Emboss(work, _p1.Value / 100f); break;
                case "边缘检测": ImageFilters.EdgeDetect(work, _p1.Value / 100f); break;
                case "背景虚化": ImageFilters.BackgroundBlur(work, _p1.Value, _p2.Value / 100f); break;
                case "纸张纹理": ImageFilters.Paper(work, _p1.Value / 100f, 20240607); break;
                case "光晕": ImageFilters.Glow(work, _p1.Value / 100f); break;
                case "圆角": ImageFilters.RoundedCorners(work, _p1.Value); break;
                case "圆形头像":
                    {
                        Bitmap circle = ImageFilters.Circle(baseImage);
                        work.Dispose();
                        work = circle;
                    }
                    break;
                case "投影":
                    {
                        Bitmap shadow = ImageFilters.DropShadow(baseImage, _p1.Value, _p1.Value, _p2.Value, 0.55f);
                        work.Dispose();
                        work = shadow;
                    }
                    break;
            }
            return work;
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null) { return null; }
            return Render(PreviewSource);
        }

        public override Bitmap BuildResult()
        {
            if (Source == null) { return null; }
            return Render(Source);
        }
    }
}
