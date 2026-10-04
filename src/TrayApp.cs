using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;

namespace ArrowOverlay
{
    // Owns the tray icon, the global hotkeys and the overlay, and runs the line-placing state machine:
    //   point 1 key   -> a 50% dot follows the mouse (snapped to building centres with smart lock)
    //   point 1 again -> lock the dot in as point 1, preview a 50% line to the cursor
    //   point 2 key   -> fix the line at full opacity
    //   both at once -> delete the line
    //   calibrate key -> set the grid for smart lock from the village's left and right corners
    //   swap key      -> switch between snapping to tile corners and tile centres
    //   delete key    -> (Backspace) delete the line or cancel calibration
    //   menu key      -> open the tray menu at the mouse
    //
    // Once a line (or calibration) is started, a right click does the same as the key for the current
    // step, and is kept from the game. Left clicks always go to the game.
    //
    // Smart lock (for Clash of Clans' Giant Arrow): point 1 snaps to the nearest building centre on
    // a calibrated grid, and point 2 sets the arrow's path through it. Corners are where 2x2/4x4
    // buildings are centred and tile centres where 3x3 ones are; a label at the top of the screen
    // shows which is in use.
    //
    // Everything is drawn on one monitor (chosen in the tray menu), and the hotkeys only work while
    // the mouse is on it, so on other monitors they type as normal.
    internal sealed class TrayApp : ApplicationContext
    {
        private enum Mode { None, Aiming, Placing, Placed, Calibrating }

        private sealed class KeyState
        {
            public bool Down;
            public int LastTick;
        }

        // Set while right clicks place points, on this monitor. The mouse hook runs on its own
        // thread, so this is never changed in place, only replaced whole.
        private sealed class ClickCapture
        {
            public readonly Rectangle Monitor;

            public ClickCapture(Rectangle monitor)
            {
                Monitor = monitor;
            }
        }

        private static readonly uint OwnProcessId = (uint)Process.GetCurrentProcess().Id;

        private const byte PreviewOpacity = 128; // 50%
        private const byte FullOpacity = 255;

        // A held key repeats at least once a second, so a "still down" key that has been quiet
        // for longer than this lost its key-up somewhere (e.g. to an elevated window).
        private const int StaleKeyMs = 1500;

        // Line thickness options in the tray menu, in pixels at 100% display scaling.
        private static readonly float[] ThicknessChoices = { 1f, 2f, 3f, 4f, 5f, 6f, 8f, 10f };

        private readonly Settings settings;
        private readonly OverlayWindow overlay;
        private readonly OverlayWindow hud; // the snap mode label, separate so it can sit over anything
        private readonly KeyboardHook hook;
        private readonly MouseHook mouseHook;
        private readonly System.Threading.SynchronizationContext uiThread;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem smartLockItem;
        private readonly Timer previewTimer;
        private readonly Timer topmostTimer;
        private readonly Timer hudTimer; // keeps the label up briefly after a swap
        private readonly Timer savedTimer; // keeps a newly saved grid up briefly
        private readonly KeyState point1Key = new KeyState();
        private readonly KeyState point2Key = new KeyState();
        private readonly KeyState calibrateKey = new KeyState();
        private readonly KeyState swapKey = new KeyState();
        private readonly KeyState deleteKey = new KeyState();
        private readonly KeyState menuKey = new KeyState();

        private Mode mode = Mode.None;
        private PointF point1;
        private Point point2;
        private Point lastCursor;
        private PointF lastAim;
        private bool smartLine;
        private bool hasLeftCorner; // calibrating, and the village's left corner has been clicked
        private PointF leftCorner;
        private MonitorMetrics metrics;
        private bool dialogOpen;
        private Icon trayImage;
        private volatile ClickCapture clickCapture; // null when no clicks are taken
        private volatile bool menuOpen;
        private IntPtr focusBeforeMenu; // the window to give focus back to after the menu hotkey

        public TrayApp()
        {
            settings = Settings.Load();
            overlay = new OverlayWindow();
            hud = new OverlayWindow();

            previewTimer = new Timer { Interval = 10 };
            previewTimer.Tick += delegate { UpdatePreview(false); };

            topmostTimer = new Timer { Interval = 2000 };
            topmostTimer.Tick += delegate
            {
                overlay.KeepOnTop();
                hud.KeepOnTop();
            };
            topmostTimer.Start();

            hudTimer = new Timer { Interval = 2000 };
            hudTimer.Tick += delegate
            {
                hudTimer.Stop();
                UpdateHud();
            };

            savedTimer = new Timer { Interval = 2000 };
            savedTimer.Tick += delegate
            {
                savedTimer.Stop();
                if (mode == Mode.None)
                    overlay.Hide();
            };

            menu = new ContextMenuStrip();
            smartLockItem = new ToolStripMenuItem("Smart lock", null, delegate { ToggleSmartLock(); });
            menu.Items.Add(smartLockItem);
            menu.Items.Add("Calibrate grid", null, delegate { StartCalibration(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Hotkeys...", null, delegate { ShowHotkeyDialog(); });
            menu.Items.Add("Colour...", null, delegate { ShowColourDialog(); });
            menu.Items.Add(CreateThicknessMenu());
            menu.Items.Add(CreateMonitorMenu());
            menu.Items.Add("Delete line", null, delegate { ClearLine(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });
            menu.Opening += delegate
            {
                foreach (ToolStripItem item in menu.Items)
                    item.Enabled = !dialogOpen || item.Text == "Exit";
                smartLockItem.Checked = settings.SmartLock;
            };
            menu.Opened += delegate { menuOpen = true; };
            menu.Closed += delegate
            {
                menuOpen = false;
                if (focusBeforeMenu == IntPtr.Zero)
                    return;
                // Once any clicked item has run, hand focus back to the game, unless it opened a dialog.
                IntPtr window = focusBeforeMenu;
                focusBeforeMenu = IntPtr.Zero;
                uiThread.Post(delegate
                {
                    if (!dialogOpen)
                        NativeMethods.SetForegroundWindow(window);
                }, null);
            };

            tray = new NotifyIcon { ContextMenuStrip = menu };
            UpdateTrayIcon();
            UpdateTrayText();
            tray.Visible = true;

            hook = new KeyboardHook(OnKey);

            // Creating the menu installed the UI thread's context, which clicks are passed back through.
            uiThread = System.Threading.SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            mouseHook = new MouseHook(OnMousePress);

            tray.ShowBalloonTip(4000, "Arrow Overlay is running",
                string.Format("Press {0} to start a line, then right-click (or {0}/{1}) to place each point. {2} deletes it, {3} opens the menu.",
                    KeyNames.Get(settings.Point1Key), KeyNames.Get(settings.Point2Key),
                    KeyNames.Get(settings.DeleteKey), KeyNames.Get(settings.MenuKey)),
                ToolTipIcon.Info);
        }

        // Called from the keyboard hook for every key event. Returns true to swallow the key.
        private bool OnKey(int vk, bool down)
        {
            if (down && dialogOpen)
                return false; // let keys reach the open dialog

            KeyState hotkey = vk == settings.Point1Key ? point1Key
                : vk == settings.Point2Key ? point2Key
                : vk == settings.CalibrateKey ? calibrateKey
                : vk == settings.SwapKey ? swapKey
                : vk == settings.MenuKey ? menuKey
                : null;
            if (hotkey != null)
                return OnHotkey(hotkey, down);

            if (vk == settings.DeleteKey)
                return OnDeleteKey(down);

            if (menuOpen && vk == (int)Keys.Escape)
            {
                if (down)
                    menu.Close();
                return true;
            }

            if (mode == Mode.Calibrating)
                return OnCalibrationKey((Keys)vk, down);
            return false;
        }

        // Hotkeys act once per press (auto-repeat is ignored), and only while the mouse is on the
        // overlay's monitor. Elsewhere they're left alone so they type as normal.
        private bool OnHotkey(KeyState state, bool down)
        {
            if (!down)
            {
                bool swallowedDown = state.Down;
                state.Down = false;
                return swallowedDown;
            }

            int now = Environment.TickCount;
            if (IsHeld(state, now))
            {
                state.LastTick = now;
                return true; // auto-repeat
            }
            if (!OverlayScreen.Bounds.Contains(Cursor.Position))
                return false;
            state.Down = true;
            state.LastTick = now;

            if (state == calibrateKey)
            {
                if (mode == Mode.Calibrating)
                    ClearLine(); // pressing it again cancels
                else
                    StartCalibration();
            }
            else if (state == swapKey)
                SwapSnapMode();
            else if (state == menuKey)
                ToggleMenu();
            else if (IsHeld(point1Key, now) && IsHeld(point2Key, now))
                ClearLine();
            else if (mode == Mode.Calibrating)
            {
                if (state == point1Key)
                    MarkCorner(Cursor.Position);
            }
            else if (state == point1Key && mode == Mode.Aiming)
                LockPoint1(Cursor.Position);
            else if (state == point1Key)
                StartAiming();
            else if (mode == Mode.Placing)
                SetPoint2(Cursor.Position);
            return true;
        }

        // Unlike the other hotkeys, the delete key (Backspace by default) is only taken while there's
        // something to delete and the mouse is on the overlay's monitor, so it still works as
        // Backspace everywhere else.
        private bool OnDeleteKey(bool down)
        {
            if (!down)
            {
                bool swallowedDown = deleteKey.Down;
                deleteKey.Down = false;
                return swallowedDown;
            }

            int now = Environment.TickCount;
            if (IsHeld(deleteKey, now))
            {
                deleteKey.LastTick = now;
                return true; // auto-repeat of a press that deleted something
            }
            if (mode == Mode.None || !OverlayScreen.Bounds.Contains(Cursor.Position))
                return false;
            deleteKey.Down = true;
            deleteKey.LastTick = now;
            ClearLine();
            return true;
        }

        // Opens the tray menu at the mouse, so it can be reached from a fullscreen game.
        private void ToggleMenu()
        {
            if (menu.Visible)
            {
                menu.Close();
                return;
            }
            focusBeforeMenu = NativeMethods.GetForegroundWindow();
            menu.Show(Cursor.Position);
            // As the tray icon does, so a click anywhere else closes the menu.
            NativeMethods.SetForegroundWindow(menu.Handle);
        }

        // Runs on the mouse hook's thread: decides from clickCapture alone whether to take the
        // click, and hands the work to the UI thread. Returns true to keep the click from the game.
        private bool OnMousePress(MouseButtons button, Point point)
        {
            // Backup for when the menu couldn't take focus: a click elsewhere still closes it.
            if (menuOpen && !IsShellOrOwnWindow(point))
                uiThread.Post(delegate { menu.Close(); }, null);

            ClickCapture capture = clickCapture;
            if (button != MouseButtons.Right || capture == null || !capture.Monitor.Contains(point) || IsShellOrOwnWindow(point))
                return false;
            uiThread.Post(delegate { OnPlaceClick(point); }, null);
            return true;
        }

        // The taskbar, its tray and this app's own menus and dialogs always get their clicks,
        // so the tray icon can still be right-clicked while a line is up.
        private static bool IsShellOrOwnWindow(Point point)
        {
            IntPtr window = NativeMethods.GetAncestor(
                NativeMethods.WindowFromPoint(new NativeMethods.POINT(point.X, point.Y)), NativeMethods.GA_ROOT);
            if (window == IntPtr.Zero)
                return false;

            uint processId;
            NativeMethods.GetWindowThreadProcessId(window, out processId);
            if (processId == OwnProcessId)
                return true;

            var className = new StringBuilder(64);
            NativeMethods.GetClassName(window, className, className.Capacity);
            string name = className.ToString();
            return name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd" || name == "NotifyIconOverflowWindow";
        }

        // A right click does what the point 1 or point 2 key would for the current step, at the click.
        private void OnPlaceClick(Point point)
        {
            if (mode == Mode.Aiming)
                LockPoint1(point);
            else if (mode == Mode.Placing)
                SetPoint2(point);
            else if (mode == Mode.Calibrating)
                MarkCorner(point);
        }

        private static bool IsHeld(KeyState state, int now)
        {
            return state.Down && unchecked(now - state.LastTick) < StaleKeyMs;
        }

        // First press of the point 1 key: a 50% dot follows the mouse, snapped with smart lock.
        private void StartAiming()
        {
            metrics = MonitorMetrics.For(OverlayScreen);
            smartLine = settings.SmartLock && settings.Grid.IsSet;
            SetMode(Mode.Aiming);
            UpdatePreview(true);
            previewTimer.Start();
        }

        // Second press (or a click): the dot becomes point 1 and the line preview starts.
        private void LockPoint1(Point at)
        {
            point1 = AimPoint(at);
            SetMode(Mode.Placing);
            UpdatePreview(true);
        }

        private PointF AimPoint(Point cursor)
        {
            return smartLine ? settings.Grid.Snap(cursor, settings.SnapCorners) : cursor;
        }

        // Point 2 fixes the line.
        private void SetPoint2(Point at)
        {
            point2 = at;
            previewTimer.Stop();
            SetMode(Mode.Placed);
            Redraw();
        }

        private void ClearLine()
        {
            previewTimer.Stop();
            SetMode(Mode.None);
            overlay.Hide();
        }

        private void SetMode(Mode newMode)
        {
            mode = newMode;
            bool placing = mode == Mode.Aiming || mode == Mode.Placing || mode == Mode.Calibrating;
            clickCapture = placing ? new ClickCapture(OverlayScreen.Bounds) : null;
            UpdateHud();
        }

        private void UpdatePreview(bool force)
        {
            Point cursor = Cursor.Position;
            if (!force && cursor == lastCursor)
                return;
            lastCursor = cursor;
            if (mode == Mode.Placing)
            {
                DrawLine(cursor, PreviewOpacity);
                return;
            }
            if (mode == Mode.Calibrating)
            {
                DrawCalibration();
                return;
            }

            // With smart lock the whole grid is redrawn, so only do it when the dot moves.
            PointF aim = AimPoint(cursor);
            if (!force && aim == lastAim)
                return;
            lastAim = aim;
            if (smartLine)
                Scenes.Aiming(overlay, metrics.Bounds, settings.Grid, aim, settings.Colour, LineWidth);
            else
                Scenes.Dot(overlay, metrics.Bounds, aim, settings.Colour, LineWidth, PreviewOpacity);
        }

        private void SwapSnapMode()
        {
            settings.SnapCorners = !settings.SnapCorners;
            settings.Save();
            hudTimer.Stop();
            hudTimer.Start();
            UpdateHud();
            if (mode == Mode.Aiming)
                UpdatePreview(true);
        }

        // The snap mode label at the top of the screen: shown while choosing point 1, and for a
        // moment after the swap key is pressed.
        private void UpdateHud()
        {
            bool show = (mode == Mode.Aiming && smartLine) || hudTimer.Enabled;
            if (!show)
            {
                hud.Hide();
                return;
            }
            MonitorMetrics monitor = MonitorMetrics.For(OverlayScreen);
            Scenes.Label(hud, monitor.Bounds, settings.SnapCorners ? "Mode: 2x2/4x4" : "Mode: 3x3", monitor.Scale);
            hud.KeepOnTop();
        }

        private void Redraw()
        {
            if (mode == Mode.Placed)
                DrawLine(point2, FullOpacity);
            else if (mode == Mode.Aiming || mode == Mode.Placing)
                UpdatePreview(true);
            else if (mode == Mode.Calibrating)
                DrawCalibration();
        }

        // Draws the line from point 1 through towards: the cursor while placing point 2, then point 2.
        private void DrawLine(Point towards, byte opacity)
        {
            Scenes.Line(overlay, metrics.Bounds, point1, towards, settings.Colour, LineWidth, opacity, smartLine);
        }

        private float LineWidth
        {
            get { return settings.LineThickness * metrics.Scale; }
        }

        private void ToggleSmartLock()
        {
            if (!settings.SmartLock && !settings.Grid.IsSet)
            {
                StartCalibration(); // saving the grid turns smart lock on
                return;
            }
            settings.SmartLock = !settings.SmartLock;
            settings.Save();
        }

        // Calibrating: click (or press the point 1 key on) the village's left corner, then its right.
        private void StartCalibration()
        {
            previewTimer.Stop();
            savedTimer.Stop();
            metrics = MonitorMetrics.For(OverlayScreen);
            hasLeftCorner = false;
            SetMode(Mode.Calibrating);
            DrawCalibration();
        }

        // Esc cancels calibration. Returns true for the keys it uses so the game doesn't get them.
        private bool OnCalibrationKey(Keys key, bool down)
        {
            if (key != Keys.Escape)
                return false;
            if (down)
                ClearLine();
            return true;
        }

        private void MarkCorner(Point at)
        {
            if (!hasLeftCorner)
            {
                leftCorner = at;
                hasLeftCorner = true;
                DrawCalibration();
                previewTimer.Start(); // the grid preview stretches with the mouse to the right corner
                return;
            }
            if (Math.Abs(at.X - leftCorner.X) < IsoGrid.MinCornerGap)
                return; // too close to the left corner to make a grid

            previewTimer.Stop();
            settings.Grid = IsoGrid.FromCorners(leftCorner, at);
            settings.SmartLock = true;
            settings.Save();
            SetMode(Mode.None);

            // Show the saved grid for a moment.
            Scenes.Calibration(overlay, metrics.Bounds, "Grid saved", null, settings.Grid, FullOpacity,
                settings.Colour, LineWidth, metrics.Scale);
            savedTimer.Start();
        }

        private void DrawCalibration()
        {
            IsoGrid? preview = null;
            Point cursor = Cursor.Position;
            if (hasLeftCorner && Math.Abs(cursor.X - leftCorner.X) >= IsoGrid.MinCornerGap)
                preview = IsoGrid.FromCorners(leftCorner, cursor);
            string prompt = string.Format("Right-click the {0} corner of the village  ({1} to cancel)",
                hasLeftCorner ? "right" : "left", KeyNames.Get(settings.DeleteKey));
            Scenes.Calibration(overlay, metrics.Bounds, prompt, hasLeftCorner ? leftCorner : (PointF?)null, preview,
                PreviewOpacity, settings.Colour, LineWidth, metrics.Scale);
        }

        // The monitor chosen in the tray menu, or the main one.
        private Screen OverlayScreen
        {
            get
            {
                foreach (Screen screen in Screen.AllScreens)
                {
                    if (screen.DeviceName == settings.Monitor)
                        return screen;
                }
                return Screen.PrimaryScreen;
            }
        }

        private ToolStripMenuItem CreateMonitorMenu()
        {
            var monitorMenu = new ToolStripMenuItem("Monitor");
            monitorMenu.DropDownItems.Add(new ToolStripMenuItem()); // replaced when opened
            monitorMenu.DropDownOpening += delegate
            {
                monitorMenu.DropDownItems.Clear();
                string current = OverlayScreen.DeviceName;
                foreach (Screen screen in Screen.AllScreens)
                {
                    var item = new ToolStripMenuItem(MonitorName(screen), null, delegate { SetMonitor(screen); });
                    item.Checked = screen.DeviceName == current;
                    monitorMenu.DropDownItems.Add(item);
                }
            };
            return monitorMenu;
        }

        // e.g. "Display 2 - 1920x1080 (main)"
        private static string MonitorName(Screen screen)
        {
            string device = screen.DeviceName;
            int at = device.IndexOf("DISPLAY", StringComparison.OrdinalIgnoreCase);
            string number = at >= 0 ? device.Substring(at + "DISPLAY".Length) : device;
            return string.Format("Display {0} - {1}x{2}{3}",
                number, screen.Bounds.Width, screen.Bounds.Height, screen.Primary ? " (main)" : "");
        }

        private void SetMonitor(Screen screen)
        {
            settings.Monitor = screen.DeviceName;
            settings.Save();
            if (mode == Mode.Calibrating)
                StartCalibration();
            else
                ClearLine();
        }


        private void ShowHotkeyDialog()
        {
            dialogOpen = true;
            try
            {
                using (var form = new HotkeyForm(Settings.HotkeyLabels, settings.Hotkeys, Settings.DefaultHotkeys))
                {
                    if (form.ShowDialog() != DialogResult.OK)
                        return;
                    settings.Hotkeys = form.ChosenKeys;
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
            hudTimer.Stop();
            savedTimer.Stop();
            hook.Dispose();
            mouseHook.Dispose();
            overlay.Dispose();
            hud.Dispose();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            if (trayImage != null)
                trayImage.Dispose();
            base.ExitThreadCore();
        }
    }
}
