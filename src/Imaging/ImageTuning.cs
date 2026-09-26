using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ImageToolbox
{
    public class GradientPreset
    {
        public string Name;
        public float[] Stops;
        public Color[] Colors;

        public GradientPreset(string name, float[] stops, Color[] colors)
        {
            Name = name;
            Stops = stops;
            Colors = colors;
        }
    }

    public class TuningState
    {
        public int[] LevelBlack = { 0, 0, 0 };
        public int[] LevelWhite = { 255, 255, 255 };
        public float[] LevelGamma = { 1f, 1f, 1f };
        public PointF[][] CurvePoints = new PointF[4][];
        public float[] WhiteBalanceGain = { 1f, 1f, 1f };
        public float Hue;
        public float Saturation;
        public float Lightness;
        public int ToneMode;
        public int PosterizeLevels = 6;
        public Color DuotoneA = Color.FromArgb(20, 30, 60);
        public Color DuotoneB = Color.FromArgb(245, 230, 200);
        public int GradientPreset;
        public bool LutEnabled;
        public float LutStrength = 1f;
        public int LutSize;
        public float[] LutData;
        public bool LocalEnabled;
        public bool LocalLinear;
        public PointF LocalCenter = new PointF(0.5f, 0.5f);
        public float LocalRadiusX = 0.30f;
        public float LocalRadiusY = 0.30f;
        public float LocalFeather = 0.5f;
        public float LocalAngle;
        public float LocalExposure;
        public float LocalContrast;
        public float LocalSaturation;
    }

    public static class ImageTuning
    {
        public static readonly GradientPreset[] Gradients =
        {
            new GradientPreset("黑白", new float[] { 0f, 1f }, new Color[] { Color.Black, Color.White }),
            new GradientPreset("蓝橙", new float[] { 0f, 0.5f, 1f }, new Color[] { Color.FromArgb(20, 40, 90), Color.FromArgb(120, 110, 110), Color.FromArgb(255, 170, 90) }),
            new GradientPreset("青品", new float[] { 0f, 1f }, new Color[] { Color.FromArgb(10, 30, 40), Color.FromArgb(240, 160, 210) }),
            new GradientPreset("暖阳", new float[] { 0f, 0.6f, 1f }, new Color[] { Color.FromArgb(60, 30, 10), Color.FromArgb(220, 140, 60), Color.FromArgb(255, 245, 210) }),
            new GradientPreset("冷调", new float[] { 0f, 0.5f, 1f }, new Color[] { Color.FromArgb(5, 15, 40), Color.FromArgb(70, 120, 170), Color.FromArgb(220, 240, 255) }),
            new GradientPreset("紫绿", new float[] { 0f, 0.5f, 1f }, new Color[] { Color.FromArgb(40, 10, 60), Color.FromArgb(110, 90, 160), Color.FromArgb(180, 240, 170) })
        };

        public static int[][] Histogram(Bitmap bitmap)
        {
            int[][] hist = new int[3][];
            hist[0] = new int[256];
            hist[1] = new int[256];
            hist[2] = new int[256];

            int w = bitmap.Width;
            int h = bitmap.Height;
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
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
                        if (buf[i + 3] < 8)
                        {
                            continue;
                        }
                        hist[2][buf[i]]++;
                        hist[1][buf[i + 1]]++;
                        hist[0][buf[i + 2]]++;
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return hist;
        }

        public static void AutoLevels(Bitmap bitmap, out int[] black, out int[] white)
        {
            int[][] hist = Histogram(bitmap);
            black = new int[3];
            white = new int[3];
            for (int c = 0; c < 3; c++)
            {
                long total = 0;
                for (int i = 0; i < 256; i++)
                {
                    total += hist[c][i];
                }
                long lowCut = total / 200;
                long highCut = total - total / 200;
                long acc = 0;
                int lo = 0;
                int hi = 255;
                for (int i = 0; i < 256; i++)
                {
                    acc += hist[c][i];
                    if (acc >= lowCut)
                    {
                        lo = i;
                        break;
                    }
                }
                acc = 0;
                for (int i = 0; i < 256; i++)
                {
                    acc += hist[c][i];
                    if (acc >= highCut)
                    {
                        hi = i;
                        break;
                    }
                }
                if (hi - lo < 8)
                {
                    lo = 0;
                    hi = 255;
                }
                black[c] = lo;
                white[c] = hi;
            }
        }

        public static float[] AutoWhiteBalance(Bitmap bitmap)
        {
            int[][] hist = Histogram(bitmap);
            double sumR = 0;
            double sumG = 0;
            double sumB = 0;
            long n = 0;
            for (int i = 0; i < 256; i++)
            {
                sumR += (double)i * hist[0][i];
                sumG += (double)i * hist[1][i];
                sumB += (double)i * hist[2][i];
                n += hist[0][i];
            }
            if (n == 0)
            {
                return new float[] { 1f, 1f, 1f };
            }
            double meanR = sumR / n;
            double meanG = sumG / n;
            double meanB = sumB / n;
            double target = (meanR + meanG + meanB) / 3.0;
            float[] gains = new float[3];
            gains[0] = (float)Clamp(target / Math.Max(1.0, meanR), 0.4, 2.5);
            gains[1] = (float)Clamp(target / Math.Max(1.0, meanG), 0.4, 2.5);
            gains[2] = (float)Clamp(target / Math.Max(1.0, meanB), 0.4, 2.5);
            return gains;
        }

        public static float[] WhiteBalanceFromColor(Color color)
        {
            double mean = (color.R + color.G + color.B) / 3.0;
            float[] gains = new float[3];
            gains[0] = (float)Clamp(mean / Math.Max(1.0, color.R), 0.4, 2.5);
            gains[1] = (float)Clamp(mean / Math.Max(1.0, color.G), 0.4, 2.5);
            gains[2] = (float)Clamp(mean / Math.Max(1.0, color.B), 0.4, 2.5);
            return gains;
        }

        public static byte[] BuildCurveLut(PointF[] points)
        {
            byte[] lut = new byte[256];
            if (points == null || points.Length < 2)
            {
                for (int i = 0; i < 256; i++)
                {
                    lut[i] = (byte)i;
                }
                return lut;
            }

            List<PointF> sorted = new List<PointF>(points);
            sorted.Sort(delegate (PointF a, PointF b) { return a.X.CompareTo(b.X); });
            int n = sorted.Count;
            double[] xs = new double[n];
            double[] ys = new double[n];
            for (int i = 0; i < n; i++)
            {
                xs[i] = sorted[i].X;
                ys[i] = sorted[i].Y;
            }

            double[] h = new double[n - 1];
            double[] d = new double[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                h[i] = Math.Max(1e-6, xs[i + 1] - xs[i]);
                d[i] = (ys[i + 1] - ys[i]) / h[i];
            }

            double[] m = new double[n];
            m[0] = d[0];
            m[n - 1] = d[n - 2];
            for (int i = 1; i < n - 1; i++)
            {
                if (d[i - 1] * d[i] <= 0)
                {
                    m[i] = 0;
                }
                else
                {
                    double w1 = 2 * h[i] + h[i - 1];
                    double w2 = h[i] + 2 * h[i - 1];
                    m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]);
                }
            }

            for (int i = 0; i < 256; i++)
            {
                double x = i / 255.0;
                double value;
                if (x <= xs[0])
                {
                    value = ys[0];
                }
                else if (x >= xs[n - 1])
                {
                    value = ys[n - 1];
                }
                else
                {
                    int seg = 0;
                    for (int k = 0; k < n - 1; k++)
                    {
                        if (x >= xs[k] && x <= xs[k + 1])
                        {
                            seg = k;
                            break;
                        }
                    }
                    double t = (x - xs[seg]) / h[seg];
                    double t2 = t * t;
                    double t3 = t2 * t;
                    double h00 = 2 * t3 - 3 * t2 + 1;
                    double h10 = t3 - 2 * t2 + t;
                    double h01 = -2 * t3 + 3 * t2;
                    double h11 = t3 - t2;
                    value = h00 * ys[seg] + h10 * h[seg] * m[seg] + h01 * ys[seg + 1] + h11 * h[seg] * m[seg + 1];
                }
                lut[i] = (byte)Clamp(Math.Round(value * 255.0), 0, 255);
            }
            return lut;
        }

        private static byte[][] BuildChannelLuts(TuningState s)
        {
            byte[] master = BuildCurveLut(s.CurvePoints[0]);
            byte[][] channelCurve = new byte[3][];
            for (int c = 0; c < 3; c++)
            {
                channelCurve[c] = s.CurvePoints[c + 1] != null ? BuildCurveLut(s.CurvePoints[c + 1]) : null;
            }

            byte[][] lut = new byte[3][];
            for (int c = 0; c < 3; c++)
            {
                lut[c] = new byte[256];
                int black = s.LevelBlack[c];
                int white = s.LevelWhite[c];
                float gamma = s.LevelGamma[c] <= 0.01f ? 1f : s.LevelGamma[c];
                double range = white - black;
                for (int v = 0; v < 256; v++)
                {
                    double t = range <= 0 ? (v >= white ? 1.0 : 0.0) : (v - black) / range;
                    t = Clamp(t, 0.0, 1.0);
                    t = Math.Pow(t, 1.0 / gamma);
                    int idx = (int)Clamp(Math.Round(t * 255.0), 0, 255);
                    int value = master[idx];
                    if (channelCurve[c] != null)
                    {
                        value = channelCurve[c][value];
                    }
                    lut[c][v] = (byte)value;
                }
            }
            return lut;
        }

        public static Bitmap Apply(Bitmap source, TuningState s)
        {
            int w = source.Width;
            int h = source.Height;
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            BitmapData sourceData = source.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData resultData = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = sourceData.Stride;
                byte[] input = new byte[stride * h];
                Marshal.Copy(sourceData.Scan0, input, 0, input.Length);
                byte[] output = new byte[stride * h];

                byte[][] lut = BuildChannelLuts(s);
                float gainR = s.WhiteBalanceGain[0];
                float gainG = s.WhiteBalanceGain[1];
                float gainB = s.WhiteBalanceGain[2];
                bool wbActive = Math.Abs(gainR - 1f) > 0.001f || Math.Abs(gainG - 1f) > 0.001f || Math.Abs(gainB - 1f) > 0.001f;
                bool hslActive = s.Hue != 0f || s.Saturation != 0f || s.Lightness != 0f;
                bool toneActive = s.ToneMode > 0;
                bool lutActive = s.LutEnabled && s.LutData != null && s.LutSize > 1;
                float lutStrength = Clamp(s.LutStrength, 0f, 1f);

                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int i = row + x * 4;
                        float b = input[i];
                        float g = input[i + 1];
                        float r = input[i + 2];

                        if (wbActive)
                        {
                            r *= gainR;
                            g *= gainG;
                            b *= gainB;
                        }

                        r = lut[0][(int)Clamp(Math.Round(r), 0, 255)];
                        g = lut[1][(int)Clamp(Math.Round(g), 0, 255)];
                        b = lut[2][(int)Clamp(Math.Round(b), 0, 255)];

                        if (hslActive)
                        {
                            double hh;
                            double ss;
                            double ll;
                            RgbToHsl(r, g, b, out hh, out ss, out ll);
                            hh += s.Hue / 360.0;
                            if (hh < 0) hh += 1;
                            if (hh >= 1) hh -= 1;
                            ss = Clamp(ss * (1.0 + s.Saturation / 100.0), 0.0, 1.0);
                            double lf = s.Lightness / 100.0;
                            if (lf >= 0)
                            {
                                ll = ll + (1.0 - ll) * lf;
                            }
                            else
                            {
                                ll = ll * (1.0 + lf);
                            }
                            double nr;
                            double ng;
                            double nb;
                            HslToRgb(hh, ss, ll, out nr, out ng, out nb);
                            r = (float)(nr * 255.0);
                            g = (float)(ng * 255.0);
                            b = (float)(nb * 255.0);
                        }

                        if (toneActive)
                        {
                            if (s.ToneMode == 1)
                            {
                                int levels = Math.Max(2, s.PosterizeLevels);
                                r = (float)(Math.Round(r / 255.0 * (levels - 1)) / (levels - 1) * 255.0);
                                g = (float)(Math.Round(g / 255.0 * (levels - 1)) / (levels - 1) * 255.0);
                                b = (float)(Math.Round(b / 255.0 * (levels - 1)) / (levels - 1) * 255.0);
                            }
                            else
                            {
                                double lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
                                Color mapped;
                                if (s.ToneMode == 2)
                                {
                                    mapped = LerpColor(s.DuotoneA, s.DuotoneB, lum);
                                }
                                else
                                {
                                    mapped = SampleGradient(s.GradientPreset, lum);
                                }
                                r = mapped.R;
                                g = mapped.G;
                                b = mapped.B;
                            }
                        }

                        if (lutActive)
                        {
                            float lr;
                            float lg;
                            float lb;
                            SampleLut3D(s, r / 255f, g / 255f, b / 255f, out lr, out lg, out lb);
                            r = r + (lr * 255f - r) * lutStrength;
                            g = g + (lg * 255f - g) * lutStrength;
                            b = b + (lb * 255f - b) * lutStrength;
                        }

                        output[i] = (byte)Clamp(Math.Round(b), 0, 255);
                        output[i + 1] = (byte)Clamp(Math.Round(g), 0, 255);
                        output[i + 2] = (byte)Clamp(Math.Round(r), 0, 255);
                        output[i + 3] = input[i + 3];
                    }
                }

                if (s.LocalEnabled)
                {
                    ApplyLocal(output, w, h, stride, s);
                }

                Marshal.Copy(output, 0, resultData.Scan0, output.Length);
            }
            finally
            {
                source.UnlockBits(sourceData);
                result.UnlockBits(resultData);
            }
            return result;
        }

        public static Color SampleGradient(int preset, double t)
        {
            if (preset < 0 || preset >= Gradients.Length)
            {
                preset = 0;
            }
            GradientPreset g = Gradients[preset];
            t = Clamp(t, 0.0, 1.0);
            int stops = g.Stops.Length;
            if (t <= g.Stops[0])
            {
                return g.Colors[0];
            }
            if (t >= g.Stops[stops - 1])
            {
                return g.Colors[stops - 1];
            }
            for (int i = 0; i < stops - 1; i++)
            {
                if (t >= g.Stops[i] && t <= g.Stops[i + 1])
                {
                    double span = g.Stops[i + 1] - g.Stops[i];
                    double local = span <= 0 ? 0 : (t - g.Stops[i]) / span;
                    return LerpColor(g.Colors[i], g.Colors[i + 1], local);
                }
            }
            return g.Colors[stops - 1];
        }

        private static void ApplyLocal(byte[] buffer, int w, int h, int stride, TuningState s)
        {
            float exposure = 1f + s.LocalExposure / 100f;
            float contrast = 1f + s.LocalContrast / 100f;
            float saturation = 1f + s.LocalSaturation / 100f;

            for (int y = 0; y < h; y++)
            {
                float ny = h <= 1 ? 0f : (float)y / (h - 1);
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    float nx = w <= 1 ? 0f : (float)x / (w - 1);
                    float mask = LocalMask(s, nx, ny);
                    if (mask <= 0.001f)
                    {
                        continue;
                    }
                    int i = row + x * 4;
                    float b = buffer[i];
                    float g = buffer[i + 1];
                    float r = buffer[i + 2];

                    float nr = r * exposure;
                    float ng = g * exposure;
                    float nb = b * exposure;

                    nr = 128f + (nr - 128f) * contrast;
                    ng = 128f + (ng - 128f) * contrast;
                    nb = 128f + (nb - 128f) * contrast;

                    float gray = 0.299f * nr + 0.587f * ng + 0.114f * nb;
                    nr = gray + (nr - gray) * saturation;
                    ng = gray + (ng - gray) * saturation;
                    nb = gray + (nb - gray) * saturation;

                    r = r + (nr - r) * mask;
                    g = g + (ng - g) * mask;
                    b = b + (nb - b) * mask;

                    buffer[i] = (byte)Clamp(Math.Round(b), 0, 255);
                    buffer[i + 1] = (byte)Clamp(Math.Round(g), 0, 255);
                    buffer[i + 2] = (byte)Clamp(Math.Round(r), 0, 255);
                }
            }
        }

        private static float LocalMask(TuningState s, float nx, float ny)
        {
            if (!s.LocalLinear)
            {
                float dx = (nx - s.LocalCenter.X) / Math.Max(0.001f, s.LocalRadiusX);
                float dy = (ny - s.LocalCenter.Y) / Math.Max(0.001f, s.LocalRadiusY);
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                float inner = 1f - Clamp(s.LocalFeather, 0f, 0.99f);
                if (d <= inner)
                {
                    return 1f;
                }
                if (d >= 1f)
                {
                    return 0f;
                }
                float t = (d - inner) / (1f - inner);
                return 1f - Smoothstep(t);
            }
            else
            {
                float angle = s.LocalAngle * (float)Math.PI / 180f;
                float dx = nx - s.LocalCenter.X;
                float dy = ny - s.LocalCenter.Y;
                float proj = dx * (float)Math.Cos(angle) + dy * (float)Math.Sin(angle);
                float half = Math.Max(0.01f, s.LocalFeather);
                float t = (proj + half) / (2f * half);
                if (t <= 0f)
                {
                    return 0f;
                }
                if (t >= 1f)
                {
                    return 1f;
                }
                return Smoothstep(t);
            }
        }

        private static float Smoothstep(float t)
        {
            t = Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        private static Color LerpColor(Color a, Color b, double t)
        {
            t = Clamp(t, 0.0, 1.0);
            int r = (int)Math.Round(a.R + (b.R - a.R) * t);
            int g = (int)Math.Round(a.G + (b.G - a.G) * t);
            int bl = (int)Math.Round(a.B + (b.B - a.B) * t);
            return Color.FromArgb(r, g, bl);
        }

        private static void RgbToHsl(float r, float g, float b, out double h, out double s, out double l)
        {
            double rd = r / 255.0;
            double gd = g / 255.0;
            double bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            l = (max + min) / 2.0;
            double delta = max - min;
            if (delta < 1e-6)
            {
                h = 0;
                s = 0;
                return;
            }
            s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);
            if (max == rd)
            {
                h = (gd - bd) / delta + (gd < bd ? 6.0 : 0.0);
            }
            else if (max == gd)
            {
                h = (bd - rd) / delta + 2.0;
            }
            else
            {
                h = (rd - gd) / delta + 4.0;
            }
            h /= 6.0;
            if (h < 0)
            {
                h += 1.0;
            }
        }

        private static void HslToRgb(double h, double s, double l, out double r, out double g, out double b)
        {
            if (s < 1e-6)
            {
                r = l;
                g = l;
                b = l;
                return;
            }
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3.0);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3.0);
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        public static bool TryLoadCube(string path, out int size, out float[] data)
        {
            size = 0;
            data = null;
            try
            {
                string[] lines = File.ReadAllLines(path);
                List<float> values = new List<float>();
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                    {
                        continue;
                    }
                    if (line.StartsWith("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            int.TryParse(parts[1], out size);
                        }
                        continue;
                    }
                    if (line.StartsWith("DOMAIN_", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("LUT_1D", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string[] tokens = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length == 3)
                    {
                        float rv;
                        float gv;
                        float bv;
                        if (float.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out rv) &&
                            float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out gv) &&
                            float.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out bv))
                        {
                            values.Add(rv);
                            values.Add(gv);
                            values.Add(bv);
                        }
                    }
                }
                if (size < 2 || values.Count < size * size * size * 3)
                {
                    size = 0;
                    data = null;
                    return false;
                }
                data = values.ToArray();
                return true;
            }
            catch (Exception)
            {
                size = 0;
                data = null;
                return false;
            }
        }

        private static void SampleLut3D(TuningState s, float r, float g, float b, out float outR, out float outG, out float outB)
        {
            int size = s.LutSize;
            float x = Clamp(r, 0f, 1f) * (size - 1);
            float y = Clamp(g, 0f, 1f) * (size - 1);
            float z = Clamp(b, 0f, 1f) * (size - 1);
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            int z0 = (int)Math.Floor(z);
            int x1 = Math.Min(x0 + 1, size - 1);
            int y1 = Math.Min(y0 + 1, size - 1);
            int z1 = Math.Min(z0 + 1, size - 1);
            float fx = x - x0;
            float fy = y - y0;
            float fz = z - z0;

            outR = 0;
            outG = 0;
            outB = 0;
            for (int corner = 0; corner < 8; corner++)
            {
                int cx = (corner & 1) == 0 ? x0 : x1;
                int cy = (corner & 2) == 0 ? y0 : y1;
                int cz = (corner & 4) == 0 ? z0 : z1;
                float weight = ((corner & 1) == 0 ? 1 - fx : fx) * ((corner & 2) == 0 ? 1 - fy : fy) * ((corner & 4) == 0 ? 1 - fz : fz);
                int index = ((cz * size + cy) * size + cx) * 3;
                outR += weight * s.LutData[index];
                outG += weight * s.LutData[index + 1];
                outB += weight * s.LutData[index + 2];
            }
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
