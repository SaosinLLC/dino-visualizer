# Draws the app icon and writes icon.ico with every size Windows uses, plus icon-256.png for previewing.
# build.ps1 embeds icon.ico. The art: Dino Monitor's weathered white Spinosaurus skull in side view, jaws open wide,
# holding a dark disc made into a music visualizer (glowing blue equalizer bars), on a charcoal tile.
param([string]$OutDir = $PSScriptRoot)
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class IconArt
{
    static readonly Color Bone = Color.FromArgb(232, 230, 222);
    static readonly Color Hole = Color.FromArgb(34, 34, 34);
    static readonly Color Blue = Color.FromArgb(58, 118, 255);
    static readonly Color BlueBright = Color.FromArgb(170, 200, 255);
    static readonly Color Charcoal = Color.FromArgb(44, 44, 44);

    // Everything is drawn on a 256 x 256 canvas and scaled down; small sizes drop the fine detail.
    public static Bitmap Render(int size)
    {
        bool tiny = size <= 24, small = size <= 48;
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.ScaleTransform(size / 256f, size / 256f);
            var rnd = new Random(7);
            DrawBackground(g, rnd, small);
            // The whole skull composition is turned so it looks downward, like the poster.
            g.TranslateTransform(128, 128);
            g.RotateTransform(Turn);
            g.TranslateTransform(-128, -128);
            DrawEmblem(g, tiny, small);
            DrawSkull(g, rnd, tiny, small);
        }
        return bmp;
    }

    static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        p.AddArc(x, y, 2 * r, 2 * r, 180, 90);
        p.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
        p.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
        p.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
        p.CloseFigure();
        return p;
    }

    // Charcoal rounded tile with a soft vignette and grain, like a worn poster.
    static void DrawBackground(Graphics g, Random rnd, bool small)
    {
        using (var bg = RoundRect(4, 4, 248, 248, 40))
        {
            using (var b = new SolidBrush(Charcoal)) g.FillPath(b, bg);
            using (var vig = new PathGradientBrush(bg) { CenterColor = Color.FromArgb(0, 0, 0, 0), SurroundColors = new[] { Color.FromArgb(150, 0, 0, 0) }, FocusScales = new PointF(0.55f, 0.55f) })
                g.FillPath(vig, bg);
            if (small) return;
            g.SetClip(bg);
            for (int i = 0; i < 900; i++)
            {
                int v = rnd.Next(2) == 0 ? 0 : 255;
                using (var b = new SolidBrush(Color.FromArgb(rnd.Next(10, 28), v, v, v)))
                    g.FillRectangle(b, (float)rnd.NextDouble() * 256, (float)rnd.NextDouble() * 256, 1.6f, 1.6f);
            }
            g.ResetClip();
        }
    }

    const float Turn = 90;   // degrees the skull composition is turned (snout pointing down)
    const float EX = 172, EY = 143, ER = 31;   // emblem disc, held between the open jaws

    // Dark disc with a faint grid, made into a music visualizer: glowing blue equalizer bars with bright caps over
    // a soft glow, standing upright on screen whatever way the skull is turned.
    static void DrawEmblem(Graphics g, bool tiny, bool small)
    {
        float cx = EX, cy = EY, r = ER;
        var disc = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);
        using (var b = new SolidBrush(Color.FromArgb(10, 13, 22))) g.FillEllipse(b, disc);
        var saved = g.Transform;
        g.TranslateTransform(cx, cy);
        g.RotateTransform(-Turn);
        g.TranslateTransform(-cx, -cy);
        using (var clip = new GraphicsPath())
        {
            clip.AddEllipse(disc);
            g.SetClip(clip);
            if (!small)
                using (var grid = new Pen(Color.FromArgb(45, Blue), 1.1f))
                    for (float k = -r; k <= r; k += 12) { g.DrawLine(grid, cx + k, cy - r, cx + k, cy + r); g.DrawLine(grid, cx - r, cy + k, cx + r, cy + k); }
            // Soft blue glow behind the bars.
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - r * 0.9f, cy - r * 0.7f, r * 1.8f, r * 1.4f);
                using (var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb(90, Blue), SurroundColors = new[] { Color.FromArgb(0, Blue) } })
                    g.FillPath(pg, path);
            }
            float[] heights = tiny ? new[] { 0.45f, 0.9f, 0.6f } : new[] { 0.35f, 0.7f, 1f, 0.55f, 0.8f };
            int n = heights.Length;
            float span = r * 1.3f, bw = span / n * 0.62f, baseY = cy + r * 0.5f, maxH = r * 1.05f;
            for (int i = 0; i < n; i++)
            {
                float x = cx - span / 2 + span / n * (i + 0.5f) - bw / 2, h = maxH * heights[i];
                var bar = new RectangleF(x, baseY - h, bw, h);
                using (var b = new LinearGradientBrush(new RectangleF(bar.X, baseY - maxH - 1, bw, maxH + 2), BlueBright, Blue, 90f)) g.FillRectangle(b, bar);
                if (!tiny) using (var b = new SolidBrush(Color.FromArgb(240, 245, 255))) g.FillRectangle(b, x, bar.Y - (small ? 6 : 5), bw, small ? 3.5f : 2.5f);
            }
            g.ResetClip();
        }
        g.Transform = saved;
        using (var ring = new Pen(Blue, tiny ? 6 : 3.2f)) g.DrawEllipse(ring, disc);
    }

    const float FootTurn = -56;   // degrees
    static readonly float[] FootPts = { -0.506f, -1f, -0.471f, -1f, -0.4f, -0.929f, -0.329f, -0.906f, -0.294f, -0.824f, -0.176f, -0.718f, 0.012f, -0.647f, 0.118f, -0.388f, 0.341f, -0.153f, 0.4f, -0.024f, 0.447f, -0.047f, 0.447f, -0.541f, 0.506f, -0.6f, 0.529f, -0.659f, 0.541f, -0.8f, 0.588f, -0.835f, 0.624f, -0.788f, 0.694f, -0.753f, 0.718f, -0.588f, 0.776f, -0.529f, 0.765f, -0.518f, 0.812f, -0.435f, 0.835f, -0.047f, 0.871f, 0.035f, 0.918f, 0.282f, 0.882f, 0.718f, 0.859f, 0.8f, 0.788f, 0.906f, 0.635f, 1f, 0.2f, 1f, 0.141f, 0.976f, -0.024f, 0.882f, -0.094f, 0.765f, -0.153f, 0.753f, -0.329f, 0.647f, -0.494f, 0.6f, -0.541f, 0.541f, -0.6f, 0.518f, -0.671f, 0.424f, -0.706f, 0.412f, -0.765f, 0.318f, -0.8f, 0.318f, -0.859f, 0.188f, -0.918f, 0.153f, -0.894f, 0.129f, -0.812f, 0.118f, -0.541f, 0.118f, -0.471f, 0.176f, -0.294f, 0.224f, -0.247f, 0.259f, -0.106f, 0.271f, -0.035f, 0.318f, 0.024f, 0.282f, -0.059f, 0.141f, -0.059f, 0.106f, -0.165f, 0f, -0.235f, -0.141f, -0.365f, -0.294f, -0.424f, -0.412f, -0.471f, -0.529f, -0.459f, -0.694f, -0.529f, -0.859f, -0.518f, -0.988f };
    // A toe of even width w along a curve, with a rounded end and a slightly bulbous tip pad of radius bulb.
    static GraphicsPath RoundToe(PointF a, PointF b, PointF c, PointF d, float w, float bulb)
    {
        var path = new GraphicsPath();
        using (var centre = new GraphicsPath())
        {
            centre.AddBezier(a, b, c, d);
            using (var pen = new Pen(Color.Black, w) { StartCap = LineCap.Round, EndCap = LineCap.Round }) centre.Widen(pen);
            path.AddPath(centre, false);
        }
        path.AddEllipse(d.X - bulb, d.Y - bulb, 2 * bulb, 2 * bulb);
        path.FillMode = FillMode.Winding;
        return path;
    }

    // A toe as a filled shape following a curve, tapering from width w at the base to a point at the tip.
    static GraphicsPath TaperedToe(PointF a, PointF b, PointF c, PointF d, float w)
    {
        const int n = 14;
        var left = new List<PointF>();
        var right = new List<PointF>();
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            var p = Bez(a, b, c, d, t);
            var q = Bez(a, b, c, d, Math.Min(1, t + 0.01f));
            if (i == n) q = new PointF(p.X + (p.X - Bez(a, b, c, d, t - 0.01f).X), p.Y + (p.Y - Bez(a, b, c, d, t - 0.01f).Y));
            float dx = q.X - p.X, dy = q.Y - p.Y, l = (float)Math.Sqrt(dx * dx + dy * dy);
            float nx = -dy / l, ny = dx / l, hw = w / 2 * (1 - t * 0.72f);
            left.Add(new PointF(p.X + nx * hw, p.Y + ny * hw));
            right.Add(new PointF(p.X - nx * hw, p.Y - ny * hw));
        }
        right.Reverse();
        left.AddRange(right);
        var path = new GraphicsPath();
        path.AddPolygon(left.ToArray());
        return path;
    }

    static PointF Bez(PointF a, PointF b, PointF c, PointF d, float t)
    {
        float v = 1 - t;
        return new PointF(v * v * v * a.X + 3 * v * v * t * b.X + 3 * v * t * t * c.X + t * t * t * d.X,
                          v * v * v * a.Y + 3 * v * v * t * b.Y + 3 * v * t * t * c.Y + t * t * t * d.Y);
    }

    // Spinosaurus skull in side view, traced from a photo of a real skull (800 x 800 px, skull facing right):
    // long crocodile-like snout with a rounded, down-turned tip, nostril set far back, a small crest, the slot-shaped
    // antorbital opening, a big eye socket with its bone bar, and a long lower jaw with a hooked back end. The lower
    // jaw is swung open around its joint so the emblem fits between the teeth. Shaded for depth.
    static PointF P(float px, float py) { return new PointF(10 + (px - 20) * 0.3065f, 124 + (py - 350) * 0.3065f); }

    static readonly PointF Joint = new PointF(120, 420);
    const double Gape = 8 * Math.PI / 180;   // extra opening of the lower jaw

    static PointF PL(float px, float py)
    {
        double dx = px - Joint.X, dy = py - Joint.Y;
        return P((float)(Joint.X + dx * Math.Cos(Gape) - dy * Math.Sin(Gape)), (float)(Joint.Y + dx * Math.Sin(Gape) + dy * Math.Cos(Gape)));
    }

    static PointF[] Map(Func<float, float, PointF> f, params float[] xy)
    {
        var p = new PointF[xy.Length / 2];
        for (int i = 0; i < p.Length; i++) p[i] = f(xy[2 * i], xy[2 * i + 1]);
        return p;
    }

    // Root of a tooth: where the vertical through x crosses the tooth-row polyline (photo coordinates).
    static float RowY(float[] row, float x)
    {
        for (int i = 2; i < row.Length; i += 2)
        {
            float x0 = row[i - 2], y0 = row[i - 1], x1 = row[i], y1 = row[i + 1];
            if ((x >= Math.Min(x0, x1)) && (x <= Math.Max(x0, x1)))
                return x1 == x0 ? y0 : y0 + (y1 - y0) * (x - x0) / (x1 - x0);
        }
        return row[1];
    }

    // A slightly curved conical tooth (photo coordinates): root at (x, y), pointing down (dir = 1) or up (dir = -1),
    // leaning back toward the throat, shaded from a darker root to a light tip.
    static void Tooth(Graphics g, Func<float, float, PointF> f, float x, float y, float len, int dir)
    {
        float w = Math.Max(7, len * 0.34f);
        var root1 = f(x - w / 2, y - dir * 4);
        var root2 = f(x + w / 2, y - dir * 4);
        var tip = f(x - len * 0.18f, y + dir * len);
        var bend = f(x + w * 0.15f, y + dir * len * 0.55f);
        using (var path = new GraphicsPath())
        {
            path.AddBezier(root1, f(x - w / 2, y + dir * len * 0.5f), bend, tip);
            path.AddBezier(tip, bend, f(x + w / 2, y + dir * len * 0.45f), root2);
            path.CloseFigure();
            var bounds = path.GetBounds();
            if (bounds.Width < 0.5f || bounds.Height < 0.5f) return;
            using (var b = new LinearGradientBrush(new RectangleF(bounds.X - 1, bounds.Y - 1, bounds.Width + 2, bounds.Height + 2),
                dir > 0 ? Color.FromArgb(196, 186, 164) : Color.FromArgb(246, 243, 234),
                dir > 0 ? Color.FromArgb(246, 243, 234) : Color.FromArgb(196, 186, 164), 90f))
                g.FillPath(b, path);
            using (var p = new Pen(Color.FromArgb(90, 60, 55, 48), 0.8f)) g.DrawPath(p, path);
        }
    }

    // A little blood: stained gums toward the front of both jaws and streaks down some of the front teeth. It stays on
    // the skull; nothing runs off onto the background tile.
    static void DrawBlood(Graphics g, GraphicsPath bones, float[] upperTeeth, float[] lowerTeeth, float[] upperRow, float[] lowerRow, bool small)
    {
        var dark = Color.FromArgb(190, 120, 8, 12);
        var wet = Color.FromArgb(220, 165, 14, 20);
        var old = g.Clip;
        g.SetClip(bones);
        using (var gum = new Pen(dark, small ? 3.6f : 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            g.DrawCurve(gum, Map(P, 540, 316, 600, 309, 660, 297, 720, 290, 760, 296, 782, 304), 0.5f);
            g.DrawCurve(gum, Map(PL, 580, 459, 640, 472, 690, 477, 730, 467, 758, 472), 0.5f);
        }
        g.Clip = old;

        // Streaks from the root toward the tip on every other front tooth.
        using (var streak = new Pen(wet, small ? 2.6f : 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            for (int i = 0; i < upperTeeth.Length; i += 4)
            {
                float x = upperTeeth[i], len = upperTeeth[i + 1] * 1.4f, y = RowY(upperRow, x);
                if (x < 560) continue;
                g.DrawLine(streak, P(x, y), P(x - len * 0.12f, y + len * 0.7f));
            }
            for (int i = 2; i < lowerTeeth.Length; i += 4)
            {
                float x = lowerTeeth[i], len = lowerTeeth[i + 1] * 1.4f, y = RowY(lowerRow, x);
                if (x < 600) continue;
                g.DrawLine(streak, PL(x, y), PL(x - len * 0.12f, y - len * 0.6f));
            }
        }
    }

    static void DrawSkull(Graphics g, Random rnd, bool tiny, bool small)
    {
        var upperOutline = Map(P,
            110, 400, 85, 350, 75, 300, 88, 250, 104, 214, 112, 198, 130, 210, 170, 184, 225, 178, 255, 160, 272, 138,
            292, 156, 316, 186, 360, 208, 420, 228, 480, 240, 560, 246, 640, 240, 700, 232, 750, 228, 778, 236, 792, 258,
            790, 288, 778, 304, 740, 292, 700, 290, 650, 297, 600, 306, 550, 312, 500, 320, 450, 327, 400, 333, 350, 340,
            300, 345, 260, 360, 210, 380, 160, 395, 130, 402);
        var lowerOutline = Map(PL,
            25, 462, 60, 448, 100, 428, 130, 420, 200, 402, 300, 398, 380, 410, 420, 425, 470, 432, 520, 445, 570, 455,
            620, 468, 670, 478, 710, 472, 745, 462, 765, 478, 760, 520, 735, 545, 650, 545, 560, 538, 480, 528, 400, 520,
            340, 532, 260, 512, 170, 494, 90, 480, 40, 474);
        float[] upperRow = { 300, 345, 350, 340, 400, 333, 450, 327, 500, 320, 550, 312, 600, 306, 650, 297, 700, 290, 740, 292, 770, 305, 785, 300 };
        float[] lowerRow = { 300, 398, 380, 410, 420, 425, 470, 432, 520, 445, 570, 455, 620, 468, 670, 478, 710, 472, 745, 462, 762, 478 };
        // Tooth positions and lengths read off the photo; the front ones are the biggest.
        float[] upperTeeth = { 355, 10, 378, 12, 398, 14, 420, 18, 445, 20, 468, 26, 490, 22, 515, 30, 540, 26, 565, 32, 590, 38, 615, 30, 640, 18, 665, 24, 690, 20, 712, 26, 735, 18, 758, 35, 775, 40 };
        float[] lowerTeeth = { 415, 18, 435, 30, 455, 24, 478, 20, 500, 14, 522, 18, 545, 14, 568, 20, 590, 16, 612, 22, 632, 18, 655, 26, 672, 22, 690, 34, 708, 30, 728, 40, 748, 45 };

        using (var upper = new GraphicsPath())
        using (var lower = new GraphicsPath())
        using (var holes = new GraphicsPath())
        using (var bones = new GraphicsPath(FillMode.Winding))
        {
            upper.AddClosedCurve(upperOutline, 0.25f);
            lower.AddClosedCurve(lowerOutline, 0.25f);
            bones.AddPath(upper, false);
            bones.AddPath(lower, false);
            holes.AddClosedCurve(Map(P, 120, 240, 165, 225, 200, 250, 205, 300, 175, 325, 135, 320, 112, 290), 0.4f);   // eye socket
            holes.AddClosedCurve(Map(P, 236, 206, 300, 198, 336, 246, 290, 266, 246, 250), 0.3f);                       // antorbital slot
            holes.AddClosedCurve(Map(P, 90, 300, 104, 290, 110, 330, 95, 336), 0.4f);                                   // opening at the back
            var n = P(445, 268); holes.AddEllipse(n.X - 3.4f, n.Y - 2.2f, 6.8f, 4.4f);                                   // retracted nostril
            holes.AddClosedCurve(Map(PL, 268, 506, 312, 500, 322, 520, 286, 528), 0.4f);                                 // lower-jaw opening

            // Soft drop shadow on the tile for depth.
            using (var shadow = (GraphicsPath)bones.Clone())
            using (var m = new Matrix())
            {
                // Keep the shadow falling down-right on screen whatever way the skull is turned.
                double a = -Turn * Math.PI / 180;
                m.Translate((float)(3 * Math.Cos(a) - 5 * Math.Sin(a)), (float)(3 * Math.Sin(a) + 5 * Math.Cos(a)));
                shadow.Transform(m);
                using (var b = new SolidBrush(Color.FromArgb(small ? 90 : 120, 0, 0, 0))) g.FillPath(b, shadow);
            }

            // Teeth go first so the jaw edges cover their roots.
            int step = tiny ? 3 : small ? 2 : 1;
            for (int i = 0; i < upperTeeth.Length; i += 2 * step)
                Tooth(g, P, upperTeeth[i], RowY(upperRow, upperTeeth[i]), upperTeeth[i + 1] * (tiny ? 1.9f : 1.4f), 1);
            for (int i = 0; i < lowerTeeth.Length; i += 2 * step)
                Tooth(g, PL, lowerTeeth[i], RowY(lowerRow, lowerTeeth[i]), lowerTeeth[i + 1] * (tiny ? 1.9f : 1.4f), -1);

            // Bone: lighter on top, darker underneath, with shadowed edges for roundness.
            var bb = bones.GetBounds();
            using (var b = new LinearGradientBrush(new RectangleF(bb.X, bb.Y - 1, bb.Width, bb.Height + 2), Color.FromArgb(242, 238, 228), Color.FromArgb(186, 178, 162), 90f))
                g.FillPath(b, bones);
            var old = g.Clip;
            g.SetClip(bones);
            using (var edge = new Pen(Color.FromArgb(small ? 60 : 85, 70, 62, 52), small ? 8 : 6)) { g.DrawPath(edge, upper); g.DrawPath(edge, lower); }
            using (var hi = new Pen(Color.FromArgb(120, 255, 255, 255), 1.6f))                                         // light catching the top edge
            {
                g.TranslateTransform(0, 1.6f);
                g.DrawLines(hi, Map(P, 170, 184, 225, 178, 255, 160, 272, 138, 292, 156, 316, 186, 360, 208, 420, 228, 480, 240, 560, 246, 640, 240, 700, 232, 750, 228));
                g.TranslateTransform(0, -1.6f);
            }
            g.Clip = old;

            // Openings, darker toward their upper edge so they read as deep.
            var hb = holes.GetBounds();
            using (var b = new LinearGradientBrush(new RectangleF(hb.X, hb.Y - 1, hb.Width, hb.Height + 2), Color.FromArgb(14, 12, 11), Color.FromArgb(52, 46, 40), 90f))
                g.FillPath(b, holes);
            using (var p = new Pen(Color.FromArgb(120, 90, 80, 66), 1f)) g.DrawPath(p, holes);
            // The bony bar crossing the eye socket.
            using (var bar = new Pen(Color.FromArgb(214, 206, 190), small ? 4 : 2.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawBezier(bar, P(160, 228), P(150, 260), P(140, 290), P(128, 322));

            DrawBlood(g, bones, upperTeeth, lowerTeeth, upperRow, lowerRow, small);

            if (small) return;

            // Sutures, weathering specks and hairline cracks.
            g.SetClip(bones);
            using (var suture = new Pen(Color.FromArgb(110, 80, 72, 60), 1f))
            {
                g.DrawCurve(suture, Map(P, 230, 290, 300, 300, 380, 296, 450, 300), 0.5f);
                g.DrawCurve(suture, Map(P, 330, 215, 350, 260, 345, 330), 0.5f);
                g.DrawCurve(suture, Map(PL, 330, 420, 360, 470, 340, 530), 0.5f);
                g.DrawCurve(suture, Map(PL, 420, 470, 560, 490, 700, 505), 0.5f);
            }
            for (int i = 0; i < 260; i++)
                using (var b = new SolidBrush(Color.FromArgb(rnd.Next(30, 100), 60, 52, 42)))
                {
                    float s = 0.6f + (float)rnd.NextDouble() * 1.8f;
                    g.FillEllipse(b, bb.X + (float)rnd.NextDouble() * bb.Width, bb.Y + (float)rnd.NextDouble() * bb.Height, s, s);
                }
            using (var crack = new Pen(Color.FromArgb(90, 70, 62, 52), 0.9f))
                for (int i = 0; i < 8; i++)
                {
                    var pts = new List<PointF> { new PointF(bb.X + (float)rnd.NextDouble() * bb.Width, bb.Y + (float)rnd.NextDouble() * bb.Height) };
                    for (int k = 0; k < 4; k++)
                        pts.Add(new PointF(pts[k].X + 4 + (float)rnd.NextDouble() * 8, pts[k].Y + ((float)rnd.NextDouble() - 0.5f) * 8));
                    g.DrawLines(crack, pts.ToArray());
                }
            g.Clip = old;
        }
    }

    // 256 px is stored as PNG; smaller sizes as classic 32-bit bitmaps, which every Windows icon reader decodes.
    static byte[] Dib(Bitmap bmp)
    {
        int s = bmp.Width, maskStride = ((s + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(s * s * 4 + maskStride * s); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = s - 1; y >= 0; y--)
                for (int x = 0; x < s; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
                }
            w.Write(new byte[maskStride * s]);
            return ms.ToArray();
        }
    }

    public static void WriteIco(string path, int[] sizes)
    {
        var images = new List<byte[]>();
        foreach (var s in sizes)
            using (var bmp = Render(s))
            {
                if (s < 256) { images.Add(Dib(bmp)); continue; }
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
                }
            }
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
    }
}
'@

[IconArt]::WriteIco("$OutDir\icon.ico", @(16, 20, 24, 32, 40, 48, 64, 128, 256))
$preview = [IconArt]::Render(256); $preview.Save("$OutDir\icon-256.png"); $preview.Dispose()
Write-Host "Wrote $OutDir\icon.ico"
