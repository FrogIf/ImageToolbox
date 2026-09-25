using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageToolbox
{
    public static class WebPConverter
    {
        public static string GetTargetPath(string sourcePath, string outputDir, bool overwrite)
        {
            string dir = outputDir;
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            }

            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string dst = Path.Combine(dir, baseName + ".png");

            if (!overwrite)
            {
                int n = 1;
                while (File.Exists(dst))
                {
                    dst = Path.Combine(dir, baseName + "_" + n + ".png");
                    n++;
                }
            }

            return dst;
        }

        public static string Convert(string sourcePath, string outputDir, bool overwrite)
        {
            if (string.IsNullOrEmpty(outputDir))
            {
                outputDir = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            }

            Directory.CreateDirectory(outputDir);
            string dst = GetTargetPath(sourcePath, outputDir, overwrite);

            BitmapDecoder decoder = BitmapDecoder.Create(
                new Uri(Path.GetFullPath(sourcePath), UriKind.Absolute),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            BitmapSource frame = decoder.Frames[0];

            BitmapSource output = frame;
            if (frame.Format != PixelFormats.Bgra32 && frame.Format != PixelFormats.Bgr32)
            {
                FormatConvertedBitmap converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = frame;
                converted.DestinationFormat = PixelFormats.Bgra32;
                converted.EndInit();
                output = converted;
            }

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(output));

            using (FileStream fs = new FileStream(dst, FileMode.Create, FileAccess.Write))
            {
                encoder.Save(fs);
            }

            return dst;
        }
    }
}
