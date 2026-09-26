using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace ImageToolbox
{
    public static class MultiSizeExport
    {
        public static Size ComputeSize(int srcWidth, int srcHeight, int boxWidth, int boxHeight, bool keepAspect)
        {
            if (srcWidth < 1) { srcWidth = 1; }
            if (srcHeight < 1) { srcHeight = 1; }
            if (!keepAspect)
            {
                return new Size(Math.Max(1, boxWidth), Math.Max(1, boxHeight));
            }
            double fit = Math.Min((double)boxWidth / srcWidth, (double)boxHeight / srcHeight);
            return new Size(
                Math.Max(1, (int)Math.Round(srcWidth * fit)),
                Math.Max(1, (int)Math.Round(srcHeight * fit)));
        }

        public static Bitmap ResizeTo(Bitmap source, int width, int height)
        {
            Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, width, height));
            }
            return result;
        }

        public static void WriteIco(List<Bitmap> images, string path)
        {
            int count = images.Count;
            byte[][] pngs = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    images[i].Save(ms, ImageFormat.Png);
                    pngs[i] = ms.ToArray();
                }
            }

            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write((ushort)0);
                bw.Write((ushort)1);
                bw.Write((ushort)count);

                int offset = 6 + 16 * count;
                for (int i = 0; i < count; i++)
                {
                    int w = images[i].Width;
                    int h = images[i].Height;
                    bw.Write((byte)(w >= 256 ? 0 : w));
                    bw.Write((byte)(h >= 256 ? 0 : h));
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((ushort)1);
                    bw.Write((ushort)32);
                    bw.Write((uint)pngs[i].Length);
                    bw.Write((uint)offset);
                    offset += pngs[i].Length;
                }

                for (int i = 0; i < count; i++)
                {
                    bw.Write(pngs[i]);
                }
            }
        }
    }
}
