using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace BeamPrank
{
    static class Theme
    {
        public static readonly Color Orange = Color.FromArgb(242, 107, 15);
        public static readonly Color OrangeHot = Color.FromArgb(255, 145, 45);
        public static readonly Color Dark = Color.FromArgb(12, 12, 13);
        public static readonly Color White = Color.White;

        public static readonly Font Section = Fonts.Get("Segoe UI", 22f, FontStyle.Bold | FontStyle.Italic);
        public static readonly Font SectionSmall = Fonts.Get("Segoe UI", 19f, FontStyle.Bold | FontStyle.Italic);
        public static readonly Font Button = Fonts.Get("Segoe UI", 24f, FontStyle.Bold);
        public static readonly Font PageTitle = Fonts.Get("Segoe UI", 40f, FontStyle.Bold | FontStyle.Italic);
        public static readonly Font Body = Fonts.Get("Segoe UI", 20f, FontStyle.Regular);
        public static readonly Font BodyBold = Fonts.Get("Segoe UI", 20f, FontStyle.Bold);
        public static readonly Font Small = Fonts.Get("Segoe UI", 16f, FontStyle.Regular);
        public static readonly Font Name = Fonts.Get("Segoe UI", 18f, FontStyle.Bold);
        public static readonly Font NameTiny = Fonts.Get("Segoe UI", 14f, FontStyle.Bold);
        public static readonly Font Dialog = Fonts.Get("Segoe UI", 12f, FontStyle.Regular);
        public static readonly Font DialogBold = Fonts.Get("Segoe UI", 12f, FontStyle.Bold);
    }

    static class Fonts
    {
        public static Font Get(string family, float px, FontStyle style)
        {
            try
            {
                Font f = new Font(family, px, style, GraphicsUnit.Pixel);
                if (f.Name.Equals(family, StringComparison.OrdinalIgnoreCase))
                    return f;
                f.Dispose();
            }
            catch { }
            return new Font("Tahoma", px, style, GraphicsUnit.Pixel);
        }
    }

    static class Draw
    {
        public static readonly StringFormat LeftMid = Make(StringAlignment.Near, StringAlignment.Center);
        public static readonly StringFormat CenterMid = Make(StringAlignment.Center, StringAlignment.Center);
        public static readonly StringFormat RightMid = Make(StringAlignment.Far, StringAlignment.Center);
        public static readonly StringFormat LeftTop = Make(StringAlignment.Near, StringAlignment.Near);

        static StringFormat Make(StringAlignment h, StringAlignment v)
        {
            StringFormat f = new StringFormat(StringFormat.GenericTypographic);
            f.Alignment = h;
            f.LineAlignment = v;
            f.Trimming = StringTrimming.EllipsisCharacter;
            f.FormatFlags |= StringFormatFlags.NoWrap;
            return f;
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2f;
            if (d <= 0f)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float rad, Color c)
        {
            using (GraphicsPath p = Round(r, rad))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void GradientRound(Graphics g, RectangleF r, float rad, Color a, Color b)
        {
            using (GraphicsPath p = Round(r, rad))
            using (LinearGradientBrush br = new LinearGradientBrush(r, a, b, 90f))
                g.FillPath(br, p);
        }

        /// <summary>BeamNG-style hover: bright outline plus a soft interior lift.</summary>
        public static void HoverOutline(Graphics g, RectangleF r, float rad, float strength, float veilScale = 1f)
        {
            if (strength <= 0.01f) return;

            int a = (int)(255 * strength);
            using (GraphicsPath p = Round(r, rad))
            {
                int veil = (int)(60 * strength * veilScale);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(veil, 255, 255, 255)))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(a, Theme.Orange), 3f))
                {
                    pen.Alignment = PenAlignment.Inset;
                    g.DrawPath(pen, p);
                }
            }
        }

        public static void Text(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat fmt)
        {
            using (SolidBrush b = new SolidBrush(c))
                g.DrawString(s, f, b, r, fmt);
        }

        public static void TextShadow(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat fmt)
        {
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                g.DrawString(s, f, sh, new RectangleF(r.X + 2, r.Y + 2, r.Width, r.Height), fmt);
            Text(g, s, f, c, r, fmt);
        }

        /// <summary>Orange angled bar used by the BeamNG section headers.</summary>
        public static void Slash(Graphics g, RectangleF r)
        {
            PointF[] pts =
            {
                new PointF(r.Left + r.Width * 0.45f, r.Top),
                new PointF(r.Right, r.Top),
                new PointF(r.Right - r.Width * 0.45f, r.Bottom),
                new PointF(r.Left, r.Bottom)
            };
            using (SolidBrush b = new SolidBrush(Theme.Orange))
                g.FillPolygon(b, pts);
        }

        public static void SetupQuality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // HighQualityBicubic makes GDI+ resample every blit, even 1:1 ones, and
            // turns a simple hover into a slideshow on weak hardware.
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }
    }
}
