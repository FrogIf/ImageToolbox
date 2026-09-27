using System;
using System.Drawing;
using System.Windows.Forms;

namespace ImageToolbox
{
    // 调色：把 基础 / 色阶 / 曲线 / 白平衡 / HSL / LUT / 色调 合并为一个操作，用 Tab 组织。
    // TabControl 设 Multiline=false：Tab 放不下时显示左右滚动箭头（整体滑动），保持单行。
    public class ColorGradeOp : EditOpPanel
    {
        private TabControl _tabs;
        private EditOpPanel[] _subs;
        private int _activeSub;
        private bool _revertingTab;

        public ColorGradeOp()
        {
            _subs = new EditOpPanel[]
            {
                new BasicAdjustOp(),
                new LevelsOp(),
                new CurveOp(),
                new WhiteBalanceOp(),
                new HslOp(),
                new LutOp(),
                new ToningOp()
            };
            string[] names = { "基础", "色阶", "曲线", "白平衡", "HSL", "LUT", "色调" };

            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.Multiline = false;      // 单行；放不下时用滚动箭头，避免折成两排

            for (int i = 0; i < _subs.Length; i++)
            {
                TabPage page = new TabPage(names[i]);
                _subs[i].Dock = DockStyle.Fill;
                _subs[i].PreviewInvalidated += delegate { RaisePreview(); };
                _subs[i].ApplyRequested += delegate { RequestApply(); };
                _subs[i].ResetRequested += delegate { RequestReset(); };
                page.Controls.Add(_subs[i]);
                _tabs.TabPages.Add(page);
            }
            _tabs.SelectedIndexChanged += delegate { OnTabChanged(); };
            Controls.Add(_tabs);
        }

        public override bool CanApply
        {
            get { return _subs[_activeSub].CanApply; }
        }

        public override bool HasPendingResult
        {
            get { return _subs[_activeSub].HasPendingResult; }
        }

        public override bool LivePreview
        {
            get { return _subs[_activeSub].LivePreview; }
        }

        public override bool ImmediatePreview
        {
            get { return _subs[_activeSub].ImmediatePreview; }
        }

        public override bool ReusablePreview
        {
            get { return _subs[_activeSub].ReusablePreview; }
        }

        public override bool TryGetPreviewDirtyRect(out Rectangle rect)
        {
            return _subs[_activeSub].TryGetPreviewDirtyRect(out rect);
        }

        public override string Hint
        {
            get { return _subs[_activeSub].Hint; }
        }

        protected override void OnActivate()
        {
            if (Source != null && Canvas != null)
            {
                _subs[_activeSub].Attach(Source, PreviewSource, Canvas);
            }
        }

        protected override void OnDeactivate()
        {
            _subs[_activeSub].Detach();
        }

        protected override void OnResetState()
        {
            for (int i = 0; i < _subs.Length; i++)
            {
                _subs[i].ResetState();
            }
        }

        private void OnTabChanged()
        {
            if (_revertingTab) { return; }
            int index = _tabs.SelectedIndex;
            if (index < 0 || index == _activeSub) { return; }
            // 当前子功能有未应用的调整时先询问（应用 / 放弃 / 取消）。
            if (!NotifySubOpChanging())
            {
                _revertingTab = true;
                _tabs.SelectedIndex = _activeSub;
                _revertingTab = false;
                return;
            }
            _subs[_activeSub].Detach();
            _activeSub = index;
            if (Source != null && Canvas != null)
            {
                _subs[_activeSub].Attach(Source, PreviewSource, Canvas);
            }
            RaisePreview();
        }

        public override Bitmap RenderPreview()
        {
            return _subs[_activeSub].RenderPreview();
        }

        public override Bitmap BuildResult()
        {
            return _subs[_activeSub].BuildResult();
        }

        public override void DisposeResources()
        {
            for (int i = 0; i < _subs.Length; i++)
            {
                _subs[i].DisposeResources();
            }
        }

        public override void OnCanvasClick(Point imagePoint)
        {
            _subs[_activeSub].OnCanvasClick(imagePoint);
        }

        public override void OnCanvasSelection(Rectangle imageRect)
        {
            _subs[_activeSub].OnCanvasSelection(imageRect);
        }

        public override void OnBrushPoint(Point imagePoint, int action)
        {
            _subs[_activeSub].OnBrushPoint(imagePoint, action);
        }

        public override int BrushRadiusSession
        {
            get { return _subs[_activeSub].BrushRadiusSession; }
        }
    }
}
