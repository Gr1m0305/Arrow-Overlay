using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace ArrowOverlay
{
    // User preferences, stored as key=value lines in %APPDATA%\ArrowOverlay\settings.ini.
    internal sealed class Settings
    {
        public const int DefaultPoint1Key = 0xDB; // VK_OEM_4: the [ key on US/UK layouts
        public const int DefaultPoint2Key = 0xDD; // VK_OEM_6: the ] key on US/UK layouts

        public int Point1Key = DefaultPoint1Key;
        public int Point2Key = DefaultPoint2Key;
        public Color Colour = Color.Red;
        public float LineThickness = 2f;

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ArrowOverlay");
                return Path.Combine(dir, "settings.ini");
            }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath))
                    return s;

                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "Point1Key": s.Point1Key = ParseKey(value, s.Point1Key); break;
                        case "Point2Key": s.Point2Key = ParseKey(value, s.Point2Key); break;
                        case "Colour": s.Colour = ParseColour(value, s.Colour); break;
                        case "LineThickness": s.LineThickness = ParseFloat(value, s.LineThickness, 0.5f, 50f); break;
                    }
                }

                if (s.Point1Key == s.Point2Key)
                {
                    s.Point1Key = DefaultPoint1Key;
                    s.Point2Key = DefaultPoint2Key;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var sb = new StringBuilder();
                sb.AppendLine("Point1Key=" + Point1Key.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Point2Key=" + Point2Key.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine(string.Format("Colour=#{0:X2}{1:X2}{2:X2}", Colour.R, Colour.G, Colour.B));
                sb.AppendLine("LineThickness=" + LineThickness.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static int ParseKey(string value, int fallback)
        {
            int vk;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out vk) && vk > 0 && vk < 255)
                return vk;
            return fallback;
        }

        private static Color ParseColour(string value, Color fallback)
        {
            int rgb;
            if (int.TryParse(value.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
                return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return fallback;
        }

        private static float ParseFloat(string value, float fallback, float min, float max)
        {
            float f;
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out f) && f >= min && f <= max)
                return f;
            return fallback;
        }
    }
}
