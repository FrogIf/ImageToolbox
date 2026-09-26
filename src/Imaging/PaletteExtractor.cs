using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ImageToolbox
{
    public static class PaletteExtractor
    {
        public static List<Color> Extract(Bitmap source, int count, int maxSample)
        {
            if (count < 1) { count = 1; }
            if (count > 16) { count = 16; }

            Bitmap sample = ImageFilters.Clone(source);
            try
            {
                int w = sample.Width;
                int h = sample.Height;
                int step = Math.Max(1, Math.Max(w, h) / Math.Max(1, maxSample));

                List<int> pixels = new List<int>();
                BitmapData data = sample.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = data.Stride;
                    byte[] buf = new byte[stride * h];
                    Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                    for (int y = 0; y < h; y += step)
                    {
                        int row = y * stride;
                        for (int x = 0; x < w; x += step)
                        {
                            int i = row + x * 4;
                            if (buf[i + 3] < 8)
                            {
                                continue;
                            }
                            pixels.Add((buf[i + 2] << 16) | (buf[i + 1] << 8) | buf[i]);
                        }
                    }
                }
                finally
                {
                    sample.UnlockBits(data);
                }

                if (pixels.Count == 0)
                {
                    return new List<Color>();
                }

                List<List<int>> boxes = new List<List<int>>();
                boxes.Add(pixels);
                while (boxes.Count < count)
                {
                    int target = -1;
                    int bestRange = -1;
                    int bestChannel = 0;
                    for (int i = 0; i < boxes.Count; i++)
                    {
                        if (boxes[i].Count < 2)
                        {
                            continue;
                        }
                        int channel;
                        int range = ChannelRange(boxes[i], out channel);
                        if (range > bestRange)
                        {
                            bestRange = range;
                            target = i;
                            bestChannel = channel;
                        }
                    }
                    if (target < 0 || bestRange <= 0)
                    {
                        break;
                    }

                    List<int> box = boxes[target];
                    int ch = bestChannel;
                    box.Sort(delegate(int a, int b)
                    {
                        return Channel(a, ch).CompareTo(Channel(b, ch));
                    });
                    int mid = box.Count / 2;
                    List<int> left = box.GetRange(0, mid);
                    List<int> right = box.GetRange(mid, box.Count - mid);
                    boxes[target] = left;
                    boxes.Add(right);
                }

                List<Color> result = new List<Color>();
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (boxes[i].Count == 0)
                    {
                        continue;
                    }
                    long sr = 0, sg = 0, sb = 0;
                    for (int j = 0; j < boxes[i].Count; j++)
                    {
                        int p = boxes[i][j];
                        sr += (p >> 16) & 0xFF;
                        sg += (p >> 8) & 0xFF;
                        sb += p & 0xFF;
                    }
                    result.Add(Color.FromArgb(
                        (int)(sr / boxes[i].Count),
                        (int)(sg / boxes[i].Count),
                        (int)(sb / boxes[i].Count)));
                }
                return result;
            }
            finally
            {
                sample.Dispose();
            }
        }

        private static int Channel(int packed, int channel)
        {
            if (channel == 0) { return (packed >> 16) & 0xFF; }
            if (channel == 1) { return (packed >> 8) & 0xFF; }
            return packed & 0xFF;
        }

        private static int ChannelRange(List<int> box, out int channel)
        {
            int minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0;
            for (int i = 0; i < box.Count; i++)
            {
                int p = box[i];
                int r = (p >> 16) & 0xFF;
                int g = (p >> 8) & 0xFF;
                int b = p & 0xFF;
                if (r < minR) { minR = r; }
                if (r > maxR) { maxR = r; }
                if (g < minG) { minG = g; }
                if (g > maxG) { maxG = g; }
                if (b < minB) { minB = b; }
                if (b > maxB) { maxB = b; }
            }
            int rr = maxR - minR;
            int rg = maxG - minG;
            int rb = maxB - minB;
            if (rr >= rg && rr >= rb)
            {
                channel = 0;
                return rr;
            }
            if (rg >= rb)
            {
                channel = 1;
                return rg;
            }
            channel = 2;
            return rb;
        }

        public static string Hex(Color color)
        {
            return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }

        public static void RgbToHsv(Color color, out double hue, out double sat, out double val)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            val = max;
            sat = max <= 0 ? 0 : delta / max;
            if (delta <= 0)
            {
                hue = 0;
                return;
            }
            if (max == r)
            {
                hue = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                hue = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                hue = 60 * (((r - g) / delta) + 4);
            }
            if (hue < 0)
            {
                hue += 360;
            }
        }
    }
}
