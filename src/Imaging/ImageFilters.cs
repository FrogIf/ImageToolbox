using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ImageToolbox
{
    public static class ImageFilters
    {
        public static Bitmap Clone(Bitmap source)
        {
            Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(
                    source,
                    new Rectangle(0, 0, source.Width, source.Height),
                    0,
                    0,
                    source.Width,
                    source.Height,
                    GraphicsUnit.Pixel);
            }
            return result;
        }

        public static byte[] CopyPixels(Bitmap bmp, out int stride)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                stride = data.Stride;
                byte[] buf = new byte[stride * h];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                return buf;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        // 返回非透明像素的包围盒（去掉四周透明区域）。整图全透明时返回 Rectangle.Empty。
        public static Rectangle ContentBounds(Bitmap bmp)
        {
            int stride;
            byte[] px = CopyPixels(bmp, out stride);
            int w = bmp.Width;
            int h = bmp.Height;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                int baseIndex = row + 3;
                for (int x = 0; x < w; x++)
                {
                    if (px[baseIndex + x * 4] != 0)
                    {
                        if (x < minX) { minX = x; }
                        if (x > maxX) { maxX = x; }
                        if (y < minY) { minY = y; }
                        if (y > maxY) { maxY = y; }
                    }
                }
            }
            if (maxX < 0) { return Rectangle.Empty; }
            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        public static void PastePixels(Bitmap bmp, byte[] buf)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(buf, y * data.Stride, (IntPtr)(data.Scan0.ToInt64() + (long)y * data.Stride), data.Stride);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        public static void Mosaic(Bitmap bmp, int block)
        {
            if (block < 2)
            {
                block = 2;
            }

            int w = bmp.Width;
            int h = bmp.Height;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] buf = new byte[stride * h];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);

                for (int by = 0; by < h; by += block)
                {
                    int y2 = Math.Min(by + block, h);
                    for (int bx = 0; bx < w; bx += block)
                    {
                        int x2 = Math.Min(bx + block, w);
                        long sa = 0, sr = 0, sg = 0, sb = 0, n = 0;
                        for (int y = by; y < y2; y++)
                        {
                            int row = y * stride;
                            for (int x = bx; x < x2; x++)
                            {
                                int i = row + x * 4;
                                sb += buf[i];
                                sg += buf[i + 1];
                                sr += buf[i + 2];
                                sa += buf[i + 3];
                                n++;
                            }
                        }
                        if (n == 0)
                        {
                            continue;
                        }
                        byte bb = (byte)(sb / n);
                        byte bg = (byte)(sg / n);
                        byte br = (byte)(sr / n);
                        byte ba = (byte)(sa / n);
                        for (int y = by; y < y2; y++)
                        {
                            int row = y * stride;
                            for (int x = bx; x < x2; x++)
                            {
                                int i = row + x * 4;
                                buf[i] = bb;
                                buf[i + 1] = bg;
                                buf[i + 2] = br;
                                buf[i + 3] = ba;
                            }
                        }
                    }
                }

                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        public static void GaussianBlur(Bitmap bmp, int radius)
        {
            if (radius <= 0)
            {
                return;
            }

            int r = Math.Max(1, radius / 3);
            BoxBlur(bmp, r);
            BoxBlur(bmp, r);
            BoxBlur(bmp, r);
        }

        private static void BoxBlur(Bitmap bmp, int radius)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            if (w < 1 || h < 1)
            {
                return;
            }

            int r = Math.Max(1, radius);
            int win = 2 * r + 1;

            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] src = new byte[stride * h];
                Marshal.Copy(data.Scan0, src, 0, src.Length);
                byte[] tmp = new byte[src.Length];

                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    int sa = 0, sr = 0, sg = 0, sb = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int xx = Clamp(k, 0, w - 1);
                        int i = row + xx * 4;
                        sb += src[i];
                        sg += src[i + 1];
                        sr += src[i + 2];
                        sa += src[i + 3];
                    }
                    for (int x = 0; x < w; x++)
                    {
                        int o = row + x * 4;
                        tmp[o] = (byte)(sb / win);
                        tmp[o + 1] = (byte)(sg / win);
                        tmp[o + 2] = (byte)(sr / win);
                        tmp[o + 3] = (byte)(sa / win);

                        int xl = Clamp(x - r, 0, w - 1);
                        int iL = row + xl * 4;
                        int xr = Clamp(x + r + 1, 0, w - 1);
                        int iR = row + xr * 4;
                        sb += src[iR] - src[iL];
                        sg += src[iR + 1] - src[iL + 1];
                        sr += src[iR + 2] - src[iL + 2];
                        sa += src[iR + 3] - src[iL + 3];
                    }
                }

                for (int x = 0; x < w; x++)
                {
                    int sa = 0, sr = 0, sg = 0, sb = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int yy = Clamp(k, 0, h - 1);
                        int i = yy * stride + x * 4;
                        sb += tmp[i];
                        sg += tmp[i + 1];
                        sr += tmp[i + 2];
                        sa += tmp[i + 3];
                    }
                    for (int y = 0; y < h; y++)
                    {
                        int o = y * stride + x * 4;
                        src[o] = (byte)(sb / win);
                        src[o + 1] = (byte)(sg / win);
                        src[o + 2] = (byte)(sr / win);
                        src[o + 3] = (byte)(sa / win);

                        int yt = Clamp(y - r, 0, h - 1);
                        int iT = yt * stride + x * 4;
                        int yb = Clamp(y + r + 1, 0, h - 1);
                        int iB = yb * stride + x * 4;
                        sb += tmp[iB] - tmp[iT];
                        sg += tmp[iB + 1] - tmp[iT + 1];
                        sr += tmp[iB + 2] - tmp[iT + 2];
                        sa += tmp[iB + 3] - tmp[iT + 3];
                    }
                }

                Marshal.Copy(src, 0, data.Scan0, src.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        public static void MotionBlur(Bitmap bmp, int length, double angleDeg)
        {
            if (length < 2)
            {
                length = 2;
            }

            int w = bmp.Width;
            int h = bmp.Height;
            double rad = angleDeg * Math.PI / 180.0;
            double dx = Math.Cos(rad);
            double dy = Math.Sin(rad);
            int half = length / 2;

            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] src = new byte[stride * h];
                Marshal.Copy(data.Scan0, src, 0, src.Length);
                byte[] dst = new byte[src.Length];

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int sa = 0, sr = 0, sg = 0, sb = 0, cnt = 0;
                        for (int t = -half; t <= half; t++)
                        {
                            int xx = (int)Math.Round(x + dx * t);
                            int yy = (int)Math.Round(y + dy * t);
                            if (xx < 0) { xx = 0; }
                            if (xx >= w) { xx = w - 1; }
                            if (yy < 0) { yy = 0; }
                            if (yy >= h) { yy = h - 1; }
                            int i = yy * stride + xx * 4;
                            sb += src[i];
                            sg += src[i + 1];
                            sr += src[i + 2];
                            sa += src[i + 3];
                            cnt++;
                        }
                        int o = y * stride + x * 4;
                        dst[o] = (byte)(sb / cnt);
                        dst[o + 1] = (byte)(sg / cnt);
                        dst[o + 2] = (byte)(sr / cnt);
                        dst[o + 3] = (byte)(sa / cnt);
                    }
                }

                Marshal.Copy(dst, 0, data.Scan0, dst.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        public static void OilPaint(Bitmap bmp, int radius, int levels)
        {
            if (radius < 1) { radius = 1; }
            if (radius > 8) { radius = 8; }
            if (levels < 2) { levels = 2; }
            if (levels > 64) { levels = 64; }

            int w = bmp.Width;
            int h = bmp.Height;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] src = new byte[stride * h];
                Marshal.Copy(data.Scan0, src, 0, src.Length);
                byte[] dst = new byte[src.Length];

                int[] count = new int[levels];
                int[] sr = new int[levels];
                int[] sg = new int[levels];
                int[] sb = new int[levels];
                int[] sa = new int[levels];

                for (int y = 0; y < h; y++)
                {
                    int y0 = Math.Max(0, y - radius);
                    int y1 = Math.Min(h - 1, y + radius);
                    for (int x = 0; x < w; x++)
                    {
                        int x0 = Math.Max(0, x - radius);
                        int x1 = Math.Min(w - 1, x + radius);
                        for (int k = 0; k < levels; k++)
                        {
                            count[k] = 0;
                            sr[k] = 0;
                            sg[k] = 0;
                            sb[k] = 0;
                            sa[k] = 0;
                        }

                        for (int yy = y0; yy <= y1; yy++)
                        {
                            int row = yy * stride;
                            for (int xx = x0; xx <= x1; xx++)
                            {
                                int i = row + xx * 4;
                                int b = src[i];
                                int g = src[i + 1];
                                int r = src[i + 2];
                                int lum = (r * 77 + g * 151 + b * 28) >> 8;
                                int bin = lum * levels / 256;
                                if (bin >= levels) { bin = levels - 1; }
                                count[bin]++;
                                sb[bin] += b;
                                sg[bin] += g;
                                sr[bin] += r;
                                sa[bin] += src[i + 3];
                            }
                        }

                        int best = 0;
                        for (int k = 1; k < levels; k++)
                        {
                            if (count[k] > count[best])
                            {
                                best = k;
                            }
                        }

                        int o = y * stride + x * 4;
                        if (count[best] == 0)
                        {
                            dst[o] = src[o];
                            dst[o + 1] = src[o + 1];
                            dst[o + 2] = src[o + 2];
                            dst[o + 3] = src[o + 3];
                        }
                        else
                        {
                            dst[o] = (byte)(sb[best] / count[best]);
                            dst[o + 1] = (byte)(sg[best] / count[best]);
                            dst[o + 2] = (byte)(sr[best] / count[best]);
                            dst[o + 3] = (byte)(sa[best] / count[best]);
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

        public static void Sketch(Bitmap bmp, float strength)
        {
            if (strength <= 0f)
            {
                return;
            }
            if (strength > 1f)
            {
                strength = 1f;
            }

            int w = bmp.Width;
            int h = bmp.Height;
            if (w < 2 || h < 2)
            {
                return;
            }

            int stride;
            byte[] src = CopyPixels(bmp, out stride);

            using (Bitmap gray = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                BitmapData gd = gray.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                byte[] gbuf;
                int gstride;
                try
                {
                    gstride = gd.Stride;
                    gbuf = new byte[gstride * h];
                    for (int y = 0; y < h; y++)
                    {
                        int row = y * stride;
                        int grow = y * gstride;
                        for (int x = 0; x < w; x++)
                        {
                            int i = row + x * 4;
                            int lum = (src[i + 2] * 77 + src[i + 1] * 151 + src[i] * 28) >> 8;
                            int o = grow + x * 4;
                            gbuf[o] = (byte)lum;
                            gbuf[o + 1] = (byte)lum;
                            gbuf[o + 2] = (byte)lum;
                            gbuf[o + 3] = 255;
                        }
                    }
                    Marshal.Copy(gbuf, 0, gd.Scan0, gbuf.Length);
                }
                finally
                {
                    gray.UnlockBits(gd);
                }

                int radius = Math.Max(2, Math.Min(w, h) / 40);
                if (radius > 24)
                {
                    radius = 24;
                }
                GaussianBlur(gray, radius);

                gd = gray.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    gstride = gd.Stride;
                    gbuf = new byte[gstride * h];
                    Marshal.Copy(gd.Scan0, gbuf, 0, gbuf.Length);
                }
                finally
                {
                    gray.UnlockBits(gd);
                }

                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    int grow = y * gstride;
                    for (int x = 0; x < w; x++)
                    {
                        int i = row + x * 4;
                        int o = grow + x * 4;
                        int r = src[i + 2];
                        int g = src[i + 1];
                        int b = src[i];
                        int lum = (r * 77 + g * 151 + b * 28) >> 8;
                        int bg = gbuf[o];
                        int sk = bg <= 0 ? 255 : Math.Min(255, (int)((long)lum * 255 / bg));
                        src[i + 2] = ClampByte(r + (sk - r) * strength);
                        src[i + 1] = ClampByte(g + (sk - g) * strength);
                        src[i] = ClampByte(b + (sk - b) * strength);
                    }
                }
            }

            PastePixels(bmp, src);
        }

        public static void Emboss(Bitmap bmp, float strength)
        {
            if (strength <= 0f)
            {
                return;
            }
            if (strength > 1f)
            {
                strength = 1f;
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

                for (int y = 1; y < h - 1; y++)
                {
                    for (int x = 1; x < w - 1; x++)
                    {
                        int i = y * stride + x * 4;
                        int tl = Lum(src, i - stride - 4);
                        int t = Lum(src, i - stride);
                        int l = Lum(src, i - 4);
                        int c = Lum(src, i);
                        int r = Lum(src, i + 4);
                        int btm = Lum(src, i + stride);
                        int br = Lum(src, i + stride + 4);
                        int e = 128 + (-2 * tl - t - l + c + r + btm + 2 * br);
                        int v = ClampInt(e);
                        for (int ch = 0; ch < 3; ch++)
                        {
                            dst[i + ch] = ClampByte(src[i + ch] + (v - src[i + ch]) * strength);
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

        public static void EdgeDetect(Bitmap bmp, float strength)
        {
            if (strength <= 0f)
            {
                return;
            }
            if (strength > 1f)
            {
                strength = 1f;
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

                for (int y = 1; y < h - 1; y++)
                {
                    for (int x = 1; x < w - 1; x++)
                    {
                        int i = y * stride + x * 4;
                        int p00 = Lum(src, i - stride - 4);
                        int p01 = Lum(src, i - stride);
                        int p02 = Lum(src, i - stride + 4);
                        int p10 = Lum(src, i - 4);
                        int p12 = Lum(src, i + 4);
                        int p20 = Lum(src, i + stride - 4);
                        int p21 = Lum(src, i + stride);
                        int p22 = Lum(src, i + stride + 4);
                        int gx = -p00 + p02 - 2 * p10 + 2 * p12 - p20 + p22;
                        int gy = -p00 - 2 * p01 - p02 + p20 + 2 * p21 + p22;
                        int mag = (int)Math.Sqrt((double)(gx * gx + gy * gy));
                        if (mag > 255)
                        {
                            mag = 255;
                        }
                        int v = 255 - mag;
                        for (int ch = 0; ch < 3; ch++)
                        {
                            dst[i + ch] = ClampByte(src[i + ch] + (v - src[i + ch]) * strength);
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

        public static void BackgroundBlur(Bitmap bmp, int radius, float clearRatio)
        {
            if (radius < 1)
            {
                radius = 1;
            }
            if (clearRatio < 0f)
            {
                clearRatio = 0f;
            }
            if (clearRatio > 0.95f)
            {
                clearRatio = 0.95f;
            }

            int w = bmp.Width;
            int h = bmp.Height;

            using (Bitmap blur = Clone(bmp))
            {
                GaussianBlur(blur, radius);

                int stride;
                byte[] src = CopyPixels(bmp, out stride);
                byte[] blurred = CopyPixels(blur, out stride);

                double hw = w / 2.0;
                double hh = h / 2.0;
                if (hw < 1) { hw = 1; }
                if (hh < 1) { hh = 1; }
                double span = 1.0 - clearRatio;
                if (span < 1e-4)
                {
                    span = 1e-4;
                }

                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    double ny = (y - hh) / hh;
                    for (int x = 0; x < w; x++)
                    {
                        double nx = (x - hw) / hw;
                        double d = Math.Sqrt(nx * nx + ny * ny);
                        double t = (d - clearRatio) / span;
                        if (t < 0) { t = 0; }
                        if (t > 1) { t = 1; }
                        t = t * t * (3 - 2 * t);
                        int i = row + x * 4;
                        for (int ch = 0; ch < 4; ch++)
                        {
                            src[i + ch] = ClampByte(src[i + ch] + (blurred[i + ch] - src[i + ch]) * (float)t);
                        }
                    }
                }

                PastePixels(bmp, src);
            }
        }

        public static void Paper(Bitmap bmp, float strength, int seed)
        {
            if (strength <= 0f)
            {
                return;
            }
            if (strength > 1f)
            {
                strength = 1f;
            }

            int w = bmp.Width;
            int h = bmp.Height;
            if (w < 2 || h < 2)
            {
                return;
            }

            using (Bitmap noise = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                BitmapData nd = noise.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                int nstride;
                try
                {
                    nstride = nd.Stride;
                    byte[] nbuf = new byte[nstride * h];
                    Random random = new Random(seed);
                    for (int y = 0; y < h; y++)
                    {
                        int row = y * nstride;
                        for (int x = 0; x < w; x++)
                        {
                            int v = random.Next(256);
                            int o = row + x * 4;
                            nbuf[o] = (byte)v;
                            nbuf[o + 1] = (byte)v;
                            nbuf[o + 2] = (byte)v;
                            nbuf[o + 3] = 255;
                        }
                    }
                    Marshal.Copy(nbuf, 0, nd.Scan0, nbuf.Length);
                }
                finally
                {
                    noise.UnlockBits(nd);
                }

                GaussianBlur(noise, Math.Max(1, Math.Min(w, h) / 80));

                nd = noise.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] nb;
                try
                {
                    nstride = nd.Stride;
                    nb = new byte[nstride * h];
                    Marshal.Copy(nd.Scan0, nb, 0, nb.Length);
                }
                finally
                {
                    noise.UnlockBits(nd);
                }

                int stride;
                byte[] src = CopyPixels(bmp, out stride);
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    int nrow = y * nstride;
                    for (int x = 0; x < w; x++)
                    {
                        int i = row + x * 4;
                        int n = nb[nrow + x * 4];
                        float factor = 1f + (n - 128) / 128f * strength * 0.35f;
                        src[i] = ClampByte(src[i] * factor);
                        src[i + 1] = ClampByte(src[i + 1] * factor + strength * 5f);
                        src[i + 2] = ClampByte(src[i + 2] * factor + strength * 9f);
                    }
                }
                PastePixels(bmp, src);
            }
        }

        public static void Glow(Bitmap bmp, float strength)
        {
            if (strength <= 0f)
            {
                return;
            }
            if (strength > 1f)
            {
                strength = 1f;
            }

            int w = bmp.Width;
            int h = bmp.Height;

            using (Bitmap bright = Clone(bmp))
            {
                BitmapData bd = bright.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    int bstride = bd.Stride;
                    byte[] buf = new byte[bstride * h];
                    Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
                    for (int y = 0; y < h; y++)
                    {
                        int row = y * bstride;
                        for (int x = 0; x < w; x++)
                        {
                            int i = row + x * 4;
                            int lum = (buf[i + 2] * 77 + buf[i + 1] * 151 + buf[i] * 28) >> 8;
                            if (lum < 160)
                            {
                                buf[i] = 0;
                                buf[i + 1] = 0;
                                buf[i + 2] = 0;
                            }
                        }
                    }
                    Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
                }
                finally
                {
                    bright.UnlockBits(bd);
                }

                GaussianBlur(bright, Math.Max(2, Math.Min(w, h) / 30));

                int stride;
                byte[] src = CopyPixels(bmp, out stride);
                byte[] glow = CopyPixels(bright, out stride);
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int i = row + x * 4;
                        for (int ch = 0; ch < 3; ch++)
                        {
                            int a = src[i + ch];
                            int gl = glow[i + ch];
                            int screen = 255 - (255 - a) * (255 - gl) / 255;
                            src[i + ch] = ClampByte(a + (screen - a) * strength);
                        }
                    }
                }
                PastePixels(bmp, src);
            }
        }

        public static void RoundedCorners(Bitmap bmp, int radius)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            int max = Math.Min(w, h) / 2;
            if (radius > max)
            {
                radius = max;
            }
            if (radius <= 0)
            {
                return;
            }

            using (Bitmap mask = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(mask))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (GraphicsPath path = CreateRoundRect(0, 0, w, h, radius))
                    using (SolidBrush brush = new SolidBrush(Color.White))
                    {
                        g.FillPath(brush, path);
                    }
                }

                BitmapData md = mask.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] mbuf;
                int mstride;
                try
                {
                    mstride = md.Stride;
                    mbuf = new byte[mstride * h];
                    Marshal.Copy(md.Scan0, mbuf, 0, mbuf.Length);
                }
                finally
                {
                    mask.UnlockBits(md);
                }

                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = bd.Stride;
                    byte[] buf = new byte[stride * h];
                    Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
                    for (int y = 0; y < h; y++)
                    {
                        int row = y * stride;
                        int mrow = y * mstride;
                        for (int x = 0; x < w; x++)
                        {
                            int i = row + x * 4;
                            int m = mbuf[mrow + x * 4];
                            buf[i + 3] = (byte)(buf[i + 3] * m / 255);
                        }
                    }
                    Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
                }
                finally
                {
                    bmp.UnlockBits(bd);
                }
            }
        }

        public static Bitmap Circle(Bitmap source)
        {
            int size = Math.Min(source.Width, source.Height);
            if (size < 1)
            {
                size = 1;
            }

            Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                int sx = (source.Width - size) / 2;
                int sy = (source.Height - size) / 2;
                g.DrawImage(
                    source,
                    new Rectangle(0, 0, size, size),
                    new Rectangle(sx, sy, size, size),
                    GraphicsUnit.Pixel);
            }

            using (Bitmap mask = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics gm = Graphics.FromImage(mask))
                {
                    gm.SmoothingMode = SmoothingMode.AntiAlias;
                    gm.Clear(Color.Transparent);
                    using (SolidBrush brush = new SolidBrush(Color.White))
                    {
                        gm.FillEllipse(brush, 0, 0, size - 1, size - 1);
                    }
                }

                BitmapData md = mask.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] mbuf;
                int mstride;
                try
                {
                    mstride = md.Stride;
                    mbuf = new byte[mstride * size];
                    Marshal.Copy(md.Scan0, mbuf, 0, mbuf.Length);
                }
                finally
                {
                    mask.UnlockBits(md);
                }

                BitmapData rd = result.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = rd.Stride;
                    byte[] buf = new byte[stride * size];
                    Marshal.Copy(rd.Scan0, buf, 0, buf.Length);
                    for (int y = 0; y < size; y++)
                    {
                        int row = y * stride;
                        int mrow = y * mstride;
                        for (int x = 0; x < size; x++)
                        {
                            int i = row + x * 4;
                            int m = mbuf[mrow + x * 4];
                            buf[i + 3] = (byte)(buf[i + 3] * m / 255);
                        }
                    }
                    Marshal.Copy(buf, 0, rd.Scan0, buf.Length);
                }
                finally
                {
                    result.UnlockBits(rd);
                }
            }

            return result;
        }

        public static Bitmap DropShadow(Bitmap source, int offsetX, int offsetY, int blur, float opacity)
        {
            if (opacity < 0f) { opacity = 0f; }
            if (opacity > 1f) { opacity = 1f; }
            if (blur < 0) { blur = 0; }

            int pad = blur * 2 + Math.Max(Math.Abs(offsetX), Math.Abs(offsetY)) + 4;
            int w = source.Width + pad * 2;
            int h = source.Height + pad * 2;

            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Bitmap shadow = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(shadow))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(
                        source,
                        new Rectangle(pad + offsetX, pad + offsetY, source.Width, source.Height),
                        0,
                        0,
                        source.Width,
                        source.Height,
                        GraphicsUnit.Pixel);
                }

                BitmapData sd = shadow.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = sd.Stride;
                    byte[] buf = new byte[stride * h];
                    Marshal.Copy(sd.Scan0, buf, 0, buf.Length);
                    for (int i = 0; i < buf.Length; i += 4)
                    {
                        buf[i] = 0;
                        buf[i + 1] = 0;
                        buf[i + 2] = 0;
                        buf[i + 3] = ClampByte(buf[i + 3] * opacity);
                    }
                    Marshal.Copy(buf, 0, sd.Scan0, buf.Length);
                }
                finally
                {
                    shadow.UnlockBits(sd);
                }

                if (blur > 0)
                {
                    GaussianBlur(shadow, blur);
                }

                using (Graphics g = Graphics.FromImage(result))
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(shadow, 0, 0);
                    g.DrawImage(
                        source,
                        new Rectangle(pad, pad, source.Width, source.Height),
                        0,
                        0,
                        source.Width,
                        source.Height,
                        GraphicsUnit.Pixel);
                }
            }

            return result;
        }

        public static Bitmap MaskBlend(Bitmap baseImage, Bitmap effect, Bitmap mask)
        {
            int w = baseImage.Width;
            int h = baseImage.Height;
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            BitmapData bd = baseImage.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData ed = effect.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData md = mask.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData rd = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int bstride = bd.Stride;
                int estride = ed.Stride;
                int mstride = md.Stride;
                int rstride = rd.Stride;
                byte[] bbuf = new byte[bstride * h];
                byte[] ebuf = new byte[estride * h];
                byte[] mbuf = new byte[mstride * h];
                byte[] rbuf = new byte[rstride * h];
                Marshal.Copy(bd.Scan0, bbuf, 0, bbuf.Length);
                Marshal.Copy(ed.Scan0, ebuf, 0, ebuf.Length);
                Marshal.Copy(md.Scan0, mbuf, 0, mbuf.Length);

                for (int y = 0; y < h; y++)
                {
                    int brow = y * bstride;
                    int erow = y * estride;
                    int mrow = y * mstride;
                    int rrow = y * rstride;
                    for (int x = 0; x < w; x++)
                    {
                        int i = brow + x * 4;
                        int ei = erow + x * 4;
                        int mi = mrow + x * 4;
                        int o = rrow + x * 4;
                        int m = mbuf[mi + 3];
                        rbuf[o] = (byte)(bbuf[i] + (ebuf[ei] - bbuf[i]) * m / 255);
                        rbuf[o + 1] = (byte)(bbuf[i + 1] + (ebuf[ei + 1] - bbuf[i + 1]) * m / 255);
                        rbuf[o + 2] = (byte)(bbuf[i + 2] + (ebuf[ei + 2] - bbuf[i + 2]) * m / 255);
                        rbuf[o + 3] = bbuf[i + 3];
                    }
                }

                Marshal.Copy(rbuf, 0, rd.Scan0, rbuf.Length);
            }
            finally
            {
                baseImage.UnlockBits(bd);
                effect.UnlockBits(ed);
                mask.UnlockBits(md);
                result.UnlockBits(rd);
            }

            return result;
        }

        private static GraphicsPath CreateRoundRect(int x, int y, int w, int h, int r)
        {
            GraphicsPath path = new GraphicsPath();
            int d = r * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static int Lum(byte[] buf, int i)
        {
            return (buf[i + 2] * 77 + buf[i + 1] * 151 + buf[i] * 28) >> 8;
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) { return min; }
            if (v > max) { return max; }
            return v;
        }

        private static int ClampInt(int v)
        {
            if (v < 0) { return 0; }
            if (v > 255) { return 255; }
            return v;
        }

        private static byte ClampByte(float value)
        {
            if (value < 0f) { return 0; }
            if (value > 255f) { return 255; }
            return (byte)(value + 0.5f);
        }
    }
}
