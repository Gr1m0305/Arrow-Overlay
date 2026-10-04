using System;
using System.Drawing;
using System.Drawing.Text;

namespace ArrowOverlay
{
    // What the overlay shows: the line, or the grid while it's being lined up.
    internal static class Scenes
    {
        // The line through a and b, run past both edges of clip (its monitor) and hidden outside it.
        // If a and b coincide there's no direction yet, so a dot marks a instead. With smart lock,
        // targetDot marks a (the snapped target).
        public static void Line(OverlayWindow overlay, Rectangle clip, PointF a, PointF b, Color colour, float width,
            byte opacity, bool targetDot)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            bool isLine = length >= 1f;
            float dotRadius = DotRadius(width);

            // a lies inside clip, so reaching one diagonal out from it in both directions
            // is guaranteed to cross the edges.
            PointF start = a, end = a;
            if (isLine)
            {
                float reach = (float)Math.Sqrt((double)clip.Width * clip.Width + (double)clip.Height * clip.Height) + width;
                start = new PointF(a.X - dx / length * reach, a.Y - dy / length * reach);
                end = new PointF(a.X + dx / length * reach, a.Y + dy / length * reach);
            }

            RectangleF area = RectangleF.Union(Around(start, width), Around(end, width));
            area = RectangleF.Union(area, Around(a, dotRadius));

            overlay.Draw(Bounds(area, clip), opacity, g =>
            {
                using (var pen = new Pen(colour, width))
                using (var brush = new SolidBrush(colour))
                {
                    if (isLine)
                        g.DrawLine(pen, start, end);
                    if (!isLine || targetDot)
                        FillCircle(g, brush, a, dotRadius);
                }
            });
        }

        // A lone dot, shown while choosing point 1. It's the same size as the target dot on a smart-lock line.
        public static void Dot(OverlayWindow overlay, Rectangle clip, PointF centre, Color colour, float width, byte opacity)
        {
            float radius = DotRadius(width);
            overlay.Draw(Bounds(Around(centre, radius), clip), opacity, g =>
            {
                using (var brush = new SolidBrush(colour))
                    FillCircle(g, brush, centre, radius);
            });
        }

        // Choosing point 1 with smart lock: the village grid at 25% and the dot at 50%.
        public static void Aiming(OverlayWindow overlay, Rectangle monitor, IsoGrid grid, PointF dot, Color colour, float width)
        {
            float radius = DotRadius(width);
            overlay.Draw(monitor, 255, g =>
            {
                using (var gridPen = new Pen(Color.FromArgb(64, colour), 1f))
                    grid.Paint(g, gridPen, gridPen);
                using (var brush = new SolidBrush(Color.FromArgb(128, colour)))
                    FillCircle(g, brush, dot, radius);
            });
        }

        // Calibrating: a prompt at the top, a dot on the left corner once it's clicked, and the
        // village grid once there's a size for it. gridAlpha is the outline's opacity; tiles are fainter.
        public static void Calibration(OverlayWindow overlay, Rectangle monitor, string prompt, PointF? leftCorner,
            IsoGrid? grid, byte gridAlpha, Color colour, float width, float scale)
        {
            overlay.Draw(monitor, 255, g =>
            {
                if (grid.HasValue)
                {
                    using (var tilePen = new Pen(Color.FromArgb(gridAlpha * 150 / 255, colour), 1f))
                    using (var edgePen = new Pen(Color.FromArgb(gridAlpha, colour), 2f * scale))
                        grid.Value.Paint(g, tilePen, edgePen);
                }
                if (leftCorner.HasValue)
                {
                    using (var brush = new SolidBrush(colour))
                        FillCircle(g, brush, leftCorner.Value, DotRadius(width));
                }
                using (Font font = PromptFont(scale))
                    DrawPrompt(g, monitor, prompt, font, scale);
            });
        }

        // A short label at the top of the monitor, e.g. the snap mode.
        public static void Label(OverlayWindow overlay, Rectangle monitor, string text, float scale)
        {
            using (Font font = PromptFont(scale))
            {
                RectangleF box;
                using (Graphics measure = Graphics.FromHwnd(IntPtr.Zero))
                    box = PromptBox(measure, monitor, text, font, scale);
                overlay.Draw(Bounds(box, monitor), 255, g => DrawPrompt(g, monitor, text, font, scale));
            }
        }

        private static Font PromptFont(float scale)
        {
            return new Font("Segoe UI", 18f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        // Prompts sit at the top of the monitor, just right of centre so they don't cover
        // Clash of Clans' battle timer, which is top centre.
        private static RectangleF PromptBox(Graphics g, Rectangle monitor, string text, Font font, float scale)
        {
            SizeF size = g.MeasureString(text, font);
            float pad = 8f * scale;
            return new RectangleF(
                monitor.X + monitor.Width * 0.575f, monitor.Y + 8f * scale,
                size.Width + pad * 2, size.Height + pad * 2);
        }

        private static void DrawPrompt(Graphics g, Rectangle monitor, string text, Font font, float scale)
        {
            RectangleF box = PromptBox(g, monitor, text, font, scale);
            float pad = 8f * scale;
            using (var background = new SolidBrush(Color.FromArgb(210, 20, 20, 20)))
                g.FillRectangle(background, box);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.DrawString(text, font, Brushes.White, box.X + pad, box.Y + pad);
        }

        private static float DotRadius(float lineWidth)
        {
            return lineWidth * 1.5f + 2f;
        }

        // The pixels covering area, plus a little for anti-aliasing, kept inside clip.
        private static Rectangle Bounds(RectangleF area, Rectangle clip)
        {
            Rectangle bounds = Rectangle.FromLTRB(
                (int)Math.Floor(area.Left) - 2, (int)Math.Floor(area.Top) - 2,
                (int)Math.Ceiling(area.Right) + 2, (int)Math.Ceiling(area.Bottom) + 2);
            bounds.Intersect(clip);
            return bounds;
        }

        private static RectangleF Around(PointF p, float radius)
        {
            return new RectangleF(p.X - radius, p.Y - radius, radius * 2, radius * 2);
        }

        private static void FillCircle(Graphics g, Brush brush, PointF centre, float radius)
        {
            g.FillEllipse(brush, centre.X - radius, centre.Y - radius, radius * 2, radius * 2);
        }
    }
}
