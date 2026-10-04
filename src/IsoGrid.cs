using System;
using System.Drawing;

namespace ArrowOverlay
{
    // The game's isometric tile grid as it appears on screen. Each tile is a diamond
    // 2*HalfWidth wide and 2*HalfHeight tall, and (OriginX, OriginY) is the centre of the
    // 44x44-tile village, which is a tile corner.
    //
    // Buildings are squares of whole tiles, so even-sized ones (2x2, 4x4) are centred on a
    // tile corner and odd-sized ones (3x3) on a tile centre.
    internal struct IsoGrid
    {
        public const int VillageTiles = 44;

        // Clash of Clans tiles are 4:3 diamonds (measured from a wall block: 40x30 px at one zoom).
        public const float TileAspect = 0.75f;

        // The smallest gap between the corners that still gives tiles at least a pixel across.
        public const float MinCornerGap = 2f * VillageTiles;

        public float OriginX;
        public float OriginY;
        public float HalfWidth;
        public float HalfHeight;

        public bool IsSet
        {
            get { return HalfWidth > 0f && HalfHeight > 0f; }
        }

        // The village from its left and right corners. They sit level with its centre, 44 tiles
        // apart, and the tile shape is fixed, so the gap between them sets the whole grid.
        public static IsoGrid FromCorners(PointF left, PointF right)
        {
            var grid = new IsoGrid();
            grid.OriginX = (left.X + right.X) / 2f;
            grid.OriginY = (left.Y + right.Y) / 2f;
            grid.HalfWidth = Math.Abs(right.X - left.X) / (2f * VillageTiles);
            grid.HalfHeight = grid.HalfWidth * TileAspect;
            return grid;
        }

        // The nearest tile corner (corners = true, for 2x2/4x4 buildings) or tile centre (for 3x3).
        public PointF Snap(PointF p, bool corners)
        {
            float x = (p.X - OriginX) / HalfWidth;
            float y = (p.Y - OriginY) / HalfHeight;
            float offset = corners ? 0f : 0.5f;
            float u = (float)Math.Round((y + x) / 2f - offset) + offset;
            float v = (float)Math.Round((y - x) / 2f - offset) + offset;
            return ToScreen(u, v);
        }

        // Draws the village's tiles, with its outer edge in edgePen.
        public void Paint(Graphics g, Pen tilePen, Pen edgePen)
        {
            // Tile coordinates from the centre: screen = origin + u*(a, b) + v*(-a, b),
            // so u grows down-right and v down-left.
            const int half = VillageTiles / 2;
            for (int i = -half; i <= half; i++)
            {
                Pen pen = i == -half || i == half ? edgePen : tilePen;
                g.DrawLine(pen, ToScreen(i, -half), ToScreen(i, half));
                g.DrawLine(pen, ToScreen(-half, i), ToScreen(half, i));
            }
        }

        private PointF ToScreen(float u, float v)
        {
            return new PointF(OriginX + (u - v) * HalfWidth, OriginY + (u + v) * HalfHeight);
        }
    }
}
