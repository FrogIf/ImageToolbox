using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Windows.Media.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ImageToolbox
{
    public enum BatchOutputFormat
    {
        Keep = 0,
        Png,
        Jpeg,
        Bmp,
        Gif,
        Tiff
    }

    public enum BatchResizeMode
    {
        None = 0,
        LongEdge,
        Percent,
        WidthHeight
    }

    public enum WatermarkPosition
    {
        TopLeft = 0,
        TopCenter,
        TopRight,
        MiddleLeft,
        Center,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight
    }

    public class BatchOptions
    {
        public string OutputDir = "";
        public bool Overwrite;
        public BatchOutputFormat Format = BatchOutputFormat.Keep;
        public int JpegQuality = 90;
        public Color MatteColor = Color.White;

        public BatchResizeMode ResizeMode = BatchResizeMode.None;
        public int LongEdge = 1920;
        public int Percent = 100;
        public int TargetWidth = 800;
        public int TargetHeight = 600;
        public bool KeepAspect = true;
        public bool AllowUpscale = true;

        public bool AutoOrient = true;
        public int Rotation;
        public bool FlipHorizontal;
        public bool FlipVertical;

        public string NamePattern = "{name}";
        public int StartIndex = 1;
        public int IndexDigits = 3;
        public string FindText = "";
        public string ReplaceText = "";

        public bool WatermarkEnabled;
        public bool WatermarkIsImage;
        public string WatermarkText = "水印";
        public string WatermarkFont = "Microsoft YaHei UI";
        public float WatermarkFontSize = 36f;
        public Color WatermarkColor = Color.White;
        public string WatermarkImagePath = "";
        public int WatermarkScale = 20;
        public float WatermarkAngle = 0f;
        public WatermarkPosition WatermarkPosition = WatermarkPosition.BottomRight;
        public float WatermarkOpacity = 0.5f;
        public bool WatermarkTile;
        public int WatermarkMargin = 16;
    }

    public static class ImageBatch
    {
        private static readonly string[] SupportedExtensions =
        {
            ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".tif", ".tiff", ".webp"
        };

        public static bool IsSupported(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
            {
                return false;
            }
            ext = ext.ToLowerInvariant();
            for (int i = 0; i < SupportedExtensions.Length; i++)
            {
                if (SupportedExtensions[i] == ext)
                {
                    return true;
                }
            }
            return false;
        }

        public static BatchOutputFormat ResolveFormat(string sourcePath, BatchOutputFormat requested)
        {
            if (requested != BatchOutputFormat.Keep)
            {
                return requested;
            }

            switch (Path.GetExtension(sourcePath).ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                case ".jfif":
                    return BatchOutputFormat.Jpeg;
                case ".bmp":
                    return BatchOutputFormat.Bmp;
                case ".gif":
                    return BatchOutputFormat.Gif;
                case ".tif":
                case ".tiff":
                    return BatchOutputFormat.Tiff;
                case ".png":
                    return BatchOutputFormat.Png;
                default:
                    return BatchOutputFormat.Png;
            }
        }

        public static string FormatExtension(BatchOutputFormat format)
        {
            switch (format)
            {
                case BatchOutputFormat.Jpeg:
                    return ".jpg";
                case BatchOutputFormat.Bmp:
                    return ".bmp";
                case BatchOutputFormat.Gif:
                    return ".gif";
                case BatchOutputFormat.Tiff:
                    return ".tif";
                default:
                    return ".png";
            }
        }

        public static string BuildName(string sourcePath, BatchOptions options, int sequence)
        {
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            if (!string.IsNullOrEmpty(options.FindText))
            {
                baseName = baseName.Replace(options.FindText, options.ReplaceText == null ? "" : options.ReplaceText);
            }

            string pattern = options.NamePattern;
            if (string.IsNullOrEmpty(pattern))
            {
                pattern = "{name}";
            }

            string sequenceText = sequence.ToString();
            string padded = sequence.ToString().PadLeft(Math.Max(1, options.IndexDigits), '0');
            string result = pattern
                .Replace("{name}", baseName)
                .Replace("{nnn}", padded)
                .Replace("{n}", sequenceText)
                .Replace("{date}", DateTime.Now.ToString("yyyyMMdd"))
                .Replace("{time}", DateTime.Now.ToString("HHmmss"));

            if (result.Trim().Length == 0)
            {
                result = baseName;
            }
            return result;
        }

        public static string ResolveTargetPath(string sourcePath, BatchOptions options, int sequence)
        {
            string dir = options.OutputDir;
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            }

            Directory.CreateDirectory(dir);
            string name = BuildName(sourcePath, options, sequence);
            string ext = FormatExtension(ResolveFormat(sourcePath, options.Format));
            string dst = Path.Combine(dir, name + ext);

            if (!options.Overwrite)
            {
                int n = 1;
                while (File.Exists(dst))
                {
                    dst = Path.Combine(dir, name + "_" + n + ext);
                    n++;
                }
            }
            return dst;
        }

        public static string Run(string sourcePath, BatchOptions options, int sequence, Bitmap watermarkImage)
        {
            Bitmap work = ImageUtil.LoadImage(sourcePath);
            try
            {
                if (options.AutoOrient)
                {
                    ApplyOrientation(work, GetExifOrientation(sourcePath));
                }
                ApplyRotationFlip(work, options);

                Bitmap resized = Resize(work, options);
                if (resized != work)
                {
                    work.Dispose();
                    work = resized;
                }

                if (options.WatermarkEnabled)
                {
                    DrawWatermark(work, options, watermarkImage);
                }

                string dst = ResolveTargetPath(sourcePath, options, sequence);
                Save(work, dst, ResolveFormat(sourcePath, options.Format), options.JpegQuality, options.MatteColor);
                return dst;
            }
            finally
            {
                work.Dispose();
            }
        }

        public static Bitmap Resize(Bitmap src, BatchOptions options)
        {
            int targetWidth = src.Width;
            int targetHeight = src.Height;

            switch (options.ResizeMode)
            {
                case BatchResizeMode.LongEdge:
                    if (options.LongEdge <= 0)
                    {
                        return src;
                    }
                    if (!options.AllowUpscale && src.Width <= options.LongEdge && src.Height <= options.LongEdge)
                    {
                        return src;
                    }
                    double edgeScale = (double)options.LongEdge / Math.Max(src.Width, src.Height);
                    targetWidth = Math.Max(1, (int)Math.Round(src.Width * edgeScale));
                    targetHeight = Math.Max(1, (int)Math.Round(src.Height * edgeScale));
                    break;

                case BatchResizeMode.Percent:
                    if (options.Percent <= 0)
                    {
                        return src;
                    }
                    targetWidth = Math.Max(1, (int)Math.Round(src.Width * options.Percent / 100.0));
                    targetHeight = Math.Max(1, (int)Math.Round(src.Height * options.Percent / 100.0));
                    break;

                case BatchResizeMode.WidthHeight:
                    targetWidth = options.TargetWidth;
                    targetHeight = options.TargetHeight;
                    if (options.KeepAspect)
                    {
                        if (targetWidth <= 0 && targetHeight <= 0)
                        {
                            return src;
                        }
                        if (targetWidth > 0 && targetHeight > 0)
                        {
                            double fit = Math.Min((double)targetWidth / src.Width, (double)targetHeight / src.Height);
                            targetWidth = Math.Max(1, (int)Math.Round(src.Width * fit));
                            targetHeight = Math.Max(1, (int)Math.Round(src.Height * fit));
                        }
                        else if (targetWidth > 0)
                        {
                            double fit = (double)targetWidth / src.Width;
                            targetHeight = Math.Max(1, (int)Math.Round(src.Height * fit));
                        }
                        else
                        {
                            double fit = (double)targetHeight / src.Height;
                            targetWidth = Math.Max(1, (int)Math.Round(src.Width * fit));
                        }
                    }
                    if (targetWidth <= 0 || targetHeight <= 0)
                    {
                        return src;
                    }
                    break;

                default:
                    return src;
            }

            if (targetWidth == src.Width && targetHeight == src.Height)
            {
                return src;
            }

            Bitmap dst = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(
                    src,
                    new Rectangle(0, 0, targetWidth, targetHeight),
                    new Rectangle(0, 0, src.Width, src.Height),
                    GraphicsUnit.Pixel);
            }
            return dst;
        }

        private static void ApplyRotationFlip(Bitmap bmp, BatchOptions options)
        {
            if (options.Rotation == 90)
            {
                bmp.RotateFlip(RotateFlipType.Rotate90FlipNone);
            }
            else if (options.Rotation == 180)
            {
                bmp.RotateFlip(RotateFlipType.Rotate180FlipNone);
            }
            else if (options.Rotation == 270)
            {
                bmp.RotateFlip(RotateFlipType.Rotate270FlipNone);
            }

            if (options.FlipHorizontal)
            {
                bmp.RotateFlip(RotateFlipType.RotateNoneFlipX);
            }
            if (options.FlipVertical)
            {
                bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
            }
        }

        private static void ApplyOrientation(Bitmap bmp, int orientation)
        {
            switch (orientation)
            {
                case 2:
                    bmp.RotateFlip(RotateFlipType.RotateNoneFlipX);
                    break;
                case 3:
                    bmp.RotateFlip(RotateFlipType.Rotate180FlipNone);
                    break;
                case 4:
                    bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
                    break;
                case 5:
                    bmp.RotateFlip(RotateFlipType.Rotate90FlipX);
                    break;
                case 6:
                    bmp.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    break;
                case 7:
                    bmp.RotateFlip(RotateFlipType.Rotate270FlipX);
                    break;
                case 8:
                    bmp.RotateFlip(RotateFlipType.Rotate270FlipNone);
                    break;
            }
        }

        public static int GetExifOrientation(string path)
        {
            try
            {
                BitmapDecoder decoder = BitmapDecoder.Create(
                    new Uri(Path.GetFullPath(path), UriKind.Absolute),
                    BitmapCreateOptions.DelayCreation,
                    BitmapCacheOption.None);
                BitmapFrame frame = decoder.Frames[0];
                BitmapMetadata metadata = frame.Metadata as BitmapMetadata;
                if (metadata != null)
                {
                    object value = metadata.GetQuery("/app1/ifd/{ushort=274}");
                    if (value != null)
                    {
                        return Convert.ToInt32(value);
                    }
                }
            }
            catch (Exception)
            {
            }
            return 1;
        }

        public static void DrawWatermark(Bitmap bmp, BatchOptions options, Bitmap watermarkImage)
        {
            if (options.WatermarkOpacity <= 0f)
            {
                return;
            }

            Bitmap layer = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(layer))
                {
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;

                    if (options.WatermarkIsImage)
                    {
                        if (watermarkImage != null)
                        {
                            DrawImageMark(g, layer.Size, options, watermarkImage);
                        }
                    }
                    else
                    {
                        DrawTextMark(g, layer.Size, options);
                    }
                }

                using (Graphics g = Graphics.FromImage(bmp))
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    ColorMatrix matrix = new ColorMatrix();
                    matrix.Matrix00 = 1f;
                    matrix.Matrix11 = 1f;
                    matrix.Matrix22 = 1f;
                    matrix.Matrix33 = options.WatermarkOpacity;
                    matrix.Matrix44 = 1f;
                    attributes.SetColorMatrix(matrix);
                    g.DrawImage(
                        layer,
                        new Rectangle(0, 0, bmp.Width, bmp.Height),
                        0,
                        0,
                        bmp.Width,
                        bmp.Height,
                        GraphicsUnit.Pixel,
                        attributes);
                }
            }
            finally
            {
                layer.Dispose();
            }
        }

        private static void DrawTextMark(Graphics g, Size size, BatchOptions options)
        {
            string text = options.WatermarkText;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            float fontSize = options.WatermarkFontSize <= 0 ? 36f : options.WatermarkFontSize;
            using (Font font = new Font(options.WatermarkFont, fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush brush = new SolidBrush(options.WatermarkColor))
            {
                SizeF textSize = g.MeasureString(text, font);

                if (options.WatermarkTile)
                {
                    float stepX = textSize.Width + 80f;
                    float stepY = textSize.Height + 80f;
                    for (float y = 0; y < size.Height + stepY; y += stepY)
                    {
                        for (float x = 0; x < size.Width + stepX; x += stepX)
                        {
                            DrawRotatedText(g, text, font, brush, new PointF(x, y), textSize, options.WatermarkAngle);
                        }
                    }
                }
                else
                {
                    PointF point = AnchorPoint(size, textSize, options);
                    DrawRotatedText(g, text, font, brush, point, textSize, options.WatermarkAngle);
                }
            }
        }

        private static void DrawRotatedText(Graphics g, string text, Font font, Brush brush, PointF topLeft, SizeF textSize, float angle)
        {
            if (angle == 0f)
            {
                g.DrawString(text, font, brush, topLeft);
                return;
            }

            GraphicsState state = g.Save();
            g.TranslateTransform(topLeft.X + textSize.Width / 2f, topLeft.Y + textSize.Height / 2f);
            g.RotateTransform(angle);
            g.DrawString(text, font, brush, -textSize.Width / 2f, -textSize.Height / 2f);
            g.Restore(state);
        }

        private static void DrawImageMark(Graphics g, Size size, BatchOptions options, Bitmap watermark)
        {
            float scale = options.WatermarkScale / 100f;
            int width = Math.Max(1, (int)Math.Round(size.Width * scale));
            int height = Math.Max(1, (int)Math.Round(watermark.Height * (double)width / watermark.Width));
            SizeF itemSize = new SizeF(width, height);

            if (options.WatermarkTile)
            {
                int stepX = width + 40;
                int stepY = height + 40;
                for (int y = 0; y < size.Height + stepY; y += stepY)
                {
                    for (int x = 0; x < size.Width + stepX; x += stepX)
                    {
                        g.DrawImage(watermark, new Rectangle(x, y, width, height));
                    }
                }
            }
            else
            {
                PointF point = AnchorPoint(size, itemSize, options);
                g.DrawImage(watermark, new Rectangle((int)Math.Round(point.X), (int)Math.Round(point.Y), width, height));
            }
        }

        private static PointF AnchorPoint(Size size, SizeF item, BatchOptions options)
        {
            float margin = options.WatermarkMargin;
            float x;
            float y;

            switch (options.WatermarkPosition)
            {
                case WatermarkPosition.TopLeft:
                    x = margin; y = margin; break;
                case WatermarkPosition.TopCenter:
                    x = (size.Width - item.Width) / 2f; y = margin; break;
                case WatermarkPosition.TopRight:
                    x = size.Width - item.Width - margin; y = margin; break;
                case WatermarkPosition.MiddleLeft:
                    x = margin; y = (size.Height - item.Height) / 2f; break;
                case WatermarkPosition.Center:
                    x = (size.Width - item.Width) / 2f; y = (size.Height - item.Height) / 2f; break;
                case WatermarkPosition.MiddleRight:
                    x = size.Width - item.Width - margin; y = (size.Height - item.Height) / 2f; break;
                case WatermarkPosition.BottomLeft:
                    x = margin; y = size.Height - item.Height - margin; break;
                case WatermarkPosition.BottomCenter:
                    x = (size.Width - item.Width) / 2f; y = size.Height - item.Height - margin; break;
                default:
                    x = size.Width - item.Width - margin; y = size.Height - item.Height - margin; break;
            }
            return new PointF(x, y);
        }

        public static void Save(Bitmap bitmap, string path, BatchOutputFormat format, int jpegQuality, Color matte)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            switch (format)
            {
                case BatchOutputFormat.Jpeg:
                    SaveJpeg(bitmap, path, jpegQuality, matte);
                    break;
                case BatchOutputFormat.Bmp:
                    using (Bitmap flat = Flatten(bitmap, matte))
                    {
                        flat.Save(path, ImageFormat.Bmp);
                    }
                    break;
                case BatchOutputFormat.Gif:
                    bitmap.Save(path, ImageFormat.Gif);
                    break;
                case BatchOutputFormat.Tiff:
                    bitmap.Save(path, ImageFormat.Tiff);
                    break;
                default:
                    bitmap.Save(path, ImageFormat.Png);
                    break;
            }
        }

        private static void SaveJpeg(Bitmap bitmap, string path, int quality, Color matte)
        {
            using (Bitmap flat = Flatten(bitmap, matte))
            {
                ImageCodecInfo codec = GetEncoder(ImageFormat.Jpeg);
                if (codec == null)
                {
                    flat.Save(path, ImageFormat.Jpeg);
                    return;
                }

                int q = quality;
                if (q < 0)
                {
                    q = 0;
                }
                if (q > 100)
                {
                    q = 100;
                }

                using (EncoderParameters parameters = new EncoderParameters(1))
                {
                    parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)q);
                    flat.Save(path, codec, parameters);
                }
            }
        }

        private static Bitmap Flatten(Bitmap source, Color matte)
        {
            Bitmap flat = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(flat))
            {
                g.Clear(matte);
                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
            }
            return flat;
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] encoders = ImageCodecInfo.GetImageEncoders();
            for (int i = 0; i < encoders.Length; i++)
            {
                if (encoders[i].FormatID == format.Guid)
                {
                    return encoders[i];
                }
            }
            return null;
        }
    }
}
