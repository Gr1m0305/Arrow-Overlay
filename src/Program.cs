using System;
using System.Threading;
using System.Windows.Forms;

namespace ArrowOverlay
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // Two copies would both react to every hotkey, so only allow one.
            bool firstInstance;
            using (var mutex = new Mutex(true, "ArrowOverlay.SingleInstance", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("Arrow Overlay is already running. Look for its icon in the system tray.",
                        "Arrow Overlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }
}
