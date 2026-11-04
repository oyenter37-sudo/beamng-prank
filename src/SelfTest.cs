using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace BeamPrank
{
    /// <summary>Renders every screen to PNG so the CI can verify the build visually.</summary>
    static class SelfTest
    {
        static int _ok, _fail;
        static string _dir;
        static StreamWriter _log;

        static void Out(string s)
        {
            Console.WriteLine(s);
            if (_log != null) _log.WriteLine(s);
        }

        public static int Run(string dir)
        {
            _dir = dir;
            Directory.CreateDirectory(_dir);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _log = new StreamWriter(Path.Combine(_dir, "_report.txt"));
            Out("BeamNG.drive self-test");
            Out("---------------------------------------------");

            using (AppForm f = new AppForm())
            {
                f.TestScreen(AppForm.Screen.Main);
                f.TestHover("freeroam");
                Shot(f, "01-main-menu");

                f.TestScreen(AppForm.Screen.Maps);
                f.TestSelect(9, -1);
                f.TestHover("map:6");
                Shot(f, "02-map-select");

                f.TestHover("map:9");
                Shot(f, "03-map-select-selected");

                f.TestScreen(AppForm.Screen.Vehicles);
                f.TestSelect(9, 2);
                f.TestHover("veh:2");
                Shot(f, "04-vehicle-select");

                f.TestVehScroll(520);
                f.TestHover("veh:22");
                Shot(f, "05-vehicle-scrolled");

                f.TestScreen(AppForm.Screen.Loading);
                foreach (float t in new float[] { 0f, 1.2f, 2.4f, 3.9f })
                {
                    f.TestLoading(t);
                    Shot(f, string.Format("06-loading-{0:0.0}", t));
                }

                f.TestScreen(AppForm.Screen.Page);
                f.TestPage("options");
                Shot(f, "07-page-options");

                f.TestPage("credits");
                Shot(f, "08-page-credits");

                f.TestScreen(AppForm.Screen.Main);
                f.TestHover("more");
                Shot(f, "09-main-more-hover");
            }

            using (CrashForm c = new CrashForm())
            {
                c.TestTime(4.0f);
                Bitmap b = c.Shot();
                Save(b, "10-crash-dialog");
                b.Dispose();
            }

            Out("---------------------------------------------");
            Out(string.Format("{0} rendered, {1} failed", _ok, _fail));
            _log.Flush();
            _log.Dispose();
            return _fail == 0 ? 0 : 1;
        }

        static void Shot(AppForm f, string name)
        {
            try
            {
                f.RenderScene();
                Bitmap b = f.Buffer;
                Save(b, name);
            }
            catch (Exception ex)
            {
                _fail++;
                Out(string.Format("FAIL {0}: {1}", name, ex.GetType().Name + " " + ex.Message));
            }
        }

        static void Save(Bitmap b, string name)
        {
            string path = Path.Combine(_dir, name + ".png");
            b.Save(path, ImageFormat.Png);
            FileInfo fi = new FileInfo(path);
            if (fi.Length < 8000)
            {
                _fail++;
                Out(string.Format("FAIL {0}: only {1} bytes", name, fi.Length));
                return;
            }
            _ok++;
            Out(string.Format("  ok  {0,-24} {1}x{2}  {3} KB", name + ".png", b.Width, b.Height, fi.Length / 1024));
        }
    }
}
