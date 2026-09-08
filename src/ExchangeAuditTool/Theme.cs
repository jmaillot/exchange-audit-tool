using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ExchangeAuditTool
{
    internal static class UiTheme
    {
        public static readonly Color Window = Color.FromArgb(7, 16, 28);
        public static readonly Color Sidebar = Color.FromArgb(9, 20, 34);
        public static readonly Color Surface = Color.FromArgb(12, 25, 41);
        public static readonly Color Surface2 = Color.FromArgb(14, 29, 47);
        public static readonly Color Border = Color.FromArgb(29, 48, 69);
        public static readonly Color Blue = Color.FromArgb(47, 111, 235);
        public static readonly Color BlueHover = Color.FromArgb(60, 125, 246);
        public static readonly Color Text = Color.FromArgb(230, 235, 243);
        public static readonly Color Muted = Color.FromArgb(139, 153, 171);
        public static readonly Color NavText = Color.FromArgb(203, 215, 230);
        public static readonly Color Green = Color.FromArgb(49, 211, 94);
        public static readonly Color Orange = Color.FromArgb(250, 173, 65);
        public static readonly Color Red = Color.FromArgb(248, 113, 113);
        public static readonly Color FieldBack = Color.FromArgb(7, 16, 28);
    }

    internal class RoundedPanel : Panel
    {
        public int CornerRadius { get; set; }
        public Color BorderColor { get; set; }
        public int BorderThickness { get; set; }

        public RoundedPanel()
        {
            CornerRadius = 8;
            BorderColor = UiTheme.Border;
            BorderThickness = 1;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (Width < 4 || Height < 4) return;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Min(CornerRadius, Math.Max(1, Math.Min(Width, Height) / 2 - 1));
            using (GraphicsPath path = BuildRoundRect(rect, radius))
            using (Pen pen = new Pen(BorderColor, BorderThickness))
            {
                Region = new Region(path);
                if (BorderThickness > 0) e.Graphics.DrawPath(pen, path);
            }
        }

        internal static GraphicsPath BuildRoundRect(Rectangle rect, int radius)
        {
            int diameter = Math.Max(2, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal class ModernButton : Button
    {
        public int CornerRadius { get; set; }
        public Color NormalColor { get; set; }
        public Color HoverColor { get; set; }
        public Color BorderColor { get; set; }
        public bool Active { get; set; }

        public ModernButton()
        {
            CornerRadius = 6;
            NormalColor = UiTheme.Surface2;
            HoverColor = Color.FromArgb(22, 42, 65);
            BorderColor = UiTheme.Border;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            ForeColor = UiTheme.Text;
            BackColor = NormalColor;
            Font = new Font("Segoe UI Semibold", 9F);
            Height = 36;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (Enabled) BackColor = HoverColor;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (Enabled) BackColor = Active ? UiTheme.Blue : NormalColor;
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            if (Width < 4 || Height < 4) return;
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Min(CornerRadius, Math.Max(1, Math.Min(Width, Height) / 2 - 1));
            Color bg = BackColor;
            Color fg = ForeColor;
            Color border = Active ? Color.FromArgb(92, 151, 255) : BorderColor;
            if (!Enabled)
            {
                bg = Color.FromArgb(28, 42, 60);
                fg = UiTheme.Muted;
                border = Color.FromArgb(35, 55, 78);
            }
            using (GraphicsPath path = RoundedPanel.BuildRoundRect(rect, radius))
            using (SolidBrush brush = new SolidBrush(bg))
            using (Pen pen = new Pen(border))
            {
                Region = new Region(path);
                pevent.Graphics.FillPath(brush, path);
                pevent.Graphics.DrawPath(pen, path);
                TextRenderer.DrawText(pevent.Graphics, Text, Font, rect, fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                if (Enabled && (Focused || ContainsFocus))
                {
                    Rectangle focusRect = new Rectangle(3, 3, Width - 7, Height - 7);
                    int focusRadius = Math.Max(1, radius - 2);
                    using (GraphicsPath focusPath = RoundedPanel.BuildRoundRect(focusRect, focusRadius))
                    using (Pen focusPen = new Pen(Color.FromArgb(147, 183, 255)))
                    {
                        focusPen.DashStyle = DashStyle.Dash;
                        pevent.Graphics.DrawPath(focusPen, focusPath);
                    }
                }
            }
        }
    }

    internal static class UiAssets
    {
        private static readonly Color Steel = Color.FromArgb(186, 202, 222);
        private static readonly Color SteelDark = Color.FromArgb(64, 82, 105);
        private static readonly Color Accent = Color.FromArgb(66, 138, 247);

        public static Bitmap Render(string key, int size)
        {
            return Render(key, size, false);
        }

        // White variant for selected nav buttons: recolors the standard glyph
        // to white (alpha preserved). Keeps one drawing implementation.
        public static Bitmap Render(string key, int size, bool white)
        {
            string k = (key ?? "").ToLowerInvariant();
            string glyph = Mdl2Glyph(k);
            if (glyph != null && Mdl2Available())
            {
                try { return RenderGlyph(glyph, size, white); }
                catch { }
            }
            Bitmap bmp = RenderCore(key, size);
            if (!white) return bmp;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    Color p = bmp.GetPixel(x, y);
                    if (p.A > 0) bmp.SetPixel(x, y, Color.FromArgb(p.A, 255, 255, 255));
                }
            return bmp;
        }

        // Segoe MDL2 Assets glyphs (ships with Windows 10/11) for crisp,
        // professional nav icons. Codes verified against Microsoft docs.
        // Returns null for keys without a mapping - caller falls back to drawing.
        private static string Mdl2Glyph(string k)
        {
            if (k.Contains("connect")) return "\uE703";
            if (k.Contains("perm")) return "\uE8D7";
            if (k.Contains("folder")) return "\uE8B7";
            if (k.Contains("group")) return "\uE716";
            if (k.Contains("rule") || k.Contains("flow")) return "\uE71C";
            if (k.Contains("shield") || k.Contains("hold") || k.Contains("compliance")) return "\uE72E";
            if (k.Contains("mobile") || k.Contains("device")) return "\uE8EA";
            if (k.Contains("mailbox") || k.Contains("mail")) return "\uE715";
            return null;
        }

        private static bool? _mdl2;

        private static bool Mdl2Available()
        {
            if (_mdl2 != null) return _mdl2.Value;
            try { _mdl2 = new System.Drawing.Text.InstalledFontCollection().Families.Any(f => f.Name == "Segoe MDL2 Assets"); }
            catch { _mdl2 = false; }
            return _mdl2.Value;
        }

        private static Bitmap RenderGlyph(string glyph, int size, bool white)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                using (var font = new Font("Segoe MDL2 Assets", Math.Max(8, size - 2), FontStyle.Regular, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(white ? Color.White : Steel))
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), fmt);
                }
            }
            return bmp;
        }

        private static Bitmap RenderCore(string key, int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                string k = (key ?? "").ToLowerInvariant();
                if (k.Contains("connect")) DrawPlug(g, size);
                else if (k.Contains("perm")) DrawKey(g, size);
                else if (k.Contains("folder")) DrawFolder(g, size);
                else if (k.Contains("group")) DrawGroup(g, size);
                else if (k.Contains("stat")) DrawChart(g, size);
                else if (k.Contains("rule") || k.Contains("flow")) DrawFlow(g, size);
                else if (k.Contains("shield") || k.Contains("hold") || k.Contains("compliance")) DrawShield(g, size);
                else if (k.Contains("mobile") || k.Contains("device")) DrawDevice(g, size);
                else if (k.Contains("close")) DrawGlyph(g, size, "close");
                else if (k.Contains("max")) DrawGlyph(g, size, "max");
                else if (k.Contains("min")) DrawGlyph(g, size, "min");
                else DrawMailbox(g, size);
            }
            return bmp;
        }

        private static System.Drawing.Icon _brandIcon;

        // Builds a proper multi-resolution icon (16 -> 256 px, PNG frames) so the
        // Windows taskbar always has a crisp, clearly visible icon. The old
        // Icon.FromHandle(bmp.GetHicon()) produced a single low-quality, faint frame.
        public static System.Drawing.Icon AppIcon(int size)
        {
            if (_brandIcon != null) return _brandIcon;

            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var frames = new List<byte[]>();
            foreach (int sz in sizes)
            {
                using (var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.Clear(Color.Transparent);
                        DrawBrandEmblem(g, sz);
                    }
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        frames.Add(ms.ToArray());
                    }
                }
            }

            byte[] ico = PackIco(sizes, frames);
            _brandIcon = new System.Drawing.Icon(new MemoryStream(ico));
            return _brandIcon;
        }

        // Packs PNG frames into a valid .ico byte stream (Vista+ PNG-compressed icons).
        private static byte[] PackIco(int[] sizes, List<byte[]> frames)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((short)0);              // reserved
                w.Write((short)1);              // type: 1 = icon
                w.Write((short)frames.Count);   // image count
                int offset = 6 + 16 * frames.Count;
                for (int i = 0; i < frames.Count; i++)
                {
                    int sz = sizes[i];
                    w.Write((byte)(sz >= 256 ? 0 : sz)); // width  (0 => 256)
                    w.Write((byte)(sz >= 256 ? 0 : sz)); // height (0 => 256)
                    w.Write((byte)0);                    // palette color count
                    w.Write((byte)0);                    // reserved
                    w.Write((short)1);                   // color planes
                    w.Write((short)32);                  // bits per pixel
                    w.Write(frames[i].Length);           // size of image data
                    w.Write(offset);                     // offset of image data
                    offset += frames[i].Length;
                }
                foreach (byte[] f in frames) w.Write(f);
                w.Flush();
                return ms.ToArray();
            }
        }

        // Branded emblem: blue gradient disc + white mailbox, matching the Prodware logo.
        // High contrast so it stays visible on both light and dark taskbars.
        private static void DrawBrandEmblem(Graphics g, int size)
        {
            float s = size / 24F;

            // Blue gradient disc background.
            RectangleF disc = new RectangleF(1F * s, 1F * s, 22F * s, 22F * s);
            using (var b = new LinearGradientBrush(disc, Color.FromArgb(74, 146, 255), Color.FromArgb(28, 78, 180), 90F))
                g.FillEllipse(b, disc);
            using (var p = new Pen(Color.FromArgb(150, 255, 255, 255), Math.Max(1F, 0.7F * s)))
                g.DrawEllipse(p, disc);

            // White mailbox body.
            RectangleF body = new RectangleF(6F * s, 8.5F * s, 12F * s, 8.5F * s);
            using (var b = new SolidBrush(Color.White)) Fill(g, b, body, 1.7F * s);

            // Envelope flap in brand blue.
            PointF[] flap =
            {
                new PointF(6.6F * s, 9.1F * s),
                new PointF(12F * s, 13.2F * s),
                new PointF(17.4F * s, 9.1F * s)
            };
            using (var p = new Pen(Color.FromArgb(28, 78, 180), Math.Max(1.3F, 1.6F * s)))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                g.DrawLines(p, flap);
            }
        }

        private static void DrawMailbox(Graphics g, int size)
        {
            float s = size / 24F;
            RectangleF body = new RectangleF(3 * s, 6 * s, 18 * s, 13 * s);
            using (var b = new LinearGradientBrush(body, Steel, Color.FromArgb(120, 140, 165), 90F))
                Fill(g, b, body, 2.4F * s);
            using (var p = new Pen(SteelDark, Math.Max(1F, 1.1F * s))) Draw(g, p, body, 2.4F * s);
            PointF[] flap = { new PointF(3.6F * s, 6.6F * s), new PointF(12 * s, 13.5F * s), new PointF(20.4F * s, 6.6F * s) };
            using (var p = new Pen(Accent, Math.Max(1.4F, 1.7F * s))) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLines(p, flap); }
        }

        private static void DrawFolder(Graphics g, int size)
        {
            float s = size / 24F;
            using (var b = new SolidBrush(Steel))
            {
                g.FillPolygon(b, new PointF[] { new PointF(4*s,7*s), new PointF(9*s,7*s), new PointF(11*s,9*s), new PointF(4*s,9*s) });
                Fill(g, b, new RectangleF(4*s,8*s,16*s,10*s), 1.5F*s);
            }
            using (var b = new SolidBrush(Accent)) Fill(g, b, new RectangleF(4*s,10*s,16*s,8*s), 1.5F*s);
        }

        private static void DrawPlug(Graphics g, int size)
        {
            float s = size / 24F;
            using (var p = new Pen(Steel, Math.Max(1.6F, 2F * s))) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, 4 * s, 12 * s, 10 * s, 12 * s);
                g.DrawLine(p, 20 * s, 12 * s, 14 * s, 12 * s); }
            using (var b = new SolidBrush(Accent)) { g.FillEllipse(b, 8 * s, 8 * s, 4 * s, 4 * s); g.FillEllipse(b, 12 * s, 12 * s, 4 * s, 4 * s); }
        }

        private static void DrawKey(Graphics g, int size)
        {
            float s = size / 24F;
            using (var p = new Pen(Steel, Math.Max(1.5F, 1.9F * s)))
            { g.DrawEllipse(p, 4 * s, 7 * s, 8 * s, 8 * s); g.DrawLine(p, 11 * s, 11 * s, 20 * s, 11 * s); g.DrawLine(p, 17 * s, 11 * s, 17 * s, 15 * s); g.DrawLine(p, 20 * s, 11 * s, 20 * s, 14 * s); }
        }

        private static void DrawGroup(Graphics g, int size)
        {
            float s = size / 24F;
            using (var b = new SolidBrush(Steel)) { g.FillEllipse(b, 5 * s, 5 * s, 6 * s, 6 * s); g.FillEllipse(b, 13 * s, 6 * s, 5 * s, 5 * s); }
            using (var b = new SolidBrush(Accent)) { g.FillRectangle(b, 3 * s, 13 * s, 10 * s, 6 * s); g.FillRectangle(b, 13 * s, 14 * s, 8 * s, 5 * s); }
        }

        private static void DrawChart(Graphics g, int size)
        {
            float s = size / 24F;
            using (var b = new SolidBrush(Steel)) { g.FillRectangle(b, 5 * s, 12 * s, 3 * s, 7 * s); g.FillRectangle(b, 10.5F * s, 8 * s, 3 * s, 11 * s); }
            using (var b = new SolidBrush(Accent)) g.FillRectangle(b, 16 * s, 5 * s, 3 * s, 14 * s);
        }

        private static void DrawFlow(Graphics g, int size)
        {
            float s = size / 24F;
            using (var p = new Pen(Steel, Math.Max(1.5F, 1.9F * s))) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, 5 * s, 7 * s, 15 * s, 7 * s); g.DrawLine(p, 5 * s, 12 * s, 12 * s, 12 * s); g.DrawLine(p, 5 * s, 17 * s, 17 * s, 17 * s); }
            using (var b = new SolidBrush(Accent)) g.FillEllipse(b, 17 * s, 5 * s, 4 * s, 4 * s);
        }

        private static void DrawShield(Graphics g, int size)
        {
            float s = size / 24F;
            PointF[] shield = { new PointF(12 * s, 2.5F * s), new PointF(20 * s, 6 * s), new PointF(18.5F * s, 16 * s), new PointF(12 * s, 21 * s), new PointF(5.5F * s, 16 * s), new PointF(4 * s, 6 * s) };
            using (var b = new SolidBrush(Color.FromArgb(27, 52, 77))) g.FillPolygon(b, shield);
            using (var p = new Pen(Steel, Math.Max(1F, 1.2F * s))) g.DrawPolygon(p, shield);
            using (var p = new Pen(Accent, Math.Max(1.6F, 1.9F * s))) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, 8 * s, 11.5F * s, 11 * s, 14.5F * s); g.DrawLine(p, 11 * s, 14.5F * s, 16.5F * s, 8.5F * s); }
        }

        private static void DrawDevice(Graphics g, int size)
        {
            float s = size / 24F;
            RectangleF body = new RectangleF(7 * s, 3 * s, 10 * s, 18 * s);
            using (var b = new SolidBrush(Steel)) Fill(g, b, body, 2F * s);
            using (var b = new SolidBrush(UiTheme.Window)) g.FillRectangle(b, 8.5F * s, 6 * s, 7 * s, 11 * s);
            using (var b = new SolidBrush(Accent)) g.FillEllipse(b, 11 * s, 18 * s, 2 * s, 2 * s);
        }

        private static void DrawGlyph(Graphics g, int size, string kind)
        {
            using (var p = new Pen(Color.FromArgb(176, 192, 211), 1.25F))
            {
                p.StartCap = LineCap.Square; p.EndCap = LineCap.Square;
                if (kind == "close") { g.DrawLine(p, 4, 4, size - 4, size - 4); g.DrawLine(p, size - 4, 4, 4, size - 4); }
                else if (kind == "max") g.DrawRectangle(p, 3.5F, 3.5F, size - 7F, size - 7F);
                else g.DrawLine(p, 3, size - 4, size - 3, size - 4);
            }
        }

        private static void Fill(Graphics g, Brush b, RectangleF r, float radius)
        { using (GraphicsPath path = Round(r, radius)) g.FillPath(b, path); }
        private static void Draw(Graphics g, Pen p, RectangleF r, float radius)
        { using (GraphicsPath path = Round(r, radius)) g.DrawPath(p, path); }
        private static GraphicsPath Round(RectangleF r, float radius)
        {
            float rad = Math.Max(1F, Math.Min(radius, Math.Min(r.Width, r.Height) / 2F));
            float d = rad * 2F;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
