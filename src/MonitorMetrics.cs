using System;
using System.Drawing;
using System.Windows.Forms;

namespace ArrowOverlay
{
    // The monitor under a given point: where the line is allowed to appear, and how it is scaled.
    internal struct MonitorMetrics
    {
        // The monitor's area in screen pixels. The line is clipped to this.
        public Rectangle Bounds;

        // Windows display scaling (1.0 = 100%), used to keep the line thickness consistent.
        public float Scale;

        public static MonitorMetrics ForPoint(Point p)
        {
            float effectiveDpi = 96f;
            try
            {
                IntPtr monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT(p.X, p.Y), NativeMethods.MONITOR_DEFAULTTONEAREST);
                uint dpiX, dpiY;
                if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out dpiX, out dpiY) == 0)
                    effectiveDpi = dpiX;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            var result = new MonitorMetrics();
            result.Bounds = Screen.FromPoint(p).Bounds;
            result.Scale = effectiveDpi / 96f;
            return result;
        }
    }
}
