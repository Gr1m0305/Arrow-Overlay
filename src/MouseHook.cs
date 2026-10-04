using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ArrowOverlay
{
    // Global low-level mouse hook for left and right clicks. It runs on its own thread so that
    // drawing on the UI thread can never make the cursor lag. Only presses reach the handler; if
    // it swallows a press, the matching release is swallowed too so no program sees half a click.
    internal sealed class MouseHook : IDisposable
    {
        // Called on the hook's thread for each left or right press, with the screen point.
        // Return true to keep the click from every other program.
        public delegate bool PressHandler(MouseButtons button, Point point);

        private readonly PressHandler handler;
        private readonly NativeMethods.LowLevelHookProc proc; // kept alive so the GC can't collect it
        private readonly Thread thread;
        private uint threadId;
        private IntPtr hook;

        // Only touched on the hook thread.
        private bool leftSwallowed;
        private bool rightSwallowed;

        public MouseHook(PressHandler handler)
        {
            this.handler = handler;
            proc = HookProc;

            int error = 0;
            using (var started = new ManualResetEvent(false))
            {
                thread = new Thread(() =>
                {
                    threadId = NativeMethods.GetCurrentThreadId();
                    hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, proc, NativeMethods.GetModuleHandle(null), 0);
                    error = Marshal.GetLastWin32Error();
                    started.Set();
                    if (hook == IntPtr.Zero)
                        return;

                    // Low-level hooks are called while their thread waits for messages.
                    NativeMethods.MSG msg;
                    while (NativeMethods.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                    {
                    }
                    NativeMethods.UnhookWindowsHookEx(hook);
                });
                thread.IsBackground = true;
                thread.Name = "Mouse hook";
                thread.Start();
                started.WaitOne();
            }
            if (hook == IntPtr.Zero)
                throw new Win32Exception(error);
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                bool left = msg == NativeMethods.WM_LBUTTONDOWN || msg == NativeMethods.WM_LBUTTONUP;
                bool right = msg == NativeMethods.WM_RBUTTONDOWN || msg == NativeMethods.WM_RBUTTONUP;
                bool down = msg == NativeMethods.WM_LBUTTONDOWN || msg == NativeMethods.WM_RBUTTONDOWN;
                if ((left || right) && Swallow(left, down, lParam))
                    return new IntPtr(1);
            }
            return NativeMethods.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        private bool Swallow(bool left, bool down, IntPtr lParam)
        {
            if (!down)
            {
                bool swallowedPress = left ? leftSwallowed : rightSwallowed;
                if (left)
                    leftSwallowed = false;
                else
                    rightSwallowed = false;
                return swallowedPress;
            }

            var info = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
            bool swallow;
            try
            {
                swallow = handler(left ? MouseButtons.Left : MouseButtons.Right, new Point(info.pt.X, info.pt.Y));
            }
            catch (Exception)
            {
                swallow = false; // an exception must never unwind into the native hook chain
            }
            if (left)
                leftSwallowed = swallow;
            else
                rightSwallowed = swallow;
            return swallow;
        }

        public void Dispose()
        {
            if (thread.IsAlive)
            {
                NativeMethods.PostThreadMessage(threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                thread.Join(1000);
            }
        }
    }
}
