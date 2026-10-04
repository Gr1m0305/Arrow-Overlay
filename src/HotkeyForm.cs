using System;
using System.Drawing;
using System.Windows.Forms;

namespace ArrowOverlay
{
    // Dialog for choosing the two hotkeys. Click a box, then press the key you want.
    internal sealed class HotkeyForm : Form
    {
        private readonly HotkeyBox point1Box;
        private readonly HotkeyBox point2Box;

        public HotkeyForm(int point1Key, int point2Key)
        {
            Text = "Arrow Overlay - Hotkeys";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            point1Box = new HotkeyBox { KeyCode = point1Key };
            point2Box = new HotkeyBox { KeyCode = point2Key };

            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Dock = DockStyle.Fill,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            layout.Controls.Add(MakeLabel("Set point 1:"), 0, 0);
            layout.Controls.Add(point1Box, 1, 0);
            layout.Controls.Add(MakeLabel("Set point 2:"), 0, 1);
            layout.Controls.Add(point2Box, 1, 1);

            var hint = new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 8, 3, 3),
                Text = "Click a box, then press the key you want to use.\nPress both keys together to delete the line.",
            };
            layout.Controls.Add(hint, 0, 2);
            layout.SetColumnSpan(hint, 2);

            var ok = new Button { Text = "OK", AutoSize = true };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var defaults = new Button { Text = "Defaults", AutoSize = true };
            ok.Click += OnOk;
            defaults.Click += delegate
            {
                point1Box.KeyCode = Settings.DefaultPoint1Key;
                point2Box.KeyCode = Settings.DefaultPoint2Key;
            };

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 12, 0, 0),
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(defaults);
            layout.Controls.Add(buttons, 0, 3);
            layout.SetColumnSpan(buttons, 2);

            Controls.Add(layout);
            CancelButton = cancel;
            ActiveControl = ok; // so a stray key press doesn't immediately rebind point 1
        }

        public int Point1Key { get { return point1Box.KeyCode; } }

        public int Point2Key { get { return point2Box.KeyCode; } }

        private static Label MakeLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left };
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (point1Box.KeyCode == point2Box.KeyCode)
            {
                MessageBox.Show(this, "The two hotkeys must be different keys.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }

    // Read-only text box that captures the next key pressed while it has focus.
    internal sealed class HotkeyBox : TextBox
    {
        private int keyCode;

        public HotkeyBox()
        {
            ReadOnly = true;
            BackColor = SystemColors.Window;
            ShortcutsEnabled = false;
            TextAlign = HorizontalAlignment.Center;
            Width = 140;
            Anchor = AnchorStyles.Left | AnchorStyles.Right;
        }

        public int KeyCode
        {
            get { return keyCode; }
            set
            {
                keyCode = value;
                Text = KeyNames.Get(value);
            }
        }

        // Claim every key (Tab, arrows, Enter...) instead of letting the dialog handle it.
        protected override bool IsInputKey(Keys keyData)
        {
            return true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            // Modifier keys alone aren't supported: the hook reports left/right-specific codes for them.
            Keys key = e.KeyCode;
            if (key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
                return;
            KeyCode = (int)key;
        }
    }

    internal static class KeyNames
    {
        public static string Get(int vk)
        {
            var key = (Keys)vk;
            if (key == Keys.Space)
                return "Space";

            // Numpad keys map to the same characters as the main keys, so keep their enum names.
            bool numpad = vk >= (int)Keys.NumPad0 && vk <= (int)Keys.Divide;
            if (!numpad)
            {
                // Shows "[" rather than "OemOpenBrackets", using the current keyboard layout.
                uint ch = NativeMethods.MapVirtualKey((uint)vk, NativeMethods.MAPVK_VK_TO_CHAR) & 0xFFFF;
                if (ch > 32 && ch != 127)
                    return ((char)ch).ToString();
            }
            return key.ToString();
        }
    }
}
