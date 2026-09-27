using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ImageToolbox
{
    public abstract class EditOpPanel : UserControl
    {
        public event EventHandler PreviewInvalidated;
        public event EventHandler ApplyRequested;
        public event EventHandler ResetRequested;

        protected Bitmap Source;
        protected Bitmap PreviewSource;
        protected ImageCanvas Canvas;

        public virtual bool CanApply
        {
            get { return true; }
        }

        public virtual bool LivePreview
        {
            get { return false; }
        }

        public virtual bool WantsEntrySnapshot
        {
            get { return false; }
        }

        // true 表示 RenderPreview 返回的位图仍由操作自己持有（如画笔的持久预览），
        // 编辑器直接显示、不负责释放，可省去每个预览点一次的整图拷贝。
        public virtual bool ReusablePreview
        {
            get { return false; }
        }

        // true 表示该操作作用于整张文档（裁剪/画布/证件照/切图等会改变尺寸），
        // 编辑器会把合成结果作为 Source，应用时替换整个文档而不是当前图层。
        public virtual bool DocumentLevel
        {
            get { return false; }
        }

        public virtual string Hint
        {
            get { return ""; }
        }

        protected EditOpPanel()
        {
            Font = new Font("Microsoft YaHei UI", 9F);
            Dock = DockStyle.Fill;
            AutoScroll = true;
        }

        public void Attach(Bitmap source, Bitmap previewSource, ImageCanvas canvas)
        {
            OnDeactivate();
            Source = source;
            PreviewSource = previewSource;
            Canvas = canvas;
            OnActivate();
            RaisePreview();
        }

        public void Detach()
        {
            OnDeactivate();
            Source = null;
            PreviewSource = null;
            Canvas = null;
        }

        protected void RequestApply()
        {
            if (ApplyRequested != null)
            {
                ApplyRequested(this, EventArgs.Empty);
            }
        }

        protected void RequestReset()
        {
            if (ResetRequested != null)
            {
                ResetRequested(this, EventArgs.Empty);
            }
        }

        protected void RaisePreview()
        {
            if (_resetting)
            {
                return;
            }
            if (PreviewInvalidated != null)
            {
                PreviewInvalidated(this, EventArgs.Empty);
            }
        }

        public virtual void DisposeResources() { }

        private bool _resetting;

        public void ResetState()
        {
            _resetting = true;
            OnResetState();
            _resetting = false;
            RaisePreview();
        }

        protected virtual void OnResetState() { }

        protected virtual void OnActivate() { }
        protected virtual void OnDeactivate() { }
        public virtual Bitmap RenderPreview() { return null; }
        public virtual Bitmap BuildResult() { return null; }

        public virtual void OnCanvasClick(Point imagePoint) { }
        public virtual void OnCanvasSelection(Rectangle imageRect) { }
        public virtual void OnBrushPoint(Point imagePoint, int action) { }
        public virtual int BrushRadiusSession { get { return 0; } }

        // true 表示该操作需要在画布上按住左键拖动（编辑器会把画布切到拖动模式，
        // 并通过 OnCanvasDrag 派发起点/移动/结束，action 分别为 0/1/2）。
        public virtual bool WantsCanvasDrag { get { return false; } }
        public virtual void OnCanvasDrag(Point imagePoint, int action) { }

        // 垂直方向“压紧”布局（见 LayoutCompact）。在 DpiScaler 按设计坐标缩放前调用，
        // 所以 gap 用设计像素。
        internal void CompactLayout(int gap)
        {
            LayoutCompact.Compact(this, gap);
        }
    }

    public static class EditOpUi
    {
        public static Label Title(Control parent, string text, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(10, y);
            label.AutoSize = true;
            label.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            parent.Controls.Add(label);
            return label;
        }

        public static Label Note(Control parent, string text, int y, int height)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(10, y);
            label.Size = new Size(250, height);
            label.ForeColor = Color.FromArgb(80, 80, 80);
            parent.Controls.Add(label);
            return label;
        }

        public static Label Caption(Control parent, string text, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(10, y);
            label.AutoSize = true;
            parent.Controls.Add(label);
            return label;
        }

        public static TrackBar Slider(Control parent, string text, int y, int min, int max, int value, out Label valueLabel)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(10, y + 2);
            label.AutoSize = true;
            parent.Controls.Add(label);

            TrackBar bar = new TrackBar();
            bar.AutoSize = false;
            bar.TickStyle = TickStyle.None;
            bar.Minimum = min;
            bar.Maximum = max;
            bar.Value = value;
            bar.Location = new Point(78, y);
            bar.Size = new Size(132, 20);
            parent.Controls.Add(bar);

            valueLabel = new Label();
            valueLabel.Text = value.ToString();
            valueLabel.Location = new Point(214, y + 2);
            valueLabel.Size = new Size(36, 16);
            valueLabel.TextAlign = ContentAlignment.MiddleRight;
            parent.Controls.Add(valueLabel);

            return bar;
        }

        public static ComboBox Combo(Control parent, int y, string[] items, int selected)
        {
            ComboBox box = new ComboBox();
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Location = new Point(10, y);
            box.Size = new Size(250, 22);
            for (int i = 0; i < items.Length; i++)
            {
                box.Items.Add(items[i]);
            }
            box.SelectedIndex = selected;
            parent.Controls.Add(box);
            return box;
        }

        public static Button Button(Control parent, string text, int x, int y, int width, EventHandler onClick)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, 22);
            button.Click += onClick;
            parent.Controls.Add(button);
            return button;
        }
    }
}
