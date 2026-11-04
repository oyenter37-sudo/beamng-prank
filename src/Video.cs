using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace BeamPrank
{
    /// <summary>
    /// Renders a scripted demo of the whole prank to JPEG frames. The CI feeds the
    /// frames to ffmpeg, so the video is produced by the real UI code, not a mock.
    /// </summary>
    class Video
    {
        const int Fps = 30;
        const int OutW = 1280, OutH = 720;

        const float FadeMain = 2.30f;
        const float FadeMaps = 4.90f;
        const float FadeVeh = 7.50f;
        const float DemoLoad = 24.0f;                    // mid-range of the 20-30 s random load
        const float LoadLen = DemoLoad + AppForm.LoadHold;
        const float CrashLen = 5.1f;
        const float CrashAt = FadeVeh + LoadLen;
        const float Duration = CrashAt + CrashLen;

        enum Scene { None, Main, Maps, Vehicles, Loading, Crash }

        Scene _scene = Scene.None;
        readonly AppForm _app;
        readonly CrashForm _crash;
        static ImageCodecInfo _jpeg;

        Video(AppForm app, CrashForm crash)
        {
            _app = app;
            _crash = crash;
        }

        public static int Run(string dir)
        {
            Directory.CreateDirectory(dir);
            _jpeg = JpegEncoder();

            using (AppForm app = new AppForm())
            using (CrashForm crash = new CrashForm(CrashKind.StoppedWorking, RunState.IsModernWindows))
            using (Bitmap frame = new Bitmap(OutW, OutH))
            using (Graphics g = Graphics.FromImage(frame))
            {
                Video v = new Video(app, crash);
                int total = (int)(Duration * Fps);
                for (int i = 0; i < total; i++)
                {
                    v.Render(i / (float)Fps, g);
                    frame.Save(Path.Combine(dir, string.Format("f_{0:00000}.jpg", i)), _jpeg, JpegQuality());
                    if (i % 90 == 0) Console.WriteLine("video frame " + i + " / " + total);
                }
            }
            return 0;
        }

        void Render(float t, Graphics g)
        {
            if (t < FadeMain)
            {
                SetScene(Scene.Main);
                RenderMain(t);
            }
            else if (t < FadeMaps)
            {
                SetScene(Scene.Maps);
                RenderMaps(t);
            }
            else if (t < FadeVeh)
            {
                SetScene(Scene.Vehicles);
                RenderVehicles(t);
            }
            else if (t < CrashAt)
            {
                SetScene(Scene.Loading);
                RenderLoading(t);
            }
            else
            {
                SetScene(Scene.Crash);
                RenderCrash(t);
            }

            _app.RenderScene();
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(_app.Buffer, 0, 0, OutW, OutH);

            if (_scene == Scene.Crash) DrawCrashLayer(g, t);
        }

        void SetScene(Scene s)
        {
            if (_scene == s) return;
            _scene = s;

            switch (s)
            {
                case Scene.Main:
                    _app.TestScreen(AppForm.Screen.Main);
                    break;
                case Scene.Maps:
                    _app.TestScreen(AppForm.Screen.Maps);
                    _app.TestSelect(9, -1);
                    break;
                case Scene.Vehicles:
                    _app.TestScreen(AppForm.Screen.Vehicles);
                    _app.TestSelect(3, 2);
                    break;
                case Scene.Loading:
                    _app.TestScreen(AppForm.Screen.Loading);
                    _app.TestLoadAnim(DemoLoad);
                    _app.TestCursorHide();
                    break;
            }
        }

        // ------------------------------------------------------------------ menu
        void RenderMain(float t)
        {
            float hover = Ramp(t, 0.55f, 0.95f);
            _app.TestHoverLevel("freeroam", hover);

            PointF m = Between(new PointF(1640f, 900f), new PointF(958f, 535f),
                Ease(Ramp(t, 0.05f, 0.95f)));
            _app.TestCursor(m.X, m.Y);

            _app.TestFade(Ramp(t, FadeMain - 0.35f, FadeMain));
        }

        // ------------------------------------------------------------------ maps
        void RenderMaps(float t)
        {
            // hover a real card (slot 6, Derby Arenas); the selected default map
            // stays Grid, Small, Pure so the baked info panel still matches
            _app.TestHoverLevel("map:6", Ramp(t, 2.90f, 3.28f));
            _app.TestHoverLevel("maps_start", Ramp(t, 3.95f, 4.30f));

            PointF m;
            if (t < 3.45f)
                m = Between(new PointF(960f, 540f), new PointF(944f, 447f),
                    Ease(Ramp(t, FadeMain, 3.35f)));
            else
                m = Between(new PointF(944f, 447f), new PointF(1300f, 1006f),
                    Ease(Ramp(t, 3.45f, 4.32f)));
            _app.TestCursor(m.X, m.Y);

            float fade = 1f - Ramp(t, FadeMain, FadeMain + 0.32f);
            fade = Math.Max(fade, Ramp(t, FadeMaps - 0.38f, FadeMaps));
            _app.TestFade(fade);
        }

        // -------------------------------------------------------------- vehicles
        void RenderVehicles(float t)
        {
            if (t > 5.98f) _app.TestSelect(3, 7);

            _app.TestHoverLevel("veh:7", Ramp(t, 5.55f, 5.95f));
            _app.TestHoverLevel("veh_start", Ramp(t, 6.38f, 6.74f));

            PointF m;
            if (t < 5.98f)
                m = Between(new PointF(960f, 540f), new PointF(749f, 484f),
                    Ease(Ramp(t, FadeMaps, 5.70f)));
            else
                m = Between(new PointF(749f, 484f), new PointF(1285f, 90f),
                    Ease(Ramp(t, 5.98f, 6.80f)));
            _app.TestCursor(m.X, m.Y);

            float fade = 1f - Ramp(t, FadeMaps, FadeMaps + 0.32f);
            fade = Math.Max(fade, Ramp(t, FadeVeh - 0.36f, FadeVeh));
            _app.TestFade(fade);
        }

        // --------------------------------------------------------------- loading
        void RenderLoading(float t)
        {
            float loadT = Math.Min(t - FadeVeh, LoadLen);
            _app.TestLoading(loadT);
            _app.TestCursorHide();

            // the first frames fade in from black
            _app.TestFade(1f - Ramp(t, FadeVeh, FadeVeh + 0.34f));
        }

        // ----------------------------------------------------------------- crash
        void RenderCrash(float t)
        {
            _app.TestLoadAnim(DemoLoad);
            _app.TestLoading(LoadLen);
            _app.TestCursorHide();
            _app.TestFade(0f);

            float ct = t - CrashAt;
            _crash.TestTime(ct);
            _crash.TestHoverPrimary(ct > 2.1f);
        }

        void DrawCrashLayer(Graphics g, float t)
        {
            float ct = t - CrashAt;
            float a = Ramp(ct, 0.65f, 0.95f);          // dialog pops in after the freeze
            if (a <= 0.01f) return;

            using (Bitmap shot = _crash.Shot())
            {
                float scale = (float)OutW / 1920f;
                float w = shot.Width * scale, h = shot.Height * scale;
                float x = (OutW - w) / 2f, y = (OutH - h) / 2f;
                g.DrawImage(shot, x, y, w, h);

                // a normal Windows pointer, as the system one would be over the dialog
                float px = x + w * 0.72f;
                float py = y + h * 0.88f;
                if (ct < 1.2f)
                {
                    float k = Ease(Ramp(ct, 0.4f, 1.2f));
                    px = x + w * (0.86f - 0.14f * k);
                    py = y + h * (0.72f + 0.16f * k);
                }
                DrawPointer(g, px, py);
            }
        }

        static void DrawPointer(Graphics g, float x, float y)
        {
            PointF[] p =
            {
                new PointF(x, y),
                new PointF(x, y + 18f),
                new PointF(x + 4.4f, y + 13.6f),
                new PointF(x + 7.6f, y + 20f),
                new PointF(x + 10.7f, y + 18.5f),
                new PointF(x + 7.6f, y + 12.2f),
                new PointF(x + 13.2f, y + 12.2f)
            };
            using (SolidBrush b = new SolidBrush(Color.White))
                g.FillPolygon(b, p);
            using (Pen pen = new Pen(Color.FromArgb(30, 30, 30), 1.2f))
                g.DrawPolygon(pen, p);
        }

        // ----------------------------------------------------------------- utils
        static float Ramp(float t, float a, float b)
        {
            if (b <= a) return t >= b ? 1f : 0f;
            float v = (t - a) / (b - a);
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        static float Ease(float v)
        {
            return v * v * (3f - 2f * v);
        }

        static PointF Between(PointF a, PointF b, float k)
        {
            return new PointF(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k);
        }

        static ImageCodecInfo JpegEncoder()
        {
            foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                if (c.FormatID == ImageFormat.Jpeg.Guid) return c;
            return null;
        }

        static EncoderParameters JpegQuality()
        {
            EncoderParameters p = new EncoderParameters(1);
            p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
            return p;
        }
    }
}
