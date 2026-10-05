using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ImageToolbox
{
    // 任意形状的选区遮罩：以每像素 0..255 的覆盖度保存在图层像素坐标系里。
    // 支持矩形/椭圆/多边形/画笔形状的生成，以及新建/加选/减选/交集四种运算。
    public class ImageSelection
    {
        public readonly int Width;
        public readonly int Height;
        private byte[] _mask;

        public ImageSelection(int width, int height)
        {
            if (width < 1) { width = 1; }
            if (height < 1) { height = 1; }
            Width = width;
            Height = height;
            _mask = new byte[width * height];
        }

        public byte[] Mask { get { return _mask; } }

        public void Clear()
        {
            Array.Clear(_mask, 0, _mask.Length);
        }

        public void SelectAll()
        {
            for (int i = 0; i < _mask.Length; i++) { _mask[i] = 255; }
        }

        public void Invert()
        {
            for (int i = 0; i < _mask.Length; i++) { _mask[i] = (byte)(255 - _mask[i]); }
        }

        // 由位图的 alpha 通道生成选区遮罩：不透明处=255、透明处=0，半透明按 alpha 取值。
        // 用于「选择不透明」——选中当前图层已有像素的轮廓。
        public static byte[] FromAlpha(Bitmap image)
        {
            if (image == null) { return null; }
            int w = image.Width, h = image.Height;
            int stride;
            byte[] px = ImageFilters.CopyPixels(image, out stride);
            byte[] mask = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                int mrow = y * w;
                for (int x = 0; x < w; x++)
                {
                    mask[mrow + x] = px[row + x * 4 + 3];
                }
            }
            return mask;
        }

        // mode: 0 新建（覆盖）, 1 加选, 2 减选, 3 交集。shape 为整图大小的 0/255 覆盖。
        public void Combine(byte[] shape, int mode)
        {
            if (shape == null || shape.Length != _mask.Length) { return; }
            if (mode == 0)
            {
                Array.Copy(shape, _mask, _mask.Length);
                return;
            }
            if (mode == 1)
            {
                for (int i = 0; i < _mask.Length; i++) { if (shape[i] > _mask[i]) { _mask[i] = shape[i]; } }
            }
            else if (mode == 2)
            {
                for (int i = 0; i < _mask.Length; i++) { if (shape[i] > 0) { _mask[i] = 0; } }
            }
            else
            {
                for (int i = 0; i < _mask.Length; i++) { if (shape[i] == 0) { _mask[i] = 0; } }
            }
        }

        // 画笔刷子：以 (cx,cy) 为圆心、radius 为半径画实心圆并合并到遮罩。
        // mode: 1 加选, 2 减选, 3 交集。
        public void Stamp(int cx, int cy, int radius, int mode)
        {
            if (radius < 1) { radius = 1; }
            long r2 = (long)radius * radius;
            int x0 = Math.Max(0, cx - radius), x1 = Math.Min(Width - 1, cx + radius);
            int y0 = Math.Max(0, cy - radius), y1 = Math.Min(Height - 1, cy + radius);
            for (int y = y0; y <= y1; y++)
            {
                int dy = y - cy;
                int row = y * Width;
                for (int x = x0; x <= x1; x++)
                {
                    int dx = x - cx;
                    if ((long)dx * dx + (long)dy * dy > r2) { continue; }
                    int i = row + x;
                    if (mode == 2) { _mask[i] = 0; }
                    else if (mode == 3) { _mask[i] = (_mask[i] >= 128) ? (byte)255 : (byte)0; }
                    else { _mask[i] = 255; }
                }
            }
        }

        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < _mask.Length; i++) { if (_mask[i] > 0) { return false; } }
                return true;
            }
        }

        public Rectangle Bounds()
        {
            int minX = Width, minY = Height, maxX = -1, maxY = -1;
            for (int y = 0; y < Height; y++)
            {
                int row = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    if (_mask[row + x] > 0)
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

        // 返回羽化后的覆盖度数组；feather<=0 时直接返回内部遮罩（调用方只读）。
        public byte[] EffectiveMask(int feather)
        {
            if (feather <= 0) { return _mask; }
            int w = Width, h = Height;
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            try
            {
                BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = d.Stride;
                    byte[] buf = new byte[stride * h];
                    for (int y = 0; y < h; y++)
                    {
                        int row = y * stride;
                        int mrow = y * w;
                        for (int x = 0; x < w; x++)
                        {
                            int i = row + x * 4;
                            byte m = _mask[mrow + x];
                            buf[i] = 255;
                            buf[i + 1] = 255;
                            buf[i + 2] = 255;
                            buf[i + 3] = m;
                        }
                    }
                    Marshal.Copy(buf, 0, d.Scan0, buf.Length);
                }
                finally
                {
                    bmp.UnlockBits(d);
                }

                ImageFilters.GaussianBlur(bmp, feather);

                byte[] outMask = new byte[w * h];
                d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = d.Stride;
                    byte[] buf = new byte[stride * h];
                    Marshal.Copy(d.Scan0, buf, 0, buf.Length);
                    for (int y = 0; y < h; y++)
                    {
                        int row = y * stride;
                        int mrow = y * w;
                        for (int x = 0; x < w; x++)
                        {
                            outMask[mrow + x] = buf[row + x * 4 + 3];
                        }
                    }
                }
                finally
                {
                    bmp.UnlockBits(d);
                }
                return outMask;
            }
            finally
            {
                bmp.Dispose();
            }
        }

        // ---- 形状生成（均返回整图大小的 0/255 覆盖）----

        public static byte[] ShapeRect(int w, int h, Rectangle r)
        {
            byte[] m = new byte[w * h];
            int x0 = Math.Max(0, r.Left), y0 = Math.Max(0, r.Top);
            int x1 = Math.Min(w, r.Right), y1 = Math.Min(h, r.Bottom);
            for (int y = y0; y < y1; y++)
            {
                int row = y * w;
                for (int x = x0; x < x1; x++) { m[row + x] = 255; }
            }
            return m;
        }

        public static byte[] ShapeEllipse(int w, int h, Rectangle r)
        {
            byte[] m = new byte[w * h];
            double rx = r.Width / 2.0, ry = r.Height / 2.0;
            if (rx < 0.5) { rx = 0.5; }
            if (ry < 0.5) { ry = 0.5; }
            double cx = r.Left + rx, cy = r.Top + ry;
            int x0 = Math.Max(0, r.Left), x1 = Math.Min(w, r.Right);
            int y0 = Math.Max(0, r.Top), y1 = Math.Min(h, r.Bottom);
            for (int y = y0; y < y1; y++)
            {
                double ny = (y + 0.5 - cy) / ry;
                double ny2 = ny * ny;
                int row = y * w;
                for (int x = x0; x < x1; x++)
                {
                    double nx = (x + 0.5 - cx) / rx;
                    if (nx * nx + ny2 <= 1.0) { m[row + x] = 255; }
                }
            }
            return m;
        }

        public static byte[] ShapePolygon(int w, int h, List<Point> pts)
        {
            byte[] m = new byte[w * h];
            if (pts == null || pts.Count < 3) { return m; }
            int n = pts.Count;
            int minY = int.MaxValue, maxY = int.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (pts[i].Y < minY) { minY = pts[i].Y; }
                if (pts[i].Y > maxY) { maxY = pts[i].Y; }
            }
            if (minY < 0) { minY = 0; }
            if (maxY > h) { maxY = h; }
            double[] xs = new double[n];
            for (int y = minY; y < maxY; y++)
            {
                double yc = y + 0.5;
                int cnt = 0;
                for (int i = 0; i < n; i++)
                {
                    Point a = pts[i];
                    Point b = pts[(i + 1) % n];
                    double ay = a.Y, by = b.Y;
                    if (ay == by) { continue; }
                    if ((yc >= ay && yc < by) || (yc >= by && yc < ay))
                    {
                        double t = (yc - ay) / (by - ay);
                        xs[cnt] = a.X + (b.X - a.X) * t;
                        cnt++;
                    }
                }
                Array.Sort(xs, 0, cnt);
                int row = y * w;
                for (int k = 0; k + 1 < cnt; k += 2)
                {
                    // 以像素中心为准：像素 x 覆盖 [x,x+1)，中心 x+0.5。
                    int sx0 = (int)Math.Ceiling(xs[k] - 0.5);
                    int sx1 = (int)Math.Floor(xs[k + 1] - 0.5);
                    if (sx0 < 0) { sx0 = 0; }
                    if (sx1 >= w) { sx1 = w - 1; }
                    for (int x = sx0; x <= sx1; x++) { m[row + x] = 255; }
                }
            }
            return m;
        }

        // ---- 蚂蚁线轮廓：把遮罩降采样到 maxDim 长边后提取并合并边界线段，返回扁平的
        // (x1,y1,x2,y2) 数组（图层坐标）。降采样保证重建开销与图像尺寸无关。
        public int[] BuildOutline(int maxDim)
        {
            int W = Width, H = Height;
            float f = Math.Min(1f, (float)maxDim / Math.Max(W, H));
            if (f <= 0f) { f = 1f; }
            int sw = Math.Max(1, (int)Math.Floor(W * f));
            int sh = Math.Max(1, (int)Math.Floor(H * f));
            byte[] sm = new byte[sw * sh];
            for (int sy = 0; sy < sh; sy++)
            {
                int y = (int)((sy + 0.5f) * H / sh);
                if (y >= H) { y = H - 1; }
                int row = y * W;
                int srow = sy * sw;
                for (int sx = 0; sx < sw; sx++)
                {
                    int x = (int)((sx + 0.5f) * W / sw);
                    if (x >= W) { x = W - 1; }
                    sm[srow + sx] = _mask[row + x];
                }
            }

            List<int> seg = new List<int>();
            for (int y = 0; y <= sh; y++)
            {
                int run = -1;
                for (int x = 0; x < sw; x++)
                {
                    bool a = InsideSmall(sm, sw, sh, x, y - 1);
                    bool b = InsideSmall(sm, sw, sh, x, y);
                    if (a != b)
                    {
                        if (run < 0) { run = x; }
                    }
                    else if (run >= 0)
                    {
                        AddSeg(seg, run, y, x, y, sw, sh, W, H);
                        run = -1;
                    }
                }
                if (run >= 0) { AddSeg(seg, run, y, sw, y, sw, sh, W, H); }
            }
            for (int x = 0; x <= sw; x++)
            {
                int run = -1;
                for (int y = 0; y < sh; y++)
                {
                    bool a = InsideSmall(sm, sw, sh, x - 1, y);
                    bool b = InsideSmall(sm, sw, sh, x, y);
                    if (a != b)
                    {
                        if (run < 0) { run = y; }
                    }
                    else if (run >= 0)
                    {
                        AddSeg(seg, x, run, x, y, sw, sh, W, H);
                        run = -1;
                    }
                }
                if (run >= 0) { AddSeg(seg, x, run, x, sh, sw, sh, W, H); }
            }
            return seg.ToArray();
        }

        private static bool InsideSmall(byte[] sm, int sw, int sh, int x, int y)
        {
            if (x < 0 || y < 0 || x >= sw || y >= sh) { return false; }
            return sm[y * sw + x] >= 128;
        }

        private static void AddSeg(List<int> seg, int xs0, int ys0, int xs1, int ys1, int sw, int sh, int W, int H)
        {
            int mx0 = (int)Math.Round((float)xs0 * W / sw);
            int my0 = (int)Math.Round((float)ys0 * H / sh);
            int mx1 = (int)Math.Round((float)xs1 * W / sw);
            int my1 = (int)Math.Round((float)ys1 * H / sh);
            seg.Add(mx0);
            seg.Add(my0);
            seg.Add(mx1);
            seg.Add(my1);
        }
    }
}
