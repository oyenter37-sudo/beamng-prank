using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        float _loadT;
        float _holdT;
        bool _frozen;
        bool _dialogShown;
        bool _instantCrash;

        Bitmap _buf;
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

        static readonly RectangleF MapsGrid = new RectangleF(6, 95, 1500, 940);
        static readonly RectangleF MapsStart = new RectangleF(1120, 896, 385, 46);
        static readonly RectangleF MapsBack = new RectangleF(0, 0, 130, 46);
        static readonly RectangleF MapsTitleBand = new RectangleF(0, 45, 1506, 50);

        static readonly RectangleF VehGrid = new RectangleF(0, 125, 1483, 837);
        static readonly RectangleF VehStart = new RectangleF(1110, 66, 350, 50);
        static readonly RectangleF VehBack = new RectangleF(0, 0, 130, 46);

        const float MapCardW = 364, MapCardH = 210, MapPitchX = 378, MapPitchY = 210;
        // the game leaves a little extra air before the last row of the 4x4 grid
        static readonly float[] MapRowY = { 0f, 210f, 420f, 662f };
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
            WindowState = FormWindowState.Maximized;
            BackColor = Color.Black;
            DoubleBuffered = true;
            KeyPreview = true;
            Icon = Assets.AppIcon;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            _buf = new Bitmap(W, H);
            _tick = new Timer();
            _tick.Interval = 16;
            _tick.Tick += OnTick;
            _tick.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Cursor = Cursors.Default;
        }

        // ------------------------------------------------------------------ loop
        void OnTick(object sender, EventArgs e)
        {
            float dt = 0.016f;
            _time += dt;

            foreach (string k in new List<string>(_hoverAnim.Keys))
            {
                float target = (k == _hoverId) ? 1f : 0f;
                float v = _hoverAnim[k];
                v += (target - v) * 0.35f;
                if (Math.Abs(target - v) < 0.02f) v = target;
                _hoverAnim[k] = v;
            }
            if (_hoverId != "" && !_hoverAnim.ContainsKey(_hoverId))
                _hoverAnim[_hoverId] = 0f;

            _mapsScroll += (_mapsTarget - _mapsScroll) * 0.25f;
            _vehScroll += (_vehTarget - _vehScroll) * 0.25f;

            if (_fadeDir < 0)
            {
                _fade += dt * 6f;
                if (_fade >= 1f) { _fade = 1f; _screen = _pending; EnterScreen(); _fadeDir = 1; }
            }
            else if (_fadeDir > 0)
            {
                _fade -= dt * 5f;
                if (_fade <= 0f) { _fade = 0f; _fadeDir = 0; }
            }

            if (_screen == Screen.Loading && !_frozen) UpdateLoading(dt);

            Invalidate();
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
            if (_loadT > animLen + 1.0f)
                TriggerCrash(false);
        }

        void TriggerCrash(bool instant)
        {
            if (_frozen) return;
            _instantCrash = instant;
            _frozen = true;
            _holdT = instant ? 0.45f : 0.9f;
            Cursor = Cursors.WaitCursor;

            Timer t = new Timer();
            t.Interval = (int)(_holdT * 1000);
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                ShowCrash();
            };
            t.Start();
        }

        void ShowCrash()
        {
            if (_dialogShown) return;
            _dialogShown = true;
            CrashForm f = new CrashForm();
            f.Show(this);
            f.BringToFront();
        }

        // ---------------------------------------------------------------- input
        PointF ToVirtual(Point p)
        {
            return new PointF((p.X - _ox) / _scale, (p.Y - _oy) / _scale);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_frozen) return;
            PointF v = ToVirtual(e.Location);
            string id = HitTest(v);
            if (id != _hoverId)
            {
                _hoverId = id;
                Cursor = IsClickable(id) ? Cursors.Hand : Cursors.Default;
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverId = "";
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
            using (Graphics g = Graphics.FromImage(_buf))
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
            }

        }

        protected override void OnPaint(PaintEventArgs e)
        {
            RenderScene();

            Graphics gg = e.Graphics;
            gg.InterpolationMode = InterpolationMode.HighQualityBicubic;
            gg.PixelOffsetMode = PixelOffsetMode.HighQuality;
            gg.Clear(Color.Black);

            Rectangle cb = ClientRectangle;
            _scale = Math.Min((float)cb.Width / W, (float)cb.Height / H);
            float dw = W * _scale, dh = H * _scale;
            _ox = (cb.Width - dw) / 2f;
            _oy = (cb.Height - dh) / 2f;
            gg.DrawImage(_buf, new RectangleF(_ox, _oy, dw, dh));
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
                if (sel) DrawSelectedLabel(g, r, 36f, string.Format("cards/m{0:00}l.png", art));
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
                    int card = idx % 15;
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
            DrawVehCard(g, id, x, y, id % 15);
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
            g.DrawImage(Assets.Get("bg_loading.jpg"), 0, 0, W, H);

            int frame = (int)(_loadT * 20f);
            if (frame > Assets.FrameCount - 1) frame = Assets.FrameCount - 1;
            g.DrawImage(Assets.Frame(frame), new RectangleF(25, 398, 700, 294));
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
                if (_buf != null) _buf.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
