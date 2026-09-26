using System;
using System.Drawing;

namespace ImageToolbox
{
    public class EditLayer
    {
        public string Name;
        public Bitmap Image;
        public bool Visible;
        public BlendMode Mode;
        public float Opacity;
        public Point Offset;

        public EditLayer(string name, Bitmap image)
        {
            Name = name;
            Image = image;
            Visible = true;
            Mode = BlendMode.Normal;
            Opacity = 1f;
            Offset = Point.Empty;
        }

        public EditLayer Clone()
        {
            EditLayer copy = new EditLayer(Name, ImageFilters.Clone(Image));
            copy.Visible = Visible;
            copy.Mode = Mode;
            copy.Opacity = Opacity;
            copy.Offset = Offset;
            return copy;
        }

        public void Dispose()
        {
            if (Image != null)
            {
                Image.Dispose();
                Image = null;
            }
        }
    }
}
