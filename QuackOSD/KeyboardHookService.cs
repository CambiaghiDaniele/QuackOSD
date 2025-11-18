using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace QuackOSD
{
    public class KeyboardHookService : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        private const int VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const int VK_MEDIA_NEXT_TRACK = 0xB0;
        private const int VK_MEDIA_PREV_TRACK = 0xB1;
        private const int VK_MEDIA_STOP = 0xB2;

        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;

        public event EventHandler<MediaKeyEventArgs> MediaKeyPressed;

        public KeyboardHookService()
        {
            _proc = HookCallback;
        }

        public void Start()
        {
            if (_hookID != IntPtr.Zero)
                return;

            _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);

            if (_hookID == IntPtr.Zero)
            {
                Debug.WriteLine($"SetWindowsHookEx failed. Error {Marshal.GetLastWin32Error()}");
            }
        }

        public void Stop()
        {
            if (_hookID == IntPtr.Zero)
                return;

            try { UnhookWindowsHookEx(_hookID); }
            catch (Exception ex) { Debug.WriteLine("Error unhook: " + ex.Message); }

            _hookID = IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                if (vkCode == VK_MEDIA_PLAY_PAUSE ||
                    vkCode == VK_MEDIA_NEXT_TRACK ||
                    vkCode == VK_MEDIA_PREV_TRACK ||
                    vkCode == VK_MEDIA_STOP)
                {
                    Task.Run(() =>
                        MediaKeyPressed?.Invoke(this, new MediaKeyEventArgs(vkCode))
                    );
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
        }

        public class MediaKeyEventArgs : EventArgs
        {
            public int KeyCode { get; }
            public MediaKeyEventArgs(int keyCode) => KeyCode = keyCode;
        }

        // WinAPI
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    }
}
