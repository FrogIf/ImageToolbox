using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class SliceCollageOp : EditOpPanel
    {
        private ComboBox _mode;
        private TrackBar _rows, _cols, _spacing, _gridCols, _longEdge;
        private Label _rowsV, _colsV, _spacingV, _gridColsV, _longEdgeV;
        private ComboBox _direction;
        private ComboBox _bgCombo;
        private ListBox _images;
        private List<Bitmap> _extra = new List<Bitmap>();
        private static readonly Color[] BgColors = { Color.White, Color.Black, Color.FromArgb(240, 240, 240), Color.Transparent };

        public override bool DocumentLevel
        {
            get { return true; }
        }

        public SliceCollageOp()
        {
            EditOpUi.Title(this, "切图拼图", 10);
            EditOpUi.Caption(this, "模式", 44);
            _mode = EditOpUi.Combo(this, 64, new string[] { "九宫格切图（导出）", "拼图（合成到当前图）" }, 0);
            _mode.SelectedIndexChanged += delegate { UpdateMode(); RaisePreview(); };

            _rows = EditOpUi.Slider(this, "行数", 100, 1, 10, 3, out _rowsV);
            _cols = EditOpUi.Slider(this, "列数", 136, 1, 10, 3, out _colsV);
            _rows.ValueChanged += delegate { _rowsV.Text = _rows.Value.ToString(); RaisePreview(); };
            _cols.ValueChanged += delegate { _colsV.Text = _cols.Value.ToString(); RaisePreview(); };

            EditOpUi.Button(this, "选择输出目录并导出切图", 10, 178, 200, delegate { ExportSlices(); });

            EditOpUi.Caption(this, "拼图方向", 220);
            _direction = EditOpUi.Combo(this, 240, new string[] { "横向拼接", "纵向拼接", "网格" }, 0);
            _direction.SelectedIndexChanged += delegate { RaisePreview(); };

            _images = new ListBox();
            _images.Location = new Point(10, 276);
            _images.Size = new Size(250, 90);
            _images.IntegralHeight = false;
            Controls.Add(_images);

            EditOpUi.Button(this, "添加图片", 10, 374, 90, delegate { AddImage(); });
            EditOpUi.Button(this, "移除", 106, 374, 70, delegate { RemoveImage(); });
            EditOpUi.Button(this, "清空", 182, 374, 70, delegate { ClearImages(); });

            _spacing = EditOpUi.Slider(this, "间距", 412, 0, 80, 8, out _spacingV);
            _spacing.ValueChanged += delegate { _spacingV.Text = _spacing.Value.ToString(); RaisePreview(); };
            _gridCols = EditOpUi.Slider(this, "网格列", 448, 1, 8, 2, out _gridColsV);
            _gridCols.ValueChanged += delegate { _gridColsV.Text = _gridCols.Value.ToString(); RaisePreview(); };
            _longEdge = EditOpUi.Slider(this, "长边", 484, 0, 4000, 0, out _longEdgeV);
            _longEdge.ValueChanged += delegate { _longEdgeV.Text = _longEdge.Value == 0 ? "原样" : _longEdge.Value.ToString(); RaisePreview(); };
            EditOpUi.Caption(this, "背景色", 520);
            _bgCombo = EditOpUi.Combo(this, 540, new string[] { "白色", "黑色", "浅灰", "透明" }, 0);
            _bgCombo.SelectedIndexChanged += delegate { RaisePreview(); };
            EditOpUi.Note(this, "切图把当前图按行列切成多张并导出；拼图把当前图与所选图片合成。", 580, 48);

            UpdateMode();
        }

        private void UpdateMode()
        {
            _rowsV.Text = _rows.Value.ToString();
            _colsV.Text = _cols.Value.ToString();
        }

        public override bool CanApply
        {
            get { return _mode.SelectedIndex == 1; }
        }

        protected override void OnActivate()
        {
            if (Canvas != null) { Canvas.ReadOnly = true; }
        }

        private void AddImage()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "添加图片";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            dialog.Multiselect = true;
            if (dialog.ShowDialog(this) != DialogResult.OK) { return; }
            try
            {
                for (int i = 0; i < dialog.FileNames.Length; i++)
                {
                    Bitmap b = ImageUtil.LoadImage(dialog.FileNames[i]);
                    _extra.Add(b);
                    _images.Items.Add(Path.GetFileName(dialog.FileNames[i]));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图片：" + ex.Message);
            }
            RaisePreview();
        }

        private void RemoveImage()
        {
            int i = _images.SelectedIndex;
            if (i < 0 || i >= _extra.Count) { return; }
            _extra[i].Dispose();
            _extra.RemoveAt(i);
            _images.Items.RemoveAt(i);
            RaisePreview();
        }

        private void ClearImages()
        {
            for (int i = 0; i < _extra.Count; i++) { _extra[i].Dispose(); }
            _extra.Clear();
            _images.Items.Clear();
            RaisePreview();
        }

        public override void DisposeResources()
        {
            ClearImages();
        }

        protected override void OnResetState()
        {
            ClearImages();
        }

        private void ExportSlices()
        {
            if (Source == null) { return; }
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "选择切图输出目录";
            if (dialog.ShowDialog(this) != DialogResult.OK) { return; }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                List<Bitmap> slices = ImageLayout.Slice(Source, _cols.Value, _rows.Value);
                for (int i = 0; i < slices.Count; i++)
                {
                    int row = i / _cols.Value + 1;
                    int col = i % _cols.Value + 1;
                    string path = Path.Combine(dialog.SelectedPath, "切图_r" + row + "_c" + col + ".png");
                    ImageUtil.SavePng(slices[i], path);
                    slices[i].Dispose();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message);
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private Bitmap SlicePreview()
        {
            Bitmap preview = ImageUtil.CreatePreview(Source, 1000);
            bool own = preview != null;
            if (preview == null) { preview = ImageFilters.Clone(Source); own = true; }
            using (Graphics g = Graphics.FromImage(preview))
            using (Pen pen = new Pen(Color.FromArgb(0, 174, 255), 2f))
            {
                for (int c = 1; c < _cols.Value; c++)
                {
                    int x = preview.Width * c / _cols.Value;
                    g.DrawLine(pen, x, 0, x, preview.Height);
                }
                for (int r = 1; r < _rows.Value; r++)
                {
                    int y = preview.Height * r / _rows.Value;
                    g.DrawLine(pen, 0, y, preview.Width, y);
                }
            }
            return preview;
        }

        private Bitmap BuildCollage(Bitmap first)
        {
            List<Bitmap> list = new List<Bitmap>();
            list.Add(first);
            List<Bitmap> temps = new List<Bitmap>();
            for (int i = 0; i < _extra.Count; i++)
            {
                Bitmap p = ImageUtil.CreatePreview(_extra[i], 1400);
                if (p == null) { p = _extra[i]; }
                else { temps.Add(p); }
                list.Add(p);
            }

            CollageOptions options = new CollageOptions();
            options.Direction = (CollageDirection)_direction.SelectedIndex;
            options.Columns = _direction.SelectedIndex == 2
                ? Math.Max(1, _gridCols.Value)
                : Math.Max(1, (int)Math.Ceiling(Math.Sqrt(list.Count)));
            options.Spacing = _spacing.Value;
            options.Margin = _spacing.Value;
            options.LongEdge = _longEdge.Value;
            options.Background = BgColors[Math.Max(0, _bgCombo.SelectedIndex)];
            Bitmap result = ImageLayout.Collage(list, options);

            for (int i = 0; i < temps.Count; i++) { temps[i].Dispose(); }
            return result;
        }

        public override Bitmap RenderPreview()
        {
            if (PreviewSource == null) { return null; }
            if (_mode.SelectedIndex == 0) { return SlicePreview(); }
            return BuildCollage(PreviewSource);
        }

        public override Bitmap BuildResult()
        {
            if (Source == null || _mode.SelectedIndex != 1) { return null; }
            return BuildCollage(Source);
        }
    }
}
