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
        public const int DefaultPoint1Key = 0x09; // VK_TAB: Tab
        public const int DefaultCalibrateKey = 0xDC; // VK_OEM_5: the \ key on US layouts
        public const int DefaultSwapKey = 0xDE; // VK_OEM_7: the ' key on US layouts
        public const int DefaultDeleteKey = 0x08; // VK_BACK: Backspace
        public const int DefaultMenuKey = 0x4D; // M

        public int Point1Key = DefaultPoint1Key;
        public int CalibrateKey = DefaultCalibrateKey;
        public int SwapKey = DefaultSwapKey;
        public int DeleteKey = DefaultDeleteKey;
        public int MenuKey = DefaultMenuKey;
        public Color Colour = Color.Red;
        public float LineThickness = 2f;
        public string Monitor = ""; // device name of the monitor to draw on; empty means the main one

        public bool SmartLock;
        public IsoGrid Grid; // not set until calibrated
        public bool SnapCorners; // true: "Mode: 2x2/4x4" (tile corners), false: "Mode: 3x3" (tile centres)

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
                        case "CalibrateKey": s.CalibrateKey = ParseKey(value, s.CalibrateKey); break;
                        case "SwapKey": s.SwapKey = ParseKey(value, s.SwapKey); break;
                        case "DeleteKey": s.DeleteKey = ParseKey(value, s.DeleteKey); break;
                        case "MenuKey": s.MenuKey = ParseKey(value, s.MenuKey); break;
                        case "Colour": s.Colour = ParseColour(value, s.Colour); break;
                        case "LineThickness": s.LineThickness = ParseFloat(value, s.LineThickness, 0.5f, 50f); break;
                        case "Monitor": s.Monitor = value; break;
                        case "SmartLock": s.SmartLock = value == "1"; break;
                        case "SnapCorners": s.SnapCorners = value == "1"; break;
                        case "GridX": s.Grid.OriginX = ParseFloat(value, 0f, -100000f, 100000f); break;
                        case "GridY": s.Grid.OriginY = ParseFloat(value, 0f, -100000f, 100000f); break;
                        case "GridTileHalfWidth": s.Grid.HalfWidth = ParseFloat(value, 0f, 1f, 1000f); break;
                        case "GridTileHalfHeight": s.Grid.HalfHeight = ParseFloat(value, 0f, 1f, 1000f); break;
                    }
                }

                if (!AllDifferent(s.Hotkeys))
                    s.Hotkeys = DefaultHotkeys;
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
                sb.AppendLine("CalibrateKey=" + CalibrateKey.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("SwapKey=" + SwapKey.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("DeleteKey=" + DeleteKey.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("MenuKey=" + MenuKey.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine(string.Format("Colour=#{0:X2}{1:X2}{2:X2}", Colour.R, Colour.G, Colour.B));
                sb.AppendLine("LineThickness=" + LineThickness.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Monitor=" + Monitor);
                sb.AppendLine("SmartLock=" + (SmartLock ? "1" : "0"));
                sb.AppendLine("SnapCorners=" + (SnapCorners ? "1" : "0"));
                if (Grid.IsSet)
                {
                    sb.AppendLine("GridX=" + Grid.OriginX.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("GridY=" + Grid.OriginY.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("GridTileHalfWidth=" + Grid.HalfWidth.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("GridTileHalfHeight=" + Grid.HalfHeight.ToString(CultureInfo.InvariantCulture));
                }
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // The hotkeys as a list, in the order HotkeyLabels describes them.
        public static readonly string[] HotkeyLabels =
        {
            "Set point 1:", "Calibrate grid:", "Swap snap mode:", "Delete line:", "Open menu:",
        };

        public static int[] DefaultHotkeys
        {
            get
            {
                return new[] { DefaultPoint1Key, DefaultCalibrateKey, DefaultSwapKey, DefaultDeleteKey, DefaultMenuKey };
            }
        }

        public int[] Hotkeys
        {
            get { return new[] { Point1Key, CalibrateKey, SwapKey, DeleteKey, MenuKey }; }
            set
            {
                Point1Key = value[0];
                CalibrateKey = value[1];
                SwapKey = value[2];
                DeleteKey = value[3];
                MenuKey = value[4];
            }
        }

        public static bool AllDifferent(params int[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
                for (int j = i + 1; j < keys.Length; j++)
                    if (keys[i] == keys[j])
                        return false;
            return true;
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
