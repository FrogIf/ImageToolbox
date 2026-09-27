using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ImageToolbox
{
    public enum CollageDirection
    {
        Horizontal = 0,
        Vertical,
        Grid
    }

    public class CollageOptions
    {
        public CollageDirection Direction = CollageDirection.Horizontal;
        public int Columns = 2;
        public int Spacing = 8;
        public int Margin = 8;
        public Color Background = Color.White;
        public int CellWidth;
        public int CellHeight;
        public bool Fill;
        public int LongEdge;
    }

    public static class ImageLayout
    {
        public static Bitmap Clone(Bitmap source)
        {
            Bitmap dst = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
            }
            return dst;
        }

        public static Bitmap Crop(Bitmap source, Rectangle region)
        {
            int x = Math.Max(0, region.X);
            int y = Math.Max(0, region.Y);
            int w = Math.Min(region.Width, source.Width - x);
            int h = Math.Min(region.Height, source.Height - y);
            if (w <= 0 || h <= 0)
            {
                return Clone(source);
            }

            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(
                    source,
                    new Rectangle(0, 0, w, h),
                    new Rectangle(x, y, w, h),
                    GraphicsUnit.Pixel);
            }
            return dst;
        }

        public static Bitmap ResizeLongEdge(Bitmap source, int longEdge)
        {
            if (longEdge <= 0 || (source.Width <= longEdge && source.Height <= longEdge))
            {
                return Clone(source);
            }

            double scale = (double)longEdge / Math.Max(source.Width, source.Height);
            int w = Math.Max(1, (int)Math.Round(source.Width * scale));
            int h = Math.Max(1, (int)Math.Round(source.Height * scale));
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, w, h));
            }
            return dst;
        }

        public static List<Bitmap> Slice(Bitmap source, int columns, int rows)
        {
            columns = Math.Max(1, columns);
            rows = Math.Max(1, rows);

            int[] xs = Divide(source.Width, columns);
            int[] ys = Divide(source.Height, rows);
            List<Bitmap> tiles = new List<Bitmap>();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    Rectangle region = new Rectangle(
                        xs[c],
                        ys[r],
                        Math.Max(1, xs[c + 1] - xs[c]),
                        Math.Max(1, ys[r + 1] - ys[r]));
                    tiles.Add(Crop(source, region));
                }
            }
            return tiles;
        }

        private static int[] Divide(int total, int parts)
        {
            int[] result = new int[parts + 1];
            for (int i = 0; i <= parts; i++)
            {
                result[i] = (int)Math.Round((double)total * i / parts);
            }
            return result;
        }

        public static Bitmap Collage(List<Bitmap> images, CollageOptions options)
        {
            if (images == null || images.Count == 0)
            {
                return null;
            }

            Bitmap canvas;
            if (options.Direction == CollageDirection.Horizontal)
            {
                canvas = CollageLine(images, options, true);
            }
            else if (options.Direction == CollageDirection.Vertical)
            {
                canvas = CollageLine(images, options, false);
            }
            else
            {
                canvas = CollageGrid(images, options);
            }

            if (options.LongEdge > 0 && (canvas.Width > options.LongEdge || canvas.Height > options.LongEdge))
            {
                double scale = (double)options.LongEdge / Math.Max(canvas.Width, canvas.Height);
                int w = Math.Max(1, (int)Math.Round(canvas.Width * scale));
                int h = Math.Max(1, (int)Math.Round(canvas.Height * scale));
                Bitmap small = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(canvas, new Rectangle(0, 0, w, h));
                }
                canvas.Dispose();
                canvas = small;
            }

            return canvas;
        }

        private static Bitmap CollageLine(List<Bitmap> images, CollageOptions options, bool horizontal)
        {
            int count = images.Count;
            int spacing = Math.Max(0, options.Spacing);
            int margin = Math.Max(0, options.Margin);

            if (horizontal)
            {
                int height = options.CellHeight;
                if (height <= 0)
                {
                    height = 0;
                    for (int i = 0; i < count; i++)
                    {
                        height = Math.Max(height, images[i].Height);
                    }
                }

                int[] widths = new int[count];
                int totalWidth = margin * 2 + spacing * (count - 1);
                for (int i = 0; i < count; i++)
                {
                    widths[i] = Math.Max(1, (int)Math.Round(images[i].Width * (double)height / images[i].Height));
                    totalWidth += widths[i];
                }

                Bitmap canvas = new Bitmap(totalWidth, height + margin * 2, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.Clear(options.Background);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    int x = margin;
                    for (int i = 0; i < count; i++)
                    {
                        g.DrawImage(images[i], new Rectangle(x, margin, widths[i], height));
                        x += widths[i] + spacing;
                    }
                }
                return canvas;
            }
            else
            {
                int width = options.CellWidth;
                if (width <= 0)
                {
                    width = 0;
                    for (int i = 0; i < count; i++)
                    {
                        width = Math.Max(width, images[i].Width);
                    }
                }

                int[] heights = new int[count];
                int totalHeight = margin * 2 + spacing * (count - 1);
                for (int i = 0; i < count; i++)
                {
                    heights[i] = Math.Max(1, (int)Math.Round(images[i].Height * (double)width / images[i].Width));
                    totalHeight += heights[i];
                }

                Bitmap canvas = new Bitmap(width + margin * 2, totalHeight, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.Clear(options.Background);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    int y = margin;
                    for (int i = 0; i < count; i++)
                    {
                        g.DrawImage(images[i], new Rectangle(margin, y, width, heights[i]));
                        y += heights[i] + spacing;
                    }
                }
                return canvas;
            }
        }

        private static Bitmap CollageGrid(List<Bitmap> images, CollageOptions options)
        {
            int count = images.Count;
            int cols = Math.Max(1, options.Columns);
            int rows = (count + cols - 1) / cols;
            int spacing = Math.Max(0, options.Spacing);
            int margin = Math.Max(0, options.Margin);

            int cellWidth = options.CellWidth;
            int cellHeight = options.CellHeight;
            if (cellWidth <= 0)
            {
                cellWidth = 0;
                for (int i = 0; i < count; i++)
                {
                    cellWidth = Math.Max(cellWidth, images[i].Width);
                }
            }
            if (cellHeight <= 0)
            {
                cellHeight = 0;
                for (int i = 0; i < count; i++)
                {
                    cellHeight = Math.Max(cellHeight, images[i].Height);
                }
            }

            int canvasWidth = margin * 2 + cols * cellWidth + spacing * (cols - 1);
            int canvasHeight = margin * 2 + rows * cellHeight + spacing * (rows - 1);
            Bitmap canvas = new Bitmap(canvasWidth, canvasHeight, PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(options.Background);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                for (int i = 0; i < count; i++)
                {
                    int r = i / cols;
                    int c = i % cols;
                    int x = margin + c * (cellWidth + spacing);
                    int y = margin + r * (cellHeight + spacing);
                    DrawInBox(g, images[i], new Rectangle(x, y, cellWidth, cellHeight), options.Fill);
                }
            }
            return canvas;
        }

        public static void DrawInBox(Graphics g, Bitmap image, Rectangle box, bool fill)
        {
            if (fill)
            {
                double boxAspect = (double)box.Width / box.Height;
                double imageAspect = (double)image.Width / image.Height;
                Rectangle source;
                if (imageAspect > boxAspect)
                {
                    int cropWidth = Math.Max(1, (int)Math.Round(image.Height * boxAspect));
                    source = new Rectangle((image.Width - cropWidth) / 2, 0, cropWidth, image.Height);
                }
                else
                {
                    int cropHeight = Math.Max(1, (int)Math.Round(image.Width / boxAspect));
                    source = new Rectangle(0, (image.Height - cropHeight) / 2, image.Width, cropHeight);
                }
                g.DrawImage(image, box, source, GraphicsUnit.Pixel);
            }
            else
            {
                double scale = Math.Min((double)box.Width / image.Width, (double)box.Height / image.Height);
                int w = Math.Max(1, (int)Math.Round(image.Width * scale));
                int h = Math.Max(1, (int)Math.Round(image.Height * scale));
                int x = box.X + (box.Width - w) / 2;
                int y = box.Y + (box.Height - h) / 2;
                g.DrawImage(image, new Rectangle(x, y, w, h));
            }
        }

        public static Bitmap BuildIdPhoto(Bitmap source, int width, int height, bool fill, Color background)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            Bitmap canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(background);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                DrawInBox(g, source, new Rectangle(0, 0, width, height), fill);
            }
            return canvas;
        }

        public static Bitmap BuildSheet(
            Bitmap idPhoto,
            int paperWidth,
            int paperHeight,
            int columns,
            int count,
            int spacing,
            int margin,
            Color background,
            bool cutLines)
        {
            columns = Math.Max(1, columns);
            spacing = Math.Max(0, spacing);
            margin = Math.Max(0, margin);

            int availWidth = paperWidth - margin * 2;
            int availHeight = paperHeight - margin * 2;
            int fitCols = Math.Max(1, (availWidth + spacing) / (idPhoto.Width + spacing));
            int fitRows = Math.Max(1, (availHeight + spacing) / (idPhoto.Height + spacing));
            int useCols = Math.Min(columns, fitCols);
            int useRows = fitRows;
            int total = useCols * useRows;

            if (count > 0 && count < total)
            {
                useRows = Math.Max(1, (count + useCols - 1) / useCols);
                useRows = Math.Min(useRows, fitRows);
                total = Math.Min(count, useCols * useRows);
            }

            Bitmap canvas = new Bitmap(paperWidth, paperHeight, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(background);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                int gridWidth = useCols * idPhoto.Width + (useCols - 1) * spacing;
                int gridHeight = useRows * idPhoto.Height + (useRows - 1) * spacing;
                int startX = (paperWidth - gridWidth) / 2;
                int startY = (paperHeight - gridHeight) / 2;

                for (int i = 0; i < total; i++)
                {
                    int r = i / useCols;
                    int c = i % useCols;
                    int x = startX + c * (idPhoto.Width + spacing);
                    int y = startY + r * (idPhoto.Height + spacing);
                    g.DrawImage(idPhoto, new Rectangle(x, y, idPhoto.Width, idPhoto.Height));
                    if (cutLines)
                    {
                        using (Pen pen = new Pen(Color.FromArgb(150, 150, 150), 1f))
                        {
                            g.DrawRectangle(pen, x, y, idPhoto.Width - 1, idPhoto.Height - 1);
                        }
                    }
                }
            }
            return canvas;
        }
    }
}
