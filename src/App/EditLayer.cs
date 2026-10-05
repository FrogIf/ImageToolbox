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
        public bool LockTransparent;   // 锁定透明像素：编辑只作用于已有（不透明）像素，alpha 不变
        public bool LockImage;         // 锁定图像像素：禁止任何像素改动（颜色与透明都不变）

        public EditLayer(string name, Bitmap image)
        {
            Name = name;
            Image = image;
            Visible = true;
            Mode = BlendMode.Normal;
            Opacity = 1f;
            Offset = Point.Empty;
            LockTransparent = false;
            LockImage = false;
        }

        public EditLayer Clone()
        {
            EditLayer copy = new EditLayer(Name, ImageFilters.Clone(Image));
            copy.Visible = Visible;
            copy.Mode = Mode;
            copy.Opacity = Opacity;
            copy.Offset = Offset;
            copy.LockTransparent = LockTransparent;
            copy.LockImage = LockImage;
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
