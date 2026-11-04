using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BeamPrank
{
    struct Hit
    {
        public string Id;
        public RectangleF R;
        public Hit(string id, RectangleF r) { Id = id; R = r; }
    }

    public class AppForm : Form
    {
        // ---------------------------------------------------------------- state
        public enum Screen { Main, Maps, Vehicles, Loading, Page }

        Screen _screen = Screen.Main;
        Screen _pending;
        float _fade;
        int _fadeDir;
        readonly Stopwatch _clock = Stopwatch.StartNew();
        float _lastTick;
        bool _dirty = true;

        bool _quickStart;
        string _page = "";
        string _hoverId = "";
        readonly Dictionary<string, float> _hoverAnim = new Dictionary<string, float>();
        readonly List<Hit> _hits = new List<Hit>();

        int _mapSel = 9;
        int _vehSel = -1;

        float _mapsScroll, _mapsTarget;
        float _vehScroll, _vehTarget;

        float _time;
        readonly Timer _tick;

        // loading / crash
        const float BootLen = 5.2f;    // fake shader compile before the real loading frames
        float _loadT;
        float _holdT;
        bool _frozen;
        bool _dialogShown;
        bool _instantCrash;
        CrashKind _crashKind;
        readonly bool _modern = RunState.IsModernWindows;

        // the game draws its own orange pointer; the system cursor stays hidden
        PointF _mouse;
        bool _mouseInside;
        readonly Cursor _blankCursor;

        Bitmap _buf;
        Graphics _bufG;
        IntPtr _memDc, _dib;

        [StructLayout(LayoutKind.Sequential)]
        struct BitmapInfoHeader
        {
            public int Size, Width, Height;
            public short Planes, BitCount;
            public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct BitmapInfo
        {
            public BitmapInfoHeader Header;
            public int Colors;
        }

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo bmi, uint usage,
            out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr d, int x, int y, int w, int h,
            IntPtr s, int sx, int sy, int rop);
        [DllImport("gdi32.dll")]
        static extern bool StretchBlt(IntPtr d, int x, int y, int w, int h,
            IntPtr s, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")]
        static extern int SetStretchBltMode(IntPtr hdc, int mode);
        const int SRCCOPY = 0x00CC0020;
        const int HALFTONE = 4;
        float _scale = 1f;
        float _ox, _oy;

        static readonly string UserName = BuildUserName();

        static string BuildUserName()
        {
            string n = "";
            try { n = Environment.UserName; } catch { }
            if (string.IsNullOrEmpty(n)) n = "Player";
            n = n.Trim();
            n = char.ToUpperInvariant(n[0]) + n.Substring(1);
            if (n.Length > 16) n = n.Substring(0, 15) + "\u2026";
            return n;
        }

        // ------------------------------------------------------------- geometry
        const int W = 1920, H = 1080;

        static readonly RectangleF[] MenuCards =
        {
            new RectangleF(443, 390, 250, 290),
            new RectangleF(708, 390, 250, 290),
            new RectangleF(973, 390, 250, 290),
            new RectangleF(1238, 390, 250, 290)
        };
        static readonly string[] MenuCardIds = { "quick", "freeroam", "career", "more" };

        static readonly RectangleF BtnRepo = new RectangleF(468, 718, 230, 42);
        static readonly RectangleF BtnCredits = new RectangleF(712, 718, 230, 42);
        static readonly RectangleF BtnOptions = new RectangleF(972, 718, 230, 42);
        static readonly RectangleF BtnQuit = new RectangleF(1213, 719, 238, 50);

        static readonly RectangleF MapsGrid = new RectangleF(6, 111, 1500, 924);
        static readonly RectangleF MapsStart = new RectangleF(1140, 984, 365, 46);
        static readonly RectangleF MapsBack = new RectangleF(0, 0, 130, 46);
        static readonly RectangleF MapsTitleBand = new RectangleF(0, 45, 1506, 50);

        static readonly RectangleF VehGrid = new RectangleF(0, 125, 1483, 837);
        static readonly RectangleF VehStart = new RectangleF(1110, 66, 350, 50);
        static readonly RectangleF VehBack = new RectangleF(0, 0, 130, 46);

        const float MapCardW = 364, MapCardH = 210, MapPitchX = 378, MapPitchY = 210;
        // the game leaves a little extra air before the last row of the 4x4 grid
        static readonly float[] MapRowY = { 0f, 231f, 444f, 658f };
        const float VehCardW = 290, VehCardH = 183, VehPitchX = 294, VehPitchY = 188;

        // 4x4 slots, -1 = the empty cell the game leaves in its first row
        static readonly int[] MapArt = { 0, 1, 2, -1, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };
        const int MapSlots = 16;

        const int VehSlots = 30;
        const int RecentCount = 5;

        // -------------------------------------------------------------- lifecycle
        public AppForm()
        {
            Text = "BeamNG.drive";
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            BackColor = Color.Black;
            DoubleBuffered = false;
            KeyPreview = true;
            Icon = Assets.AppIcon;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);

            // the system pointer is replaced by the orange one drawn into the frame
            _blankCursor = LoadBlankCursor();
            Cursor = _blankCursor;

            // a DIB section doubles as the GDI+ drawing surface and as the source
            // of a native blit; a plain Bitmap cannot be blitted by GDI, and
            // DrawImage is far too slow to rescale every frame
            BitmapInfo bmi = new BitmapInfo();
            bmi.Header.Size = Marshal.SizeOf(typeof(BitmapInfoHeader));
            bmi.Header.Width = W;
            bmi.Header.Height = -H;            // top-down, same as GDI+
            bmi.Header.Planes = 1;
            bmi.Header.BitCount = 32;
            bmi.Header.Compression = 0;        // BI_RGB

            IntPtr bits;
            _memDc = CreateCompatibleDC(IntPtr.Zero);
            _dib = CreateDIBSection(_memDc, ref bmi, 0, out bits, IntPtr.Zero, 0);
            SelectObject(_memDc, _dib);
            _buf = new Bitmap(W, H, W * 4, PixelFormat.Format32bppRgb, bits);
            _bufG = Graphics.FromImage(_buf);
            _tick = new Timer();
            _tick.Interval = 16;
            _tick.Tick += OnTick;
            _tick.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // a maximized borderless window still leaves the taskbar visible;
            // the real game owns the whole screen
            Bounds = System.Windows.Forms.Screen.FromPoint(Cursor.Position).Bounds;
            Cursor = _blankCursor;
        }

        static Cursor LoadBlankCursor()
        {
            // a 1x1 fully transparent .cur, so only the cursor we draw is visible
            byte[] cur =
            {
                0, 0, 2, 0, 1, 0, 1, 1, 0, 0, 1, 0, 32, 0, 48, 0, 0, 0, 22, 0, 0, 0,
                40, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 1, 0, 32, 0, 0, 0, 0, 0, 4, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                255, 0, 0, 0
            };
            try { return new Cursor(new MemoryStream(cur)); }
            catch { return Cursors.Default; }
        }

        // ------------------------------------------------------------------ loop
        void OnTick(object sender, EventArgs e)
        {
            float now = (float)_clock.Elapsed.TotalSeconds;
            float dt = now - _lastTick;
            _lastTick = now;
            if (dt > 0.1f) dt = 0.1f;
            _time += dt;

            bool anim = false;

            foreach (string k in new List<string>(_hoverAnim.Keys))
            {
                float target = (k == _hoverId) ? 1f : 0f;
                float v = _hoverAnim[k];
                if (v == target) continue;
                v += (target - v) * Math.Min(1f, dt * 18f);
                if (Math.Abs(target - v) < 0.01f) v = target;
                _hoverAnim[k] = v;
                anim = true;
            }
            if (_hoverId != "" && !_hoverAnim.ContainsKey(_hoverId))
            {
                _hoverAnim[_hoverId] = 0f;
                anim = true;
            }

            if (Math.Abs(_mapsTarget - _mapsScroll) > 0.3f)
            {
                _mapsScroll += (_mapsTarget - _mapsScroll) * Math.Min(1f, dt * 14f);
                anim = true;
            }
            else if (_mapsScroll != _mapsTarget) { _mapsScroll = _mapsTarget; anim = true; }

            if (Math.Abs(_vehTarget - _vehScroll) > 0.3f)
            {
                _vehScroll += (_vehTarget - _vehScroll) * Math.Min(1f, dt * 14f);
                anim = true;
            }
            else if (_vehScroll != _vehTarget) { _vehScroll = _vehTarget; anim = true; }

            if (_fadeDir < 0)
            {
                _fade += dt * 6f;
                anim = true;
                if (_fade >= 1f) { _fade = 1f; _screen = _pending; EnterScreen(); _fadeDir = 1; }
            }
            else if (_fadeDir > 0)
            {
                _fade -= dt * 6f;
                anim = true;
                if (_fade <= 0f) { _fade = 0f; _fadeDir = 0; }
            }

            if (_screen == Screen.Loading && !_frozen)
            {
                UpdateLoading(dt);
                anim = true;
            }

            if (anim || _dirty)
            {
                _dirty = false;
                Invalidate();
            }
        }

        void Go(Screen s)
        {
            if (_fadeDir != 0) return;
            _pending = s;
            _fadeDir = -1;
        }

        void EnterScreen()
        {
            _hoverId = "";
            if (_screen == Screen.Loading)
            {
                _loadT = 0f;
                _holdT = 0f;
                _frozen = false;
                _dialogShown = false;
                _instantCrash = false;
            }
            if (_screen == Screen.Maps) _mapsTarget = 0f;
        }

        void UpdateLoading(float dt)
        {
            _loadT += dt;
            float animLen = Assets.FrameCount / 20f;
            if (_loadT > BootLen + animLen + 1.2f)
                TriggerCrash(false);
        }

        void TriggerCrash(bool instant)
        {
            if (_frozen) return;
            _instantCrash = instant;
            _frozen = true;
            _holdT = instant ? 0.45f : 0.9f;
            Cursor = Cursors.WaitCursor;

            _crashKind = RunState.PickCrash(RunState.CurrentRun);
            // "is not responding" is a Windows 10/11 dialog; 7 and 8 get the driver message instead
            if (!_modern && _crashKind == CrashKind.NotResponding)
                _crashKind = CrashKind.DisplayDriver;
            RunState.WriteCrashArtifacts(RunState.ReasonFor(_crashKind));

            Timer t = new Timer();
            t.Interval = (int)(_holdT * 1000);
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                try { System.Media.SystemSounds.Hand.Play(); } catch { }
                ShowCrash();
            };
            t.Start();
        }

        void ShowCrash()
        {
            if (_dialogShown) return;
            _dialogShown = true;
            CrashForm f = new CrashForm(_crashKind, _modern);
            f.Show(this);
            f.BringToFront();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Alt+F4 while loading is exactly how a real game dies mid-load
            if (!_frozen && _screen == Screen.Loading && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                TriggerCrash(true);
                return;
            }
            base.OnFormClosing(e);
        }

        // ---------------------------------------------------------------- input
        PointF ToVirtual(Point p)
        {
            return new PointF((p.X - _ox) / _scale, (p.Y - _oy) / _scale);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _mouse = ToVirtual(e.Location);
            _mouseInside = true;
            if (_frozen) return;
            string id = HitTest(_mouse);
            if (id != _hoverId)
            {
                _hoverId = id;
                _dirty = true;
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _mouse = ToVirtual(PointToClient(Cursor.Position));
            _mouseInside = true;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mouseInside = false;
            _hoverId = "";
            _dirty = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_frozen || e.Button != MouseButtons.Left) return;
            Click(HitTest(ToVirtual(e.Location)));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_frozen) return;
            float delta = -e.Delta / 120f * 120f;

            if (_screen == Screen.Maps && _mapsTarget + delta > 0f)
                _mapsTarget = Clamp(_mapsTarget + delta, 0, MapsMaxScroll());
            else if (_screen == Screen.Vehicles)
                _vehTarget = Clamp(_vehTarget + delta, 0, VehMaxScroll());
            _dirty = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_frozen) return;

            if (e.KeyCode == Keys.Escape)
            {
                if (_screen == Screen.Vehicles) Go(Screen.Maps);
                else if (_screen == Screen.Maps || _screen == Screen.Page) Go(Screen.Main);
                return;
            }
            if (e.KeyCode == Keys.Enter && _screen == Screen.Vehicles) { Go(Screen.Loading); return; }
            if (e.KeyCode == Keys.Enter && _screen == Screen.Maps) { Go(Screen.Vehicles); return; }
            _dirty = true;
        }

        string HitTest(PointF p)
        {
            for (int i = _hits.Count - 1; i >= 0; i--)
                if (_hits[i].R.Contains(p)) return _hits[i].Id;
            return "";
        }

        bool IsClickable(string id)
        {
            return id != "" && id != "career";
        }

        void Click(string id)
        {
            if (id == "") return;
            switch (id)
            {
                case "freeroam": _quickStart = false; Go(Screen.Maps); break;
                case "quick": _quickStart = true; Go(Screen.Maps); break;
                case "more": TriggerCrash(true); break;
                case "quit": Close(); break;
                case "repo": _page = "repository"; Go(Screen.Page); break;
                case "credits": _page = "credits"; Go(Screen.Page); break;
                case "options": _page = "options"; Go(Screen.Page); break;
                case "career": break;

                case "maps_back": Go(Screen.Main); break;
                case "maps_start": Go(Screen.Vehicles); break;
                case "veh_back": Go(Screen.Maps); break;
                case "veh_start": Go(Screen.Loading); break;
                case "page_back": Go(Screen.Main); break;
            }

            if (id.StartsWith("map:"))
            {
                int i = int.Parse(id.Substring(4));
                _mapSel = i;
            }
            if (id.StartsWith("veh:"))
            {
                int i = int.Parse(id.Substring(4));
                if (_vehSel == i) { Go(Screen.Loading); }
                else _vehSel = i;
            }
        }

        // ---------------------------------------------------------------- render
        protected override void OnPaintBackground(PaintEventArgs e) { }

        /// <summary>Draws the whole frame into the offscreen buffer.</summary>
        internal void RenderScene()
        {
            Graphics g = _bufG;
            g.ResetClip();
            g.ResetTransform();
            {
                Draw.SetupQuality(g);
                g.Clear(Color.Black);
                _hits.Clear();

                switch (_screen)
                {
                    case Screen.Main: DrawMain(g); break;
                    case Screen.Maps: DrawMaps(g); break;
                    case Screen.Vehicles: DrawVehicles(g); break;
                    case Screen.Loading: DrawLoading(g); break;
                    case Screen.Page: DrawPage(g); break;
                }

                if (_frozen && _screen != Screen.Loading)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                        g.FillRectangle(b, 0, 0, W, H);
                }

                if (_fade > 0.001f)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(_fade * 255), 0, 0, 0)))
                        g.FillRectangle(b, 0, 0, W, H);
                }

                DrawCursor(g);
            }

        }

        protected override void OnPaint(PaintEventArgs e)
        {
            RenderScene();

            Graphics gg = e.Graphics;
            Rectangle cb = ClientRectangle;
            _scale = Math.Min((float)cb.Width / W, (float)cb.Height / H);
            float dw = W * _scale, dh = H * _scale;
            _ox = (cb.Width - dw) / 2f;
            _oy = (cb.Height - dh) / 2f;

            gg.Clear(Color.Black);

            // blit the frame with GDI instead of GDI+: DrawImage rescales every
            // pixel in managed code and is the reason transitions felt laggy
            int bx = (int)Math.Round(_ox), by = (int)Math.Round(_oy);
            int bw = (int)Math.Round(dw), bh = (int)Math.Round(dh);
            if (!Present(gg, bx, by, bw, bh))
            {
                gg.InterpolationMode = InterpolationMode.Bilinear;
                gg.PixelOffsetMode = PixelOffsetMode.Half;
                gg.DrawImage(_buf, new RectangleF(_ox, _oy, dw, dh));
            }
        }

        bool Present(Graphics dstG, int x, int y, int w, int h)
        {
            _bufG.Flush(FlushIntention.Sync);
            IntPtr dst = dstG.GetHdc();
            try
            {
                if (w == W && h == H)
                    return BitBlt(dst, x, y, w, h, _memDc, 0, 0, SRCCOPY);
                SetStretchBltMode(dst, HALFTONE);
                return StretchBlt(dst, x, y, w, h, _memDc, 0, 0, W, H, SRCCOPY);
            }
            finally
            {
                dstG.ReleaseHdc(dst);
            }
        }

        internal bool TestPresent(Bitmap dest)
        {
            RenderScene();
            using (Graphics dg = Graphics.FromImage(dest))
            {
                bool ok = Present(dg, 0, 0, dest.Width, dest.Height);
                if (!ok) return false;
            }
            return true;
        }

        float Hover(string id)
        {
            float v;
            return _hoverAnim.TryGetValue(id, out v) ? v : 0f;
        }

        // ------------------------------------------------------------ main menu
        void DrawMain(Graphics g)
        {
            g.DrawImage(Assets.Get("bg_main.jpg"), 0, 0, W, H);

            for (int i = 0; i < MenuCards.Length; i++)
            {
                string id = MenuCardIds[i];
                RectangleF r = MenuCards[i];
                _hits.Add(new Hit(id, r));
                if (id == "career") continue;
                Draw.HoverOutline(g, r, 10f, Hover(id));
            }

            DrawButtonHit(g, "repo", BtnRepo);
            DrawButtonHit(g, "credits", BtnCredits);
            DrawButtonHit(g, "options", BtnOptions);
            DrawButtonHit(g, "quit", BtnQuit);

            DrawUserName(g, 1042f);
        }

        void DrawButtonHit(Graphics g, string id, RectangleF r)
        {
            _hits.Add(new Hit(id, r));
            float h = Hover(id);
            if (h > 0.01f)
                using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(60 * h), 255, 255, 255)))
                    g.FillRectangle(b, r);
        }

        // -------------------------------------------------------------- map grid
        static float MapsMaxScroll()
        {
            float content = MapRowY[3] + MapCardH;
            return Math.Max(0f, content - MapsGrid.Height);
        }

        void DrawMaps(Graphics g)
        {
            g.DrawImage(Assets.Get("bg_maps.jpg"), 0, 0, W, H);

            // repaint the page heading when running the Quick-Start branch
            if (_quickStart)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(91, 91, 91)))
                    g.FillRectangle(b, MapsTitleBand);
                Draw.Text(g, "Quick-Start Experiences", Theme.PageTitle, Color.White,
                    new RectangleF(14, MapsTitleBand.Y, 1100, MapsTitleBand.Height), Draw.LeftMid);
            }

            // wipe the baked grid, we draw our own copy so it can scroll
            using (LinearGradientBrush b = new LinearGradientBrush(MapsGrid, Color.FromArgb(39, 43, 42), Color.FromArgb(33, 37, 36), 90f))
                g.FillRectangle(b, MapsGrid);

            Region old = g.Clip;
            g.SetClip(MapsGrid);

            for (int i = 0; i < MapSlots; i++)
            {
                int art = MapArt[i];
                if (art < 0) continue;

                int col = i % 4, row = i / 4;
                RectangleF r = new RectangleF(
                    MapsGrid.X + col * MapPitchX,
                    MapsGrid.Y + MapRowY[row] - _mapsScroll,
                    MapCardW, MapCardH);

                if (r.Bottom < MapsGrid.Y - 10 || r.Top > MapsGrid.Bottom + 10) continue;

                g.DrawImage(Assets.Get(string.Format("cards/m{0:00}.jpg", art)), r);

                bool sel = i == _mapSel;
                if (sel)
                {
                    DrawSelectedLabel(g, r, 48f, string.Format("cards/m{0:00}l.png", art));
                }
                else
                {
                    // the label text is baked into the art, but one card's strip was
                    // rebuilt without it, so the stored mask restores it everywhere
                    g.DrawImage(Assets.Get(string.Format("cards/m{0:00}l.png", art)),
                        new RectangleF(r.X, r.Bottom - 48f, r.Width, 48f));
                }
                Draw.HoverOutline(g, r, 4f, sel ? 1f : Hover("map:" + i), sel ? 0f : 0.85f);
                _hits.Add(new Hit("map:" + i, r));
            }

            g.Clip = old;

            // start button
            float bh = Hover("maps_start");
            Color c1 = Color.FromArgb(245, 120, 20), c2 = Color.FromArgb(214, 92, 6);
            if (bh > 0.02f)
            {
                c1 = Blend(c1, Color.White, 0.25f * bh);
                c2 = Blend(c2, Color.White, 0.25f * bh);
            }
            Draw.GradientRound(g, MapsStart, 3f, c1, c2);
            Draw.Text(g, _quickStart ? "Start Scenario" : "Start Freeroam", Theme.Button, Color.White, MapsStart, Draw.CenterMid);
            _hits.Add(new Hit("maps_start", MapsStart));

            _hits.Add(new Hit("maps_back", MapsBack));

            DrawUserName(g, 1058f);
        }

        static readonly Color LabelOrange = Color.FromArgb(230, 120, 52);

        static void DrawSelectedLabel(Graphics g, RectangleF card, float labelH, string maskPath)
        {
            RectangleF band = new RectangleF(card.X, card.Bottom - labelH, card.Width, labelH);
            using (SolidBrush b = new SolidBrush(LabelOrange))
                g.FillRectangle(b, band);
            g.DrawImage(Assets.Get(maskPath), band);
        }

        // --------------------------------------------------------- vehicle grid
        static float VehContentHeight()
        {
            return 40 + VehCardH + 40 + 4 * VehPitchY + VehCardH;
        }

        static float VehMaxScroll()
        {
            return Math.Max(0f, VehContentHeight() - VehGrid.Height);
        }

        void DrawVehicles(Graphics g)
        {
            g.DrawImage(Assets.Get("bg_vehicles.jpg"), 0, 0, W, H);

            using (LinearGradientBrush b = new LinearGradientBrush(VehGrid, Color.FromArgb(22, 26, 32), Color.FromArgb(16, 20, 26), 90f))
                g.FillRectangle(b, VehGrid);

            Region old = g.Clip;
            g.SetClip(VehGrid);

            float y = VehGrid.Y - _vehScroll;

            SectionHeader(g, "Recent", y);
            y += 40;
            for (int i = 0; i < RecentCount; i++)
            {
                DrawVehCard(g, i, VehGrid.X + 16 + i * VehPitchX, y);
            }
            y += VehPitchY;

            SectionHeader(g, "Cars and Trucks", y);
            y += 40;

            int idx = RecentCount;
            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 5; c++)
                {
                    if (idx >= VehSlots) break;
                    int card = idx % 20;
                    DrawVehCard(g, idx, VehGrid.X + 16 + c * VehPitchX, y + r * VehPitchY, card);
                    idx++;
                }
            }

            g.Clip = old;

            // start pill on the title band
            float bh = Hover("veh_start");
            Color c1 = Color.FromArgb(245, 120, 20), c2 = Color.FromArgb(214, 92, 6);
            if (bh > 0.02f) { c1 = Blend(c1, Color.White, 0.25f * bh); c2 = Blend(c2, Color.White, 0.25f * bh); }
            Draw.GradientRound(g, VehStart, 2f, c1, c2);
            Draw.Text(g, "Start", Theme.Button, Color.White, VehStart, Draw.CenterMid);
            _hits.Add(new Hit("veh_start", VehStart));

            _hits.Add(new Hit("veh_back", VehBack));
        }

        void DrawVehCard(Graphics g, int id, float x, float y)
        {
            DrawVehCard(g, id, x, y, id % 20);
        }

        void DrawVehCard(Graphics g, int id, float x, float y, int art)
        {
            if (y > VehGrid.Bottom + 10 || y + VehCardH < VehGrid.Y - 10) return;

            RectangleF r = new RectangleF(x, y, VehCardW, VehCardH);
            g.DrawImage(Assets.Get(string.Format("cards/v{0:00}.jpg", art)), r);

            bool sel = id == _vehSel;
            if (sel) DrawSelectedLabel(g, r, 32f, string.Format("cards/v{0:00}l.png", art));
            Draw.HoverOutline(g, r, 4f, sel ? 1f : Hover("veh:" + id), sel ? 0f : 0.85f);
            _hits.Add(new Hit("veh:" + id, r));
        }

        void SectionHeader(Graphics g, string title, float y)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 18, 20, 24)))
                g.FillRectangle(b, VehGrid.X, y, VehGrid.Width, 40);
            Draw.Slash(g, new RectangleF(VehGrid.X + 14, y + 10, 12, 20));
            Draw.Text(g, title, Theme.SectionSmall, Color.White,
                new RectangleF(VehGrid.X + 34, y, 600, 40), Draw.LeftMid);
        }

        // --------------------------------------------------------------- loading
        void DrawLoading(Graphics g)
        {
            if (_loadT < BootLen)
            {
                DrawShaderBoot(g);
                return;
            }

            g.DrawImage(Assets.Get("bg_loading.jpg"), 0, 0, W, H);

            float t = _loadT - BootLen;
            int frame = (int)(t * 20f);
            if (frame > Assets.FrameCount - 1) frame = Assets.FrameCount - 1;
            g.DrawImage(Assets.Frame(frame), new RectangleF(25, 398, 700, 294));

            DrawLoadingCounters(g, t);
        }

        /// <summary>The first seconds of a real launch: compiling shaders on a black screen.</summary>
        void DrawShaderBoot(Graphics g)
        {
            using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, W, H),
                Color.FromArgb(9, 10, 12), Color.FromArgb(22, 24, 28), 90f))
                g.FillRectangle(bg, 0, 0, W, H);

            float raw = Clamp(_loadT / (BootLen - 1.1f), 0f, 1f);
            float p = raw * raw * (3f - 2f * raw);

            Draw.Slash(g, new RectangleF(140, 148, 22, 58));
            Draw.Text(g, "BeamNG.drive", Theme.PageTitle, Color.White,
                new RectangleF(186, 144, 700, 66), Draw.LeftMid);

            int dots = ((int)(_loadT * 3f)) % 4;
            Draw.Text(g, "Compiling shaders" + new string('.', dots), Theme.Section,
                Color.FromArgb(226, 229, 233), new RectangleF(140, 258, 800, 40), Draw.LeftMid);

            int pct = (int)(p * 100f);
            Draw.Text(g, pct + " %", Theme.Section, Theme.Orange,
                new RectangleF(1140, 258, 180, 40), Draw.RightMid);

            RectangleF track = new RectangleF(140, 310, 1180, 5);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(44, 48, 54)))
                g.FillRectangle(b, track);
            using (SolidBrush b = new SolidBrush(Theme.Orange))
                g.FillRectangle(b, track.X, track.Y, track.Width * p, track.Height);

            int variants = 512;
            int done = (int)(p * variants);
            string detail = p >= 0.999f
                ? variants + " / " + variants + " variants · " + variants + " cached · 0 failed"
                : done + " / " + variants + " variants · 0 cached · 1 worker";
            Draw.Text(g, detail, Theme.Load, Color.FromArgb(126, 132, 140),
                new RectangleF(140, 330, 1180, 28), Draw.LeftMid);

            // scrolling compiler log
            int shown = 7;
            int last = (int)(p * BootLog.Length);
            if (last > BootLog.Length - 1) last = BootLog.Length - 1;
            int first = Math.Max(0, last - shown + 1);
            for (int i = first; i <= last && i < BootLog.Length; i++)
            {
                int row = i - first;
                bool current = i == last;
                Color c = current ? Color.FromArgb(206, 211, 217) : Color.FromArgb(112, 118, 126);
                string stamp = string.Format("00:00:{0:00}.{1:000}", i / 3, (i * 137) % 1000);
                Draw.Text(g, stamp, Theme.Mono, Color.FromArgb(74, 79, 86),
                    new RectangleF(140, 406 + row * 30, 120, 26), Draw.LeftMid);
                Draw.Text(g, BootLog[i], Theme.Mono, c,
                    new RectangleF(264, 406 + row * 30, 1050, 26), Draw.LeftMid);
            }

            Draw.Text(g, "BeamNG.drive 0.36.4.0 (build 18294)", Theme.Load,
                Color.FromArgb(92, 98, 106), new RectangleF(140, 992, 700, 24), Draw.LeftMid);
            Draw.Text(g, "Direct3D 11 · 1920 x 1080 · 60 Hz", Theme.Load,
                Color.FromArgb(92, 98, 106), new RectangleF(1180, 992, 600, 24), Draw.RightMid);
        }

        static readonly string[] BootLog =
        {
            "INFO   ShaderDB: loading shader cache",
            "INFO   ShaderDB: cache empty, full rebuild",
            "INFO   D3D11: creating device on adapter 0",
            "INFO   D3D11: feature level 11_1 selected",
            "INFO   RT: shadow map 4096 x 4096",
            "shaderc: terrain_standard       [1/512]",
            "shaderc: terrain_standard      [16/512]",
            "shaderc: terrain_standard      [32/512]",
            "shaderc: vegetation_leaf       [64/512]",
            "shaderc: water_surface         [96/512]",
            "shaderc: vehicle_paint        [128/512]",
            "WARN   shaderc: variant 0x8f31 compile took 4211 ms",
            "shaderc: vehicle_glass        [192/512]",
            "shaderc: road_asphalt         [224/512]",
            "shaderc: sky_atmosphere       [256/512]",
            "shaderc: terrain_standard     [320/512]",
            "shaderc: vegetation_leaf      [384/512]",
            "shaderc: vehicle_paint        [448/512]",
            "shaderc: postfx_bloom         [480/512]",
            "shaderc: postfx_tonemap       [504/512]",
            "shaderc: postfx_taa           [512/512]",
            "INFO   ShaderDB: 512 variants compiled",
            "INFO   ShaderDB: writing cache (41.2 MB)",
            "INFO   Render: shaders ready, 0 failed"
        };

        /// <summary>Live status lines under the baked loading text, so they actually tick.</summary>
        void DrawLoadingCounters(Graphics g, float t)
        {
            float animLen = Assets.FrameCount / 20f;
            float p = Clamp(t / animLen, 0f, 1f);
            int textures = (int)(1284 * p);
            int beams = (int)(388 * Math.Min(1f, p * 1.35f));
            int materials = (int)(2210 * p);

            Draw.TextShadow(g, "Streaming: " + textures + " / 1284 textures", Theme.Load,
                Color.FromArgb(204, 209, 214), new RectangleF(60, 700, 780, 24), Draw.LeftMid);
            Draw.TextShadow(g, "Physics: " + beams + " / 388 beams  ·  " + materials + " materials",
                Theme.Load, Color.FromArgb(204, 209, 214), new RectangleF(60, 726, 780, 24), Draw.LeftMid);
        }

        /// <summary>BeamNG-flavoured orange pointer, drawn at the virtual mouse position.</summary>
        void DrawCursor(Graphics g)
        {
            if (!_mouseInside || _frozen || _screen == Screen.Loading) return;

            float s = 1.55f;
            PointF[] pts =
            {
                new PointF(0f, 0f),
                new PointF(0f, 18f * s),
                new PointF(4.4f * s, 13.6f * s),
                new PointF(7.6f * s, 20f * s),
                new PointF(10.8f * s, 18.5f * s),
                new PointF(7.7f * s, 12.2f * s),
                new PointF(13.4f * s, 12.2f * s)
            };

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddPolygon(pts);
                using (Matrix m = new Matrix())
                {
                    m.Translate(_mouse.X, _mouse.Y);
                    path.Transform(m);
                }

                using (GraphicsPath shadow = (GraphicsPath)path.Clone())
                using (Matrix sm = new Matrix())
                {
                    sm.Translate(2.2f, 2.2f);
                    shadow.Transform(sm);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                        g.FillPath(b, shadow);
                }

                using (SolidBrush b = new SolidBrush(Color.FromArgb(242, 116, 28)))
                    g.FillPath(b, path);
                using (Pen pen = new Pen(Color.FromArgb(255, 255, 255), 1.6f))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, path);
                }
            }
        }

        // ------------------------------------------------------------------ page
        void DrawPage(Graphics g)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, W, H),
                Color.FromArgb(22, 24, 27), Color.FromArgb(10, 11, 13), 90f))
                g.FillRectangle(b, 0, 0, W, H);

            string title, body;
            if (_page == "repository")
            {
                title = "Repository";
                body = "Installed content\n\n" +
                       "  12 / 13 items installed\n\n" +
                       "  BeamNG.drive                     built-in\n" +
                       "  Garage UI Overhaul               mod\n" +
                       "  Realistic Headlights             mod\n" +
                       "  Dynamic Sky                      mod\n" +
                       "  Pro Rally Suspension             mod\n" +
                       "  ...                              8 more";
            }
            else if (_page == "credits")
            {
                title = "Credits";
                body = "BeamNG.drive\n\n" +
                       "  A dynamic soft-body physics vehicle simulator.\n\n" +
                       "  Development team\n" +
                       "  Community contributors\n" +
                       "  Sound design\n" +
                       "  Vehicle artists\n\n" +
                       "  Thanks for playing.";
            }
            else
            {
                title = "Options";
                body = "Graphics\n" +
                       "  Resolution                     1920 x 1080\n" +
                       "  Vertical sync                  Enabled\n" +
                       "  Texture quality                High\n" +
                       "  Shadow quality                 High\n\n" +
                       "Audio\n" +
                       "  Master volume                  80 %\n" +
                       "  Engine volume                  100 %\n\n" +
                       "Controls\n" +
                       "  Input device                   Keyboard";
            }

            Draw.Text(g, title, Theme.PageTitle, Color.White, new RectangleF(80, 70, 900, 60), Draw.LeftMid);
            Draw.Slash(g, new RectangleF(60, 66, 10, 60));

            using (SolidBrush b = new SolidBrush(Color.FromArgb(225, 230, 235)))
                g.DrawString(body, Theme.Body, b, new RectangleF(84, 170, 1400, 800), Draw.LeftTop);

            RectangleF back = new RectangleF(80, 960, 240, 54);
            float h = Hover("page_back");
            Color c1 = Color.FromArgb(60, 62, 66), c2 = Color.FromArgb(40, 42, 46);
            if (h > 0.02f) { c1 = Blend(c1, Theme.Orange, 0.5f * h); c2 = Blend(c2, Theme.Orange, 0.3f * h); }
            Draw.GradientRound(g, back, 3f, c1, c2);
            Draw.Text(g, "Back", Theme.Button, Color.White, back, Draw.CenterMid);
            _hits.Add(new Hit("page_back", back));
        }

        // ------------------------------------------------------------ test hooks
        internal Bitmap Buffer { get { return _buf; } }
        internal void TestScreen(Screen s) { _screen = s; _fade = 0f; _fadeDir = 0; EnterScreen(); }
        internal void TestHover(string id) { _hoverId = id; if (id != "") _hoverAnim[id] = 1f; }
        internal void TestLoading(float t) { _loadT = t; }
        internal void TestCursor(float x, float y) { _mouse = new PointF(x, y); _mouseInside = true; }
        internal void TestMapScroll(float v) { _mapsScroll = _mapsTarget = v; }
        internal void TestVehScroll(float v) { _vehScroll = _vehTarget = v; }
        internal void TestSelect(int map, int veh) { _mapSel = map; _vehSel = veh; }
        internal void TestPage(string page) { _page = page; }

        // ----------------------------------------------------------------- utils
        static string FitName(Graphics g, Font f, string s, float maxWidth)
        {
            if (g.MeasureString(s, f, 4000, StringFormat.GenericTypographic).Width <= maxWidth)
                return s;
            for (int len = s.Length - 1; len > 1; len--)
            {
                string t = s.Substring(0, len) + "\u2026";
                if (g.MeasureString(t, f, 4000, StringFormat.GenericTypographic).Width <= maxWidth)
                    return t;
            }
            return s.Substring(0, 1);
        }

        void DrawUserName(Graphics g, float centerY)
        {
            string n = FitName(g, Theme.Name, UserName, 118);
            Draw.TextShadow(g, n, Theme.Name, Color.White,
                new RectangleF(68, centerY - 15, 124, 30), Draw.LeftMid);
        }

        static float Clamp(float v, float a, float b)
        {
            return v < a ? a : (v > b ? b : v);
        }

        static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tick.Stop();
                _tick.Dispose();
                if (_bufG != null) _bufG.Dispose();
                if (_buf != null) _buf.Dispose();
                if (_dib != IntPtr.Zero) { DeleteObject(_dib); _dib = IntPtr.Zero; }
                if (_memDc != IntPtr.Zero) { DeleteDC(_memDc); _memDc = IntPtr.Zero; }
            }
            base.Dispose(disposing);
        }
    }
}
