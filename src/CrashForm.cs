using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BeamPrank
{
    /// <summary>Windows 7 style "has stopped working" dialog.</summary>
    class CrashForm : Form
    {
        readonly Timer _tick;
        float _t;
        bool _hotClose;
        bool _hotBtn;
        bool _dragging;
        Point _dragStart;
        RectangleF _btnClose;

        const int CW = 520, CH = 240;

        public CrashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(CW, CH);
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(240, 240, 240);
            DoubleBuffered = true;
            KeyPreview = true;
            Icon = Assets.AppIcon;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.X + (wa.Width - CW) / 2, wa.Y + (wa.Height - CH) / 2);

            _tick = new Timer();
            _tick.Interval = 16;
            _tick.Tick += delegate { _t += 0.016f; Invalidate(); };
            _tick.Start();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            DrawContent(e.Graphics);
        }

        internal void TestTime(float t) { _t = t; }

        internal Bitmap Shot()
        {
            Bitmap b = new Bitmap(CW, CH);
            using (Graphics g = Graphics.FromImage(b))
                DrawContent(g);
            return b;
        }

        internal void DrawContent(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // window body
            using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 240, 240)))
                g.FillRectangle(b, 0, 0, CW, CH);

            // title bar
            Rectangle title = new Rectangle(0, 0, CW, 30);
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, CW, 30),
                Color.FromArgb(238, 238, 238), Color.FromArgb(214, 214, 214), 90f))
                g.FillRectangle(b, title);
            using (Pen p = new Pen(Color.FromArgb(176, 176, 176)))
                g.DrawLine(p, 0, 29, CW, 29);

            g.DrawIcon(Assets.AppIcon, new Rectangle(8, 6, 16, 16));
            using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 30, 30)))
                g.DrawString("BeamNG.drive", Theme.Dialog, b, 30, 6);

            // close button
            Rectangle close = new Rectangle(CW - 46, 1, 45, 27);
            if (_hotClose)
                using (SolidBrush b = new SolidBrush(Color.FromArgb(220, 80, 80)))
                    g.FillRectangle(b, close);
            using (Pen p = new Pen(Color.FromArgb(90, 90, 90), 1.4f))
            {
                g.DrawLine(p, close.X + 17, 9, close.X + 29, 21);
                g.DrawLine(p, close.X + 29, 9, close.X + 17, 21);
            }

            // icon + heading
            g.DrawIcon(Assets.AppIcon, new Rectangle(22, 52, 32, 32));

            using (SolidBrush b = new SolidBrush(Color.FromArgb(20, 20, 20)))
                g.DrawString("BeamNG.drive has stopped working", Theme.DialogBold, b, 66, 52);

            string body = "A problem caused the program to stop working correctly. " +
                          "Windows will close the program and notify you if a solution is available.";
            using (SolidBrush b = new SolidBrush(Color.FromArgb(45, 45, 45)))
                g.DrawString(body, Theme.Dialog, b, new RectangleF(66, 78, CW - 100, 90));

            if (_t > 1.2f)
            {
                int dots = ((int)(_t * 2.5f)) % 4;
                string s = "Windows is checking for a solution to the problem" + new string('.', dots);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(90, 90, 90)))
                    g.DrawString(s, Theme.Dialog, b, new RectangleF(66, 150, CW - 100, 20));

                // marquee
                RectangleF track = new RectangleF(66, 176, CW - 132, 6);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(224, 224, 224)))
                    g.FillRectangle(b, track);
                float pos = ((_t * 0.6f) % 1f);
                float w = track.Width * 0.3f;
                float x = track.X + (track.Width - w) * pos;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(6, 90, 180)))
                    g.FillRectangle(b, x, track.Y, w, track.Height);
            }

            // button
            _btnClose = new RectangleF(CW - 132, CH - 48, 112, 30);
            DrawWin7Button(g, _btnClose, "Close program", _hotBtn);

            // outer border
            using (Pen p = new Pen(Color.FromArgb(120, 120, 120)))
                g.DrawRectangle(p, 0, 0, CW - 1, CH - 1);
        }

        static void DrawWin7Button(Graphics g, RectangleF r, string text, bool hot)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(r,
                hot ? Color.FromArgb(240, 246, 252) : Color.FromArgb(245, 245, 245),
                hot ? Color.FromArgb(214, 232, 250) : Color.FromArgb(226, 226, 226), 90f))
                g.FillRectangle(b, r);
            using (Pen p = new Pen(hot ? Color.FromArgb(60, 130, 200) : Color.FromArgb(173, 173, 173)))
                g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
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
            bool hot = _btnClose.Contains(e.Location);
            if (hot != _hotBtn) { _hotBtn = hot; Invalidate(); }
            bool hotX = e.Y < 30 && e.X > CW - 46;
            if (hotX != _hotClose) { _hotClose = hotX; Invalidate(); }
            Cursor = (hot || hotX) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (e.Y < 30 && e.X > CW - 46) { Bail(); return; }
            if (e.Y < 30) { _dragging = true; _dragStart = e.Location; return; }
            if (_btnClose.Contains(e.Location)) Bail();
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
