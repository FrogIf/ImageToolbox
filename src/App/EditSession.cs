using System;
using System.Collections.Generic;
using System.Drawing;

namespace ImageToolbox
{
    public class EditSession
    {
        private Bitmap _original;
        private Bitmap _current;
        private readonly List<Bitmap> _undo = new List<Bitmap>();
        private readonly List<Bitmap> _redo = new List<Bitmap>();
        private const int Limit = 12;

        public event EventHandler Changed;

        public Bitmap Current
        {
            get { return _current; }
        }

        public bool HasImage
        {
            get { return _current != null; }
        }

        public bool CanUndo
        {
            get { return _undo.Count > 0; }
        }

        public bool CanRedo
        {
            get { return _redo.Count > 0; }
        }

        public void SetOriginal(Bitmap image)
        {
            DisposeAll();
            _original = ImageFilters.Clone(image);
            _current = ImageFilters.Clone(image);
            Notify();
        }

        public void Commit(Bitmap result)
        {
            if (result == null)
            {
                return;
            }
            if (_current != null)
            {
                _undo.Add(_current);
            }
            _current = result;
            DisposeList(_redo);
            while (_undo.Count > Limit)
            {
                Bitmap oldest = _undo[0];
                _undo.RemoveAt(0);
                if (oldest != null)
                {
                    oldest.Dispose();
                }
            }
            Notify();
        }

        public void Undo()
        {
            if (_undo.Count == 0)
            {
                return;
            }
            if (_current != null)
            {
                _redo.Add(_current);
            }
            _current = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Notify();
        }

        public void Redo()
        {
            if (_redo.Count == 0)
            {
                return;
            }
            if (_current != null)
            {
                _undo.Add(_current);
            }
            _current = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            Notify();
        }

        public void ResetToOriginal()
        {
            if (_original == null)
            {
                return;
            }
            if (_current != null)
            {
                _undo.Add(_current);
            }
            _current = ImageFilters.Clone(_original);
            DisposeList(_redo);
            Notify();
        }

        public void DisposeAll()
        {
            if (_original != null) { _original.Dispose(); _original = null; }
            if (_current != null) { _current.Dispose(); _current = null; }
            DisposeList(_undo);
            DisposeList(_redo);
        }

        private static void DisposeList(List<Bitmap> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                {
                    list[i].Dispose();
                }
            }
            list.Clear();
        }

        private void Notify()
        {
            if (Changed != null)
            {
                Changed(this, EventArgs.Empty);
            }
        }
    }
}
