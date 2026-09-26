using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;

namespace ImageToolbox
{
    public class ImageInfo
    {
        public string FileName = "";
        public string Format = "";
        public int Width;
        public int Height;
        public double DpiX;
        public double DpiY;
        public long FileSize;
    }

    public class MetaEntry
    {
        public string Name = "";
        public string Value = "";
    }

    public static class ImageMeta
    {
        public static ImageInfo ReadInfo(string path)
        {
            ImageInfo info = new ImageInfo();
            info.FileName = Path.GetFileName(path);
            string ext = Path.GetExtension(path);
            info.Format = string.IsNullOrEmpty(ext) ? "未知" : ext.TrimStart('.').ToUpperInvariant();
            try
            {
                FileInfo fi = new FileInfo(path);
                if (fi.Exists)
                {
                    info.FileSize = fi.Length;
                }
            }
            catch (Exception)
            {
            }

            BitmapDecoder decoder = BitmapDecoder.Create(
                new Uri(Path.GetFullPath(path), UriKind.Absolute),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            BitmapFrame frame = decoder.Frames[0];
            info.Width = frame.PixelWidth;
            info.Height = frame.PixelHeight;
            info.DpiX = frame.DpiX;
            info.DpiY = frame.DpiY;
            return info;
        }

        public static List<MetaEntry> ReadMetadata(string path)
        {
            List<MetaEntry> list = new List<MetaEntry>();
            ImageInfo info = ReadInfo(path);
            Add(list, "文件名", info.FileName);
            Add(list, "格式", info.Format);
            Add(list, "尺寸", info.Width + " x " + info.Height + " 像素");
            Add(list, "分辨率", FormatDpi(info.DpiX, info.DpiY));
            Add(list, "文件大小", FormatSize(info.FileSize));

            BitmapDecoder decoder = BitmapDecoder.Create(
                new Uri(Path.GetFullPath(path), UriKind.Absolute),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            BitmapFrame frame = decoder.Frames[0];
            BitmapMetadata meta = frame.Metadata as BitmapMetadata;
            if (meta == null)
            {
                Add(list, "元数据", "无（该格式不包含可读元数据）");
                return list;
            }

            Add(list, "相机厂商", SafeProperty(meta, "CameraManufacturer"));
            Add(list, "相机型号", SafeProperty(meta, "CameraModel"));
            Add(list, "拍摄时间", SafeProperty(meta, "DateTaken"));
            Add(list, "软件", SafeProperty(meta, "ApplicationName"));
            Add(list, "作者", SafeProperty(meta, "Author"));
            Add(list, "版权", SafeProperty(meta, "Copyright"));
            Add(list, "标题", SafeProperty(meta, "Title"));
            Add(list, "主题", SafeProperty(meta, "Subject"));
            Add(list, "评分", SafeProperty(meta, "Rating"));

            Add(list, "曝光时间", SafeQuery(meta, "/app1/ifd/exif/{ushort=33434}"));
            Add(list, "光圈(F)", SafeQuery(meta, "/app1/ifd/exif/{ushort=33437}"));
            Add(list, "ISO", SafeQuery(meta, "/app1/ifd/exif/{ushort=34855}"));
            Add(list, "焦距(mm)", SafeQuery(meta, "/app1/ifd/exif/{ushort=37386}"));
            Add(list, "曝光程序", SafeQuery(meta, "/app1/ifd/exif/{ushort=34850}"));
            Add(list, "测光模式", SafeQuery(meta, "/app1/ifd/exif/{ushort=37383}"));
            Add(list, "白平衡", SafeQuery(meta, "/app1/ifd/exif/{ushort=41987}"));
            Add(list, "方向", SafeQuery(meta, "/app1/ifd/{ushort=274}"));
            Add(list, "纬度", SafeProperty(meta, "Latitude"));
            Add(list, "经度", SafeProperty(meta, "Longitude"));

            return list;
        }

        public static void StripMetadata(string sourcePath, string targetPath)
        {
            string ext = Path.GetExtension(targetPath).ToLowerInvariant();
            BatchOutputFormat format;
            switch (ext)
            {
                case ".jpg":
                case ".jpeg":
                    format = BatchOutputFormat.Jpeg;
                    break;
                case ".bmp":
                    format = BatchOutputFormat.Bmp;
                    break;
                case ".gif":
                    format = BatchOutputFormat.Gif;
                    break;
                case ".tif":
                case ".tiff":
                    format = BatchOutputFormat.Tiff;
                    break;
                default:
                    format = BatchOutputFormat.Png;
                    break;
            }

            Bitmap bitmap = ImageUtil.LoadImage(sourcePath);
            try
            {
                ImageBatch.Save(bitmap, targetPath, format, 92, Color.White);
            }
            finally
            {
                bitmap.Dispose();
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes <= 0)
            {
                return "-";
            }
            if (bytes < 1024)
            {
                return bytes + " B";
            }
            if (bytes < 1024L * 1024L)
            {
                return (bytes / 1024.0).ToString("0.0") + " KB";
            }
            return (bytes / (1024.0 * 1024.0)).ToString("0.00") + " MB";
        }

        private static string FormatDpi(double x, double y)
        {
            return x.ToString("0.##") + " x " + y.ToString("0.##") + " DPI";
        }

        private static void Add(List<MetaEntry> list, string name, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            MetaEntry entry = new MetaEntry();
            entry.Name = name;
            entry.Value = value;
            list.Add(entry);
        }

        private static string SafeProperty(BitmapMetadata meta, string name)
        {
            try
            {
                object value = meta.GetType().GetProperty(name).GetValue(meta, null);
                return FormatValue(value);
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string SafeQuery(BitmapMetadata meta, string query)
        {
            try
            {
                object value = meta.GetQuery(query);
                return FormatValue(value);
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string FormatValue(object value)
        {
            if (value == null)
            {
                return "";
            }
            Array array = value as Array;
            if (array != null)
            {
                string[] parts = new string[array.Length];
                for (int i = 0; i < array.Length; i++)
                {
                    object item = array.GetValue(i);
                    parts[i] = item == null ? "" : item.ToString();
                }
                return string.Join(", ", parts);
            }
            return value.ToString();
        }
    }
}
