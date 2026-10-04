using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ArrowOverlay
{
    // Global low-level keyboard hook. Unlike RegisterHotKey it sees key-up events and
    // plain (unmodified) keys, which is what lets us detect two keys held together.
    internal sealed class KeyboardHook : IDisposable
    {
        // Return true to swallow the key so the focused application never sees it.
        public delegate bool KeyHandler(int vk, bool down);

        private readonly KeyHandler handler;
        private readonly NativeMethods.LowLevelHookProc proc; // kept alive so the GC can't collect it
        private IntPtr hook;

        public KeyboardHook(KeyHandler handler)
        {
            this.handler = handler;
            proc = HookProc;
            hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, proc, NativeMethods.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                bool down = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
                bool up = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;
                if (down || up)
                {
                    var info = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                    try
                    {
                        if (handler((int)info.vkCode, down))
                            return new IntPtr(1);
                    }
                    catch (Exception)
                    {
                        // An exception must never unwind into the native hook chain.
                    }
                }
            }
            return NativeMethods.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
        }
    }
}
