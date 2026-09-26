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

        public EditLayer(string name, Bitmap image)
        {
            Name = name;
            Image = image;
            Visible = true;
            Mode = BlendMode.Normal;
            Opacity = 1f;
        }

        public EditLayer Clone()
        {
            EditLayer copy = new EditLayer(Name, ImageFilters.Clone(Image));
            copy.Visible = Visible;
            copy.Mode = Mode;
            copy.Opacity = Opacity;
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
