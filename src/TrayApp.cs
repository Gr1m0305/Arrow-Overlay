using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ArrowOverlay
{
    // Owns the tray icon, the global hotkeys and the overlay, and runs the line-placing state machine:
    //   point 1 key  -> remember the cursor, preview a 50% line to the cursor
    //   point 2 key  -> fix the line at full opacity
    //   both at once -> delete the line
    internal sealed class TrayApp : ApplicationContext
    {
        private enum Mode { None, Placing, Placed }

        private sealed class KeyState
        {
            public bool Down;
            public int LastTick;
        }

        private const byte PreviewOpacity = 128; // 50%
        private const byte FullOpacity = 255;

        // A held key repeats at least once a second, so a "still down" key that has been quiet
        // for longer than this lost its key-up somewhere (e.g. to an elevated window).
        private const int StaleKeyMs = 1500;

        // Line thickness options in the tray menu, in pixels at 100% display scaling.
        private static readonly float[] ThicknessChoices = { 1f, 2f, 3f, 4f, 5f, 6f, 8f, 10f };

        private readonly Settings settings;
        private readonly OverlayWindow overlay;
        private readonly KeyboardHook hook;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly Timer previewTimer;
        private readonly Timer topmostTimer;
        private readonly KeyState point1Key = new KeyState();
        private readonly KeyState point2Key = new KeyState();

        private Mode mode = Mode.None;
        private Point point1;
        private Point point2;
        private Point lastCursor;
        private MonitorMetrics metrics;
        private bool dialogOpen;
        private Icon trayImage;

        public TrayApp()
        {
            settings = Settings.Load();
            overlay = new OverlayWindow();

            previewTimer = new Timer { Interval = 10 };
            previewTimer.Tick += delegate { UpdatePreview(false); };

            topmostTimer = new Timer { Interval = 2000 };
            topmostTimer.Tick += delegate { overlay.KeepOnTop(); };
            topmostTimer.Start();

            menu = new ContextMenuStrip();
            menu.Items.Add("Hotkeys...", null, delegate { ShowHotkeyDialog(); });
            menu.Items.Add("Colour...", null, delegate { ShowColourDialog(); });
            menu.Items.Add(CreateThicknessMenu());
            menu.Items.Add("Delete line", null, delegate { ClearLine(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });
            menu.Opening += delegate
            {
                foreach (ToolStripItem item in menu.Items)
                    item.Enabled = !dialogOpen || item.Text == "Exit";
            };

            tray = new NotifyIcon { ContextMenuStrip = menu };
            UpdateTrayIcon();
            UpdateTrayText();
            tray.Visible = true;

            hook = new KeyboardHook(OnKey);

            tray.ShowBalloonTip(4000, "Arrow Overlay is running",
                string.Format("Press {0} to start a line and {1} to finish it. Press both to delete it. Right-click the tray icon for options.",
                    KeyNames.Get(settings.Point1Key), KeyNames.Get(settings.Point2Key)),
                ToolTipIcon.Info);
        }

        // Called from the keyboard hook for every key event. Returns true to swallow the key.
        private bool OnKey(int vk, bool down)
        {
            KeyState state = vk == settings.Point1Key ? point1Key : vk == settings.Point2Key ? point2Key : null;
            if (state == null)
                return false;

            if (!down)
            {
                bool swallowedDown = state.Down;
                state.Down = false;
                return swallowedDown;
            }

            if (dialogOpen)
                return false; // let the key reach the hotkey dialog

            int now = Environment.TickCount;
            bool autoRepeat = IsHeld(state, now);
            state.Down = true;
            state.LastTick = now;
            if (autoRepeat)
                return true;

            if (IsHeld(point1Key, now) && IsHeld(point2Key, now))
                ClearLine();
            else if (state == point1Key)
                StartLine();
            else
                FinishLine();
            return true;
        }

        private static bool IsHeld(KeyState state, int now)
        {
            return state.Down && unchecked(now - state.LastTick) < StaleKeyMs;
        }

        private void StartLine()
        {
            point1 = Cursor.Position;
            metrics = MonitorMetrics.ForPoint(point1);
            mode = Mode.Placing;
            UpdatePreview(true);
            previewTimer.Start();
        }

        private void FinishLine()
        {
            if (mode != Mode.Placing)
                return;
            previewTimer.Stop();
            point2 = Cursor.Position;
            mode = Mode.Placed;
            Draw(point1, point2, FullOpacity);
        }

        private void ClearLine()
        {
            previewTimer.Stop();
            mode = Mode.None;
            overlay.Hide();
        }

        private void UpdatePreview(bool force)
        {
            Point cursor = Cursor.Position;
            if (!force && cursor == lastCursor)
                return;
            lastCursor = cursor;
            Draw(point1, cursor, PreviewOpacity);
        }

        private void Redraw()
        {
            if (mode == Mode.Placed)
                Draw(point1, point2, FullOpacity);
            else if (mode == Mode.Placing)
                UpdatePreview(true);
        }

        private void Draw(Point a, Point b, byte opacity)
        {
            overlay.Draw(a, b, metrics.Bounds, settings.Colour, settings.LineThickness * metrics.Scale, opacity);
        }

        private void ShowHotkeyDialog()
        {
            dialogOpen = true;
            try
            {
                using (var form = new HotkeyForm(settings.Point1Key, settings.Point2Key))
                {
                    if (form.ShowDialog() != DialogResult.OK)
                        return;
                    settings.Point1Key = form.Point1Key;
                    settings.Point2Key = form.Point2Key;
                    settings.Save();
                    UpdateTrayText();
                }
            }
            finally
            {
                dialogOpen = false;
            }
        }

        private void ShowColourDialog()
        {
            dialogOpen = true;
            try
            {
                using (var dialog = new ColorDialog { Color = settings.Colour, FullOpen = true, AnyColor = true })
                {
                    if (dialog.ShowDialog() != DialogResult.OK)
                        return;
                    settings.Colour = dialog.Color;
                    settings.Save();
                    UpdateTrayIcon();
                    Redraw();
                }
            }
            finally
            {
                dialogOpen = false;
            }
        }

        private ToolStripMenuItem CreateThicknessMenu()
        {
            var thickness = new ToolStripMenuItem("Thickness");
            foreach (float px in ThicknessChoices)
            {
                var item = new ToolStripMenuItem(px + " px", null, delegate { SetThickness(px); });
                item.Tag = px;
                thickness.DropDownItems.Add(item);
            }
            thickness.DropDownOpening += delegate
            {
                foreach (ToolStripMenuItem item in thickness.DropDownItems)
                    item.Checked = (float)item.Tag == settings.LineThickness;
            };
            return thickness;
        }

        private void SetThickness(float px)
        {
            settings.LineThickness = px;
            settings.Save();
            Redraw();
        }

        private void UpdateTrayText()
        {
            string text = string.Format("Arrow Overlay\n{0} = point 1, {1} = point 2, both = delete",
                KeyNames.Get(settings.Point1Key), KeyNames.Get(settings.Point2Key));
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text; // NotifyIcon's limit
        }

        // Draws the tray icon in the current colour: a diagonal line running off both corners.
        private void UpdateTrayIcon()
        {
            int size = SystemInformation.SmallIconSize.Width;
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (var line = new Pen(settings.Colour, Math.Max(2f, size / 8f)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawLine(line, 0, size, size, 0);
                }

                IntPtr handle = bmp.GetHicon();
                Icon icon;
                using (Icon temp = Icon.FromHandle(handle))
                    icon = (Icon)temp.Clone(); // the clone owns a copy of the handle
                NativeMethods.DestroyIcon(handle);

                tray.Icon = icon;
                if (trayImage != null)
                    trayImage.Dispose();
                trayImage = icon;
            }
        }

        protected override void ExitThreadCore()
        {
            previewTimer.Stop();
            topmostTimer.Stop();
            hook.Dispose();
            overlay.Dispose();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            if (trayImage != null)
                trayImage.Dispose();
            base.ExitThreadCore();
        }
    }
}
