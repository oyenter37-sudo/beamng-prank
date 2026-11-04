using System;
using System.Linq;
using System.Windows.Forms;

namespace BeamSetup
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args != null && args.Length >= 2 &&
                string.Equals(args[0], "--shot", StringComparison.OrdinalIgnoreCase))
            {
                SetupForm.Shots(args[1]);
                return;
            }

            bool uninstall = args != null &&
                args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase));

            Application.Run(new SetupForm(uninstall));
        }
    }
}
