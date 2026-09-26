using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ImageToolbox
{
    public static class ImageMatting
    {
        public static Bitmap ByColor(Bitmap source, Color key, int tolerance, bool invert)
        {
            int w = source.Width;
            int h = source.Height;
            Bitmap result = ImageFilters.Clone(source);
            BitmapData data = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] buf = new byte[stride * h];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int i = row + x * 4;
                        bool match = Within(key, buf[i + 2], buf[i + 1], buf[i], tolerance);
                        if (match != invert)
                        {
                            buf[i + 3] = 0;
                        }
                    }
                }
                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally
            {
                result.UnlockBits(data);
            }
            return result;
        }

        public static Bitmap MagicWand(Bitmap source, int seedX, int seedY, int tolerance, bool invert)
        {
            int w = source.Width;
            int h = source.Height;
            if (seedX < 0) { seedX = 0; }
            if (seedY < 0) { seedY = 0; }
            if (seedX >= w) { seedX = w - 1; }
            if (seedY >= h) { seedY = h - 1; }

            Bitmap result = ImageFilters.Clone(source);
            BitmapData data = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] buf = new byte[stride * h];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);

                int si = seedY * stride + seedX * 4;
                int sr = buf[si + 2];
                int sg = buf[si + 1];
                int sb = buf[si];
                Color seed = Color.FromArgb(255, sr, sg, sb);

                bool[] visited = new bool[w * h];
                Stack<int> stack = new Stack<int>();
                int seedIndex = seedY * w + seedX;
                visited[seedIndex] = true;
                stack.Push(seedIndex);

                while (stack.Count > 0)
                {
                    int p = stack.Pop();
                    int px = p % w;
                    int py = p / w;
                    int i = py * stride + px * 4;
                    if (!Within(seed, buf[i + 2], buf[i + 1], buf[i], tolerance))
                    {
                        continue;
                    }

                    if (!invert)
                    {
                        buf[i + 3] = 0;
                    }

                    if (px > 0 && !visited[p - 1]) { visited[p - 1] = true; stack.Push(p - 1); }
                    if (px < w - 1 && !visited[p + 1]) { visited[p + 1] = true; stack.Push(p + 1); }
                    if (py > 0 && !visited[p - w]) { visited[p - w] = true; stack.Push(p - w); }
                    if (py < h - 1 && !visited[p + w]) { visited[p + w] = true; stack.Push(p + w); }
                }

                if (invert)
                {
                    for (int y = 0; y < h; y++)
                    {
                        int row = y * stride;
                        for (int x = 0; x < w; x++)
                        {
                            int p = y * w + x;
                            if (!visited[p])
                            {
                                buf[row + x * 4 + 3] = 0;
                            }
                        }
                    }
                }

                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally
            {
                result.UnlockBits(data);
            }
            return result;
        }

        private static bool Within(Color key, int r, int g, int b, int tolerance)
        {
            int dr = Math.Abs(r - key.R);
            int dg = Math.Abs(g - key.G);
            int db = Math.Abs(b - key.B);
            return dr <= tolerance && dg <= tolerance && db <= tolerance;
        }
    }
}
