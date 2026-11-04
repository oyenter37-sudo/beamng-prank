using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BeamSetup
{
    /// <summary>Fake BeamNG.drive setup wizard: installs the prank exe and a real uninstall entry.</summary>
    class SetupForm : Form
    {
        enum Page { Welcome, Working, Done, UninstallDone }

        const int CW = 640, CH = 460;
        const string AppName = "BeamNG.drive";
        const string Version = "0.36.4.0";

        static readonly Font FTitle = F("Segoe UI", 14f, FontStyle.Bold);
        static readonly Font FHeader = F("Segoe UI", 30f, FontStyle.Bold | FontStyle.Italic);
        static readonly Font FSub = F("Segoe UI", 15f, FontStyle.Regular);
        static readonly Font FBody = F("Segoe UI", 15f, FontStyle.Regular);
        static readonly Font FSmall = F("Segoe UI", 13f, FontStyle.Regular);
        static readonly Font FTiny = F("Segoe UI", 11f, FontStyle.Regular);
        static readonly Font FBold = F("Segoe UI", 15f, FontStyle.Bold);

        static Font F(string family, float px, FontStyle style)
        {
            try { return new Font(family, px, style, GraphicsUnit.Pixel); }
            catch { return new Font("Tahoma", px, style, GraphicsUnit.Pixel); }
        }

        readonly Timer _tick;
        readonly bool _uninstall;
        readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        float _lastT;

        Page _page;
        float _progress, _t;
        int _lastStep = -1;
        bool _workStarted;
        bool _error;
        string _status = "";
        string _dir;

        bool _launch = true;
        bool _desktop = true;
        bool _hotCheck1, _hotCheck2, _hotPrimary, _hotSecondary, _hotBrowse, _hotClose;

        bool _dragging;
        Point _dragStart;

        static readonly RectangleF BtnPrimary = new RectangleF(CW - 170, CH - 60, 130, 38);
        static readonly RectangleF BtnSecondary = new RectangleF(CW - 292, CH - 60, 110, 38);
        static readonly RectangleF BrowseRect = new RectangleF(452, 260, 130, 36);
        static readonly RectangleF PathRect = new RectangleF(40, 260, 400, 36);
        static readonly RectangleF Check1 = new RectangleF(40, 336, 20, 20);
        static readonly RectangleF Check2 = new RectangleF(40, 372, 20, 20);
        static readonly RectangleF CloseRect = new RectangleF(CW - 48, 14, 30, 30);

        static readonly string[] Steps =
        {
            "Extracting game content",
            "Creating shortcuts",
            "Registering application"
        };

        public SetupForm(bool uninstall)
        {
            _uninstall = uninstall;
            _page = uninstall ? Page.Working : Page.Welcome;
            _dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "BeamNG.drive");

            Text = "BeamNG.drive Setup";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(CW, CH);
            DoubleBuffered = true;
            Icon = AppIcon;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            _tick = new Timer();
            _tick.Interval = 16;
            _tick.Tick += OnTick;
            _tick.Start();
        }

        static Icon _appIcon;
        static Icon AppIcon
        {
            get
            {
                if (_appIcon == null)
                {
                    try
                    {
                        using (Stream s = Assembly.GetExecutingAssembly()
                            .GetManifestResourceStream("BeamSetup.app.ico"))
                            _appIcon = new Icon(s);
                    }
                    catch { _appIcon = SystemIcons.Application; }
                }
                return _appIcon;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e) { DrawAll(e.Graphics); }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_uninstall && !_workStarted) StartUninstall();
        }

        // ------------------------------------------------------------------ work
        void OnTick(object sender, EventArgs e)
        {
            float now = (float)_clock.Elapsed.TotalSeconds;
            float dt = now - _lastT;
            _lastT = now;
            if (dt > 0.1f) dt = 0.1f;
            _t += dt;

            if (_page == Page.Working && _workStarted && !_error)
            {
                float len = _uninstall ? 2.0f : 2.6f;
                _progress += dt / len;
                if (_progress >= 1f)
                {
                    _progress = 1f;
                    _page = _uninstall ? Page.UninstallDone : Page.Done;
                }
                int step = (int)(_progress * Steps.Length);
                if (step != _lastStep)
                {
                    _lastStep = step;
                    if (!_uninstall && step < Steps.Length) _status = Steps[step] + "...";
                }
            }

            Invalidate();
        }

        void StartInstall()
        {
            _error = false;
            try
            {
                DoInstall();
                _status = Steps[0] + "...";
                _lastStep = 0;
            }
            catch (Exception ex)
            {
                _error = true;
                MessageBox.Show(this,
                    "Could not install BeamNG.drive.\r\n\r\n" + ex.Message,
                    "BeamNG.drive Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _page = Page.Welcome;
                return;
            }

            _page = Page.Working;
            _workStarted = true;
            _progress = 0f;
            _lastStep = -1;
        }

        void StartUninstall()
        {
            _page = Page.Working;
            _workStarted = true;
            _progress = 0f;
            _status = "Removing application files...";

            // never rmdir an arbitrary folder: only an actual BeamNG install
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            string target = File.Exists(Path.Combine(exeDir, AppName + ".exe"))
                ? exeDir
                : InstalledDir();

            if (target == null)
            {
                MessageBox.Show(this, "BeamNG.drive is not installed.",
                    "BeamNG.drive Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
                return;
            }
            _dir = target;

            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                TryDelete(Path.Combine(desktop, AppName + ".lnk"));
                TryDelete(Path.Combine(programs, AppName + ".lnk"));

                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(
                        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BeamNG.drive", false);
                }
                catch { }

                // the uninstaller lives inside the folder it has to remove
                Process.Start(new ProcessStartInfo("cmd.exe",
                    "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + _dir + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch { }
        }

        static string InstalledDir()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BeamNG.drive"))
                {
                    if (k != null)
                    {
                        string d = k.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(d) &&
                            File.Exists(Path.Combine(d, AppName + ".exe")))
                            return d;
                    }
                }
            }
            catch { }
            return null;
        }

        void DoInstall()
        {
            Directory.CreateDirectory(_dir);
            string exe = Path.Combine(_dir, AppName + ".exe");

            using (Stream s = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("BeamSetup.game"))
            {
                if (s == null) throw new InvalidOperationException("Installer payload is missing.");
                using (FileStream f = File.Create(exe))
                    s.CopyTo(f);
            }

            string self = Application.ExecutablePath;
            string selfCopy = Path.Combine(_dir, AppName + "_Setup.exe");
            if (!string.Equals(self, selfCopy, StringComparison.OrdinalIgnoreCase))
                File.Copy(self, selfCopy, true);

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);

            if (_desktop) CreateShortcut(Path.Combine(desktop, AppName + ".lnk"), exe, _dir);
            CreateShortcut(Path.Combine(programs, AppName + ".lnk"), exe, _dir);

            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BeamNG.drive"))
                {
                    k.SetValue("DisplayName", AppName);
                    k.SetValue("DisplayVersion", Version);
                    k.SetValue("Publisher", "BeamNG GmbH");
                    k.SetValue("InstallLocation", _dir);
                    k.SetValue("DisplayIcon", exe + ",0");
                    k.SetValue("UninstallString", "\"" + Path.Combine(_dir, AppName + "_Setup.exe") + "\" --uninstall");
                    k.SetValue("QuietUninstallString", "\"" + Path.Combine(_dir, AppName + "_Setup.exe") + "\" --uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        static void CreateShortcut(string link, string target, string workDir)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                dynamic shell = Activator.CreateInstance(t);
                dynamic sc = shell.CreateShortcut(link);
                sc.TargetPath = target;
                sc.WorkingDirectory = workDir;
                sc.IconLocation = target + ",0";
                sc.Description = AppName;
                sc.Save();
            }
            catch { }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        // ---------------------------------------------------------------- drawing
        internal void DrawAll(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, CW, CH),
                Color.FromArgb(16, 18, 22), Color.FromArgb(27, 30, 36), 90f))
                g.FillRectangle(bg, 0, 0, CW, CH);

            using (SolidBrush b = new SolidBrush(Color.FromArgb(10, 12, 15)))
                g.FillRectangle(b, 0, 0, CW, 96);

            Slash(g, new RectangleF(40, 30, 14, 38));
            Text(g, "BeamNG.drive", FHeader, Color.White, new RectangleF(66, 24, 400, 44), false);
            Text(g, "Setup", FSub, Color.FromArgb(140, 146, 155),
                new RectangleF(68, 64, 200, 24), false);
            Text(g, Version, FSmall, Color.FromArgb(120, 126, 135),
                new RectangleF(CW - 232, 34, 150, 24), true);

            using (SolidBrush b = new SolidBrush(Color.FromArgb(242, 107, 15)))
                g.FillRectangle(b, 0, 95, CW, 2);

            // close button
            if (_hotClose)
                using (SolidBrush b = new SolidBrush(Color.FromArgb(50, 54, 62)))
                    g.FillRectangle(b, CloseRect);
            using (Pen p = new Pen(Color.FromArgb(160, 166, 175), 1.6f))
            {
                g.DrawLine(p, CloseRect.X + 10, CloseRect.Y + 10, CloseRect.Right - 10, CloseRect.Bottom - 10);
                g.DrawLine(p, CloseRect.Right - 10, CloseRect.Y + 10, CloseRect.X + 10, CloseRect.Bottom - 10);
            }

            switch (_page)
            {
                case Page.Working: DrawWorking(g); break;
                case Page.Done: DrawDone(g, false); break;
                case Page.UninstallDone: DrawDone(g, true); break;
                default: DrawWelcome(g); break;
            }

            using (Pen p = new Pen(Color.FromArgb(160, 166, 175), 1f))
                g.DrawRectangle(p, 0, 0, CW - 1, CH - 1);
        }

        void DrawWelcome(Graphics g)
        {
            Text(g, "Welcome to the BeamNG.drive Setup Wizard", FTitle, Color.White,
                new RectangleF(40, 126, 560, 30), false);
            Text(g, "This wizard will install BeamNG.drive " + Version + " on your computer.\r\n" +
                    "Click Install to continue, or Cancel to exit Setup.",
                FBody, Color.FromArgb(178, 184, 192), new RectangleF(40, 168, 560, 54), false);

            Text(g, "Destination folder", FBold, Color.FromArgb(216, 221, 227),
                new RectangleF(40, 232, 400, 24), false);

            using (SolidBrush b = new SolidBrush(Color.FromArgb(9, 11, 14)))
                g.FillRectangle(b, PathRect);
            using (Pen p = new Pen(Color.FromArgb(62, 68, 78), 1f))
                g.DrawRectangle(p, PathRect.X, PathRect.Y, PathRect.Width - 1, PathRect.Height - 1);
            Text(g, _dir, FSmall, Color.FromArgb(170, 177, 186),
                new RectangleF(PathRect.X + 10, PathRect.Y, PathRect.Width - 20, PathRect.Height), false);

            Button(g, BrowseRect, "Browse...", false, _hotBrowse);

            Text(g, "Installs for the current user only - no administrator rights required.",
                FSmall, Color.FromArgb(118, 125, 134), new RectangleF(40, 304, 560, 22), false);

            Check(g, Check1, _desktop, "Create a desktop shortcut", _hotCheck1);
            Check(g, Check2, _launch, "Launch BeamNG.drive after setup", _hotCheck2);

            Button(g, BtnPrimary, "Install", true, _hotPrimary);
            Button(g, BtnSecondary, "Cancel", false, _hotSecondary);

            Text(g, "BeamNG.drive " + Version + "  ·  © 2012-2026 BeamNG GmbH",
                FTiny, Color.FromArgb(92, 98, 106), new RectangleF(40, CH - 18, 340, 16), false);
        }

        void DrawWorking(Graphics g)
        {
            string title = _uninstall ? "Uninstalling BeamNG.drive" : "Installing BeamNG.drive";
            Text(g, title, FTitle, Color.White, new RectangleF(40, 126, 560, 30), false);

            Text(g, "Installing to " + _dir, FSmall, Color.FromArgb(140, 147, 156),
                new RectangleF(40, 164, 560, 22), false);

            RectangleF track = new RectangleF(40, 208, 560, 8);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(42, 46, 54)))
                g.FillRectangle(b, track);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(242, 107, 15)))
                g.FillRectangle(b, track.X, track.Y, track.Width * Math.Min(1f, _progress), track.Height);

            Text(g, (int)(_progress * 100f) + " %", FSmall, Color.FromArgb(210, 215, 222),
                new RectangleF(520, 224, 80, 22), true);

            if (_uninstall)
            {
                Text(g, _status, FBody, Color.FromArgb(200, 206, 213),
                    new RectangleF(40, 250, 560, 24), false);
            }
            else
            {
                for (int i = 0; i < Steps.Length; i++)
                {
                    float y = 258 + i * 38;
                    bool done = _progress * Steps.Length > i + 0.85f;
                    bool current = !done && _progress * Steps.Length >= i;

                    RectangleF box = new RectangleF(40, y, 20, 20);
                    if (done)
                    {
                        FillRound(g, box, 3, Color.FromArgb(242, 107, 15));
                        using (Pen p = new Pen(Color.White, 2f))
                        {
                            g.DrawLine(p, box.X + 4, box.Y + 10, box.X + 8.5f, box.Y + 15);
                            g.DrawLine(p, box.X + 8.5f, box.Y + 15, box.Right - 4, box.Y + 5);
                        }
                    }
                    else
                    {
                        using (Pen p = new Pen(Color.FromArgb(72, 78, 88), 1.4f))
                            g.DrawRectangle(p, box.X, box.Y, box.Width - 1, box.Height - 1);
                    }

                    Color c = done ? Color.FromArgb(170, 176, 184)
                        : current ? Color.White : Color.FromArgb(110, 117, 126);
                    Text(g, Steps[i], FBody, c, new RectangleF(72, y - 4, 500, 28), false);
                }
            }

            Text(g, _uninstall ? "Removing files, shortcuts and registry entries."
                              : "This may take a few minutes. Do not close the window.",
                FSmall, Color.FromArgb(112, 119, 128), new RectangleF(40, 380, 560, 22), false);

            Button(g, BtnSecondary, "Cancel", false, _hotSecondary);

            Text(g, "BeamNG.drive " + Version + "  ·  © 2012-2026 BeamNG GmbH",
                FTiny, Color.FromArgb(92, 98, 106), new RectangleF(40, CH - 18, 340, 16), false);
        }

        void DrawDone(Graphics g, bool uninstall)
        {
            Text(g, uninstall ? "BeamNG.drive has been removed"
                             : "Completing the BeamNG.drive Setup Wizard",
                FTitle, Color.White, new RectangleF(40, 126, 560, 30), false);

            Text(g, uninstall
                    ? "BeamNG.drive has been removed from your computer."
                    : "BeamNG.drive " + Version + " has been installed on your computer.",
                FBody, Color.FromArgb(178, 184, 192), new RectangleF(40, 168, 560, 28), false);

            if (!uninstall)
            {
                Text(g, "Installed to", FBold, Color.FromArgb(216, 221, 227),
                    new RectangleF(40, 216, 300, 24), false);

                using (SolidBrush b = new SolidBrush(Color.FromArgb(9, 11, 14)))
                    g.FillRectangle(b, PathRect);
                using (Pen p = new Pen(Color.FromArgb(62, 68, 78), 1f))
                    g.DrawRectangle(p, PathRect.X, PathRect.Y, PathRect.Width - 1, PathRect.Height - 1);
                Text(g, _dir, FSmall, Color.FromArgb(170, 177, 186),
                    new RectangleF(PathRect.X + 10, PathRect.Y, PathRect.Width - 20, PathRect.Height), false);

                Check(g, Check2, _launch, "Launch BeamNG.drive", _hotCheck2);
            }

            Text(g, "Click Finish to exit Setup.", FSmall, Color.FromArgb(118, 125, 134),
                new RectangleF(40, 330, 560, 22), false);

            Button(g, BtnPrimary, "Finish", true, _hotPrimary);

            Text(g, "BeamNG.drive " + Version + "  ·  © 2012-2026 BeamNG GmbH",
                FTiny, Color.FromArgb(92, 98, 106), new RectangleF(40, CH - 18, 340, 16), false);
        }

        // ------------------------------------------------------------------ parts
        static void Text(Graphics g, string s, Font f, Color c, RectangleF r, bool right)
        {
            using (SolidBrush b = new SolidBrush(c))
            {
                StringFormat sf = new StringFormat(StringFormat.GenericTypographic);
                sf.Alignment = right ? StringAlignment.Far : StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Near;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(s, f, b, r, sf);
                sf.Dispose();
            }
        }

        static void Slash(Graphics g, RectangleF r)
        {
            PointF[] pts =
            {
                new PointF(r.Left + r.Width * 0.45f, r.Top),
                new PointF(r.Right, r.Top),
                new PointF(r.Right - r.Width * 0.45f, r.Bottom),
                new PointF(r.Left, r.Bottom)
            };
            using (SolidBrush b = new SolidBrush(Color.FromArgb(242, 107, 15)))
                g.FillPolygon(b, pts);
        }

        static void FillRound(Graphics g, RectangleF r, float rad, Color c)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                float d = rad * 2f;
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
                using (SolidBrush b = new SolidBrush(c))
                    g.FillPath(b, p);
            }
        }

        void Button(Graphics g, RectangleF r, string text, bool primary, bool hot)
        {
            Color c1, c2;
            if (primary)
            {
                c1 = hot ? Color.FromArgb(255, 140, 44) : Color.FromArgb(245, 123, 28);
                c2 = hot ? Color.FromArgb(232, 106, 8) : Color.FromArgb(211, 92, 4);
            }
            else
            {
                c1 = hot ? Color.FromArgb(46, 50, 58) : Color.FromArgb(34, 37, 44);
                c2 = hot ? Color.FromArgb(36, 40, 47) : Color.FromArgb(26, 29, 35);
            }

            using (GraphicsPath p = Round(r, 4f))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(r, c1, c2, 90f))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(primary ? Color.FromArgb(255, 158, 74) : Color.FromArgb(64, 70, 80), 1f))
                    g.DrawPath(pen, p);
            }

            using (SolidBrush b = new SolidBrush(Color.White))
            {
                StringFormat f = new StringFormat();
                f.Alignment = StringAlignment.Center;
                f.LineAlignment = StringAlignment.Center;
                g.DrawString(text, FBold, b, r, f);
                f.Dispose();
            }
        }

        void Check(Graphics g, RectangleF box, bool on, string label, bool hot)
        {
            FillRound(g, box, 3, on ? Color.FromArgb(242, 107, 15)
                                    : Color.FromArgb(26, 29, 35));
            using (Pen p = new Pen(on ? Color.FromArgb(255, 158, 74)
                                      : hot ? Color.FromArgb(120, 127, 137)
                                            : Color.FromArgb(72, 78, 88), 1.3f))
            {
                using (GraphicsPath path = Round(box, 3f))
                    g.DrawPath(p, path);
            }
            if (on)
            {
                using (Pen p = new Pen(Color.White, 2f))
                {
                    g.DrawLine(p, box.X + 4, box.Y + 10, box.X + 8.5f, box.Y + 15);
                    g.DrawLine(p, box.X + 8.5f, box.Y + 15, box.Right - 4, box.Y + 5);
                }
            }
            Text(g, label, FBody, Color.FromArgb(200, 206, 214),
                new RectangleF(box.Right + 10, box.Y - 3, 460, 26), false);
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2f;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ------------------------------------------------------------------ input
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_dragging)
            {
                Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
                return;
            }

            bool c1 = _page == Page.Welcome && Check1.Contains(e.Location);
            bool c2 = (_page == Page.Welcome && Check2.Contains(e.Location)) ||
                      (_page == Page.Done && Check2.Contains(e.Location));
            bool bp = (_page == Page.Welcome || _page == Page.Done) && BtnPrimary.Contains(e.Location);
            bool bs = (_page == Page.Welcome || _page == Page.Working) && BtnSecondary.Contains(e.Location);
            bool bb = _page == Page.Welcome && BrowseRect.Contains(e.Location);
            bool bc = CloseRect.Contains(e.Location);

            if (c1 != _hotCheck1 || c2 != _hotCheck2 || bp != _hotPrimary ||
                bs != _hotSecondary || bb != _hotBrowse || bc != _hotClose)
            {
                _hotCheck1 = c1; _hotCheck2 = c2; _hotPrimary = bp;
                _hotSecondary = bs; _hotBrowse = bb; _hotClose = bc;
                Invalidate();
            }

            Cursor = (c1 || c2 || bp || bs || bb || bc) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (CloseRect.Contains(e.Location)) { Close(); return; }
            if (e.Y < 96) { _dragging = true; _dragStart = e.Location; return; }

            if (_page == Page.Welcome)
            {
                if (Check1.Contains(e.Location)) { _desktop = !_desktop; Invalidate(); return; }
                if (Check2.Contains(e.Location)) { _launch = !_launch; Invalidate(); return; }
                if (BrowseRect.Contains(e.Location))
                {
                    using (FolderBrowserDialog d = new FolderBrowserDialog())
                    {
                        d.Description = "Select the folder where BeamNG.drive should be installed";
                        d.SelectedPath = _dir;
                        if (d.ShowDialog(this) == DialogResult.OK)
                        {
                            _dir = Path.Combine(d.SelectedPath, "BeamNG.drive");
                            Invalidate();
                        }
                    }
                    return;
                }
                if (BtnPrimary.Contains(e.Location)) { StartInstall(); return; }
                if (BtnSecondary.Contains(e.Location)) { Close(); return; }
            }
            else if (_page == Page.Done)
            {
                if (Check2.Contains(e.Location)) { _launch = !_launch; Invalidate(); return; }
                if (BtnPrimary.Contains(e.Location)) { Finish(); return; }
            }
            else if (_page == Page.Working)
            {
                if (BtnSecondary.Contains(e.Location)) Close();
            }
            else if (_page == Page.UninstallDone)
            {
                if (BtnPrimary.Contains(e.Location)) Close();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }

        void Finish()
        {
            if (_launch && !_uninstall)
            {
                try
                {
                    string exe = Path.Combine(_dir, AppName + ".exe");
                    if (File.Exists(exe))
                        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = _dir });
                }
                catch { }
            }
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _tick.Stop(); _tick.Dispose(); }
            base.Dispose(disposing);
        }

        // ---------------------------------------------------------------- shots
        public static void Shots(string dir)
        {
            Directory.CreateDirectory(dir);
            using (SetupForm f = new SetupForm(false))
            {
                f._tick.Stop();
                f._page = Page.Welcome;
                f.Render(Path.Combine(dir, "18-installer-welcome.png"));
                f._page = Page.Working;
                f._progress = 0.62f;
                f.Render(Path.Combine(dir, "19-installer-working.png"));
                f._page = Page.Done;
                f._progress = 1f;
                f.Render(Path.Combine(dir, "20-installer-done.png"));
            }
        }

        void Render(string path)
        {
            using (Bitmap b = new Bitmap(CW, CH))
            {
                using (Graphics g = Graphics.FromImage(b))
                    DrawAll(g);
                b.Save(path, ImageFormat.Png);
            }
        }
    }
}
