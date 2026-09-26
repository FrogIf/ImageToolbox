using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ImageToolbox
{
    public static class ImageCompare
    {
        public static Bitmap SideBySide(Bitmap a, Bitmap b, bool vertical, int gap)
        {
            if (vertical)
            {
                int width = Math.Min(a.Width, b.Width);
                int ha = ScaleHeight(a, width);
                int hb = ScaleHeight(b, width);
                Bitmap result = new Bitmap(width, ha + gap + hb, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(result))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.FromArgb(255, 240, 240, 240));
                    g.DrawImage(a, new Rectangle(0, 0, width, ha));
                    g.DrawImage(b, new Rectangle(0, ha + gap, width, hb));
                }
                return result;
            }
            else
            {
                int height = Math.Min(a.Height, b.Height);
                int wa = ScaleWidth(a, height);
                int wb = ScaleWidth(b, height);
                Bitmap result = new Bitmap(wa + gap + wb, height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(result))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.FromArgb(255, 240, 240, 240));
                    g.DrawImage(a, new Rectangle(0, 0, wa, height));
                    g.DrawImage(b, new Rectangle(wa + gap, 0, wb, height));
                }
                return result;
            }
        }

        public static Bitmap Slider(Bitmap a, Bitmap b, float ratio)
        {
            if (ratio < 0f) { ratio = 0f; }
            if (ratio > 1f) { ratio = 1f; }

            int w = a.Width;
            int h = a.Height;
            int split = (int)Math.Round(w * ratio);

            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(a, new Rectangle(0, 0, w, h));
                if (split > 0)
                {
                    Rectangle dest = new Rectangle(0, 0, split, h);
                    Rectangle src = new Rectangle(0, 0, (int)Math.Round(b.Width * split / (double)w), b.Height);
                    g.DrawImage(b, dest, src, GraphicsUnit.Pixel);
                }
                if (split > 0 && split < w)
                {
                    using (Pen pen = new Pen(Color.FromArgb(255, 0, 174, 255), 2f))
                    {
                        g.DrawLine(pen, split, 0, split, h);
                    }
                }
            }
            return result;
        }

        public static Bitmap Difference(Bitmap a, Bitmap b, float strength)
        {
            if (strength <= 0f) { strength = 1f; }

            int w = a.Width;
            int h = a.Height;
            Bitmap scaled = Scale(b, w, h);
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            BitmapData ad = a.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData bd = scaled.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData rd = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int astride = ad.Stride;
                int bstride = bd.Stride;
                int rstride = rd.Stride;
                byte[] abuf = new byte[astride * h];
                byte[] bbuf = new byte[bstride * h];
                byte[] rbuf = new byte[rstride * h];
                Marshal.Copy(ad.Scan0, abuf, 0, abuf.Length);
                Marshal.Copy(bd.Scan0, bbuf, 0, bbuf.Length);

                for (int y = 0; y < h; y++)
                {
                    int ao = y * astride;
                    int bo = y * bstride;
                    int ro = y * rstride;
                    for (int x = 0; x < w; x++)
                    {
                        int i = ao + x * 4;
                        int j = bo + x * 4;
                        int o = ro + x * 4;
                        int dr = Math.Abs(abuf[i + 2] - bbuf[j + 2]);
                        int dg = Math.Abs(abuf[i + 1] - bbuf[j + 1]);
                        int db = Math.Abs(abuf[i] - bbuf[j]);
                        rbuf[o] = ClampByte(db * strength);
                        rbuf[o + 1] = ClampByte(dg * strength);
                        rbuf[o + 2] = ClampByte(dr * strength);
                        rbuf[o + 3] = 255;
                    }
                }

                Marshal.Copy(rbuf, 0, rd.Scan0, rbuf.Length);
            }
            finally
            {
                a.UnlockBits(ad);
                scaled.UnlockBits(bd);
                result.UnlockBits(rd);
                scaled.Dispose();
            }
            return result;
        }

        public static Bitmap Scale(Bitmap source, int width, int height)
        {
            Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, width, height));
            }
            return result;
        }

        private static int ScaleHeight(Bitmap b, int width)
        {
            return Math.Max(1, (int)Math.Round(b.Height * (double)width / b.Width));
        }

        private static int ScaleWidth(Bitmap b, int height)
        {
            return Math.Max(1, (int)Math.Round(b.Width * (double)height / b.Height));
        }

        private static byte ClampByte(float value)
        {
            if (value < 0f) { return 0; }
            if (value > 255f) { return 255; }
            return (byte)(value + 0.5f);
        }
    }
}
