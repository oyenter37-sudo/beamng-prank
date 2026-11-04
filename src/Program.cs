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
        static void Main()
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
                Application.Run(new AppForm());
            }
        }
    }
}
