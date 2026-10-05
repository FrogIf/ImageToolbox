using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ImageToolbox
{
    public static class ImageUtil
    {
        public static Bitmap LoadImage(string path)
        {
            BitmapDecoder decoder = BitmapDecoder.Create(
                new Uri(Path.GetFullPath(path), UriKind.Absolute),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            return ToBitmap(decoder.Frames[0]);
        }

        public static Bitmap CreatePreview(Bitmap source, int maxSize)
        {
            if (source.Width <= maxSize && source.Height <= maxSize)
            {
                return null;
            }

            float scale = Math.Min((float)maxSize / source.Width, (float)maxSize / source.Height);
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));

            Bitmap small = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(small))
            {
                // 预览缩略图：用 HighQualityBicubic 做大比例缩小时比 Bilinear 明显更清晰
                // （Bilinear 在大倍数缩小时会发虚/走样），代价是生成缩放图稍慢，但结果被缓存。
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // 平铺翻转采样：缩放时不让插值核采到源图外的“透明”像素，否则缩略图最外圈
                // 会带一圈半透明边，画布上看起来就像图片多了一条白边。
                using (ImageAttributes wrap = new ImageAttributes())
                {
                    wrap.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(
                        source,
                        new Rectangle(0, 0, width, height),
                        0,
                        0,
                        source.Width,
                        source.Height,
                        GraphicsUnit.Pixel,
                        wrap);
                }
            }
            return small;
        }

        public static Bitmap LoadThumbnail(string sourcePath, int maxSize)
        {
            Uri uri = new Uri(Path.GetFullPath(sourcePath), UriKind.Absolute);

            BitmapDecoder info = BitmapDecoder.Create(uri, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            BitmapFrame infoFrame = info.Frames[0];
            int pixelWidth = infoFrame.PixelWidth;
            int pixelHeight = infoFrame.PixelHeight;

            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (pixelWidth > maxSize || pixelHeight > maxSize)
            {
                if (pixelWidth >= pixelHeight)
                {
                    image.DecodePixelWidth = maxSize;
                }
                else
                {
                    image.DecodePixelHeight = maxSize;
                }
            }
            image.EndInit();

            return ToBitmap(image);
        }

        public static Rectangle MapRegion(Rectangle region, Size fromSize, Size toSize)
        {
            if (fromSize.Width <= 0 || fromSize.Height <= 0)
            {
                return Rectangle.Empty;
            }

            int x = (int)Math.Round(region.X * (double)toSize.Width / fromSize.Width);
            int y = (int)Math.Round(region.Y * (double)toSize.Height / fromSize.Height);
            int w = (int)Math.Round(region.Width * (double)toSize.Width / fromSize.Width);
            int h = (int)Math.Round(region.Height * (double)toSize.Height / fromSize.Height);
            return new Rectangle(x, y, w, h);
        }

        public static Bitmap Compose(Bitmap target, Bitmap overlay, Rectangle region)
        {
            Bitmap result = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.DrawImage(
                    target,
                    new Rectangle(0, 0, target.Width, target.Height),
                    new Rectangle(0, 0, target.Width, target.Height),
                    GraphicsUnit.Pixel);

                Rectangle source = MapRegion(
                    region,
                    new Size(target.Width, target.Height),
                    new Size(overlay.Width, overlay.Height));
                source = Rectangle.Intersect(source, new Rectangle(0, 0, overlay.Width, overlay.Height));

                Rectangle destination = MapRegion(
                    source,
                    new Size(overlay.Width, overlay.Height),
                    new Size(target.Width, target.Height));

                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(overlay, destination, source, GraphicsUnit.Pixel);
            }
            return result;
        }

        public static Bitmap ApplyColorMatrix(Bitmap source, ColorMatrix matrix)
        {
            Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    g.DrawImage(
                        source,
                        new Rectangle(0, 0, source.Width, source.Height),
                        0,
                        0,
                        source.Width,
                        source.Height,
                        GraphicsUnit.Pixel,
                        attributes);
                }
            }
            return result;
        }

        public static void SavePng(Bitmap bitmap, string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            bitmap.Save(path, ImageFormat.Png);
        }

        private static Bitmap ToBitmap(BitmapSource source)
        {
            if (source.Format != PixelFormats.Bgra32)
            {
                FormatConvertedBitmap converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = source;
                converted.DestinationFormat = PixelFormats.Bgra32;
                converted.EndInit();
                source = converted;
            }

            int width = source.PixelWidth;
            int height = source.PixelHeight;
            int sourceStride = width * 4;
            byte[] pixels = new byte[sourceStride * height];
            source.CopyPixels(pixels, sourceStride, 0);

            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                if (data.Stride == sourceStride)
                {
                    // 32bpp 位图 stride 恒为 width*4：整块一次拷贝，避免逐行 P/Invoke（大图加载明显更快）。
                    Marshal.Copy(pixels, 0, data.Scan0, sourceStride * height);
                }
                else
                {
                    for (int y = 0; y < height; y++)
                    {
                        IntPtr destination;
                        if (data.Stride >= 0)
                        {
                            destination = (IntPtr)(data.Scan0.ToInt64() + (long)y * data.Stride);
                        }
                        else
                        {
                            destination = (IntPtr)(data.Scan0.ToInt64() + (long)(height - 1 - y) * (-data.Stride));
                        }
                        Marshal.Copy(pixels, y * sourceStride, destination, sourceStride);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }
    }
}
