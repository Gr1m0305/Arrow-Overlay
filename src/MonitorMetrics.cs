using System;
using System.Drawing;
using System.Windows.Forms;

namespace ArrowOverlay
{
    // The monitor the overlay runs on: where it's allowed to draw, and how it's scaled.
    internal struct MonitorMetrics
    {
        // The monitor's area in screen pixels. Nothing is drawn outside it.
        public Rectangle Bounds;

        // Windows display scaling (1.0 = 100%), used to keep the line thickness consistent.
        public float Scale;

        public static MonitorMetrics For(Screen screen)
        {
            var result = new MonitorMetrics();
            result.Bounds = screen.Bounds;

            float effectiveDpi = 96f;
            try
            {
                var centre = new NativeMethods.POINT(result.Bounds.X + result.Bounds.Width / 2, result.Bounds.Y + result.Bounds.Height / 2);
                IntPtr monitor = NativeMethods.MonitorFromPoint(centre, NativeMethods.MONITOR_DEFAULTTONEAREST);
                uint dpiX, dpiY;
                if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out dpiX, out dpiY) == 0)
                    effectiveDpi = dpiX;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            result.Scale = effectiveDpi / 96f;
            return result;
        }
    }
}
