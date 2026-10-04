using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArrowOverlay
{
    // A borderless, click-through, always-on-top layered window. It covers only the area
    // being drawn and is painted with per-pixel alpha via UpdateLayeredWindow, so it never
    // takes focus or intercepts the mouse.
    internal sealed class OverlayWindow : NativeWindow, IDisposable
    {
        // Backing DIB that the window's pixels are copied from. Grown on demand, never shrunk.
        private IntPtr memDC;
        private IntPtr dib;
        private IntPtr oldBitmap;
        private Bitmap surface;
        private int surfaceWidth;
        private int surfaceHeight;

        private bool visible;

        public OverlayWindow()
        {
            var cp = new CreateParams();
            cp.Caption = "Arrow Overlay";
            cp.Style = NativeMethods.WS_POPUP;
            cp.ExStyle = NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST;
            cp.Width = 1;
            cp.Height = 1;
            CreateHandle(cp);
        }

        // Shows the overlay over bounds (screen pixels), painted by paint in screen coordinates.
        // opacity scales the whole overlay (255 = solid, 128 = half).
        public void Draw(Rectangle bounds, byte opacity, Action<Graphics> paint)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                Hide();
                return;
            }

            EnsureSurface(bounds.Width, bounds.Height);
            using (Graphics g = Graphics.FromImage(surface))
            {
                g.SetClip(new Rectangle(0, 0, bounds.Width, bounds.Height));
                g.CompositingMode = CompositingMode.SourceCopy;
                using (var clear = new SolidBrush(Color.Transparent))
                    g.FillRectangle(clear, 0, 0, bounds.Width, bounds.Height);

                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(-bounds.X, -bounds.Y);
                paint(g);
            }

            var dst = new NativeMethods.POINT(bounds.X, bounds.Y);
            var size = new NativeMethods.SIZE(bounds.Width, bounds.Height);
            var src = new NativeMethods.POINT(0, 0);
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                SourceConstantAlpha = opacity,
                AlphaFormat = NativeMethods.AC_SRC_ALPHA,
            };
            NativeMethods.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDC, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);

            if (!visible)
            {
                NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
                visible = true;
                KeepOnTop();
            }
        }

        public void Hide()
        {
            if (!visible)
                return;
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
            visible = false;
        }

        // Other topmost windows (games, video players) can end up above us; push back to the front.
        public void KeepOnTop()
        {
            if (visible)
            {
                NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
        }

        private void EnsureSurface(int width, int height)
        {
            if (surface != null && width <= surfaceWidth && height <= surfaceHeight)
                return;

            int newWidth = Math.Max(width, surfaceWidth);
            int newHeight = Math.Max(height, surfaceHeight);
            FreeSurface();

            var header = new NativeMethods.BITMAPINFOHEADER();
            header.biSize = Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER));
            header.biWidth = newWidth;
            header.biHeight = -newHeight; // top-down
            header.biPlanes = 1;
            header.biBitCount = 32;

            IntPtr bits;
            dib = NativeMethods.CreateDIBSection(IntPtr.Zero, ref header, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            memDC = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            oldBitmap = NativeMethods.SelectObject(memDC, dib);

            // GDI+ draws straight into the DIB's memory; UpdateLayeredWindow wants premultiplied alpha.
            surface = new Bitmap(newWidth, newHeight, newWidth * 4, PixelFormat.Format32bppPArgb, bits);
            surfaceWidth = newWidth;
            surfaceHeight = newHeight;
        }

        private void FreeSurface()
        {
            if (surface != null)
            {
                surface.Dispose();
                surface = null;
            }
            if (memDC != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memDC, oldBitmap);
                NativeMethods.DeleteDC(memDC);
                memDC = IntPtr.Zero;
            }
            if (dib != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(dib);
                dib = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            Hide();
            FreeSurface();
            DestroyHandle();
        }
    }
}
