using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ImageToolbox
{
    public class LocalOverlayPage : ToolPage
    {
        private TextBox _targetBox;
        private TextBox _overlayBox;
        private ImageCanvas _targetCanvas;
        private ImageCanvas _overlayCanvas;
        private ImageCanvas _resultCanvas;
        private Label _status;
        private Label _info;

        private Bitmap _targetImage;
        private Bitmap _overlayImage;
        private Bitmap _resultImage;

        public LocalOverlayPage()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(940, 620);

            BuildUi();
        }

        public override string ToolName
        {
            get { return "局部覆盖"; }
        }

        public override void Shutdown()
        {
            if (_targetImage != null)
            {
                _targetImage.Dispose();
            }
            if (_overlayImage != null)
            {
                _overlayImage.Dispose();
            }
            if (_resultImage != null)
            {
                _resultImage.Dispose();
            }
        }

        private void BuildUi()
        {
            Label targetLabel = new Label();
            targetLabel.Text = "需要修改的图片：";
            targetLabel.Location = new Point(10, 14);
            targetLabel.AutoSize = true;
            Controls.Add(targetLabel);

            _targetBox = new TextBox();
            _targetBox.Location = new Point(122, 10);
            _targetBox.Size = new Size(650, 25);
            _targetBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _targetBox.ReadOnly = true;
            Controls.Add(_targetBox);

            Button targetBrowse = new Button();
            targetBrowse.Text = "浏览...";
            targetBrowse.Location = new Point(784, 9);
            targetBrowse.Size = new Size(85, 27);
            targetBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            targetBrowse.Click += delegate { BrowseTarget(); };
            Controls.Add(targetBrowse);

            Label overlayLabel = new Label();
            overlayLabel.Text = "用于覆盖的图片：";
            overlayLabel.Location = new Point(10, 48);
            overlayLabel.AutoSize = true;
            Controls.Add(overlayLabel);

            _overlayBox = new TextBox();
            _overlayBox.Location = new Point(122, 44);
            _overlayBox.Size = new Size(650, 25);
            _overlayBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _overlayBox.ReadOnly = true;
            Controls.Add(_overlayBox);

            Button overlayBrowse = new Button();
            overlayBrowse.Text = "浏览...";
            overlayBrowse.Location = new Point(784, 43);
            overlayBrowse.Size = new Size(85, 27);
            overlayBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            overlayBrowse.Click += delegate { BrowseOverlay(); };
            Controls.Add(overlayBrowse);

            _targetCanvas = new ImageCanvas();
            _targetCanvas.Location = new Point(10, 80);
            _targetCanvas.Size = new Size(560, 450);
            _targetCanvas.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _targetCanvas.SelectionChanged += delegate { OnSelectionChanged(); };
            Controls.Add(_targetCanvas);

            Label overlayCanvasLabel = new Label();
            overlayCanvasLabel.Text = "覆盖图片（选区对应区域）";
            overlayCanvasLabel.Location = new Point(585, 84);
            overlayCanvasLabel.AutoSize = true;
            overlayCanvasLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(overlayCanvasLabel);

            _overlayCanvas = new ImageCanvas();
            _overlayCanvas.Location = new Point(585, 104);
            _overlayCanvas.Size = new Size(345, 190);
            _overlayCanvas.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            _overlayCanvas.ReadOnly = true;
            Controls.Add(_overlayCanvas);

            Label resultLabel = new Label();
            resultLabel.Text = "结果预览";
            resultLabel.Location = new Point(585, 304);
            resultLabel.AutoSize = true;
            resultLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            Controls.Add(resultLabel);

            _resultCanvas = new ImageCanvas();
            _resultCanvas.Location = new Point(585, 324);
            _resultCanvas.Size = new Size(345, 196);
            _resultCanvas.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _resultCanvas.ReadOnly = true;
            Controls.Add(_resultCanvas);

            Button clearSelection = new Button();
            clearSelection.Text = "清除选区";
            clearSelection.Location = new Point(10, 540);
            clearSelection.Size = new Size(100, 30);
            clearSelection.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            clearSelection.Click += delegate { ClearSelection(); };
            Controls.Add(clearSelection);

            Button preview = new Button();
            preview.Text = "预览结果";
            preview.Location = new Point(120, 540);
            preview.Size = new Size(100, 30);
            preview.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            preview.Click += delegate { PreviewResult(); };
            Controls.Add(preview);

            Button save = new Button();
            save.Text = "保存为 PNG";
            save.Location = new Point(230, 540);
            save.Size = new Size(120, 30);
            save.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            save.Click += delegate { SaveResult(); };
            Controls.Add(save);

            _status = new Label();
            _status.Text = "请选择两张图片，然后在左侧图片上拖动框选区域";
            _status.Location = new Point(362, 546);
            _status.Size = new Size(410, 22);
            _status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_status);

            _info = new Label();
            _info.TextAlign = ContentAlignment.MiddleRight;
            _info.Location = new Point(784, 546);
            _info.Size = new Size(146, 22);
            _info.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            Controls.Add(_info);
        }

        private void BrowseTarget()
        {
            string path = PickImage();
            if (path == null)
            {
                return;
            }
            LoadTarget(path);
        }

        private void BrowseOverlay()
        {
            string path = PickImage();
            if (path == null)
            {
                return;
            }
            LoadOverlay(path);
        }

        private string PickImage()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择图片";
            dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*";
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                return dialog.FileName;
            }
            return null;
        }

        private void LoadTarget(string path)
        {
            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap bitmap = ImageUtil.LoadImage(path);
                if (_targetImage != null)
                {
                    _targetImage.Dispose();
                }
                _targetImage = bitmap;
                _targetBox.Text = path;
                _targetCanvas.SetImage(_targetImage);
                ResetResult();
                _status.Text = "已加载需要修改的图片，请在左侧拖动框选区域";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
            UpdateInfo();
        }

        private void LoadOverlay(string path)
        {
            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap bitmap = ImageUtil.LoadImage(path);
                if (_overlayImage != null)
                {
                    _overlayImage.Dispose();
                }
                _overlayImage = bitmap;
                _overlayBox.Text = path;
                _overlayCanvas.SetImage(_overlayImage);
                _overlayCanvas.Selection = MapSelectionToOverlay();
                ResetResult();
                _status.Text = "已加载用于覆盖的图片";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法加载图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
            UpdateInfo();
        }

        private void ClearSelection()
        {
            _targetCanvas.Selection = Rectangle.Empty;
            _overlayCanvas.Selection = Rectangle.Empty;
            ResetResult();
            UpdateInfo();
            _status.Text = "已清除选区";
        }

        private void OnSelectionChanged()
        {
            _overlayCanvas.Selection = MapSelectionToOverlay();
            ResetResult();
            UpdateInfo();
        }

        private Rectangle MapSelectionToOverlay()
        {
            if (_targetImage == null || _overlayImage == null)
            {
                return Rectangle.Empty;
            }
            return ImageUtil.MapRegion(
                _targetCanvas.Selection,
                new Size(_targetImage.Width, _targetImage.Height),
                new Size(_overlayImage.Width, _overlayImage.Height));
        }

        private void ResetResult()
        {
            if (_resultImage != null)
            {
                _resultImage.Dispose();
                _resultImage = null;
            }
            _resultCanvas.SetImage(null);
        }

        private void UpdateInfo()
        {
            string target = _targetImage == null ? "-" : _targetImage.Width + "x" + _targetImage.Height;
            string overlay = _overlayImage == null ? "-" : _overlayImage.Width + "x" + _overlayImage.Height;
            Rectangle sel = _targetCanvas.Selection;
            string region = sel.Width > 0 ? sel.Width + "x" + sel.Height : "-";
            _info.Text = "目标 " + target + " / 覆盖 " + overlay + " / 选区 " + region;
        }

        private bool TryGetRegion(out Rectangle region)
        {
            region = Rectangle.Empty;

            if (_targetImage == null)
            {
                _status.Text = "请先选择需要修改的图片";
                return false;
            }
            if (_overlayImage == null)
            {
                _status.Text = "请先选择用于覆盖的图片";
                return false;
            }

            Rectangle selection = _targetCanvas.Selection;
            if (selection.Width < 1 || selection.Height < 1)
            {
                _status.Text = "请在左侧图片上拖动框选一个区域";
                return false;
            }

            region = Rectangle.Intersect(selection, new Rectangle(0, 0, _targetImage.Width, _targetImage.Height));
            if (region.Width < 1 || region.Height < 1)
            {
                _status.Text = "选区超出图片范围，请重新框选";
                return false;
            }
            return true;
        }

        private void PreviewResult()
        {
            Rectangle region;
            if (!TryGetRegion(out region))
            {
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap result = ImageUtil.Compose(_targetImage, _overlayImage, region);
                if (_resultImage != null)
                {
                    _resultImage.Dispose();
                }
                _resultImage = result;
                _resultCanvas.SetImage(_resultImage);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "预览失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private void SaveResult()
        {
            Rectangle region;
            if (!TryGetRegion(out region))
            {
                return;
            }

            string targetPath = _targetBox.Text;
            string dir = Path.GetDirectoryName(targetPath);
            string name = Path.GetFileNameWithoutExtension(targetPath) + "_局部覆盖.png";

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "保存结果";
            dialog.Filter = "PNG 图片|*.png";
            if (!string.IsNullOrEmpty(dir))
            {
                dialog.InitialDirectory = dir;
            }
            dialog.FileName = name;
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap result = ImageUtil.Compose(_targetImage, _overlayImage, region);
                try
                {
                    ImageUtil.SavePng(result, dialog.FileName);
                }
                finally
                {
                    result.Dispose();
                }
                _status.Text = "已保存：" + dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = previous;
            }
        }
    }
}
