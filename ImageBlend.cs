using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ImageToolbox
{
    public enum BlendMode
    {
        Normal = 0,
        Multiply,
        Screen,
        Overlay,
        SoftLight,
        HardLight,
        Difference,
        Darken,
        Lighten,
        Add,
        Subtract
    }

    public static class ImageBlend
    {
        public static string[] ModeNames =
        {
            "正常", "正片叠底", "滤色", "叠加", "柔光", "强光",
            "差值", "变暗", "变亮", "相加", "相减"
        };

        public static Bitmap Blend(Bitmap baseImage, Bitmap overlay, BlendMode mode, float opacity)
        {
            int w = baseImage.Width;
            int h = baseImage.Height;
            if (opacity < 0f) { opacity = 0f; }
            if (opacity > 1f) { opacity = 1f; }

            Bitmap scaled = overlay;
            bool own = false;
            if (overlay.Width != w || overlay.Height != h)
            {
                scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(overlay, new Rectangle(0, 0, w, h));
                }
                own = true;
            }

            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData ad = baseImage.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData od = scaled.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData rd = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int astride = ad.Stride;
                int ostride = od.Stride;
                int rstride = rd.Stride;
                byte[] abuf = new byte[astride * h];
                byte[] obuf = new byte[ostride * h];
                byte[] rbuf = new byte[rstride * h];
                Marshal.Copy(ad.Scan0, abuf, 0, abuf.Length);
                Marshal.Copy(od.Scan0, obuf, 0, obuf.Length);

                for (int y = 0; y < h; y++)
                {
                    int ao = y * astride;
                    int oo = y * ostride;
                    int ro = y * rstride;
                    for (int x = 0; x < w; x++)
                    {
                        int i = ao + x * 4;
                        int j = oo + x * 4;
                        int k = ro + x * 4;
                        float alpha = (obuf[j + 3] / 255f) * opacity;
                        rbuf[k] = Mix(abuf[i], obuf[j], mode, alpha);
                        rbuf[k + 1] = Mix(abuf[i + 1], obuf[j + 1], mode, alpha);
                        rbuf[k + 2] = Mix(abuf[i + 2], obuf[j + 2], mode, alpha);
                        rbuf[k + 3] = abuf[i + 3];
                    }
                }

                Marshal.Copy(rbuf, 0, rd.Scan0, rbuf.Length);
            }
            finally
            {
                baseImage.UnlockBits(ad);
                scaled.UnlockBits(od);
                result.UnlockBits(rd);
                if (own)
                {
                    scaled.Dispose();
                }
            }
            return result;
        }

        private static byte Mix(byte baseB, byte overB, BlendMode mode, float alpha)
        {
            if (alpha <= 0f)
            {
                return baseB;
            }
            float a = baseB / 255f;
            float b = overB / 255f;
            float blended;
            switch (mode)
            {
                case BlendMode.Multiply:
                    blended = a * b;
                    break;
                case BlendMode.Screen:
                    blended = 1f - (1f - a) * (1f - b);
                    break;
                case BlendMode.Overlay:
                    blended = a < 0.5f ? 2f * a * b : 1f - 2f * (1f - a) * (1f - b);
                    break;
                case BlendMode.HardLight:
                    blended = b < 0.5f ? 2f * a * b : 1f - 2f * (1f - a) * (1f - b);
                    break;
                case BlendMode.SoftLight:
                    blended = SoftLight(a, b);
                    break;
                case BlendMode.Difference:
                    blended = Math.Abs(a - b);
                    break;
                case BlendMode.Darken:
                    blended = Math.Min(a, b);
                    break;
                case BlendMode.Lighten:
                    blended = Math.Max(a, b);
                    break;
                case BlendMode.Add:
                    blended = Math.Min(1f, a + b);
                    break;
                case BlendMode.Subtract:
                    blended = Math.Max(0f, a - b);
                    break;
                default:
                    blended = b;
                    break;
            }
            float value = a + (blended - a) * alpha;
            if (value < 0f) { value = 0f; }
            if (value > 1f) { value = 1f; }
            return (byte)(value * 255f + 0.5f);
        }

        private static float SoftLight(float a, float b)
        {
            if (b <= 0.5f)
            {
                return a - (1f - 2f * b) * a * (1f - a);
            }
            float d = a <= 0.25f
                ? ((16f * a - 12f) * a + 4f) * a
                : (float)Math.Sqrt(a);
            return a + (2f * b - 1f) * (d - a);
        }
    }
}
