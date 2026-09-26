using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private readonly Dictionary<EditLayer, Bitmap> _previewCache = new Dictionary<EditLayer, Bitmap>();
        private int _previewEpoch;
        private int _cachedEpoch = -1;

        private int _active = -1;
        private int _width;
        private int _height;
        private Bitmap _original;
        private const int Limit = 16;

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

        public void SetOriginal(Bitmap image)
        {
            DisposeAll();
            _width = image.Width;
            _height = image.Height;
            _original = Register(ImageFilters.Clone(image));
            _layers.Add(CreateLayer("背景", ImageFilters.Clone(image)));
            _active = 0;
            BumpPreview();
            Notify();
        }

        public void CommitToActive(Bitmap result)
        {
            if (result == null) { return; }
            EditLayer layer = ActiveLayer;
            if (layer == null)
            {
                return;
            }
            if (result.Width != _width || result.Height != _height)
            {
                CommitDocument(result);
                return;
            }
            Bitmap before = layer.Image;
            layer.Image = Register(result);
            Push(new PixelCommand(layer, before, result));
            BumpPreview();
        }

        public void CommitDocument(Bitmap result)
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
            });
        }

        public void AddImageLayer(Bitmap image, string name)
        {
            if (image == null) { return; }
            if (!HasImage)
            {
                SetOriginal(image);
                return;
            }
            EditLayer layer = CreateLayer(name, Register(FitToCanvas(image)));
            CommitStructure(delegate
            {
                _layers.Insert(_active + 1, layer);
                _active = _active + 1;
            });
        }

        public void AddBlankLayer(string name)
        {
            if (!HasImage) { return; }
            Bitmap blank = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
            EditLayer layer = CreateLayer(name, Register(blank));
            CommitStructure(delegate
            {
                _layers.Insert(_active + 1, layer);
                _active = _active + 1;
            });
        }

        public void DuplicateActive()
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
            });
        }

        public bool CanRemoveActive
        {
            get { return _layers.Count > 1 && _active >= 0 && _active < _layers.Count; }
        }

        public void RemoveActive()
        {
            if (!CanRemoveActive) { return; }
            CommitStructure(delegate
            {
                _layers.RemoveAt(_active);
                if (_active >= _layers.Count) { _active = _layers.Count - 1; }
            });
        }

        public void MoveActive(int delta)
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
            });
        }

        public void MergeDown()
        {
            int i = _active;
            if (i <= 0 || i >= _layers.Count) { return; }
            EditLayer upper = _layers[i];
            EditLayer lower = _layers[i - 1];
            Bitmap before = lower.Image;
            Bitmap after = Register(ImageBlend.Composite(lower.Image, upper.Image, upper.Mode, upper.Opacity));
            lower.Image = after;
            _layers.RemoveAt(i);
            _active = i - 1;
            Push(new MergeDownCommand(lower, upper, before, after, i - 1, i));
            BumpPreview();
        }

        public void StampVisible()
        {
            if (!HasImage) { return; }
            EditLayer layer = CreateLayer("盖印", Register(Composite()));
            CommitStructure(delegate
            {
                _layers.Insert(_active + 1, layer);
                _active = _active + 1;
            });
        }

        public void Flatten()
        {
            if (_layers.Count <= 1) { return; }
            EditLayer layer = CreateLayer("背景", Register(Composite()));
            CommitStructure(delegate
            {
                _layers.Clear();
                _layers.Add(layer);
                _active = 0;
            });
        }

        public void SetVisible(EditLayer layer, bool visible)
        {
            if (layer == null || layer.Visible == visible) { return; }
            PushProps(layer, delegate { layer.Visible = visible; });
        }

        public void SetMode(EditLayer layer, BlendMode mode)
        {
            if (layer == null || layer.Mode == mode) { return; }
            PushProps(layer, delegate { layer.Mode = mode; });
        }

        public void SetOpacity(EditLayer layer, float opacity)
        {
            if (opacity < 0f) { opacity = 0f; }
            if (opacity > 1f) { opacity = 1f; }
            if (layer == null || layer.Opacity == opacity) { return; }
            PushProps(layer, delegate { layer.Opacity = opacity; });
        }

        public void Rename(EditLayer layer, string name)
        {
            if (layer == null || string.IsNullOrEmpty(name) || layer.Name == name) { return; }
            CommitStructure(delegate { layer.Name = name; });
        }

        public void Undo()
        {
            if (_undo.Count == 0) { return; }
            EditCommand cmd = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            cmd.Undo(this);
            _redo.Add(cmd);
            BumpPreview();
            Notify();
        }

        public void Redo()
        {
            if (_redo.Count == 0) { return; }
            EditCommand cmd = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            cmd.Redo(this);
            _undo.Add(cmd);
            BumpPreview();
            Notify();
        }

        public void ResetToOriginal()
        {
            if (_original == null) { return; }
            CommitDocument(ImageFilters.Clone(_original));
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
                Bitmap next = ImageBlend.Composite(acc, source, layer.Mode, layer.Opacity);
                acc.Dispose();
                acc = next;
            }
            return acc;
        }

        public Bitmap CompositePreview(int layerIndex, Bitmap replacement, int maxSize)
        {
            if (!HasImage) { return null; }
            if (_cachedEpoch != _previewEpoch)
            {
                ClearPreviewCache();
                _cachedEpoch = _previewEpoch;
            }
            Bitmap acc = null;
            for (int i = 0; i < _layers.Count; i++)
            {
                EditLayer layer = _layers[i];
                if (!layer.Visible || layer.Opacity <= 0f || layer.Image == null) { continue; }
                Bitmap part = (i == layerIndex && replacement != null) ? replacement : PreviewOf(layer, maxSize);
                if (part == null) { continue; }
                if (acc == null)
                {
                    Bitmap transparent = new Bitmap(part.Width, part.Height, PixelFormat.Format32bppArgb);
                    Bitmap first = ImageBlend.Composite(transparent, part, layer.Mode, layer.Opacity);
                    transparent.Dispose();
                    acc = first;
                }
                else
                {
                    Bitmap next = ImageBlend.Composite(acc, part, layer.Mode, layer.Opacity);
                    acc.Dispose();
                    acc = next;
                }
            }
            if (acc == null) { acc = new Bitmap(1, 1, PixelFormat.Format32bppArgb); }
            return acc;
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
            _previewEpoch++;
            _cachedEpoch = -1;
            Notify();
        }

        // ---- internals ----

        private void PushProps(EditLayer layer, Action apply)
        {
            bool bv = layer.Visible;
            BlendMode bm = layer.Mode;
            float bo = layer.Opacity;
            apply();
            if (bv == layer.Visible && bm == layer.Mode && bo == layer.Opacity) { return; }

            if (_undo.Count > 0)
            {
                PropsCommand top = _undo[_undo.Count - 1] as PropsCommand;
                if (top != null && top.Layer == layer)
                {
                    top.AVisible = layer.Visible;
                    top.AMode = layer.Mode;
                    top.AOpacity = layer.Opacity;
                    ClearCommands(_redo);
                    BumpPreview();
                    Notify();
                    return;
                }
            }

            PropsCommand cmd = new PropsCommand();
            cmd.Layer = layer;
            cmd.BVisible = bv; cmd.BMode = bm; cmd.BOpacity = bo;
            cmd.AVisible = layer.Visible; cmd.AMode = layer.Mode; cmd.AOpacity = layer.Opacity;
            Push(cmd);
            BumpPreview();
        }

        private void CommitStructure(Action mutate)
        {
            List<LayerState> before = CaptureStructure();
            int beforeActive = _active;
            int beforeW = _width;
            int beforeH = _height;
            mutate();
            List<LayerState> after = CaptureStructure();
            int afterActive = _active;
            Push(new StructureCommand(before, beforeActive, beforeW, beforeH, after, afterActive, _width, _height));
            BumpPreview();
        }

        private void Push(EditCommand cmd)
        {
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
            foreach (Bitmap p in _previewCache.Values)
            {
                if (p == bmp) { return true; }
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

        private Bitmap PreviewOf(EditLayer layer, int maxSize)
        {
            Bitmap cached;
            if (_previewCache.TryGetValue(layer, out cached)) { return cached; }
            Bitmap small = ImageUtil.CreatePreview(layer.Image, maxSize);
            if (small == null) { return layer.Image; }
            _previewCache[layer] = small;
            return small;
        }

        private void ClearPreviewCache()
        {
            foreach (Bitmap bmp in _previewCache.Values)
            {
                if (bmp != null) { bmp.Dispose(); }
            }
            _previewCache.Clear();
        }

        private void BumpPreview()
        {
            _previewEpoch++;
        }

        private int Clamp(int index)
        {
            if (index < 0 || index >= _layers.Count) { return _layers.Count - 1; }
            return index;
        }

        private void Notify()
        {
            if (Changed != null)
            {
                Changed(this, EventArgs.Empty);
            }
        }
    }

    internal class LayerState
    {
        public EditLayer Layer;
        public string Name;
        public bool Visible;
        public BlendMode Mode;
        public float Opacity;
    }

    internal abstract class EditCommand
    {
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
        private readonly int _lowerIndex;
        private readonly int _upperIndex;

        public MergeDownCommand(EditLayer lower, EditLayer upper, Bitmap before, Bitmap after, int lowerIndex, int upperIndex)
        {
            _lower = lower;
            _upper = upper;
            _before = before;
            _after = after;
            _lowerIndex = lowerIndex;
            _upperIndex = upperIndex;
        }

        public override void Undo(EditSession session)
        {
            _lower.Image = _before;
            session.InsertLayerRaw(_upper, _upperIndex);
            session.SetActiveRaw(_lowerIndex);
        }

        public override void Redo(EditSession session)
        {
            _lower.Image = _after;
            session.RemoveLayerRaw(_upper);
            session.SetActiveRaw(_lowerIndex);
        }

        public override void CollectBitmaps(List<Bitmap> list) { list.Add(_before); list.Add(_after); }
        public override bool References(Bitmap bmp) { return bmp == _before || bmp == _after; }
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
        public bool AVisible;
        public BlendMode AMode;
        public float AOpacity;

        public override void Undo(EditSession session)
        {
            Layer.Visible = BVisible;
            Layer.Mode = BMode;
            Layer.Opacity = BOpacity;
        }

        public override void Redo(EditSession session)
        {
            Layer.Visible = AVisible;
            Layer.Mode = AMode;
            Layer.Opacity = AOpacity;
        }
    }
}
