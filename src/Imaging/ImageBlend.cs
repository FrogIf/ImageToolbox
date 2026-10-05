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
                byte[] abuf = EnsureBuffer(ref _blendAScratch, astride * h);
                byte[] obuf = EnsureBuffer(ref _blendOScratch, ostride * h);
                byte[] rbuf = EnsureBuffer(ref _blendRScratch, rstride * h);
                Marshal.Copy(ad.Scan0, abuf, 0, astride * h);
                Marshal.Copy(od.Scan0, obuf, 0, ostride * h);

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

                Marshal.Copy(rbuf, 0, rd.Scan0, rstride * h);
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

        // 正确的 source-over 合成：保留底图透明度，图层被移除/隐藏后能正确透出棋盘格。
        public static Bitmap Composite(Bitmap baseImage, Bitmap overlay, BlendMode mode, float opacity)
        {
            return Composite(baseImage, overlay, mode, opacity, 0, 0);
        }

        // 带偏移的合成：把 overlay 平移到 (offsetX, offsetY) 后再叠加（用于移动图层时的实时预览）。
        public static Bitmap Composite(Bitmap baseImage, Bitmap overlay, BlendMode mode, float opacity, int offsetX, int offsetY)
        {
            int w = baseImage.Width;
            int h = baseImage.Height;

            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(baseImage, new Rectangle(0, 0, w, h));
            }
            CompositeInto(result, overlay, mode, opacity, offsetX, offsetY);
            return result;
        }

        // 把 overlay 就地叠加进 acc（acc 同时作为底图和输出），避免每个图层都重新分配一张整图。
        // 预览/画笔等高频路径下可显著降低 GC 压力。缓冲区按线程复用。
        public static void CompositeInto(Bitmap acc, Bitmap overlay, BlendMode mode, float opacity, int offsetX, int offsetY)
        {
            if (acc == null || overlay == null) { return; }
            int w = acc.Width;
            int h = acc.Height;
            if (opacity <= 0f) { return; }
            if (opacity > 1f) { opacity = 1f; }

            Bitmap scaled = overlay;
            bool own = false;
            if (offsetX != 0 || offsetY != 0)
            {
                scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(scaled))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(overlay, offsetX, offsetY, overlay.Width, overlay.Height);
                }
                own = true;
            }
            else if (overlay.Width != w || overlay.Height != h)
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

            BitmapData ad = acc.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            BitmapData od = scaled.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int astride = ad.Stride;
                int ostride = od.Stride;
                byte[] abuf = EnsureBuffer(ref _accScratch, astride * h);
                byte[] obuf = EnsureBuffer(ref _overScratch, ostride * h);
                Marshal.Copy(ad.Scan0, abuf, 0, astride * h);
                Marshal.Copy(od.Scan0, obuf, 0, ostride * h);

                // 普通模式（最常见的图层/盖印/拼合路径）走定点整数快路径：
                // 覆盖 alpha 折成 0..255 后，为 255 直接拷贝；底图不透明时 out = over*a + base*(1-a)，
                // 完全避开逐像素浮点除法与混合模式 switch。
                bool fastNormal = (mode == BlendMode.Normal);
                int op256 = fastNormal ? (int)(opacity * 256f + 0.5f) : 256;
                for (int y = 0; y < h; y++)
                {
                    int ao = y * astride;
                    int oo = y * ostride;
                    for (int x = 0; x < w; x++)
                    {
                        int i = ao + x * 4;
                        int j = oo + x * 4;
                        int pA = obuf[j + 3];
                        if (pA == 0) { continue; }
                        if (fastNormal)
                        {
                            int oa255 = (pA * op256) >> 8;
                            if (oa255 == 255)
                            {
                                abuf[i] = obuf[j];
                                abuf[i + 1] = obuf[j + 1];
                                abuf[i + 2] = obuf[j + 2];
                                abuf[i + 3] = 255;
                                continue;
                            }
                            // 覆盖 alpha 很小时（半透明叠加或低不透明度）折整会变成 0，仍需按精确浮点
                            // 合成（底图也不透明时结果接近不变，但底图同样半透明时不能直接丢弃）。
                            if (oa255 > 0 && abuf[i + 3] == 255)
                            {
                                int inv = 255 - oa255;
                                abuf[i] = (byte)((obuf[j] * oa255 + abuf[i] * inv + 127) / 255);
                                abuf[i + 1] = (byte)((obuf[j + 1] * oa255 + abuf[i + 1] * inv + 127) / 255);
                                abuf[i + 2] = (byte)((obuf[j + 2] * oa255 + abuf[i + 2] * inv + 127) / 255);
                                continue;
                            }
                        }
                        float oa = (pA / 255f) * opacity;
                        if (oa <= 0f) { continue; }
                        float aoA = abuf[i + 3] / 255f;
                        float ra = oa + aoA * (1f - oa);
                        if (ra <= 0f)
                        {
                            abuf[i] = 0; abuf[i + 1] = 0; abuf[i + 2] = 0; abuf[i + 3] = 0;
                            continue;
                        }
                        abuf[i] = CompChannel(abuf[i], obuf[j], mode, aoA, oa, ra);
                        abuf[i + 1] = CompChannel(abuf[i + 1], obuf[j + 1], mode, aoA, oa, ra);
                        abuf[i + 2] = CompChannel(abuf[i + 2], obuf[j + 2], mode, aoA, oa, ra);
                        abuf[i + 3] = (byte)(ra * 255f + 0.5f);
                    }
                }

                Marshal.Copy(abuf, 0, ad.Scan0, astride * h);
            }
            finally
            {
                acc.UnlockBits(ad);
                scaled.UnlockBits(od);
                if (own)
                {
                    scaled.Dispose();
                }
            }
        }

        [ThreadStatic] private static byte[] _accScratch;
        [ThreadStatic] private static byte[] _overScratch;
        [ThreadStatic] private static byte[] _blendAScratch;
        [ThreadStatic] private static byte[] _blendOScratch;
        [ThreadStatic] private static byte[] _blendRScratch;

        private static byte[] EnsureBuffer(ref byte[] buffer, int size)
        {
            if (buffer == null || buffer.Length < size)
            {
                buffer = new byte[size];
            }
            return buffer;
        }

        private static byte CompChannel(byte baseB, byte overB, BlendMode mode, float aoA, float oa, float ra)
        {
            float a = baseB / 255f;
            float b = overB / 255f;
            float blended = BlendChannel(a, b, mode);
            float value = (blended * oa + a * aoA * (1f - oa)) / ra;
            if (value < 0f) { value = 0f; }
            if (value > 1f) { value = 1f; }
            return (byte)(value * 255f + 0.5f);
        }

        private static float BlendChannel(float a, float b, BlendMode mode)
        {
            switch (mode)
            {
                case BlendMode.Multiply:
                    return a * b;
                case BlendMode.Screen:
                    return 1f - (1f - a) * (1f - b);
                case BlendMode.Overlay:
                    return a < 0.5f ? 2f * a * b : 1f - 2f * (1f - a) * (1f - b);
                case BlendMode.HardLight:
                    return b < 0.5f ? 2f * a * b : 1f - 2f * (1f - a) * (1f - b);
                case BlendMode.SoftLight:
                    return SoftLight(a, b);
                case BlendMode.Difference:
                    return Math.Abs(a - b);
                case BlendMode.Darken:
                    return Math.Min(a, b);
                case BlendMode.Lighten:
                    return Math.Max(a, b);
                case BlendMode.Add:
                    return Math.Min(1f, a + b);
                case BlendMode.Subtract:
                    return Math.Max(0f, a - b);
                default:
                    return b;
            }
        }

        private static byte Mix(byte baseB, byte overB, BlendMode mode, float alpha)
        {
            if (alpha <= 0f)
            {
                return baseB;
            }
            float a = baseB / 255f;
            float b = overB / 255f;
            float blended = BlendChannel(a, b, mode);
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
