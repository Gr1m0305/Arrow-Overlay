using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArrowOverlay
{
    // A borderless, click-through, always-on-top layered window that draws the line.
    // It is sized to the line's bounding box and painted with per-pixel alpha via
    // UpdateLayeredWindow, so it never takes focus or intercepts the mouse.
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

        // Draws the line through a and b (screen pixels), extended past both edges of clip,
        // and shows nothing outside clip. If a and b coincide there's no direction yet, so
        // just a dot is drawn at a. opacity scales the whole overlay (255 = solid, 128 = half).
        public void Draw(Point a, Point b, Rectangle clip, Color colour, float lineWidth, byte opacity)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            bool isLine = length >= 1f;
            float dotRadius = lineWidth * 1.5f;

            // a lies inside clip, so reaching one diagonal out from it in both directions
            // is guaranteed to cross the edges.
            PointF start = a, end = a;
            if (isLine)
            {
                float reach = (float)Math.Sqrt((double)clip.Width * clip.Width + (double)clip.Height * clip.Height) + lineWidth;
                float ux = dx / length * reach;
                float uy = dy / length * reach;
                start = new PointF(a.X - ux, a.Y - uy);
                end = new PointF(a.X + ux, a.Y + uy);
            }

            float pad = (isLine ? lineWidth : dotRadius) + 2f; // spare pixels for anti-aliasing
            Rectangle bounds = Rectangle.FromLTRB(
                (int)Math.Floor(Math.Min(start.X, end.X) - pad), (int)Math.Floor(Math.Min(start.Y, end.Y) - pad),
                (int)Math.Ceiling(Math.Max(start.X, end.X) + pad), (int)Math.Ceiling(Math.Max(start.Y, end.Y) + pad));
            bounds.Intersect(clip);
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

                if (isLine)
                {
                    using (var pen = new Pen(colour, lineWidth))
                        g.DrawLine(pen, start, end);
                }
                else
                {
                    using (var brush = new SolidBrush(colour))
                        g.FillEllipse(brush, a.X - dotRadius, a.Y - dotRadius, dotRadius * 2, dotRadius * 2);
                }
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
