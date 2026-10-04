using System;
using System.Drawing;
using System.Windows.Forms;

namespace ArrowOverlay
{
    // Dialog for choosing the hotkeys: one row per label. Click a box, then press the key you want.
    internal sealed class HotkeyForm : Form
    {
        private readonly HotkeyBox[] boxes;

        public HotkeyForm(string[] labels, int[] keys, int[] defaultKeys)
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

            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Dock = DockStyle.Fill,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            boxes = new HotkeyBox[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                boxes[i] = new HotkeyBox { KeyCode = keys[i] };
                layout.Controls.Add(MakeLabel(labels[i]), 0, i);
                layout.Controls.Add(boxes[i], 1, i);
            }

            var hint = new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 8, 3, 3),
                Text = "Click a box, then press the key you want to use.\nHotkeys are ignored while Alt, Ctrl or Windows is held.",
            };
            layout.Controls.Add(hint, 0, labels.Length);
            layout.SetColumnSpan(hint, 2);

            var ok = new Button { Text = "OK", AutoSize = true };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var defaults = new Button { Text = "Defaults", AutoSize = true };
            ok.Click += OnOk;
            defaults.Click += delegate
            {
                for (int i = 0; i < boxes.Length; i++)
                    boxes[i].KeyCode = defaultKeys[i];
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
            layout.Controls.Add(buttons, 0, labels.Length + 1);
            layout.SetColumnSpan(buttons, 2);

            Controls.Add(layout);
            CancelButton = cancel;
            ActiveControl = ok; // so a stray key press doesn't immediately rebind the first key
        }

        // The chosen keys, in the same order as the labels.
        public int[] ChosenKeys
        {
            get
            {
                var keys = new int[boxes.Length];
                for (int i = 0; i < boxes.Length; i++)
                    keys[i] = boxes[i].KeyCode;
                return keys;
            }
        }

        private static Label MakeLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left };
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (!Settings.AllDifferent(ChosenKeys))
            {
                MessageBox.Show(this, "Each hotkey must be a different key.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            if (key == Keys.Back)
                return "Backspace";

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
