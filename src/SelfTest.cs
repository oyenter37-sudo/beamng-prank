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

            Perf("main menu", f => { f.TestScreen(AppForm.Screen.Main); f.TestHover("freeroam"); });
            Perf("maps", f => { f.TestScreen(AppForm.Screen.Maps); f.TestHover("map:6"); });
            Perf("vehicles", f => { f.TestScreen(AppForm.Screen.Vehicles); f.TestHover("veh:2"); });
            PresentCheck();

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

        /// <summary>Verifies the native GDI blit used to put the frame on screen.</summary>
        static void PresentCheck()
        {
            try
            {
                using (AppForm f = new AppForm())
                {
                    f.TestScreen(AppForm.Screen.Main);
                    f.TestHover("freeroam");

                    using (Bitmap shot = new Bitmap(960, 540))
                    {
                        if (!f.TestPresent(shot))
                        {
                            _fail++;
                            Out("FAIL present: StretchBlt returned false");
                            return;
                        }
                        Save(shot, "11-present");

                        using (Bitmap refb = new Bitmap(960, 540))
                        {
                            using (Graphics g = Graphics.FromImage(refb))
                            {
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                g.DrawImage(f.Buffer, 0, 0, 960, 540);
                            }
                            double sum = 0;
                            int n = 0;
                            for (int y = 0; y < 540; y += 3)
                                for (int x = 0; x < 960; x += 3)
                                {
                                    Color a = shot.GetPixel(x, y), b = refb.GetPixel(x, y);
                                    sum += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                                    n += 3;
                                }
                            double mean = sum / n;
                            Out(string.Format("present check: mean diff {0:0.0}", mean));
                            if (mean > 14)
                            {
                                _fail++;
                                Out("FAIL present: blit does not match the rendered frame");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _fail++;
                Out("FAIL present: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        static void Perf(string label, Action<AppForm> setup)
        {
            using (AppForm f = new AppForm())
            {
                setup(f);
                f.RenderScene();                 // warm the asset cache
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                const int n = 30;
                for (int i = 0; i < n; i++) f.RenderScene();
                sw.Stop();
                Out(string.Format("perf {0}: {1:0.0} ms/frame", label,
                    sw.Elapsed.TotalMilliseconds / n));
            }
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
