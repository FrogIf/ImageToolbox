using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ImageToolbox
{
    public class EditSession
    {
        private readonly List<EditLayer> _layers = new List<EditLayer>();
        private readonly List<EditLayer> _knownLayers = new List<EditLayer>();
        private readonly List<Bitmap> _knownBitmaps = new List<Bitmap>();
        private readonly List<EditCommand> _undo = new List<EditCommand>();
        private readonly List<EditCommand> _redo = new List<EditCommand>();
        private readonly Dictionary<EditLayer, PreviewEntry> _previewCache = new Dictionary<EditLayer, PreviewEntry>();
        private Bitmap _bgBelow;          // 当前图层之下所有可见图层的合成（实时绘制时静态不变，缓存避免每帧重画）
        private Bitmap _bgAbove;          // 当前图层之上所有可见图层的合成（仅当它们都是普通+不透明+无偏移时才有值）
        private int _bgCacheLayer = int.MinValue;
        private int _bgCacheMax;
        private int _bgCacheVersion = -1;
        private int _version;             // 任何图层结构/属性/内容变化时自增，用于失效背景缓存

        private int _active = -1;
        private int _width;
        private int _height;
        private Bitmap _original;
        private string _rootLabel = "打开图片";   // 历史记录第 0 条（初始状态）的名称
        private const int Limit = 50;

        public event EventHandler Changed;

        public IList<EditLayer> Layers { get { return _layers; } }
        public int Width { get { return _width; } }
        public int Height { get { return _height; } }
        public bool HasImage { get { return _layers.Count > 0 && _width > 0 && _height > 0; } }
        public bool CanUndo { get { return _undo.Count > 0; } }
        public bool CanRedo { get { return _redo.Count > 0; } }

        public int ActiveIndex
        {
            get { return _active; }
            set
            {
                _active = Clamp(value);
                Notify();
            }
        }

        public EditLayer ActiveLayer
        {
            get { return (_active >= 0 && _active < _layers.Count) ? _layers[_active] : null; }
        }

        // 只有一个可见图层，且它就是当前图层、没有偏移/混合模式/透明度时，
        // 合成结果就等于该图层本身，预览可以跳过整图合成（单图层编辑的主路径）。
        public bool IsSoloNormalActive
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _layers.Count; i++)
                {
                    EditLayer l = _layers[i];
                    if (!l.Visible || l.Opacity <= 0f || l.Image == null) { continue; }
                    count++;
                }
                EditLayer a = ActiveLayer;
                return count == 1 && a != null && a.Visible && a.Image != null && a.Offset == Point.Empty &&
                    a.Mode == BlendMode.Normal && a.Opacity >= 1f;
            }
        }

        // 复用合成要求：当前图层**之上**的每个可见图层都是普通+不透明+无偏移，
        // 这样它们可以先合并成一张位图再叠加（否则混合模式需要真实底图，只能整图合成）。
        public bool CanReuseComposite(int layerIndex)
        {
            for (int i = layerIndex + 1; i < _layers.Count; i++)
            {
                EditLayer l = _layers[i];
                if (!l.Visible || l.Opacity <= 0f || l.Image == null) { continue; }
                if (l.Mode != BlendMode.Normal || l.Opacity < 1f || l.Offset != Point.Empty) { return false; }
            }
            return true;
        }

        public void SetOriginal(Bitmap image)
        {
            SetOriginal(image, "打开图片");
        }

        public void SetOriginal(Bitmap image, string rootLabel)
        {
            DisposeAll();
            _rootLabel = string.IsNullOrEmpty(rootLabel) ? "打开图片" : rootLabel;
            _width = image.Width;
            _height = image.Height;
            _original = Register(ImageFilters.Clone(image));
            _layers.Add(CreateLayer("背景", ImageFilters.Clone(image)));
            _active = 0;
            Notify();
        }

        public void CommitToActive(Bitmap result)
        {
            CommitToActive(result, null);
        }

        public void CommitToActive(Bitmap result, string label)
        {
            if (result == null) { return; }
            EditLayer layer = ActiveLayer;
            if (layer == null)
            {
                return;
            }
            // 锁定图像像素：任何像素改动都不生效（编辑器通常已在应用前拦截，这里再兜底一次）。
            if (layer.LockImage)
            {
                result.Dispose();
                return;
            }
            if (result.Width != _width || result.Height != _height)
            {
                CommitDocument(result, label);
                return;
            }
            // 锁定透明像素：结果 alpha 采用原图层 alpha（透明处保持透明、alpha 不变）。
            if (layer.LockTransparent && layer.Image != null)
            {
                ApplyAlphaLock(result, layer.Image);
            }
            Bitmap before = layer.Image;
            layer.Image = Register(result);
            Push(new PixelCommand(layer, before, result), label);
        }

        // 把 result 的 alpha 通道替换为 original 的 alpha（两者同尺寸，均为 32bppArgb）。
        internal static void ApplyAlphaLock(Bitmap result, Bitmap original)
        {
            if (result == null || original == null) { return; }
            if (result.Width != original.Width || result.Height != original.Height || result.Width < 1) { return; }
            BitmapData dr = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            BitmapData sr = original.LockBits(new Rectangle(0, 0, original.Width, original.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int drs = dr.Stride, srs = sr.Stride;
                byte[] dbuf = new byte[drs * result.Height];
                byte[] sbuf = new byte[srs * original.Height];
                Marshal.Copy(dr.Scan0, dbuf, 0, dbuf.Length);
                Marshal.Copy(sr.Scan0, sbuf, 0, sbuf.Length);
                for (int y = 0; y < result.Height; y++)
                {
                    int drow = y * drs, srow = y * srs;
                    for (int x = 0; x < result.Width; x++)
                    {
                        dbuf[drow + x * 4 + 3] = sbuf[srow + x * 4 + 3];
                    }
                }
                Marshal.Copy(dbuf, 0, dr.Scan0, dbuf.Length);
            }
            finally
            {
                result.UnlockBits(dr);
                original.UnlockBits(sr);
            }
        }

        public void CommitDocument(Bitmap result)
        {
            CommitDocument(result, null);
        }

        public void CommitDocument(Bitmap result, string label)
        {
            if (result == null) { return; }
            EditLayer layer = CreateLayer("背景", Register(result));
            CommitStructure(delegate
            {
                _width = result.Width;
                _height = result.Height;
                _layers.Clear();
                _layers.Add(layer);
                _active = 0;
            }, label);
        }

        public void AddImageLayer(Bitmap image, string name)
        {
            AddImageLayer(image, name, null);
        }

        public void AddImageLayer(Bitmap image, string name, string label)
        {
            if (image == null) { return; }
            if (!HasImage)
            {
                SetOriginal(image);
                return;
            }
            // 已经与文档同尺寸的位图直接放进去，避免再走一次 1:1 的双三次重采样
            // （透明边缘会被插值核采样出一圈白边）；尺寸不符时才适配到画布。
            Bitmap content = (image.Width == _width && image.Height == _height) ? image : FitToCanvas(image);
            EditLayer layer = CreateLayer(name, Register(content));
            CommitStructure(delegate
            {
                _layers.Insert(_active + 1, layer);
                _active = _active + 1;
            }, label);
        }

        public void AddBlankLayer(string name)
        {
            AddBlankLayer(name, null);
        }

        public void AddBlankLayer(string name, string label)
        {
            if (!HasImage) { return; }
            Bitmap blank = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
            EditLayer layer = CreateLayer(name, Register(blank));
            CommitStructure(delegate
            {
                _layers.Insert(_active + 1, layer);
                _active = _active + 1;
            }, label);
        }

        public void DuplicateActive()
        {
            DuplicateActive(null);
        }

        public void DuplicateActive(string label)
        {
            EditLayer active = ActiveLayer;
            if (active == null) { return; }
            EditLayer copy = CreateLayer(active.Name + " 副本", Register(ImageFilters.Clone(active.Image)));
            copy.Visible = active.Visible;
            copy.Mode = active.Mode;
            copy.Opacity = active.Opacity;
            CommitStructure(delegate
            {
                _layers.Insert(_active + 1, copy);
                _active = _active + 1;
            }, label);
        }

        public bool CanRemoveActive
        {
            get { return _layers.Count > 1 && _active >= 0 && _active < _layers.Count; }
        }

        public void RemoveActive()
        {
            RemoveActive(null);
        }

        public void RemoveActive(string label)
        {
            if (!CanRemoveActive) { return; }
            CommitStructure(delegate
            {
                _layers.RemoveAt(_active);
                if (_active >= _layers.Count) { _active = _layers.Count - 1; }
            }, label);
        }

        public void MoveActive(int delta)
        {
            MoveActive(delta, null);
        }

        public void MoveActive(int delta, string label)
        {
            int i = _active;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _layers.Count) { return; }
            CommitStructure(delegate
            {
                EditLayer t = _layers[i];
                _layers[i] = _layers[j];
                _layers[j] = t;
                _active = j;
            }, label);
        }

        public void MergeDown()
        {
            MergeDown(null);
        }

        public void MergeDown(string label)
        {
            int i = _active;
            if (i <= 0 || i >= _layers.Count) { return; }
            EditLayer upper = _layers[i];
            EditLayer lower = _layers[i - 1];
            Bitmap before = lower.Image;
            Point beforeOffset = lower.Offset;
            bool ownBase = (lower.Offset.X != 0 || lower.Offset.Y != 0);
            Bitmap baseImage = ownBase ? Positioned(lower.Image, lower.Offset) : lower.Image;
            Bitmap after = Register(ImageBlend.Composite(baseImage, upper.Image, upper.Mode, upper.Opacity, upper.Offset.X, upper.Offset.Y));
            if (ownBase) { baseImage.Dispose(); }
            lower.Image = after;
            lower.Offset = Point.Empty;
            _layers.RemoveAt(i);
            _active = i - 1;
            Push(new MergeDownCommand(lower, upper, before, beforeOffset, after, i - 1, i), label);
        }

        public void StampVisible()
        {
            StampVisible(null);
        }

        public void StampVisible(string label)
        {
            if (!HasImage) { return; }
            EditLayer layer = CreateLayer("盖印", Register(Composite()));
            CommitStructure(delegate
            {
                _layers.Insert(_active + 1, layer);
                _active = _active + 1;
            }, label);
        }

        public void Flatten()
        {
            Flatten(null);
        }

        public void Flatten(string label)
        {
            if (_layers.Count <= 1) { return; }
            EditLayer layer = CreateLayer("背景", Register(Composite()));
            CommitStructure(delegate
            {
                _layers.Clear();
                _layers.Add(layer);
                _active = 0;
            }, label);
        }

        public void SetVisible(EditLayer layer, bool visible)
        {
            SetVisible(layer, visible, null);
        }

        public void SetVisible(EditLayer layer, bool visible, string label)
        {
            if (layer == null || layer.Visible == visible) { return; }
            PushProps(layer, delegate { layer.Visible = visible; }, label);
        }

        public void SetMode(EditLayer layer, BlendMode mode)
        {
            SetMode(layer, mode, null);
        }

        public void SetMode(EditLayer layer, BlendMode mode, string label)
        {
            if (layer == null || layer.Mode == mode) { return; }
            PushProps(layer, delegate { layer.Mode = mode; }, label);
        }

        public void SetOpacity(EditLayer layer, float opacity)
        {
            SetOpacity(layer, opacity, null);
        }

        public void SetOpacity(EditLayer layer, float opacity, string label)
        {
            if (opacity < 0f) { opacity = 0f; }
            if (opacity > 1f) { opacity = 1f; }
            if (layer == null || layer.Opacity == opacity) { return; }
            PushProps(layer, delegate { layer.Opacity = opacity; }, label);
        }

        public void SetLockTransparent(EditLayer layer, bool value)
        {
            SetLockTransparent(layer, value, null);
        }

        public void SetLockTransparent(EditLayer layer, bool value, string label)
        {
            if (layer == null || layer.LockTransparent == value) { return; }
            PushProps(layer, delegate { layer.LockTransparent = value; }, label);
        }

        public void SetLockImage(EditLayer layer, bool value)
        {
            SetLockImage(layer, value, null);
        }

        public void SetLockImage(EditLayer layer, bool value, string label)
        {
            if (layer == null || layer.LockImage == value) { return; }
            PushProps(layer, delegate { layer.LockImage = value; }, label);
        }

        public void CommitOffset(EditLayer layer, Point before)
        {
            CommitOffset(layer, before, null);
        }

        public void CommitOffset(EditLayer layer, Point before, string label)
        {
            if (layer == null) { return; }
            Point after = layer.Offset;
            if (before == after) { return; }
            Push(new MoveCommand(layer, before, after), label);
        }

        public void Rename(EditLayer layer, string name)
        {
            Rename(layer, name, null);
        }

        public void Rename(EditLayer layer, string name, string label)
        {
            if (layer == null || string.IsNullOrEmpty(name) || layer.Name == name) { return; }
            CommitStructure(delegate { layer.Name = name; }, label);
        }

        public void Undo()
        {
            if (_undo.Count == 0) { return; }
            EditCommand cmd = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            cmd.Undo(this);
            _redo.Add(cmd);
            Notify();
        }

        public void Redo()
        {
            if (_redo.Count == 0) { return; }
            EditCommand cmd = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            cmd.Redo(this);
            _undo.Add(cmd);
            Notify();
        }

        // ---- 历史记录视图（供 HistoryPanel 使用）----

        // 记录的条数：初始状态 + 已应用 + 已撤销。
        public int HistoryCount
        {
            get { return _undo.Count + _redo.Count + 1; }
        }

        // 当前处在时间线的哪一步（等于已应用命令数）。
        public int CurrentHistoryIndex
        {
            get { return _undo.Count; }
        }

        public string HistoryLabel(int index)
        {
            if (index <= 0) { return _rootLabel; }
            int n = _undo.Count;
            if (index <= n) { return LabelOf(_undo[index - 1]); }
            int t = index - n;                       // 1.._redo.Count
            if (t > _redo.Count) { return ""; }
            return LabelOf(_redo[_redo.Count - t]);
        }

        // 跳到时间线的第 index 步（0 = 初始状态），通过连续撤销/重做实现。
        public void JumpTo(int index)
        {
            int total = _undo.Count + _redo.Count;
            if (index < 0) { index = 0; }
            if (index > total) { index = total; }
            while (_undo.Count < index) { Redo(); }
            while (_undo.Count > index) { Undo(); }
        }

        private static string LabelOf(EditCommand cmd)
        {
            if (cmd == null) { return "操作"; }
            if (!string.IsNullOrEmpty(cmd.Label)) { return cmd.Label; }
            if (cmd is PixelCommand) { return "像素修改"; }
            if (cmd is StructureCommand) { return "图层变更"; }
            if (cmd is MergeDownCommand) { return "向下合并"; }
            if (cmd is MoveCommand) { return "移动"; }
            if (cmd is PropsCommand) { return "图层属性"; }
            return "操作";
        }

        public void ResetToOriginal()
        {
            if (_original == null) { return; }
            CommitDocument(ImageFilters.Clone(_original), "复位");
        }

        public Bitmap Composite()
        {
            return Composite(-1, null);
        }

        public Bitmap Composite(int layerIndex, Bitmap replacement)
        {
            if (!HasImage) { return null; }
            Bitmap acc = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(acc))
            {
                g.Clear(Color.Transparent);
            }
            for (int i = 0; i < _layers.Count; i++)
            {
                EditLayer layer = _layers[i];
                if (!layer.Visible || layer.Opacity <= 0f || layer.Image == null) { continue; }
                Bitmap source = (i == layerIndex && replacement != null) ? replacement : layer.Image;
                ImageBlend.CompositeInto(acc, source, layer.Mode, layer.Opacity, layer.Offset.X, layer.Offset.Y);
            }
            return acc;
        }

        public Bitmap CompositePreview(int layerIndex, Bitmap replacement, int maxSize)
        {
            if (!HasImage) { return null; }
            Bitmap acc = null;
            Graphics g = null;
            try
            {
                for (int i = 0; i < _layers.Count; i++)
                {
                    EditLayer layer = _layers[i];
                    if (!layer.Visible || layer.Opacity <= 0f || layer.Image == null) { continue; }
                    Bitmap part = (i == layerIndex && replacement != null) ? replacement : PreviewOf(layer, maxSize);
                    if (part == null) { continue; }
                    if (acc == null)
                    {
                        acc = new Bitmap(part.Width, part.Height, PixelFormat.Format32bppArgb);
                        g = Graphics.FromImage(acc);
                        g.CompositingMode = CompositingMode.SourceOver;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.Clear(Color.Transparent);
                    }
                    float s = (float)acc.Width / _width;
                    int offX = (int)Math.Round(layer.Offset.X * s);
                    int offY = (int)Math.Round(layer.Offset.Y * s);
                    if (layer.Mode == BlendMode.Normal && layer.Opacity >= 1f)
                    {
                        // 快路径：普通+不透明图层用 GDI+ 原生 source-over 叠加，比逐像素合成快一个数量级，
                        // 这是多图层下实时绘制流畅的关键。
                        g.DrawImage(part, new Rectangle(offX, offY, part.Width, part.Height));
                    }
                    else
                    {
                        // 混合模式/半透明仍需逐像素，先让出 Graphics 释放对 acc 的占用。
                        g.Dispose();
                        g = null;
                        ImageBlend.CompositeInto(acc, part, layer.Mode, layer.Opacity, offX, offY);
                        g = Graphics.FromImage(acc);
                        g.CompositingMode = CompositingMode.SourceOver;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                    }
                }
            }
            finally
            {
                if (g != null) { g.Dispose(); }
            }
            if (acc == null) { acc = new Bitmap(1, 1, PixelFormat.Format32bppArgb); }
            PrunePreviewCache();
            return acc;
        }

        private void DisposeBackground()
        {
            if (_bgBelow != null) { _bgBelow.Dispose(); _bgBelow = null; }
            if (_bgAbove != null) { _bgAbove.Dispose(); _bgAbove = null; }
            _bgCacheVersion = -1;
        }

        // 计算并缓存「当前图层之下」/「之上」的合成。之上仅在全部为普通+不透明+无偏移时才有值
        // （此时它们可先合并再叠加）；否则为 null，编辑器必须回退到有序的整图合成。
        private void EnsureBackground(int layerIndex, int maxSize)
        {
            if (_bgCacheLayer == layerIndex && _bgCacheMax == maxSize && _bgCacheVersion == _version)
            {
                return;
            }
            DisposeBackground();
            _bgBelow = BuildSide(layerIndex, false, maxSize);
            _bgAbove = BuildSide(layerIndex, true, maxSize);
            _bgCacheLayer = layerIndex;
            _bgCacheMax = maxSize;
            _bgCacheVersion = _version;
            PrunePreviewCache();
        }

        private Bitmap BuildSide(int layerIndex, bool above, int maxSize)
        {
            int lo = above ? layerIndex + 1 : 0;
            int hi = above ? _layers.Count : layerIndex;
            Bitmap acc = null;
            Graphics g = null;
            try
            {
                for (int i = lo; i < hi; i++)
                {
                    EditLayer layer = _layers[i];
                    if (!layer.Visible || layer.Opacity <= 0f || layer.Image == null) { continue; }
                    if (above && (layer.Mode != BlendMode.Normal || layer.Opacity < 1f || layer.Offset != Point.Empty)) { continue; }
                    Bitmap part = PreviewOf(layer, maxSize);
                    if (part == null) { continue; }
                    if (acc == null)
                    {
                        acc = new Bitmap(part.Width, part.Height, PixelFormat.Format32bppArgb);
                        g = Graphics.FromImage(acc);
                        g.CompositingMode = CompositingMode.SourceOver;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.Clear(Color.Transparent);
                    }
                    float s = (float)acc.Width / _width;
                    int offX = (int)Math.Round(layer.Offset.X * s);
                    int offY = (int)Math.Round(layer.Offset.Y * s);
                    if (layer.Mode == BlendMode.Normal && layer.Opacity >= 1f)
                    {
                        g.DrawImage(part, new Rectangle(offX, offY, part.Width, part.Height));
                    }
                    else
                    {
                        g.Dispose();
                        g = null;
                        ImageBlend.CompositeInto(acc, part, layer.Mode, layer.Opacity, offX, offY);
                        g = Graphics.FromImage(acc);
                        g.CompositingMode = CompositingMode.SourceOver;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                    }
                }
            }
            finally
            {
                if (g != null) { g.Dispose(); }
            }
            return acc;
        }

        // 把「缓存的背景（下 + 当前 + 上）+ 当前图层内容」合成进调用方复用的 buffer，避免每帧新建整图。
        // 仅在 CanReuseComposite(layerIndex) 为真时调用。whole=false 时只重画 dirty 区域
        // （当前图层为普通+不透明+无偏移时），实时绘制每帧只处理笔触范围。
        public void CompositePreviewOver(Bitmap buffer, int layerIndex, Bitmap replacement, int maxSize, Rectangle dirty, bool whole)
        {
            if (buffer == null || replacement == null) { return; }
            EnsureBackground(layerIndex, maxSize);
            Rectangle full = new Rectangle(0, 0, buffer.Width, buffer.Height);
            EditLayer a = ActiveLayer;
            bool activeVisible = a != null && a.Visible && a.Opacity > 0f;
            bool fastActive = activeVisible && a.Mode == BlendMode.Normal && a.Opacity >= 1f && a.Offset == Point.Empty;
            bool hasBelow = _bgBelow != null && _bgBelow.Width == buffer.Width && _bgBelow.Height == buffer.Height;
            bool hasAbove = _bgAbove != null && _bgAbove.Width == buffer.Width && _bgAbove.Height == buffer.Height;
            bool useDirty = !whole && fastActive && !dirty.IsEmpty;

            Graphics g = Graphics.FromImage(buffer);
            try
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                if (useDirty) { g.SetClip(dirty); }

                // 1) 当前图层下方
                g.CompositingMode = CompositingMode.SourceCopy;
                if (hasBelow) { g.DrawImage(_bgBelow, full); }
                else if (useDirty) { using (SolidBrush clear = new SolidBrush(Color.Transparent)) { g.FillRectangle(clear, full); } }
                else { g.Clear(Color.Transparent); }
                g.CompositingMode = CompositingMode.SourceOver;

                // 2) 当前图层
                if (fastActive)
                {
                    g.DrawImage(replacement, full);
                }
                else if (activeVisible)
                {
                    // 非普通模式需逐像素且会写整图：让出 Graphics，整块合成后再补画上方（上方也是整图）。
                    g.ResetClip();
                    g.Dispose();
                    g = null;
                    float s = (float)buffer.Width / _width;
                    int offX = (int)Math.Round(a.Offset.X * s);
                    int offY = (int)Math.Round(a.Offset.Y * s);
                    ImageBlend.CompositeInto(buffer, replacement, a.Mode, a.Opacity, offX, offY);
                    if (hasAbove)
                    {
                        g = Graphics.FromImage(buffer);
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.CompositingMode = CompositingMode.SourceOver;
                        g.DrawImage(_bgAbove, full);
                    }
                    return;
                }

                // 3) 当前图层上方
                if (hasAbove) { g.DrawImage(_bgAbove, full); }
            }
            finally
            {
                if (g != null) { g.Dispose(); }
            }
        }

        public void DisposeAll()
        {
            _undo.Clear();
            _redo.Clear();
            ClearPreviewCache();
            for (int i = 0; i < _knownBitmaps.Count; i++)
            {
                if (_knownBitmaps[i] != null) { _knownBitmaps[i].Dispose(); }
            }
            _knownBitmaps.Clear();
            for (int i = 0; i < _knownLayers.Count; i++)
            {
                EditLayer layer = _knownLayers[i];
                if (layer.Image != null) { layer.Image.Dispose(); }
            }
            _knownLayers.Clear();
            if (_original != null) { _original.Dispose(); _original = null; }
            _layers.Clear();
            _active = -1;
            _width = 0;
            _height = 0;
            Notify();
        }

        // ---- internals ----

        private void PushProps(EditLayer layer, Action apply, string label)
        {
            bool bv = layer.Visible;
            BlendMode bm = layer.Mode;
            float bo = layer.Opacity;
            bool blt = layer.LockTransparent;
            bool bli = layer.LockImage;
            apply();
            if (bv == layer.Visible && bm == layer.Mode && bo == layer.Opacity &&
                blt == layer.LockTransparent && bli == layer.LockImage) { return; }

            if (_undo.Count > 0)
            {
                PropsCommand top = _undo[_undo.Count - 1] as PropsCommand;
                if (top != null && top.Layer == layer)
                {
                    top.AVisible = layer.Visible;
                    top.AMode = layer.Mode;
                    top.AOpacity = layer.Opacity;
                    top.ALockTransparent = layer.LockTransparent;
                    top.ALockImage = layer.LockImage;
                    if (!string.IsNullOrEmpty(label)) { top.Label = label; }
                    ClearCommands(_redo);
                    Notify();
                    return;
                }
            }

            PropsCommand cmd = new PropsCommand();
            cmd.Layer = layer;
            cmd.BVisible = bv; cmd.BMode = bm; cmd.BOpacity = bo;
            cmd.BLockTransparent = blt; cmd.BLockImage = bli;
            cmd.AVisible = layer.Visible; cmd.AMode = layer.Mode; cmd.AOpacity = layer.Opacity;
            cmd.ALockTransparent = layer.LockTransparent; cmd.ALockImage = layer.LockImage;
            Push(cmd, label);
        }

        private void CommitStructure(Action mutate)
        {
            CommitStructure(mutate, null);
        }

        private void CommitStructure(Action mutate, string label)
        {
            List<LayerState> before = CaptureStructure();
            int beforeActive = _active;
            int beforeW = _width;
            int beforeH = _height;
            mutate();
            List<LayerState> after = CaptureStructure();
            int afterActive = _active;
            Push(new StructureCommand(before, beforeActive, beforeW, beforeH, after, afterActive, _width, _height), label);
        }

        private void Push(EditCommand cmd)
        {
            Push(cmd, null);
        }

        private void Push(EditCommand cmd, string label)
        {
            if (cmd != null && !string.IsNullOrEmpty(label)) { cmd.Label = label; }
            _undo.Add(cmd);
            ClearCommands(_redo);
            while (_undo.Count > Limit)
            {
                EditCommand oldest = _undo[0];
                _undo.RemoveAt(0);
                ReleaseCommand(oldest);
            }
            Notify();
        }

        private void ClearCommands(List<EditCommand> list)
        {
            while (list.Count > 0)
            {
                EditCommand cmd = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                ReleaseCommand(cmd);
            }
        }

        private void ReleaseCommand(EditCommand cmd)
        {
            List<Bitmap> bitmaps = new List<Bitmap>();
            cmd.CollectBitmaps(bitmaps);
            for (int i = 0; i < bitmaps.Count; i++)
            {
                if (!IsReferenced(bitmaps[i]))
                {
                    if (bitmaps[i] != null) { bitmaps[i].Dispose(); }
                }
            }
        }

        private bool IsReferenced(Bitmap bmp)
        {
            if (bmp == null) { return true; }
            if (bmp == _original) { return true; }
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].Image == bmp) { return true; }
            }
            for (int i = 0; i < _undo.Count; i++)
            {
                if (_undo[i].References(bmp)) { return true; }
            }
            for (int i = 0; i < _redo.Count; i++)
            {
                if (_redo[i].References(bmp)) { return true; }
            }
            foreach (PreviewEntry entry in _previewCache.Values)
            {
                if (entry.Preview == bmp) { return true; }
            }
            return false;
        }

        private List<LayerState> CaptureStructure()
        {
            List<LayerState> list = new List<LayerState>();
            for (int i = 0; i < _layers.Count; i++)
            {
                EditLayer layer = _layers[i];
                LayerState state = new LayerState();
                state.Layer = layer;
                state.Name = layer.Name;
                state.Visible = layer.Visible;
                state.Mode = layer.Mode;
                state.Opacity = layer.Opacity;
                state.Offset = layer.Offset;
                state.LockTransparent = layer.LockTransparent;
                state.LockImage = layer.LockImage;
                list.Add(state);
            }
            return list;
        }

        internal void ApplyStructure(List<LayerState> states, int active, int w, int h)
        {
            _layers.Clear();
            for (int i = 0; i < states.Count; i++)
            {
                LayerState state = states[i];
                state.Layer.Name = state.Name;
                state.Layer.Visible = state.Visible;
                state.Layer.Mode = state.Mode;
                state.Layer.Opacity = state.Opacity;
                state.Layer.Offset = state.Offset;
                state.Layer.LockTransparent = state.LockTransparent;
                state.Layer.LockImage = state.LockImage;
                _layers.Add(state.Layer);
            }
            _width = w;
            _height = h;
            _active = Clamp(active);
        }

        internal void InsertLayerRaw(EditLayer layer, int index)
        {
            if (index < 0) { index = 0; }
            if (index > _layers.Count) { index = _layers.Count; }
            _layers.Insert(index, layer);
        }

        internal void RemoveLayerRaw(EditLayer layer)
        {
            _layers.Remove(layer);
        }

        internal void SetActiveRaw(int index)
        {
            _active = Clamp(index);
        }

        private EditLayer CreateLayer(string name, Bitmap image)
        {
            EditLayer layer = new EditLayer(name, image);
            _knownLayers.Add(layer);
            return layer;
        }

        private Bitmap Register(Bitmap bmp)
        {
            if (bmp != null) { _knownBitmaps.Add(bmp); }
            return bmp;
        }

        private Bitmap Positioned(Bitmap source, Point offset)
        {
            Bitmap dst = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(source, offset.X, offset.Y, source.Width, source.Height);
            }
            return dst;
        }

        private Bitmap FitToCanvas(Bitmap image)
        {
            Bitmap dst = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                ImageLayout.DrawInBox(g, image, new Rectangle(0, 0, _width, _height), false);
            }
            return dst;
        }

        // 按图层缓存缩小后的预览；仅当该图层的 Image 变化（或请求尺寸变化）时才重新生成。
        // 增删/移动/属性变化都不会让其他图层的缓存失效，避免重复的整图缩放。
        internal Bitmap PreviewOf(EditLayer layer, int maxSize)
        {
            PreviewEntry entry;
            if (layer == null || layer.Image == null) { return null; }
            if (_previewCache.TryGetValue(layer, out entry) &&
                object.ReferenceEquals(entry.Source, layer.Image) && entry.MaxSize == maxSize)
            {
                return entry.Preview;
            }
            Bitmap small = ImageUtil.CreatePreview(layer.Image, maxSize);
            Bitmap preview = (small != null) ? small : layer.Image;
            if (entry != null) { DisposeEntry(entry); }
            PreviewEntry next = new PreviewEntry();
            next.Source = layer.Image;
            next.Preview = preview;
            next.MaxSize = maxSize;
            _previewCache[layer] = next;
            return preview;
        }

        private void PrunePreviewCache()
        {
            if (_previewCache.Count == 0) { return; }
            List<EditLayer> stale = null;
            foreach (EditLayer layer in _previewCache.Keys)
            {
                if (!_layers.Contains(layer))
                {
                    if (stale == null) { stale = new List<EditLayer>(); }
                    stale.Add(layer);
                }
            }
            if (stale == null) { return; }
            for (int i = 0; i < stale.Count; i++)
            {
                DisposeEntry(_previewCache[stale[i]]);
                _previewCache.Remove(stale[i]);
            }
        }

        private static void DisposeEntry(PreviewEntry entry)
        {
            if (entry == null || entry.Preview == null) { return; }
            if (!object.ReferenceEquals(entry.Preview, entry.Source)) { entry.Preview.Dispose(); }
        }

        private void ClearPreviewCache()
        {
            foreach (PreviewEntry entry in _previewCache.Values)
            {
                DisposeEntry(entry);
            }
            _previewCache.Clear();
            DisposeBackground();
        }

        private int Clamp(int index)
        {
            if (index < 0 || index >= _layers.Count) { return _layers.Count - 1; }
            return index;
        }

        private void Notify()
        {
            _version++;
            if (Changed != null)
            {
                Changed(this, EventArgs.Empty);
            }
        }
    }

    internal class PreviewEntry
    {
        public Bitmap Source;
        public Bitmap Preview;
        public int MaxSize;
    }

    internal class LayerState
    {
        public EditLayer Layer;
        public string Name;
        public bool Visible;
        public BlendMode Mode;
        public float Opacity;
        public Point Offset;
        public bool LockTransparent;
        public bool LockImage;
    }

    internal abstract class EditCommand
    {
        public string Label;   // 历史记录里显示的名称
        public abstract void Undo(EditSession session);
        public abstract void Redo(EditSession session);
        public virtual void CollectBitmaps(List<Bitmap> list) { }
        public virtual bool References(Bitmap bmp) { return false; }
    }

    internal class PixelCommand : EditCommand
    {
        private readonly EditLayer _layer;
        private readonly Bitmap _before;
        private readonly Bitmap _after;

        public PixelCommand(EditLayer layer, Bitmap before, Bitmap after)
        {
            _layer = layer;
            _before = before;
            _after = after;
        }

        public override void Undo(EditSession session) { _layer.Image = _before; }
        public override void Redo(EditSession session) { _layer.Image = _after; }
        public override void CollectBitmaps(List<Bitmap> list) { list.Add(_before); list.Add(_after); }
        public override bool References(Bitmap bmp) { return bmp == _before || bmp == _after; }
    }

    internal class MergeDownCommand : EditCommand
    {
        private readonly EditLayer _lower;
        private readonly EditLayer _upper;
        private readonly Bitmap _before;
        private readonly Bitmap _after;
        private readonly Point _beforeOffset;
        private readonly int _lowerIndex;
        private readonly int _upperIndex;

        public MergeDownCommand(EditLayer lower, EditLayer upper, Bitmap before, Point beforeOffset, Bitmap after, int lowerIndex, int upperIndex)
        {
            _lower = lower;
            _upper = upper;
            _before = before;
            _beforeOffset = beforeOffset;
            _after = after;
            _lowerIndex = lowerIndex;
            _upperIndex = upperIndex;
        }

        public override void Undo(EditSession session)
        {
            _lower.Image = _before;
            _lower.Offset = _beforeOffset;
            session.InsertLayerRaw(_upper, _upperIndex);
            session.SetActiveRaw(_lowerIndex);
        }

        public override void Redo(EditSession session)
        {
            _lower.Image = _after;
            _lower.Offset = Point.Empty;
            session.RemoveLayerRaw(_upper);
            session.SetActiveRaw(_lowerIndex);
        }

        public override void CollectBitmaps(List<Bitmap> list) { list.Add(_before); list.Add(_after); }
        public override bool References(Bitmap bmp) { return bmp == _before || bmp == _after; }
    }

    internal class MoveCommand : EditCommand
    {
        private readonly EditLayer _layer;
        private readonly Point _before;
        private readonly Point _after;

        public MoveCommand(EditLayer layer, Point before, Point after)
        {
            _layer = layer;
            _before = before;
            _after = after;
        }

        public override void Undo(EditSession session) { _layer.Offset = _before; }
        public override void Redo(EditSession session) { _layer.Offset = _after; }
    }

    internal class StructureCommand : EditCommand
    {
        private readonly List<LayerState> _before;
        private readonly List<LayerState> _after;
        private readonly int _beforeActive;
        private readonly int _afterActive;
        private readonly int _beforeW;
        private readonly int _beforeH;
        private readonly int _afterW;
        private readonly int _afterH;

        public StructureCommand(List<LayerState> before, int beforeActive, int beforeW, int beforeH,
            List<LayerState> after, int afterActive, int afterW, int afterH)
        {
            _before = before;
            _beforeActive = beforeActive;
            _beforeW = beforeW;
            _beforeH = beforeH;
            _after = after;
            _afterActive = afterActive;
            _afterW = afterW;
            _afterH = afterH;
        }

        public override void Undo(EditSession session)
        {
            session.ApplyStructure(_before, _beforeActive, _beforeW, _beforeH);
        }

        public override void Redo(EditSession session)
        {
            session.ApplyStructure(_after, _afterActive, _afterW, _afterH);
        }
    }

    internal class PropsCommand : EditCommand
    {
        public EditLayer Layer;
        public bool BVisible;
        public BlendMode BMode;
        public float BOpacity;
        public bool BLockTransparent;
        public bool BLockImage;
        public bool AVisible;
        public BlendMode AMode;
        public float AOpacity;
        public bool ALockTransparent;
        public bool ALockImage;

        public override void Undo(EditSession session)
        {
            Layer.Visible = BVisible;
            Layer.Mode = BMode;
            Layer.Opacity = BOpacity;
            Layer.LockTransparent = BLockTransparent;
            Layer.LockImage = BLockImage;
        }

        public override void Redo(EditSession session)
        {
            Layer.Visible = AVisible;
            Layer.Mode = AMode;
            Layer.Opacity = AOpacity;
            Layer.LockTransparent = ALockTransparent;
            Layer.LockImage = ALockImage;
        }
    }
}
