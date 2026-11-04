using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace BeamPrank
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            bool owned;
            using (Mutex m = new Mutex(true, "BeamNG.drive.9f2c1a", out owned))
            {
                if (!owned)
                    return;

                try { SetProcessDPIAware(); }
                catch { }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // CI hook: render every screen to PNG instead of showing the game.
                if (args != null && args.Length > 0 &&
                    string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
                {
                    string dir = args.Length > 1 ? args[1] : "shots";
                    Environment.ExitCode = SelfTest.Run(dir);
                    return;
                }

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate { Environment.Exit(0); };
                AppDomain.CurrentDomain.UnhandledException += delegate { Environment.Exit(0); };
                Application.Run(new AppForm());
            }
        }
    }
}
