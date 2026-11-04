using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BeamPrank
{
    enum CrashKind
    {
        StoppedWorking,   // "has stopped working"
        NotResponding,    // "is not responding"
        StartupError,     // "unable to start correctly (0xc000007b)"
        DisplayDriver     // "Display driver stopped responding"
    }

    /// <summary>Fake Windows dialogs shown when the game "dies".</summary>
    class CrashForm : Form
    {
        readonly Timer _tick;
        readonly CrashKind _kind;
        readonly bool _modern;
        float _t;
        float _reappear;
        bool _hotClose, _hotBtn, _hotBtn2;
        bool _dragging;
        Point _dragStart;

        RectangleF _btn1, _btn2;
        int _w, _h;

        public CrashForm() : this(CrashKind.StoppedWorking, RunState.IsModernWindows) { }

        public CrashForm(CrashKind kind, bool modern)
        {
            _kind = kind;
            _modern = modern;
            switch (kind)
            {
                case CrashKind.NotResponding: _w = 540; _h = 196; break;
                case CrashKind.StartupError: _w = 486; _h = 176; break;
                case CrashKind.DisplayDriver: _w = 540; _h = 196; break;
                default: _w = 520; _h = 240; break;
            }

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(_w, _h);
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(240, 240, 240);
            DoubleBuffered = true;
            KeyPreview = true;
            Icon = Assets.AppIcon;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.X + (wa.Width - _w) / 2, wa.Y + (wa.Height - _h) / 2);

            _tick = new Timer();
            _tick.Interval = 16;
            _tick.Tick += delegate
            {
                _t += 0.016f;
                if (_reappear > 0f)
                {
                    _reappear -= 0.016f;
                    if (_reappear <= 0f) { Show(); Activate(); }
                }
                Invalidate();
            };
            _tick.Start();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            DrawContent(e.Graphics);
        }

        internal void TestTime(float t) { _t = t; }
        internal void TestHoverPrimary(bool hot) { _hotBtn = hot; }

        internal Bitmap Shot()
        {
            Bitmap b = new Bitmap(_w, _h);
            using (Graphics g = Graphics.FromImage(b))
                DrawContent(g);
            return b;
        }

        internal void DrawContent(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            switch (_kind)
            {
                case CrashKind.NotResponding: DrawNotResponding(g); break;
                case CrashKind.StartupError: DrawStartupError(g); break;
                case CrashKind.DisplayDriver: DrawDisplayDriver(g); break;
                default: DrawStoppedWorking(g); break;
            }

            using (Pen p = new Pen(Color.FromArgb(120, 120, 120)))
                g.DrawRectangle(p, 0, 0, _w - 1, _h - 1);
        }

        // ------------------------------------------------------------ windows 7
        void DrawStoppedWorking(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 240, 240)))
                g.FillRectangle(b, 0, 0, _w, _h);

            DrawTitleBar(g, _modern);

            g.DrawIcon(Assets.AppIcon, new Rectangle(22, 52, 32, 32));

            using (SolidBrush b = new SolidBrush(Color.FromArgb(20, 20, 20)))
                g.DrawString("BeamNG.drive has stopped working", Theme.DialogBold, b, 66, 52);

            string body = "A problem caused the program to stop working correctly. " +
                          "Windows will close the program and notify you if a solution is available.";
            using (SolidBrush b = new SolidBrush(Color.FromArgb(45, 45, 45)))
                g.DrawString(body, Theme.Dialog, b, new RectangleF(66, 78, _w - 100, 90));

            if (_t > 1.2f)
            {
                int dots = ((int)(_t * 2.5f)) % 4;
                string s = "Windows is checking for a solution to the problem" + new string('.', dots);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(90, 90, 90)))
                    g.DrawString(s, Theme.Dialog, b, new RectangleF(66, 150, _w - 100, 20));

                if (!_modern)
                {
                    // the classic Windows 7 dialog has the marquee progress bar;
                    // the flat Windows 10+ look does not
                    RectangleF track = new RectangleF(66, 176, _w - 132, 6);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(224, 224, 224)))
                        g.FillRectangle(b, track);
                    float pos = (_t * 0.6f) % 1f;
                    float w = track.Width * 0.3f;
                    float x = track.X + (track.Width - w) * pos;
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(6, 90, 180)))
                        g.FillRectangle(b, x, track.Y, w, track.Height);
                }
            }

            _btn1 = new RectangleF(_w - 132, _h - 48, 112, 30);
            DrawButton(g, _btn1, "Close program", _hotBtn, _modern);
        }

        // ------------------------------------------------------- windows 10/11
        void DrawNotResponding(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(243, 243, 243)))
                g.FillRectangle(b, 0, 0, _w, _h);

            DrawTitleBar(g, true);

            g.DrawIcon(Assets.AppIcon, new Rectangle(22, 48, 32, 32));

            using (SolidBrush b = new SolidBrush(Color.FromArgb(20, 20, 20)))
                g.DrawString("BeamNG.drive is not responding", Theme.DialogBold, b, 66, 48);

            string body = "If you close the program, you might lose information.";
            using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 60, 60)))
                g.DrawString(body, Theme.Dialog, b, new RectangleF(66, 74, _w - 100, 40));

            _btn2 = new RectangleF(_w - 349, _h - 48, 205, 30);
            _btn1 = new RectangleF(_w - 134, _h - 48, 120, 30);
            DrawButton(g, _btn1, "Close the program", _hotBtn, true);
            DrawButton(g, _btn2, "Wait for the program to respond", _hotBtn2, true);
        }

        // --------------------------------------------------------- message box
        void DrawStartupError(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 240, 240)))
                g.FillRectangle(b, 0, 0, _w, _h);

            DrawTitleBar(g, true);
            DrawErrorBadge(g, new RectangleF(24, 52, 32, 32));

            using (SolidBrush b = new SolidBrush(Color.FromArgb(25, 25, 25)))
                g.DrawString("The application was unable to start correctly (0xc000007b).\r\n" +
                             "Click OK to close the application.",
                    Theme.Dialog, b, new RectangleF(70, 54, _w - 96, 70));

            _btn1 = new RectangleF(_w - 98, _h - 44, 76, 28);
            DrawButton(g, _btn1, "OK", _hotBtn, true);
        }

        // ------------------------------------------------------- display driver
        void DrawDisplayDriver(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(243, 243, 243)))
                g.FillRectangle(b, 0, 0, _w, _h);

            DrawTitleBar(g, _modern);
            DrawWarningBadge(g, new RectangleF(24, 50, 32, 32));

            using (SolidBrush b = new SolidBrush(Color.FromArgb(25, 25, 25)))
                g.DrawString("Display driver stopped responding and has recovered",
                    Theme.DialogBold, b, new RectangleF(70, 48, _w - 96, 24));

            using (SolidBrush b = new SolidBrush(Color.FromArgb(55, 55, 55)))
                g.DrawString("Display driver NVIDIA Windows Kernel Mode Driver, Version 566.36 " +
                             "stopped responding and has successfully recovered.",
                    Theme.Dialog, b, new RectangleF(70, 76, _w - 96, 60));

            _btn1 = new RectangleF(_w - 98, _h - 44, 76, 28);
            DrawButton(g, _btn1, "OK", _hotBtn, true);
        }

        // ------------------------------------------------------------- shared
        void DrawTitleBar(Graphics g, bool flat)
        {
            Rectangle title = new Rectangle(0, 0, _w, 30);
            if (flat)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(250, 250, 250)))
                    g.FillRectangle(b, title);
            }
            else
            {
                using (LinearGradientBrush b = new LinearGradientBrush(title,
                    Color.FromArgb(238, 238, 238), Color.FromArgb(214, 214, 214), 90f))
                    g.FillRectangle(b, title);
            }
            using (Pen p = new Pen(Color.FromArgb(176, 176, 176)))
                g.DrawLine(p, 0, 29, _w, 29);

            g.DrawIcon(Assets.AppIcon, new Rectangle(8, 6, 16, 16));
            using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 30, 30)))
                g.DrawString("BeamNG.drive", Theme.Dialog, b, 30, 6);

            Rectangle close = new Rectangle(_w - 46, 1, 45, 27);
            if (_hotClose)
                using (SolidBrush b = new SolidBrush(flat ? Color.FromArgb(230, 230, 230) : Color.FromArgb(220, 80, 80)))
                    g.FillRectangle(b, close);
            using (Pen p = new Pen(Color.FromArgb(70, 70, 70), 1.4f))
            {
                g.DrawLine(p, close.X + 17, 9, close.X + 29, 21);
                g.DrawLine(p, close.X + 29, 9, close.X + 17, 21);
            }
        }

        static void DrawErrorBadge(Graphics g, RectangleF icon)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(214, 48, 40)))
            using (Pen p = new Pen(Color.FromArgb(150, 30, 24), 2f))
            {
                g.FillEllipse(b, icon);
                g.DrawEllipse(p, icon);
            }
            using (Pen p = new Pen(Color.White, 3.2f))
            {
                g.DrawLine(p, icon.X + 10, icon.Y + 10, icon.Right - 10, icon.Bottom - 10);
                g.DrawLine(p, icon.Right - 10, icon.Y + 10, icon.X + 10, icon.Bottom - 10);
            }
        }

        static void DrawWarningBadge(Graphics g, RectangleF r)
        {
            PointF[] pts =
            {
                new PointF(r.X + r.Width / 2f, r.Y),
                new PointF(r.Right, r.Bottom),
                new PointF(r.X, r.Bottom)
            };
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 196, 0)))
                g.FillPolygon(b, pts);
            using (Pen p = new Pen(Color.FromArgb(190, 140, 0), 1.4f))
                g.DrawPolygon(p, pts);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(40, 34, 10)))
            {
                g.FillRectangle(b, r.X + r.Width / 2f - 1.8f, r.Y + 7, 3.6f, 11);
                g.FillEllipse(b, r.X + r.Width / 2f - 2f, r.Bottom - 7, 4f, 4f);
            }
        }

        static void DrawButton(Graphics g, RectangleF r, string text, bool hot, bool flat)
        {
            if (flat)
            {
                using (SolidBrush b = new SolidBrush(hot ? Color.FromArgb(229, 241, 251) : Color.FromArgb(240, 240, 240)))
                    g.FillRectangle(b, r);
                using (Pen p = new Pen(hot ? Color.FromArgb(0, 120, 215) : Color.FromArgb(173, 173, 173)))
                    g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
            else
            {
                using (LinearGradientBrush b = new LinearGradientBrush(r,
                    hot ? Color.FromArgb(240, 246, 252) : Color.FromArgb(245, 245, 245),
                    hot ? Color.FromArgb(214, 232, 250) : Color.FromArgb(226, 226, 226), 90f))
                    g.FillRectangle(b, r);
                using (Pen p = new Pen(hot ? Color.FromArgb(60, 130, 200) : Color.FromArgb(173, 173, 173)))
                    g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            }

            using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 30, 30)))
            {
                StringFormat f = new StringFormat();
                f.Alignment = StringAlignment.Center;
                f.LineAlignment = StringAlignment.Center;
                g.DrawString(text, Theme.Dialog, b, r, f);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
                return;
            }

            bool h1 = _btn1.Contains(e.Location);
            bool h2 = _btn2 != RectangleF.Empty && _btn2.Contains(e.Location);
            bool hx = e.Y < 30 && e.X > _w - 46;
            if (h1 != _hotBtn || h2 != _hotBtn2 || hx != _hotClose)
            {
                _hotBtn = h1; _hotBtn2 = h2; _hotClose = hx;
                Invalidate();
            }
            Cursor = (h1 || h2 || hx) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (e.Y < 30 && e.X > _w - 46) { Bail(); return; }
            if (e.Y < 30) { _dragging = true; _dragStart = e.Location; return; }

            if (_btn1.Contains(e.Location)) { Bail(); return; }
            if (_btn2 != RectangleF.Empty && _btn2.Contains(e.Location))
            {
                // "wait for the program to respond": the dialog comes back anyway
                Hide();
                _reappear = 4f;
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
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                Bail();
        }

        static void Bail()
        {
            Environment.Exit(0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _tick.Stop(); _tick.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
