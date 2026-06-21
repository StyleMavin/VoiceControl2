using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace VoskCompanion {
    static class Program {
        // Single-instance guard. A named mutex held for the whole app lifetime, plus a
        // process-name backstop to catch the rapid-double-click race where two processes
        // start before either has created the mutex.
        private static Mutex _instanceMutex;
        private const string MutexName = "VoskCompanion_SingleInstance_v1";

        [STAThread]
        static void Main() {
            // Backstop: if another VoskCompanion is already running, bail immediately.
            // (Catches the case where the mutex check races during fast multi-clicks.)
            try {
                var me = Process.GetCurrentProcess();
                var others = Process.GetProcessesByName(me.ProcessName);
                if (others.Length > 1) {
                    // Another instance exists — exit quietly so we never open a 2nd mic.
                    return;
                }
            }
            catch { /* if the check fails, fall through to the mutex */ }

            bool createdNew = false;
            try {
                _instanceMutex = new Mutex(true, MutexName, out createdNew);
            }
            catch {
                createdNew = true; // if the mutex can't be created, don't hard-block startup
            }

            if (!createdNew) {
                MessageBox.Show(
                    "VoskCompanion is already running.\n\nLook in the system tray (the ^ near the clock) and double-click its icon to open the window.",
                    "VoskCompanion already running",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            finally {
                try { _instanceMutex?.ReleaseMutex(); } catch { }
                try { _instanceMutex?.Dispose(); } catch { }
            }
        }
    }
}
