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
                try { SetProcessDPIAware(); }
                catch { }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (!owned)
                {
                    // clicking the shortcut twice is not how you get two copies of a game
                    try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
                    MessageBox.Show(
                        "BeamNG.drive is already running.\r\n\r\n" +
                        "Only one instance of the game can be launched at a time.",
                        "BeamNG.drive", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // CI hook: render every screen to PNG instead of showing the game.
                if (args != null && args.Length > 0 &&
                    string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
                {
                    string dir = args.Length > 1 ? args[1] : "shots";
                    RunState.CurrentRun = 1;
                    Environment.ExitCode = SelfTest.Run(dir);
                    return;
                }

                // every launch is counted; later launches occasionally fail even before the menu
                RunState.CurrentRun = RunState.NextRun();
                if (RunState.ShouldFailStartup(RunState.CurrentRun))
                {
                    RunState.WriteCrashArtifacts(RunState.ReasonFor(CrashKind.StartupError));
                    try { System.Media.SystemSounds.Hand.Play(); } catch { }
                    using (CrashForm f = new CrashForm(CrashKind.StartupError, RunState.IsModernWindows))
                        f.ShowDialog();
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
