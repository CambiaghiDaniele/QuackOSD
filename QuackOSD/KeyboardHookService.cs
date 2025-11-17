using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuackOSD
{
    /// <summary>
    /// Gestisce l'hook della tastiera di basso livello (WinAPI) per intercettare
    /// i tasti media a livello di sistema.
    /// </summary>
    public class KeyboardHookService
    {
        // --- Costanti WinAPI ---
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const int VK_MEDIA_NEXT_TRACK = 0xB0;
        private const int VK_MEDIA_PREV_TRACK = 0xB1;
        private const int VK_MEDIA_STOP = 0xB2;

        // --- Variabili Hook ---
        // 'static' è fondamentale perché il GC non elimini il delegate
        // mentre l'hook è attivo.
        private static LowLevelKeyboardProc _proc;
        private static IntPtr _hookID = IntPtr.Zero;

        // --- Evento Pubblico ---
        /// <summary>
        /// Scatta quando un tasto media (Play, Pausa, Next, Prev, Stop) viene premuto
        /// in qualsiasi punto del sistema.
        /// </summary>
        public event EventHandler MediaKeyPressed;

        public KeyboardHookService()
        {
            // Assegniamo il delegate nel costruttore
            _proc = HookCallback;
        }

        /// <summary>
        /// Installa l'hook.
        /// </summary>
        public void Start()
        {
            if (_hookID == IntPtr.Zero)
            {
                _hookID = SetHook(_proc);
            }
        }

        /// <summary>
        /// Rimuove l'hook della tastiera in modo sicuro.
        /// </summary>
        public void Stop()
        {
            if (_hookID != IntPtr.Zero)
            {
                // Ignora eventuali errori se l'hook è già stato rimosso
                try { UnhookWindowsHookEx(_hookID); }
                catch (Exception ex) { Debug.WriteLine($"Errore durante Unhook: {ex.Message}"); }
                _hookID = IntPtr.Zero;
            }
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        /// <summary>
        /// Il metodo "callback" che riceve tutte le pressioni dei tasti dal sistema operativo.
        /// </summary>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                // Controlla se è un tasto che ci interessa
                if (vkCode == VK_MEDIA_PLAY_PAUSE ||
                    vkCode == VK_MEDIA_NEXT_TRACK ||
                    vkCode == VK_MEDIA_PREV_TRACK ||
                    vkCode == VK_MEDIA_STOP)
                {
                    // Lancia il nostro evento C# pulito
                    MediaKeyPressed?.Invoke(this, EventArgs.Empty);
                }
            }
            // Passa il messaggio al prossimo hook nella catena
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        #region P/Invoke Imports (Spostati da MainWindow)

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        #endregion
    }
}