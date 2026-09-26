using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ImageToolbox
{
    public static class ImageEffects
    {
        public static ColorMatrix Identity()
        {
            ColorMatrix m = new ColorMatrix();
            m.Matrix00 = 1f;
            m.Matrix11 = 1f;
            m.Matrix22 = 1f;
            m.Matrix33 = 1f;
            m.Matrix44 = 1f;
            return m;
        }

        public static ColorMatrix Multiply(ColorMatrix a, ColorMatrix b)
        {
            float[,] x = ToArray(a);
            float[,] y = ToArray(b);
            float[,] r = new float[5, 5];
            for (int i = 0; i < 5; i++)
            {
                for (int j = 0; j < 5; j++)
                {
                    float sum = 0f;
                    for (int k = 0; k < 5; k++)
                    {
                        sum += x[i, k] * y[k, j];
                    }
                    r[i, j] = sum;
                }
            }
            return FromArray(r);
        }

        public static ColorMatrix Lerp(ColorMatrix a, ColorMatrix b, float t)
        {
            if (t < 0f)
            {
                t = 0f;
            }
            if (t > 1f)
            {
                t = 1f;
            }
            float[,] x = ToArray(a);
            float[,] y = ToArray(b);
            float[,] r = new float[5, 5];
            for (int i = 0; i < 5; i++)
            {
                for (int j = 0; j < 5; j++)
                {
                    r[i, j] = x[i, j] + (y[i, j] - x[i, j]) * t;
                }
            }
            return FromArray(r);
        }

        public static ColorMatrix Brightness(float b)
        {
            ColorMatrix m = Identity();
            float offset = b * 0.4f;
            m.Matrix40 = offset;
            m.Matrix41 = offset;
            m.Matrix42 = offset;
            return m;
        }

        public static ColorMatrix Contrast(float c)
        {
            ColorMatrix m = Identity();
            float scale = 1f + c;
            float offset = 0.5f * (1f - scale);
            m.Matrix00 = scale;
            m.Matrix11 = scale;
            m.Matrix22 = scale;
            m.Matrix40 = offset;
            m.Matrix41 = offset;
            m.Matrix42 = offset;
            return m;
        }

        public static ColorMatrix Saturation(float s)
        {
            float lumR = 0.3086f;
            float lumG = 0.6094f;
            float lumB = 0.0820f;
            ColorMatrix m = Identity();
            m.Matrix00 = lumR * (1f - s) + s;
            m.Matrix01 = lumR * (1f - s);
            m.Matrix02 = lumR * (1f - s);
            m.Matrix10 = lumG * (1f - s);
            m.Matrix11 = lumG * (1f - s) + s;
            m.Matrix12 = lumG * (1f - s);
            m.Matrix20 = lumB * (1f - s);
            m.Matrix21 = lumB * (1f - s);
            m.Matrix22 = lumB * (1f - s) + s;
            return m;
        }

        public static ColorMatrix Temperature(float t)
        {
            ColorMatrix m = Identity();
            m.Matrix00 = 1f + 0.15f * t;
            m.Matrix11 = 1f + 0.03f * t;
            m.Matrix22 = 1f - 0.15f * t;
            return m;
        }

        public static ColorMatrix Tint(float t)
        {
            ColorMatrix m = Identity();
            m.Matrix00 = 1f + 0.05f * t;
            m.Matrix11 = 1f - 0.10f * t;
            m.Matrix22 = 1f + 0.05f * t;
            return m;
        }

        public static ColorMatrix Grayscale()
        {
            ColorMatrix m = Identity();
            m.Matrix00 = 0.299f;
            m.Matrix01 = 0.299f;
            m.Matrix02 = 0.299f;
            m.Matrix10 = 0.587f;
            m.Matrix11 = 0.587f;
            m.Matrix12 = 0.587f;
            m.Matrix20 = 0.114f;
            m.Matrix21 = 0.114f;
            m.Matrix22 = 0.114f;
            return m;
        }

        public static ColorMatrix Sepia()
        {
            ColorMatrix m = Identity();
            m.Matrix00 = 0.393f;
            m.Matrix01 = 0.349f;
            m.Matrix02 = 0.272f;
            m.Matrix10 = 0.769f;
            m.Matrix11 = 0.686f;
            m.Matrix12 = 0.534f;
            m.Matrix20 = 0.189f;
            m.Matrix21 = 0.168f;
            m.Matrix22 = 0.131f;
            return m;
        }

        public static ColorMatrix PresetMatrix(string name)
        {
            switch (name)
            {
                case "冷白明亮":
                    ColorMatrix cool = Identity();
                    cool.Matrix00 = 0.98f;
                    cool.Matrix11 = 1.03f;
                    cool.Matrix22 = 1.09f;
                    cool.Matrix40 = 0.07f;
                    cool.Matrix41 = 0.08f;
                    cool.Matrix42 = 0.11f;
                    return cool;

                case "暖阳":
                    return Multiply(Multiply(Brightness(0.10f), Temperature(0.65f)), Multiply(Contrast(0.05f), Tint(0.06f)));

                case "清新自然":
                    return Multiply(Multiply(Brightness(0.10f), Saturation(1.10f)), Multiply(Tint(-0.10f), Temperature(-0.12f)));

                case "日系通透":
                    return Multiply(Multiply(Brightness(0.18f), Contrast(-0.16f)), Multiply(Temperature(-0.22f), Saturation(0.98f)));

                case "高级灰":
                    return Multiply(Multiply(Saturation(0.68f), Contrast(-0.06f)), Multiply(Temperature(-0.16f), Brightness(0.04f)));

                case "复古胶片":
                    return Multiply(Multiply(Saturation(0.78f), Temperature(0.30f)), Multiply(Contrast(-0.12f), Multiply(Brightness(0.07f), Tint(-0.08f))));

                case "青橙":
                    return Multiply(Multiply(Saturation(1.06f), Contrast(0.10f)), Multiply(Temperature(0.28f), Tint(-0.06f)));

                case "黑白":
                    return Grayscale();

                case "高对比黑白":
                    return Multiply(Grayscale(), Contrast(0.35f));

                case "鲜艳增强":
                    return Multiply(Saturation(1.35f), Contrast(0.14f));

                case "冷调夜景":
                    return Multiply(Multiply(Temperature(-0.65f), Brightness(-0.08f)), Multiply(Contrast(0.12f), Saturation(0.92f)));

                case "蓝调忧郁":
                    return Multiply(Multiply(Temperature(-0.70f), Saturation(0.80f)), Brightness(-0.05f));

                case "怀旧泛黄":
                    return Sepia();

                default:
                    return Identity();
            }
        }

        public class ColorStats
        {
            public double Bright;
            public double Contrast;
            public double Sat;
            public double WarmRatio;
            public double TintRatio;
        }

        public static ColorStats MeasureStats(Bitmap source, int maxSize)
        {
            Bitmap work = ImageUtil.CreatePreview(source, maxSize);
            bool own = work != null;
            if (work == null)
            {
                work = source;
            }

            try
            {
                int w = work.Width;
                int h = work.Height;
                BitmapData data = work.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = data.Stride;
                    byte[] buf = new byte[stride * h];
                    Marshal.Copy(data.Scan0, buf, 0, buf.Length);

                    double sumY = 0, sumY2 = 0, sumSat = 0, sumR = 0, sumG = 0, sumB = 0;
                    long n = 0;
                    for (int y = 0; y < h; y++)
                    {
                        int row = y * stride;
                        for (int x = 0; x < w; x++)
                        {
                            int i = row + x * 4;
                            int b = buf[i], g = buf[i + 1], r = buf[i + 2], a = buf[i + 3];
                            if (a < 8)
                            {
                                continue;
                            }
                            double yv = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
                            int mx = Math.Max(r, Math.Max(g, b));
                            int mn = Math.Min(r, Math.Min(g, b));
                            double sat = mx <= 0 ? 0 : (mx - mn) / (double)mx;
                            sumY += yv;
                            sumY2 += yv * yv;
                            sumSat += sat;
                            sumR += r;
                            sumG += g;
                            sumB += b;
                            n++;
                        }
                    }

                    if (n == 0)
                    {
                        n = 1;
                    }
                    double meanY = sumY / n;
                    double variance = sumY2 / n - meanY * meanY;
                    double meanR = sumR / n, meanG = sumG / n, meanB = sumB / n;

                    ColorStats stats = new ColorStats();
                    stats.Bright = meanY;
                    stats.Contrast = Math.Sqrt(Math.Max(0, variance));
                    stats.Sat = sumSat / n;
                    stats.WarmRatio = meanB < 1 ? 1 : meanR / meanB;
                    double rb = (meanR + meanB) / 2.0;
                    stats.TintRatio = rb < 1 ? 1 : meanG / rb;
                    return stats;
                }
                finally
                {
                    work.UnlockBits(data);
                }
            }
            finally
            {
                if (own)
                {
                    work.Dispose();
                }
            }
        }

        public static double[] EstimateAdjustment(Bitmap target, ColorStats reference, int iterations)
        {
            Bitmap small = ImageUtil.CreatePreview(target, 256);
            bool own = small != null;
            Bitmap work = small != null ? small : target;
            double[] p = { 0, 0, 1, 0, 0 };

            try
            {
                for (int it = 0; it <= iterations; it++)
                {
                    using (Bitmap current = ImageUtil.ApplyColorMatrix(work, MatrixFromParams(p)))
                    {
                        ColorStats s = MeasureStats(current, 256);
                        p[0] += (reference.Bright - s.Bright) / 0.4;
                        p[1] += SafeDiv(reference.Contrast, s.Contrast) - 1.0;
                        p[2] *= SafeDiv(reference.Sat, s.Sat);
                        p[3] += (SafeDiv(reference.WarmRatio, s.WarmRatio) - 1.0) / 0.30;
                        p[4] += (1.0 - SafeDiv(reference.TintRatio, s.TintRatio)) / 0.15;
                        ClampParams(p);
                    }
                }
            }
            finally
            {
                if (own)
                {
                    work.Dispose();
                }
            }
            return p;
        }

        public static ColorMatrix MatrixFromParams(double[] p)
        {
            ColorMatrix m = Saturation((float)p[2]);
            m = Multiply(m, Contrast((float)p[1]));
            m = Multiply(m, Brightness((float)p[0]));
            m = Multiply(m, Temperature((float)p[3]));
            m = Multiply(m, Tint((float)p[4]));
            return m;
        }

        private static double SafeDiv(double a, double b)
        {
            if (b < 1e-6 && b > -1e-6)
            {
                return 1;
            }
            return a / b;
        }

        private static void ClampParams(double[] p)
        {
            p[0] = Math.Max(-1, Math.Min(1, p[0]));
            p[1] = Math.Max(-1, Math.Min(1, p[1]));
            p[2] = Math.Max(0, Math.Min(2, p[2]));
            p[3] = Math.Max(-1, Math.Min(1, p[3]));
            p[4] = Math.Max(-1, Math.Min(1, p[4]));
        }

        public static void Vignette(Bitmap bmp, float strength)
        {
            if (strength <= 0f)
            {
                return;
            }

            int w = bmp.Width;
            int h = bmp.Height;
            int alpha = (int)Math.Min(255f, strength * 230f);

            using (Graphics g = Graphics.FromImage(bmp))
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(0, 0, w, h);
                using (PathGradientBrush brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = Color.FromArgb(0, 0, 0, 0);
                    PointF[] points = path.PathPoints;
                    Color[] colors = new Color[points.Length];
                    for (int i = 0; i < colors.Length; i++)
                    {
                        colors[i] = Color.FromArgb(alpha, 0, 0, 0);
                    }
                    brush.SurroundColors = colors;
                    g.FillRectangle(brush, 0, 0, w, h);
                }
            }
        }

        public static void LightLeak(Bitmap bmp, float strength)
        {
            if (strength <= 0f)
            {
                return;
            }

            int w = bmp.Width;
            int h = bmp.Height;
            int alpha = (int)Math.Min(255f, strength * 170f);

            using (Graphics g = Graphics.FromImage(bmp))
            using (LinearGradientBrush brush = new LinearGradientBrush(
                new Rectangle(0, 0, w, h),
                Color.FromArgb(alpha, 255, 168, 84),
                Color.FromArgb(0, 255, 168, 84),
                55f))
            {
                g.FillRectangle(brush, 0, 0, w, h);
            }
        }

        public static void Frame(Bitmap bmp, float strength)
        {
            int thickness = 4 + (int)(strength * 26f);
            using (Graphics g = Graphics.FromImage(bmp))
            using (Pen pen = new Pen(Color.White, thickness * 2f))
            {
                g.DrawRectangle(pen, 0, 0, bmp.Width - 1, bmp.Height - 1);
            }
        }

        public static void Soften(Bitmap bmp, float strength)
        {
            int factor = 1 + (int)(strength * 8f);
            if (factor < 2)
            {
                return;
            }

            int w = bmp.Width;
            int h = bmp.Height;
            int sw = Math.Max(1, w / factor);
            int sh = Math.Max(1, h / factor);

            using (Bitmap small = new Bitmap(sw, sh, PixelFormat.Format32bppArgb))
            {
                using (Graphics gs = Graphics.FromImage(small))
                {
                    gs.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    gs.DrawImage(bmp, new Rectangle(0, 0, sw, sh), new Rectangle(0, 0, w, h), GraphicsUnit.Pixel);
                }
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(small, new Rectangle(0, 0, w, h), new Rectangle(0, 0, sw, sh), GraphicsUnit.Pixel);
                }
            }
        }

        public static void Sharpen(Bitmap bmp, float strength)
        {
            if (strength <= 0f)
            {
                return;
            }

            int w = bmp.Width;
            int h = bmp.Height;
            if (w < 3 || h < 3)
            {
                return;
            }

            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] src = new byte[stride * h];
                Marshal.Copy(data.Scan0, src, 0, src.Length);
                byte[] dst = new byte[src.Length];
                Array.Copy(src, dst, src.Length);

                float s = strength;
                float center = 1f + 4f * s;

                for (int y = 1; y < h - 1; y++)
                {
                    for (int x = 1; x < w - 1; x++)
                    {
                        int idx = y * stride + x * 4;
                        for (int c = 0; c < 3; c++)
                        {
                            float value = src[idx + c] * center
                                - s * (src[idx - 4 + c] + src[idx + 4 + c] + src[idx - stride + c] + src[idx + stride + c]);
                            dst[idx + c] = ClampByte(value);
                        }
                    }
                }

                Marshal.Copy(dst, 0, data.Scan0, dst.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        public static void Grain(Bitmap bmp, float strength, int seed)
        {
            if (strength <= 0f)
            {
                return;
            }

            int w = bmp.Width;
            int h = bmp.Height;
            int amount = (int)(strength * 48f);

            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] buf = new byte[stride * h];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);

                Random random = new Random(seed);
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int idx = row + x * 4;
                        int n = random.Next(-amount, amount + 1);
                        buf[idx] = ClampByte(buf[idx] + n);
                        buf[idx + 1] = ClampByte(buf[idx + 1] + n);
                        buf[idx + 2] = ClampByte(buf[idx + 2] + n);
                    }
                }

                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private static byte ClampByte(float value)
        {
            if (value < 0f)
            {
                return 0;
            }
            if (value > 255f)
            {
                return 255;
            }
            return (byte)(value + 0.5f);
        }

        private static float[,] ToArray(ColorMatrix m)
        {
            return new float[,]
            {
                { m.Matrix00, m.Matrix01, m.Matrix02, m.Matrix03, m.Matrix04 },
                { m.Matrix10, m.Matrix11, m.Matrix12, m.Matrix13, m.Matrix14 },
                { m.Matrix20, m.Matrix21, m.Matrix22, m.Matrix23, m.Matrix24 },
                { m.Matrix30, m.Matrix31, m.Matrix32, m.Matrix33, m.Matrix34 },
                { m.Matrix40, m.Matrix41, m.Matrix42, m.Matrix43, m.Matrix44 }
            };
        }

        private static ColorMatrix FromArray(float[,] a)
        {
            ColorMatrix m = new ColorMatrix();
            m.Matrix00 = a[0, 0]; m.Matrix01 = a[0, 1]; m.Matrix02 = a[0, 2]; m.Matrix03 = a[0, 3]; m.Matrix04 = a[0, 4];
            m.Matrix10 = a[1, 0]; m.Matrix11 = a[1, 1]; m.Matrix12 = a[1, 2]; m.Matrix13 = a[1, 3]; m.Matrix14 = a[1, 4];
            m.Matrix20 = a[2, 0]; m.Matrix21 = a[2, 1]; m.Matrix22 = a[2, 2]; m.Matrix23 = a[2, 3]; m.Matrix24 = a[2, 4];
            m.Matrix30 = a[3, 0]; m.Matrix31 = a[3, 1]; m.Matrix32 = a[3, 2]; m.Matrix33 = a[3, 3]; m.Matrix34 = a[3, 4];
            m.Matrix40 = a[4, 0]; m.Matrix41 = a[4, 1]; m.Matrix42 = a[4, 2]; m.Matrix43 = a[4, 3]; m.Matrix44 = a[4, 4];
            return m;
        }
    }
}
