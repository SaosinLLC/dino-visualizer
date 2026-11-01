// Dino Visualizer - a music visualizer and "Now Playing" overlay in the Dino Monitor style. Shows what Spotify,
// YouTube, YouTube Music or any player is playing, with album art, player controls and a live visualizer, and puts an
// always-on-top Now Playing widget on screen in one of four looks (Dino, Windows 10, Frutiger Aero, Windows XP).
// It uses only what ships with Windows (the system media controls and the audio session meters), so nothing needs
// installing. Build with build.ps1 (uses the C# compiler that ships with .NET Framework 4.x).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("1.1.2.0")]
[assembly: System.Reflection.AssemblyTitle("Dino Visualizer")]
[assembly: System.Reflection.AssemblyProduct("Dino Visualizer")]
[assembly: System.Reflection.AssemblyCompany("Saosin LLC")]
[assembly: System.Reflection.AssemblyCopyright("Saosin LLC 2026")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0")]

namespace DinoVisualizer
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [STAThread]
        static void Main()
        {
            // Per-monitor awareness keeps the widget sharp on every display; older Windows falls back.
            bool aware = false;
            try { aware = SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            if (!aware) try { SetProcessDPIAware(); } catch { }

            // Started by an update: the old version is closing, so wait for it first.
            var args = Environment.GetCommandLineArgs();
            int after = Array.IndexOf(args, "--after"), pid;
            if (after > 0 && after + 1 < args.Length && int.TryParse(args[after + 1], out pid))
                try { using (var old = Process.GetProcessById(pid)) old.WaitForExit(20000); } catch { }

            // One copy at a time: starting it again just brings the running one's window back.
            bool first;
            using (var single = new Mutex(true, "DinoVisualizer.SingleInstance", out first))
            {
                if (!first)
                {
                    var other = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName)
                                       .FirstOrDefault(p => p.Id != Process.GetCurrentProcess().Id);
                    if (other != null) Native.PostMessage(Native.HwndBroadcast, Native.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Updater.CleanUp();
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2, for the lyrics lookups
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = g.DpiX / 96f;
                Application.Run(new MainForm());
            }
        }
    }

    // ------------------------------------------------------------------ look (shared with Dino Monitor)

    // Retro terminal look: black scratched backdrop, monospace type, thin frames, and a single accent hue
    // that the user can change with the hue slider (everything is derived from Theme.Hue).
    static class Theme
    {
        public static float Scale = 1f;
        public static float Hue = DefaultHue;
        public const float DefaultHue = 225f;
        public static readonly Color Bg = Color.FromArgb(6, 7, 9);
        public static readonly Color Ink = Color.FromArgb(3, 3, 5);
        public static readonly Color Glass = Color.FromArgb(200, 2, 3, 5);
        public static readonly Color Rec = Color.FromArgb(255, 64, 64);
        public static Color Primary, Bright, Dim, Faint, Grid;
        public static event Action Changed;

        // Saturation multiplier for every accent shade: 0 = gray, 1 = the normal look, up to MaxSaturation = most vivid.
        public static float Saturation = 1f;
        public const float MaxSaturation = 1.2f;

        static Theme() { SetHue(DefaultHue); }

        public static void SetHue(float h) { SetColor(h, Saturation); }

        public static void SetSaturation(float s) { SetColor(Hue, s); }

        static void SetColor(float h, float sat)
        {
            Hue = ((h % 360) + 360) % 360;
            Saturation = Math.Max(0, Math.Min(MaxSaturation, sat));
            Primary = Hsv(Hue, Sat(0.82f), 1.0f);
            Bright = Hsv(Hue, Sat(0.42f), 1.0f);
            Dim = Hsv(Hue, Sat(0.8f), 0.64f);
            Faint = Hsv(Hue, Sat(0.75f), 0.38f);
            Grid = Color.FromArgb(48, Primary);
            if (Changed != null) Changed();
        }

        static float Sat(float s) { return Math.Min(1f, s * Saturation); }

        public static Color Hsv(float h, float s, float v)
        {
            h = ((h % 360) + 360) % 360 / 60f;
            int i = (int)Math.Floor(h);
            float f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            float r, g, b;
            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }

        static string F(params string[] families)
        {
            using (var fc = new System.Drawing.Text.InstalledFontCollection())
                foreach (var name in families)
                    if (fc.Families.Any(f => f.Name == name)) return name;
            return "Courier New";
        }

        static readonly string Mono = F("Consolas", "Lucida Console");

        public static readonly Font Body = new Font(Mono, 10f);
        public static readonly Font Header = new Font(Mono, 13f);
        public static readonly Font OptionTitle = new Font(Mono, 18f, FontStyle.Bold);
        public static readonly Font Option = new Font(Mono, 10.5f, FontStyle.Bold);
        public static readonly Font OptionValue = new Font(Mono, 12f);
        public static readonly Font Button = new Font(Mono, 17f, FontStyle.Bold);
        public static readonly Font ButtonSmall = new Font(Mono, 12.5f, FontStyle.Bold);
        public static readonly Font PageTitle = new Font(Mono, 18f, FontStyle.Bold);
        public static readonly Font TileTitle = new Font(Mono, 10f, FontStyle.Bold);
        public static readonly Font TileValue = new Font(Mono, 15f);
        public static readonly Font Stat = new Font(Mono, 20f);
        public static readonly Font Big = new Font(Mono, 72f, FontStyle.Bold);

        public static int S(int v) { return (int)Math.Round(v * Scale); }

        // Letter-spaced text, drawn a character at a time (GDI text ignores alpha, so colors must be opaque).
        // clip: keep to the Graphics clip region (GDI text ignores it otherwise), e.g. for scrolling text.
        public static int DrawSpaced(Graphics g, string s, Font f, Color c, int x, int y, int spacing, bool clip = false)
        {
            int cx = x;
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | (clip ? TextFormatFlags.PreserveGraphicsClipping : 0);
            foreach (char ch in s)
            {
                TextRenderer.DrawText(g, ch.ToString(), f, new Point(cx, y), c, flags);
                cx += CharWidth(g, ch, f) + spacing;
            }
            return cx - x;
        }

        public static int SpacedWidth(IDeviceContext g, string s, Font f, int spacing)
        {
            int w = 0;
            foreach (char ch in s) w += CharWidth(g, ch, f) + spacing;
            return w;
        }

        public static int CharWidth(IDeviceContext g, char ch, Font f)
        {
            string t = ch == ' ' ? "a" : ch.ToString();
            return g == null ? TextRenderer.MeasureText(t, f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width
                             : TextRenderer.MeasureText(g, t, f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        }
    }

    // The painted backdrop shared by the whole window: a worn black terminal screen. Every surface paints the
    // slice of it that sits behind itself, so panels read as dark glass over a single fixed scene. The scene is laid
    // out for the whole screen and fitted to cover the window (like a wallpaper set to Fill), but only the picture at
    // the window's own size is kept: it's made again in the background when the window changes size (the old one
    // stretched to fit meanwhile), and let go while the window is hidden.
    static class Backdrop
    {
        static Bitmap cache;            // the backdrop at the window's size
        static Size screen, making;     // the screen it's laid out for; the size being made in the background
        static readonly object sync = new object();
        public static event Action Ready;   // a new picture is ready (raised on a background thread)

        // Makes the picture for the window in the background (it paints plain black until that's ready).
        public static void Start(Size screenSize, Size window)
        {
            lock (sync)
            {
                screen = screenSize;
                Request(window);
            }
        }

        // The window is hidden: its picture goes until it's shown again.
        public static void Release()
        {
            lock (sync)
            {
                if (cache != null) cache.Dispose();
                cache = null;
            }
        }

        // Starts making the picture at this size, unless it's already on its way. Called with the lock held.
        static void Request(Size size)
        {
            if (Fast || size == making || size.Width <= 0 || size.Height <= 0 || screen.IsEmpty) return;
            making = size;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Bitmap b = null;
                try { b = Make(size); } catch { }
                lock (sync)
                {
                    if (making == size)
                    {
                        making = Size.Empty;
                        if (b != null)
                        {
                            if (cache != null) cache.Dispose();
                            cache = b;
                            b = null;
                        }
                    }
                }
                if (b != null) b.Dispose();   // the window changed size again while it was being made
                var ready = Ready;
                if (ready != null) ready();
            });
        }

        // The scene, laid out for the screen and fitted to cover a picture the window's size.
        static Bitmap Make(Size size)
        {
            Size s;
            lock (sync) s = screen;
            float k = Math.Max(size.Width / (float)s.Width, size.Height / (float)s.Height);
            var b = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.FromArgb(9, 10, 12));
                g.TranslateTransform((size.Width - s.Width * k) / 2, (size.Height - s.Height * k) / 2);
                g.ScaleTransform(k, k);
                Scratch(g, s.Width, s.Height);
                Vignette(g, s.Width, s.Height);
            }
            Grain(b, k);
            return b;
        }

        static void Scratch(Graphics g, int w, int h)
        {
            var rnd = new Random(2024);
            float k = h / 1080f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Uneven grime: big soft lighter and darker patches.
            for (int i = 0; i < 16; i++)
            {
                int shade = rnd.Next(2) == 0 ? 130 : 0;
                Glow(g, new PointF(R(rnd) * w, R(rnd) * h), w * (0.08f + R(rnd) * 0.25f), Color.FromArgb(rnd.Next(8, 22), shade, shade, shade + 5));
            }
            // Long faint scratches sweeping across.
            for (int i = 0; i < 90; i++)
            {
                var p0 = new PointF(R(rnd) * w, R(rnd) * h);
                double a = R(rnd) * Math.PI;
                float len = (60 + R(rnd) * w * 0.35f) * (0.5f + R(rnd));
                var p3 = new PointF(p0.X + (float)Math.Cos(a) * len, p0.Y + (float)Math.Sin(a) * len);
                var bend = new PointF((p0.X + p3.X) / 2 + (R(rnd) - 0.5f) * len * 0.15f, (p0.Y + p3.Y) / 2 + (R(rnd) - 0.5f) * len * 0.15f);
                using (var pen = new Pen(Color.FromArgb(rnd.Next(10, 38), 190, 190, 195), (0.6f + R(rnd)) * k))
                    g.DrawBezier(pen, p0, bend, bend, p3);
            }
            // Short sharp nicks.
            for (int i = 0; i < 420; i++)
            {
                float x = R(rnd) * w, y = R(rnd) * h, len = (4 + R(rnd) * 36) * k;
                double a = R(rnd) * Math.PI;
                using (var pen = new Pen(Color.FromArgb(rnd.Next(16, 60), 200, 200, 205), (0.5f + R(rnd) * 0.8f) * k))
                    g.DrawLine(pen, x, y, x + (float)Math.Cos(a) * len, y + (float)Math.Sin(a) * len);
            }
            // Dust specks and a few smudges.
            for (int i = 0; i < 1600; i++)
            {
                float s = (0.8f + R(rnd) * 2.6f) * k;
                using (var br = new SolidBrush(Color.FromArgb(rnd.Next(20, 90), 210, 210, 215)))
                    g.FillEllipse(br, R(rnd) * w, R(rnd) * h, s, s * (0.6f + R(rnd) * 0.8f));
            }
            for (int i = 0; i < 18; i++)
            {
                float s = (6 + R(rnd) * 18) * k;
                using (var br = new SolidBrush(Color.FromArgb(rnd.Next(10, 30), 200, 200, 205)))
                    g.FillEllipse(br, R(rnd) * w, R(rnd) * h, s, s * 0.7f);
            }
        }

        public static void Paint(Graphics g, Control c)
        {
            var form = c.FindForm();
            if (form == null) { g.Clear(Theme.Bg); return; }
            var size = form.ClientSize;
            lock (sync)
            {
                if (cache == null || cache.Size != size) Request(size);
                if (cache == null) { g.Clear(Theme.Bg); return; }
                Point p = c == form ? Point.Empty : form.PointToClient(c.PointToScreen(Point.Empty));
                var r = Rectangle.Ceiling(g.ClipBounds);
                if (cache.Size == size)
                {
                    g.DrawImage(cache, r, new Rectangle(r.X + p.X, r.Y + p.Y, r.Width, r.Height), GraphicsUnit.Pixel);
                    return;
                }
                // The window has just changed size: the old picture stretched to fit until the new one's ready.
                float kx = cache.Width / (float)size.Width, ky = cache.Height / (float)size.Height;
                var mode = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.Low;
                g.DrawImage(cache, r, new RectangleF((r.X + p.X) * kx, (r.Y + p.Y) * ky, r.Width * kx, r.Height * ky), GraphicsUnit.Pixel);
                g.InterpolationMode = mode;
            }
        }

        // While the user drags the window edge the old picture just stretches, so resizing stays smooth; the main
        // form clears this when the drag ends, and the picture is made again at the new size.
        public static volatile bool Fast;

        static float R(Random rnd) { return (float)rnd.NextDouble(); }

        static void Glow(Graphics g, PointF c, float r, Color color)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(c.X - r, c.Y - r * 0.7f, r * 2, r * 1.4f);
                using (var pg = new PathGradientBrush(path) { CenterColor = color, SurroundColors = new[] { Color.FromArgb(0, color) } })
                    g.FillPath(pg, path);
            }
        }

        // Darker toward the edges and corners.
        static void Vignette(Graphics g, int w, int h)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-w * 0.25f, -h * 0.35f, w * 1.5f, h * 1.7f);
                using (var pg = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb(0, 0, 0, 0), SurroundColors = new[] { Color.FromArgb(235, 0, 0, 0) },
                    FocusScales = new PointF(0.45f, 0.45f)
                })
                    g.FillPath(pg, path);
            }
        }

        // Film grain, a row at a time (fainter when the scene is shrunk to fit, as it would be if scaled down).
        static void Grain(Bitmap b, float k)
        {
            int w = b.Width, h = b.Height, amount = Math.Max(1, (int)Math.Round(6 * Math.Min(1, k)));
            var data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                var row = new byte[w * 4];
                var rnd = new Random(42);
                for (int y = 0; y < h; y++)
                {
                    IntPtr line = data.Scan0 + y * data.Stride;
                    Marshal.Copy(line, row, 0, row.Length);
                    for (int i = 0; i < row.Length; i += 4)
                    {
                        int n = rnd.Next(-amount, amount + 1);
                        for (int c = 0; c < 3; c++) row[i + c] = (byte)Math.Max(0, Math.Min(255, row[i + c] + n));
                    }
                    Marshal.Copy(row, 0, line, row.Length);
                }
            }
            finally { b.UnlockBits(data); }
        }
    }

    // Hands memory the app isn't using back to Windows: a full collection (compacting the big objects too, once) and
    // the working set emptied, so Windows counts only what's still in use as it's touched again. Runs in the
    // background.
    static class Ram
    {
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

        static int busy;

        public static void Trim()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
                }
                catch { }
                finally { busy = 0; }
            });
        }
    }

    static class Util
    {
        public static double Num(string s)
        {
            if (s == null) return double.NaN;
            double v;
            return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
        }

        public static string Clock(TimeSpan t)
        {
            return string.Format("{0:00}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
        }

        public static string Bytes(long n)
        {
            return n >= 1L << 30 ? (n / (double)(1L << 30)).ToString("0.00") + " GB" : (n / 1048576.0).ToString("0.0") + " MB";
        }

        public static long FileSize(string path)
        {
            try { return new FileInfo(path).Length; } catch { return 0; }
        }
    }

    static class UiDraw
    {
        // CRT-style scanlines: faint dark rows every few pixels. They are aligned to the window, so lines stay
        // continuous across controls, and only really show on the bright accent parts (black stays black).
        public static int ScanlineAlpha = 70;

        public static void Scanlines(Graphics g, Control c, Rectangle area)
        {
            if (ScanlineAlpha <= 0) return;
            var form = c.FindForm();
            int oy = form == null || c == form ? 0 : form.PointToClient(c.PointToScreen(Point.Empty)).Y;
            var clip = Rectangle.Intersect(Rectangle.Ceiling(g.ClipBounds), area);
            if (clip.Width <= 0 || clip.Height <= 0) return;
            int period = Math.Max(3, Theme.S(3)), thick = Math.Max(1, Theme.S(1));
            int y = clip.Top - ((clip.Top + oy) % period + period) % period;
            using (var b = new SolidBrush(Color.FromArgb(ScanlineAlpha, 0, 0, 0)))
                for (; y < clip.Bottom; y += period) g.FillRectangle(b, clip.X, y, clip.Width, thick);
        }

        public static void Scanlines(Graphics g, Control c) { Scanlines(g, c, c.ClientRectangle); }

        // A framed button with a solid tab on its left edge and a bold label inside (the bottom bar's look).
        public static void TabButton(Graphics g, Rectangle r, string text, Font font, bool hover, bool disabled)
        {
            int tab = Theme.S(16), sp = Theme.S(1);
            hover = hover && !disabled;
            Color c = disabled ? Theme.Faint : hover ? Theme.Bright : Theme.Primary;
            using (var b = new SolidBrush(disabled ? Theme.Faint : Theme.Dim)) g.FillRectangle(b, r.X, r.Y, tab, r.Height);
            var frame = new Rectangle(r.X + tab + Theme.S(2), r.Y, r.Width - tab - Theme.S(2), r.Height);
            if (hover) using (var b = new SolidBrush(Color.FromArgb(55, Theme.Primary))) g.FillRectangle(b, frame);
            Box(g, frame, Theme.S(3), disabled ? Theme.Faint : Theme.Primary);
            int tw = Theme.SpacedWidth(g, text, font, sp);
            Theme.DrawSpaced(g, text, font, c, frame.X + (frame.Width - tw) / 2, frame.Y + (frame.Height - font.Height) / 2, sp);
        }

        // How wide a TabButton needs to be for its label.
        public static int TabButtonWidth(IDeviceContext g, string text, Font font)
        {
            return Theme.S(18) + Theme.SpacedWidth(g, text, font, Theme.S(1)) + (font == Theme.Button ? Theme.S(40) : Theme.S(22));
        }

        // Rectangle outline drawn inward with the given thickness (crisp at any DPI, unlike centered pens).
        public static void Box(Graphics g, Rectangle r, int t, Color c)
        {
            using (var b = new SolidBrush(c))
            {
                g.FillRectangle(b, r.X, r.Y, r.Width, t);
                g.FillRectangle(b, r.X, r.Bottom - t, r.Width, t);
                g.FillRectangle(b, r.X, r.Y, t, r.Height);
                g.FillRectangle(b, r.Right - t, r.Y, t, r.Height);
            }
        }

        public static void DownTriangle(Graphics g, Color c, int cx, int cy, int size)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(c))
                g.FillPolygon(b, new[] { new Point(cx - size, cy - size / 2), new Point(cx + size, cy - size / 2), new Point(cx, cy + size / 2 + 1) });
            g.SmoothingMode = SmoothingMode.None;
        }

        public static void UpTriangle(Graphics g, Color c, int cx, int cy, int size)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(c))
                g.FillPolygon(b, new[] { new Point(cx - size, cy + size / 2), new Point(cx + size, cy + size / 2), new Point(cx, cy - size / 2 - 1) });
            g.SmoothingMode = SmoothingMode.None;
        }
    }

    // Options are kept in %AppData%\Dino Visualizer\settings.ini. On the very first run the widget's look, place and
    // size and the accent color are brought over from Dino Recorder, if it was used before.
    static class Settings
    {
        static readonly string Folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        static readonly string FilePath = Path.Combine(Folder, "Dino Visualizer", "settings.ini");
        static readonly string[] FromRecorder = { "widget", "widgetstyle", "widgetx", "widgety", "widgetscale", "widgetopacity", "hue", "saturation", "scanlines" };
        static readonly Dictionary<string, string> values = Load();

        static Dictionary<string, string> Read(string path)
        {
            var d = new Dictionary<string, string>();
            try
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch { }
            return d;
        }

        static Dictionary<string, string> Load()
        {
            if (File.Exists(FilePath)) return Read(FilePath);
            var d = new Dictionary<string, string>();
            var recorder = Read(Path.Combine(Folder, "Dino Recorder", "settings.ini"));
            foreach (var key in FromRecorder)
            {
                string v;
                if (recorder.TryGetValue(key, out v)) d[key] = v;
            }
            string viz;
            if (recorder.TryGetValue("xpviz", out viz)) d["viz"] = viz;
            d["widget"] = "1";   // the widget is what this app is for, so it starts on
            return d;
        }

        public static string Str(string key, string def)
        {
            string s;
            return values.TryGetValue(key, out s) ? s : def;
        }

        public static double Num(string key, double def)
        {
            double v = Util.Num(Str(key, null));
            return double.IsNaN(v) ? def : v;
        }

        public static void Set(string key, object value)
        {
            values[key] = Convert.ToString(value, CultureInfo.InvariantCulture);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, values.Select(kv => kv.Key + "=" + kv.Value).ToArray());
            }
            catch { }
        }
    }

    // Starting with Windows: a "Dino Visualizer" entry in this user's Run key (HKCU\...\CurrentVersion\Run), the usual
    // place for apps that start when you sign in. With "--tray" it starts hidden in the tray. The entry itself is the
    // setting, so turning it off in Task Manager's Startup apps or elsewhere shows here too.
    static class AutoStart
    {
        public static readonly string[] Names = { "Off", "On", "On (In Tray)" };
        public static string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "Dino Visualizer", TrayArg = "--tray";

        // Started by Windows with "--tray": stay in the tray.
        public static bool InTray { get { return Environment.GetCommandLineArgs().Skip(1).Any(a => a.Equals(TrayArg, StringComparison.OrdinalIgnoreCase)); } }

        static string Entry()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(KeyPath))
                    return key == null ? null : key.GetValue(ValueName) as string;
            }
            catch { return null; }
        }

        // 0 off, 1 on, 2 on in the tray.
        public static int Mode
        {
            get
            {
                string e = Entry();
                return e == null ? 0 : e.IndexOf(TrayArg, StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 1;
            }
            set
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(KeyPath))
                    {
                        if (value == 0) key.DeleteValue(ValueName, false);
                        else key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"" + (value == 2 ? " " + TrayArg : ""));
                    }
                }
                catch { }
            }
        }

        // If the app has moved since it was set to start with Windows, the entry follows it here.
        public static void Follow()
        {
            string e = Entry();
            if (e != null && e.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) < 0) Mode = Mode;
        }
    }

    // Keeps the app up to date from its GitHub releases (github.com/<Repo>/releases). Now and then it asks GitHub for
    // the latest release; when that's a newer version it downloads the release's DinoVisualizer.exe next to this one,
    // checks it really is this app at that version, swaps it in (a running exe can be renamed, though not
    // overwritten) and restarts into it. Only the release's details and its exe are fetched, over HTTPS from GitHub.
    static class Updater
    {
        public static string Repo = "SaosinLLC/dino-visualizer";
        public static string Api = "https://api.github.com/repos/";
        const string AssetName = "DinoVisualizer.exe";
        public static readonly string[] Names = { "Notify Me", "Install Automatically", "Off" };
        public static int Mode;
        public static readonly Version Current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

        public enum Step { Idle, Checking, UpToDate, Available, Downloading, Ready, Failed }
        public static volatile Step Now = Step.Idle;
        public static Version Latest;
        public static string Problem = "";
        public static volatile int Percent;
        static string downloadUrl;
        public static event Action Changed;   // raised on a background thread

        public static string Shown(Version v) { return v == null ? "" : v.ToString(v.Revision > 0 ? 4 : 3); }

        public static string ExeFile;   // the exe to update (tests point it elsewhere); normally this one
        static string ExePath { get { return ExeFile ?? Application.ExecutablePath; } }
        static string OldPath { get { return ExePath + ".old"; } }
        static string NewPath { get { return ExePath + ".download"; } }

        static void Go(Step s)
        {
            Now = s;
            var changed = Changed;
            if (changed != null) changed();
        }

        static void Fail(string problem)
        {
            Problem = problem;
            Go(Step.Failed);
        }

        // At startup: what's left of the last update (the old exe, or a download that didn't finish) goes.
        public static void CleanUp()
        {
            foreach (var p in new[] { OldPath, NewPath })
                try { if (File.Exists(p)) File.Delete(p); } catch { }
        }

        public static void Check()
        {
            if (Now == Step.Checking || Now == Step.Downloading || Now == Step.Ready) return;
            Go(Step.Checking);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int status;
                    var release = Web.Send("GET", Api + Repo + "/releases/latest", null, null, null, out status);
                    if (status == 0) { Fail("Offline"); return; }
                    if (status == 404) { Fail("No Releases Yet"); return; }
                    Version v;
                    string tag = Web.Str(release, "tag_name") ?? "";
                    if (!Version.TryParse(Regex.Replace(tag, "^[vV]", ""), out v)) { Fail("Check Failed"); return; }
                    string url = null;
                    var assets = Web.At(release, "assets") as object[];
                    if (assets != null)
                        foreach (var a in assets)
                            if (string.Equals(Web.Str(a, "name"), AssetName, StringComparison.OrdinalIgnoreCase)) url = Web.Str(a, "browser_download_url");
                    Latest = Normal(v);
                    downloadUrl = url;
                    Go(Latest > Normal(Current) && url != null ? Step.Available : Step.UpToDate);
                }
                catch { Fail("Check Failed"); }
            });
        }

        // 1.2 and 1.2.0.0 are the same version.
        static Version Normal(Version v) { return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision)); }

        // Downloads and swaps in the new version; Ready means it's in place and the app should restart (Restart).
        public static void Install()
        {
            if (Now != Step.Available || downloadUrl == null) return;
            Percent = 0;
            Go(Step.Downloading);
            string url = downloadUrl;
            var want = Latest;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.UserAgent = "DinoVisualizer/" + Shown(Current) + " (Windows desktop app)";
                    req.Timeout = 30000;
                    req.ReadWriteTimeout = 30000;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var from = resp.GetResponseStream())
                    using (var to = File.Create(NewPath))
                    {
                        long total = resp.ContentLength, got = 0;
                        var buffer = new byte[81920];
                        int n, shown = -1;
                        while ((n = from.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            to.Write(buffer, 0, n);
                            got += n;
                            int pct = total > 0 ? (int)(got * 100 / total) : 0;
                            if (pct != shown) { shown = pct; Percent = pct; Go(Step.Downloading); }
                        }
                    }
                    // It has to be this app, at the version the release says.
                    var name = System.Reflection.AssemblyName.GetAssemblyName(NewPath);
                    if (name.Name != "DinoVisualizer" || Normal(name.Version) != want) { File.Delete(NewPath); Fail("Update Failed"); return; }
                    if (File.Exists(OldPath)) File.Delete(OldPath);
                    File.Move(ExePath, OldPath);
                    try { File.Move(NewPath, ExePath); }
                    catch { File.Move(OldPath, ExePath); throw; }
                    Go(Step.Ready);
                }
                catch
                {
                    try { if (File.Exists(NewPath)) File.Delete(NewPath); } catch { }
                    Fail("Update Failed");
                }
            });
        }

        // Starts the new version, which waits for this one to close before it opens (see Program.Main).
        public static void Restart(bool inTray)
        {
            try { Process.Start(ExePath, "--after " + Process.GetCurrentProcess().Id + (inTray ? " --tray" : "")); } catch { }
        }
    }

    static class Native
    {
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, int mods, int key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string name);

        public static readonly IntPtr HwndBroadcast = new IntPtr(0xffff);
        // Sent by a second copy of the app to ask the running one to show its window.
        public static readonly int ShowMessage = RegisterWindowMessage("DinoVisualizer.ShowWindow");
    }

    static class Com
    {
        public static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }

        public static void Release(object o) { if (o != null) Marshal.ReleaseComObject(o); }
    }

    static class ProcessInfo
    {
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder path, ref int size);

        public static string ImagePath(int pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);   // query limited information
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }
    }

    // Dino Monitor's Spinosaurus skull (the app icon's tracing: side view facing right, jaws open) as hologram line art,
    // plus its dino footprint outline. Photo coordinates (an 800 px tracing) are mapped onto the camera's design grid.
    static class SkullArt
    {
        const float K = 0.32f;
        static readonly PointF Joint = new PointF(120, 420);   // the lower jaw swings open around this
        const double Gape = 8 * Math.PI / 180;

        static PointF P(float px, float py) { return new PointF(5 + (px - 25) * K, 2 + (py - 138) * K); }

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

        static float RowY(float[] row, float x)
        {
            for (int i = 2; i < row.Length; i += 2)
                if (x >= Math.Min(row[i - 2], row[i]) && x <= Math.Max(row[i - 2], row[i]))
                    return row[i] == row[i - 2] ? row[i - 1] : row[i - 1] + (row[i + 1] - row[i - 1]) * (x - row[i - 2]) / (row[i] - row[i - 2]);
            return row[1];
        }

        static readonly float[] Upper =
        {
            110, 400, 85, 350, 75, 300, 88, 250, 104, 214, 112, 198, 130, 210, 170, 184, 225, 178, 255, 160, 272, 138,
            292, 156, 316, 186, 360, 208, 420, 228, 480, 240, 560, 246, 640, 240, 700, 232, 750, 228, 778, 236, 792, 258,
            790, 288, 778, 304, 740, 292, 700, 290, 650, 297, 600, 306, 550, 312, 500, 320, 450, 327, 400, 333, 350, 340,
            300, 345, 260, 360, 210, 380, 160, 395, 130, 402
        };
        static readonly float[] Lower =
        {
            25, 462, 60, 448, 100, 428, 130, 420, 200, 402, 300, 398, 380, 410, 420, 425, 470, 432, 520, 445, 570, 455,
            620, 468, 670, 478, 710, 472, 745, 462, 765, 478, 760, 520, 735, 545, 650, 545, 560, 538, 480, 528, 400, 520,
            340, 532, 260, 512, 170, 494, 90, 480, 40, 474
        };
        static readonly float[] UpperRow = { 300, 345, 350, 340, 400, 333, 450, 327, 500, 320, 550, 312, 600, 306, 650, 297, 700, 290, 740, 292, 770, 305, 785, 300 };
        static readonly float[] LowerRow = { 300, 398, 380, 410, 420, 425, 470, 432, 520, 445, 570, 455, 620, 468, 670, 478, 710, 472, 745, 462, 762, 478 };
        static readonly float[] UpperTeeth = { 378, 12, 420, 18, 468, 26, 515, 30, 565, 32, 615, 30, 665, 24, 712, 26, 758, 35, 775, 40 };
        static readonly float[] LowerTeeth = { 435, 30, 478, 20, 522, 18, 568, 20, 612, 22, 655, 26, 690, 34, 728, 40, 748, 45 };

        // Footprint outline, normalized to a height of 2 units around its center (traced from a dino-track cutout).
        public static readonly float[] Foot =
        {
            -0.506f, -1f, -0.471f, -1f, -0.4f, -0.929f, -0.329f, -0.906f, -0.294f, -0.824f, -0.176f, -0.718f, 0.012f, -0.647f, 0.118f, -0.388f,
            0.341f, -0.153f, 0.4f, -0.024f, 0.447f, -0.047f, 0.447f, -0.541f, 0.506f, -0.6f, 0.529f, -0.659f, 0.541f, -0.8f, 0.588f, -0.835f,
            0.624f, -0.788f, 0.694f, -0.753f, 0.718f, -0.588f, 0.776f, -0.529f, 0.765f, -0.518f, 0.812f, -0.435f, 0.835f, -0.047f, 0.871f, 0.035f,
            0.918f, 0.282f, 0.882f, 0.718f, 0.859f, 0.8f, 0.788f, 0.906f, 0.635f, 1f, 0.2f, 1f, 0.141f, 0.976f, -0.024f, 0.882f, -0.094f, 0.765f,
            -0.153f, 0.753f, -0.329f, 0.647f, -0.494f, 0.6f, -0.541f, 0.541f, -0.6f, 0.518f, -0.671f, 0.424f, -0.706f, 0.412f, -0.765f, 0.318f,
            -0.8f, 0.318f, -0.859f, 0.188f, -0.918f, 0.153f, -0.894f, 0.129f, -0.812f, 0.118f, -0.541f, 0.118f, -0.471f, 0.176f, -0.294f, 0.224f,
            -0.247f, 0.259f, -0.106f, 0.271f, -0.035f, 0.318f, 0.024f, 0.282f, -0.059f, 0.141f, -0.059f, 0.106f, -0.165f, 0f, -0.235f, -0.141f,
            -0.365f, -0.294f, -0.424f, -0.412f, -0.471f, -0.529f, -0.459f, -0.694f, -0.529f, -0.859f, -0.518f, -0.988f
        };

        // The skull as flat shapes in the camera art's design coordinates, ready to be built into the 3D hologram:
        // closed outlines (both jaws first, then the openings) and open strokes (the eye bar and the teeth). Curves
        // are flattened into points, close enough that nobody sees the corners at hologram size.
        public static void Shapes(List<PointF[]> outlines, List<PointF[]> strokes)
        {
            using (var m = new Matrix())
            {
                Action<PointF[], float> closed = (pts, tension) =>
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddClosedCurve(pts, tension);
                        path.Flatten(m, 0.7f);
                        outlines.Add(path.PathPoints);
                    }
                };
                closed(Map(P, Upper), 0.25f);
                closed(Map(PL, Lower), 0.25f);
                closed(Map(P, 120, 240, 165, 225, 200, 250, 205, 300, 175, 325, 135, 320, 112, 290), 0.4f);   // eye socket
                closed(Map(P, 236, 206, 300, 198, 336, 246, 290, 266, 246, 250), 0.3f);                       // antorbital slot
                closed(Map(PL, 268, 506, 312, 500, 322, 520, 286, 528), 0.4f);                                 // jaw opening
                var n = P(445, 268);
                closed(new[] { new PointF(n.X - 2.2f, n.Y), new PointF(n.X, n.Y - 1.4f), new PointF(n.X + 2.2f, n.Y), new PointF(n.X, n.Y + 1.4f) }, 0.5f);   // nostril
                using (var path = new GraphicsPath())
                {
                    path.AddBezier(P(160, 228), P(150, 260), P(140, 290), P(128, 322));                         // eye bar
                    path.Flatten(m, 0.7f);
                    strokes.Add(path.PathPoints);
                }
            }
            for (int i = 0; i < UpperTeeth.Length; i += 2)
            {
                float x = UpperTeeth[i], len = UpperTeeth[i + 1] * 1.4f, y = RowY(UpperRow, x);
                strokes.Add(new[] { P(x - 6, y), P(x - len * 0.2f, y + len), P(x + 6, y) });
            }
            for (int i = 0; i < LowerTeeth.Length; i += 2)
            {
                float x = LowerTeeth[i], len = LowerTeeth[i + 1] * 1.4f, y = RowY(LowerRow, x);
                strokes.Add(new[] { PL(x - 6, y), PL(x - len * 0.2f, y - len), PL(x + 6, y) });
            }
        }
    }

    // ------------------------------------------------------------------ audio sessions (per-app volume and loudness)

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        void GetAudioSessionControl(); void GetSimpleAudioVolume();
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        void GetState(); void GetDisplayName(); void SetDisplayName(); void GetIconPath(); void SetIconPath(); void GetGroupingParam();
        void SetGroupingParam(); void RegisterAudioSessionNotification(); void UnregisterAudioSessionNotification();
        void GetSessionIdentifier(); void GetSessionInstanceIdentifier();
        [PreserveSig] int GetProcessId(out int pid);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6F24A2A6C8CA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, [In] ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
    }

    [ComImport, Guid("1C158861-B533-4B30-B1CF-E853E51C59B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IChannelAudioVolume
    {
        [PreserveSig] int GetChannelCount(out int count);
        [PreserveSig] int SetChannelVolume(int index, float level, [In] ref Guid context);
        [PreserveSig] int GetChannelVolume(int index, out float level);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    // Per-app volume: an app's own level (as in the Windows Volume Mixer), on every output device.
    static class AppVolume
    {
        static List<IAudioSessionManager2> managers;
        static DateTime builtAt;
        static readonly Dictionary<int, string> names = new Dictionary<int, string>();

        // Process names an app's audio comes from (a browser plays through its own child processes of the same name).
        public static string[] ProcessNames(string app)
        {
            switch ((app ?? "").ToLowerInvariant())
            {
                case "": return new string[0];
                case "edge": return new[] { "msedge" };
                case "chrome": return new[] { "chrome" };
                case "apple music": return new[] { "AppleMusic", "iTunes" };
                case "amazon music": return new[] { "Amazon Music" };
                case "youtube music": return new[] { "YouTube Music" };
                default: return new[] { app.Replace(" ", "") };
            }
        }

        static readonly object sync = new object();   // the volume and the loudness meter run on different threads

        // Loudness meters for the app's audio sessions (for the XP style's visualizer). The caller reads them and
        // releases them with MF.Release when it rebuilds the list.
        public static List<IAudioMeterInformation> Meters(string[] processes)
        {
            var meters = new List<IAudioMeterInformation>();
            lock (sync)
                ForEachSession(processes, s =>
                {
                    var m = s as IAudioMeterInformation;
                    if (m == null) return false;
                    meters.Add(m);
                    return true;   // keep this session object: the meter lives on it
                });
            return meters;
        }

        // Calls use(session) for each audio session of the given processes; use returns true to keep the session
        // object alive (otherwise it's released right after).
        static void ForEachSession(string[] processes, Func<IAudioSessionControl2, bool> use)
        {
            if (processes.Length == 0) return;
            if (managers == null || (DateTime.UtcNow - builtAt).TotalSeconds > 15) Build();
            foreach (var m in managers)
            {
                IAudioSessionEnumerator list = null;
                try
                {
                    if (m.GetSessionEnumerator(out list) < 0) continue;
                    int n;
                    list.GetCount(out n);
                    for (int i = 0; i < n; i++)
                    {
                        IAudioSessionControl2 s;
                        if (list.GetSession(i, out s) < 0 || s == null) continue;
                        bool keep = false;
                        try
                        {
                            int pid;
                            if (s.GetProcessId(out pid) < 0 || pid == 0) continue;
                            string name;
                            if (!names.TryGetValue(pid, out name))
                            {
                                try { name = Process.GetProcessById(pid).ProcessName; } catch { name = ""; }
                                names[pid] = name;
                            }
                            if (!processes.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase))) continue;
                            keep = use(s);
                        }
                        catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; }
                        finally { if (!keep) Com.Release(s); }
                    }
                }
                catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; }
                finally { Com.Release(list); }
            }
        }

        // Sets (level >= 0) and returns the volume of the app's audio sessions; -1 when it has none right now.
        public static float Level(string[] processes, float level)
        {
            float found = -1;
            var context = Guid.Empty;
            lock (sync)
                ForEachSession(processes, s =>
                {
                    float cur;
                    var v = s as ISimpleAudioVolume;
                    if (v != null)
                    {
                        if (level >= 0) v.SetMasterVolume(Math.Max(0, Math.Min(1, level)), ref context);
                        if (found < 0 && v.GetMasterVolume(out cur) >= 0) found = cur;
                        return false;
                    }
                    // Some Windows builds don't hand out the mixer slider for other apps' sessions, but do the
                    // per-channel levels, which scale the app's sound just the same (on top of its mixer slider).
                    var ch = s as IChannelAudioVolume;
                    if (ch == null) return false;
                    int channels;
                    if (ch.GetChannelCount(out channels) < 0) return false;
                    for (int c = 0; c < channels && level >= 0; c++) ch.SetChannelVolume(c, Math.Max(0, Math.Min(1, level)), ref context);
                    if (found < 0 && channels > 0 && ch.GetChannelVolume(0, out cur) >= 0) found = cur;
                    return false;
                });
            return found;
        }

        public static string LastError;   // for troubleshooting

        // A session manager for every active output device (rebuilt now and then, as devices come and go).
        static void Build()
        {
            if (managers != null) foreach (var m in managers) Com.Release(m);
            managers = new List<IAudioSessionManager2>();
            names.Clear();
            builtAt = DateTime.UtcNow;
            IMMDeviceEnumerator en = null;
            IMMDeviceCollection all = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                if (en.EnumAudioEndpoints(0, 1, out all) < 0) return;
                int n;
                all.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IMMDevice d;
                    if (all.Item(i, out d) < 0) continue;
                    try
                    {
                        object o;
                        if (d.Activate(typeof(IAudioSessionManager2).GUID, 23, IntPtr.Zero, out o) >= 0) managers.Add((IAudioSessionManager2)o);
                    }
                    finally { Com.Release(d); }
                }
            }
            catch { }
            finally
            {
                Com.Release(all);
                Com.Release(en);
            }
        }
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate([In, MarshalAs(UnmanagedType.LPStruct)] Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        void OpenPropertyStore();
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    // ------------------------------------------------------------------ interface

    // Status lines across the top: recorder state and save folder on the left, app name and version on the right.
    class TopBar : Control
    {
        public string Title = "", State = "Ready", Line2 = "", Note = "", Right1 = "", Right2 = "";

        public TopBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Height = Theme.S(78);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.Paint(e.Graphics, this); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int x = Theme.S(48), sp = Theme.S(1);
            int w1 = Theme.DrawSpaced(g, Title, Theme.Header, Theme.Primary, x, Theme.S(12), sp);
            Theme.DrawSpaced(g, State, Theme.Header, Theme.Bright, x + w1, Theme.S(12), sp);
            int rw = Theme.DrawSpaced(g, Line2, Theme.Header, Theme.Primary, x + Theme.S(4), Theme.S(40), sp);
            if (!string.IsNullOrEmpty(Note)) Theme.DrawSpaced(g, "   " + Note, Theme.Body, Theme.Dim, x + Theme.S(4) + rw, Theme.S(44), sp);
            int right = Width - Theme.S(48);
            Theme.DrawSpaced(g, Right1, Theme.Header, Theme.Primary, right - Theme.SpacedWidth(g, Right1, Theme.Header, sp), Theme.S(12), sp);
            Theme.DrawSpaced(g, Right2, Theme.Header, Theme.Dim, right - Theme.SpacedWidth(g, Right2, Theme.Header, sp), Theme.S(40), sp);
            UiDraw.Scanlines(g, this);
        }
    }

    // The big accent-framed box that holds the record page and the Options panel.
    class FramePanel : Panel
    {
        public static int Inset { get { return Theme.S(44); } }

        public FramePanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            int t = Theme.S(3);
            Padding = new Padding(Inset + t, Theme.S(4) + t, Inset + t, Theme.S(4) + t);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.Paint(e.Graphics, this); }

        protected override void OnPaint(PaintEventArgs e)
        {
            UiDraw.Box(e.Graphics, new Rectangle(Inset, Theme.S(4), Width - 2 * Inset, Height - Theme.S(8)), Theme.S(3), Theme.Primary);
            UiDraw.Scanlines(e.Graphics, this);
        }
    }

    // Right-hand "Options" column: drop-down lists, check boxes and the accent sliders.
    class OptionsPanel : Control, IMessageFilter
    {
        public class Row
        {
            public string Label;
            public Func<string> Value;          // slider readout
            public string[] Choices;            // drop-down list entries
            public Func<int> Current;           // index of the chosen entry, or -1 (shown as "Custom")
            public Action<int> Choose;
            public Func<bool> Checked;
            public Action Toggle;
            public bool Slider;
            public Func<float> Position;        // slider: knob position, 0..1
            public Action<float> Slide;         // slider: called while dragging with the new position
            public Action Commit;               // slider: called when the drag ends (save the setting)
            public Func<Color[]> TrackColors;   // slider: colors spread evenly along the track
            public Rectangle Hit, Box, Track;
            public int ListWidth;               // drop-down: width the open list wants (measured once)
        }

        public readonly List<Row> Rows = new List<Row>();
        int dragging = -1;   // the slider row being dragged
        int hover = -1;
        int open = -1, hoverItem = -1;   // the open drop-down row and the list entry under the mouse
        int scroll;                      // how far the rows are scrolled up, when they don't all fit

        public OptionsPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Width = Theme.S(390);
            Application.AddMessageFilter(this);
        }

        // Bottom of the option rows (where the divider line sits), as scrolled.
        int RowsBottom()
        {
            int y = Theme.S(72) - scroll;
            float k = Squeeze();
            foreach (var row in Rows) y += Step(row, k);
            return y + Theme.S(4);
        }

        // How far the rows can scroll: enough to bring the last one fully into view (the room it needs, as Squeeze).
        int MaxScroll()
        {
            if (Rows.Count == 0) return 0;
            float k = Squeeze();
            int y = Theme.S(72);
            for (int i = 0; i < Rows.Count - 1; i++) y += Step(Rows[i], k);
            return Math.Max(0, y + (Rows[Rows.Count - 1].Slider ? Theme.S(58) : Theme.S(30)) - Height);
        }

        // A wheel message sent straight here (the VR laser's thumbstick) scrolls too.
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(-e.Delta * Theme.S(56) / 120);
        }

        void ScrollBy(int dy)
        {
            int s = Math.Max(0, Math.Min(MaxScroll(), scroll + dy));
            if (s == scroll) return;
            scroll = s;
            CloseList();
            hover = -1;
            Invalidate();
        }

        // An open list closes when the user clicks anywhere else in the app. The mouse wheel over the panel scrolls
        // it (when the rows don't fit), whichever of the window's controls has the focus.
        public bool PreFilterMessage(ref Message m)
        {
            if (open >= 0 && (m.Msg == 0x201 || m.Msg == 0x204 || m.Msg == 0x207) && m.HWnd != Handle) CloseList();
            if (m.Msg == 0x20A && IsHandleCreated && Visible && RectangleToScreen(ClientRectangle).Contains(Cursor.Position))
            {
                var c = Control.FromHandle(m.HWnd);
                if (c != null && c.FindForm() == FindForm())
                {
                    ScrollBy(-(short)((long)m.WParam >> 16) * Theme.S(56) / 120);
                    return true;
                }
            }
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Application.RemoveMessageFilter(this);
            base.Dispose(disposing);
        }

        static string Shown(Row row)
        {
            int i = row.Current();
            return i >= 0 && i < row.Choices.Length ? row.Choices[i] : "Custom";
        }

        // Long lists (the 19 visualizers) squeeze their entries a little so the whole list fits in the panel.
        // When the rows don't all fit the panel's height they're spaced a little tighter (down to 70%), and past that
        // the panel scrolls. A slider row keeps room for its knob, which reaches 55 below its label, clear of the row
        // under it. The last row only needs room for itself, not a whole step.
        const int SliderStep = 78, SliderMin = 70, RowStep = 56;

        float Squeeze()
        {
            if (Height <= 0 || Rows.Count == 0) return 1;
            int sliders = 0, rows = 0;
            for (int i = 0; i < Rows.Count - 1; i++) if (Rows[i].Slider) sliders++; else rows++;
            int room = Height - Theme.S(72) - (Rows[Rows.Count - 1].Slider ? Theme.S(58) : Theme.S(30));
            float k = room / (float)Math.Max(1, sliders * Theme.S(SliderStep) + rows * Theme.S(RowStep));
            if (k * SliderStep < SliderMin) k = (room - sliders * Theme.S(SliderMin)) / (float)Math.Max(1, rows * Theme.S(RowStep));
            return Math.Max(0.7f, Math.Min(1, k));
        }

        static int Step(Row row, float k)
        {
            return row.Slider ? Math.Max(Theme.S(SliderMin), (int)Math.Round(Theme.S(SliderStep) * k)) : (int)Math.Round(Theme.S(RowStep) * k);
        }

        int ItemHeight(Row row) { return Math.Max(Theme.S(20), Math.Min(Theme.S(32), (Height - Theme.S(8)) / Math.Max(1, row.Choices.Length))); }

        // Shortens text with "…" to fit a width (long audio device names).
        public static string Fit(IDeviceContext g, string s, Font f, int width)
        {
            int sp = Theme.S(1);
            if (Theme.SpacedWidth(g, s, f, sp) <= width) return s;
            while (s.Length > 1 && Theme.SpacedWidth(g, s + "…", f, sp) > width) s = s.Substring(0, s.Length - 1);
            return s.TrimEnd() + "…";
        }

        // Where the open list is drawn: below its box if it fits above the divider, otherwise above the box. Lists with
        // long entries grow to the left, up to the width of the panel.
        Rectangle ListRect(Row row)
        {
            int h = row.Choices.Length * ItemHeight(row) + Theme.S(4);
            if (row.ListWidth == 0)
                row.ListWidth = row.Choices.Max(c => Theme.SpacedWidth(null, c, Theme.OptionValue, Theme.S(1))) + Theme.S(28);
            int w = Math.Max(row.Box.Width, Math.Min(row.ListWidth, row.Box.Right - Theme.S(14)));
            var r = new Rectangle(row.Box.Right - w, row.Box.Bottom, w, h);
            if (r.Bottom > RowsBottom() && row.Box.Y - h >= 0) r.Y = row.Box.Y - h;
            if (r.Bottom > Height) r.Y = Math.Max(0, Height - h);   // too long for either side: as low as it fits
            return r;
        }

        int ListItemAt(Point p)
        {
            if (open < 0) return -1;
            var r = ListRect(Rows[open]);
            if (!r.Contains(p)) return -1;
            return Math.Min(Rows[open].Choices.Length - 1, Math.Max(0, (p.Y - r.Y - Theme.S(2)) / ItemHeight(Rows[open])));
        }

        public void CloseList()
        {
            if (open < 0) return;
            open = hoverItem = -1;
            Invalidate();
        }

        void DrawList(Graphics g, Row row)
        {
            var r = ListRect(row);
            using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, r);
            int cur = row.Current();
            int ih = ItemHeight(row);
            for (int i = 0; i < row.Choices.Length; i++)
            {
                var item = new Rectangle(r.X + Theme.S(2), r.Y + Theme.S(2) + i * ih, r.Width - Theme.S(4), ih);
                Color tc = Theme.Primary;
                if (i == cur)
                {
                    using (var b = new SolidBrush(Theme.Primary)) g.FillRectangle(b, item);
                    tc = Theme.Ink;
                }
                else if (i == hoverItem)
                {
                    using (var b = new SolidBrush(Color.FromArgb(70, Theme.Primary))) g.FillRectangle(b, item);
                    tc = Theme.Bright;
                }
                Theme.DrawSpaced(g, Fit(g, row.Choices[i], Theme.OptionValue, item.Width - Theme.S(20)), Theme.OptionValue, tc,
                    item.X + Theme.S(10), item.Y + (item.Height - Theme.OptionValue.Height) / 2, Theme.S(1));
            }
            UiDraw.Box(g, r, Theme.S(2), Theme.Primary);
        }

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Escape || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) CloseList();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.Paint(e.Graphics, this); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int sp = Theme.S(1), lx = Theme.S(20), rx = Width - Theme.S(18);
            scroll = Math.Min(scroll, MaxScroll());   // the panel may have grown
            using (var b = new SolidBrush(Theme.Primary)) g.FillRectangle(b, 0, 0, Theme.S(2), Height);
            int tw = Theme.SpacedWidth(g, "Options", Theme.OptionTitle, Theme.S(2));
            Theme.DrawSpaced(g, "Options", Theme.OptionTitle, Theme.Primary, (Width - tw) / 2, Theme.S(10) - scroll, Theme.S(2));

            int y = Theme.S(72) - scroll;
            float squeeze = Squeeze();
            for (int i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                int step = Step(row, squeeze);
                bool hov = i == hover;
                Color lc = hov ? Theme.Bright : Theme.Primary;
                if (row.Slider)
                {
                    Theme.DrawSpaced(g, row.Label, Theme.Option, lc, lx, y, sp);
                    string v = row.Value != null ? row.Value() : "";
                    Theme.DrawSpaced(g, v, Theme.Option, Theme.Dim, rx - Theme.SpacedWidth(g, v, Theme.Option, sp), y, sp);
                    y += Theme.S(34);
                    var track = new Rectangle(lx, y, rx - lx, Theme.S(12));
                    row.Track = track;
                    var colors = row.TrackColors();
                    using (var lg = new LinearGradientBrush(track, Color.Black, Color.Black, 0f))
                    {
                        var blend = new ColorBlend();
                        blend.Colors = colors;
                        blend.Positions = Enumerable.Range(0, colors.Length).Select(k => k / (float)(colors.Length - 1)).ToArray();
                        lg.InterpolationColors = blend;
                        g.FillRectangle(lg, track);
                    }
                    UiDraw.Box(g, track, 1, Theme.Faint);
                    // The knob stays within the track: flush with its left end at 0 and its right end at the top.
                    int kw = Theme.S(14);
                    int kx = track.X + kw / 2 + (int)(Math.Max(0, Math.Min(1, row.Position())) * (track.Width - kw));
                    var knob = new Rectangle(kx - kw / 2, y - Theme.S(9), kw, Theme.S(30));
                    using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, knob);
                    UiDraw.Box(g, knob, Theme.S(2), Theme.Bright);
                    row.Hit = new Rectangle(0, y - Theme.S(14), Width, Theme.S(40));
                    y += step - Theme.S(34);
                }
                else if (row.Checked != null)
                {
                    Theme.DrawSpaced(g, row.Label, Theme.Option, lc, lx, y, sp);
                    var box = new Rectangle(rx - Theme.S(34), y - Theme.S(8), Theme.S(34), Theme.S(34));
                    UiDraw.Box(g, box, Theme.S(2), lc);
                    if (row.Checked()) using (var b = new SolidBrush(Theme.Primary)) g.FillRectangle(b, Rectangle.Inflate(box, -Theme.S(8), -Theme.S(8)));
                    row.Hit = new Rectangle(0, y - Theme.S(14), Width, Theme.S(46));
                    y += step;
                }
                else
                {
                    Theme.DrawSpaced(g, row.Label, Theme.Option, lc, lx, y, sp);
                    var d = new Rectangle(rx - Theme.S(180), y - Theme.S(8), Theme.S(180), Theme.S(32));
                    using (var b = new SolidBrush(hov || i == open ? Theme.Bright : Theme.Primary)) g.FillRectangle(b, d);
                    Theme.DrawSpaced(g, Fit(g, Shown(row), Theme.OptionValue, d.Width - Theme.S(44)), Theme.OptionValue, Theme.Ink,
                        d.X + Theme.S(12), d.Y + (d.Height - Theme.OptionValue.Height) / 2, sp);
                    if (i == open) UiDraw.UpTriangle(g, Theme.Ink, d.Right - Theme.S(20), d.Y + d.Height / 2, Theme.S(8));
                    else UiDraw.DownTriangle(g, Theme.Ink, d.Right - Theme.S(20), d.Y + d.Height / 2, Theme.S(8));
                    row.Box = d;
                    row.Hit = new Rectangle(0, y - Theme.S(14), Width, Theme.S(46));
                    y += step;
                }
            }

            y += Theme.S(4);
            using (var b = new SolidBrush(Theme.Primary)) g.FillRectangle(b, 0, y, Width, Theme.S(2));

            // When the rows don't all fit, a thin bar at the right edge shows where the wheel has scrolled to.
            int max = MaxScroll();
            if (max > 0)
            {
                int th = Math.Max(Theme.S(30), Height * Height / (Height + max));
                using (var b = new SolidBrush(Theme.Dim)) g.FillRectangle(b, Width - Theme.S(5), (Height - th) * scroll / max, Theme.S(3), th);
            }

            // The open list is painted last so it sits on top of the rows beneath it.
            if (open >= 0) DrawList(g, Rows[open]);
            UiDraw.Scanlines(g, this);
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < Rows.Count; i++) if (Rows[i].Hit.Contains(p)) return i;
            return -1;
        }

        void SlideTo(int row, int x)
        {
            var r = Rows[row];
            int kw = Theme.S(14);
            if (r.Track.Width <= kw || r.Slide == null) return;
            r.Slide(Math.Max(0f, Math.Min(1f, (x - r.Track.X - kw / 2) / (float)(r.Track.Width - kw))));
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (open >= 0)
            {
                // With a list open, a click either picks an entry or just closes the list.
                int item = ListItemAt(e.Location), was = open;
                CloseList();
                if (item >= 0) Rows[was].Choose(item);
                else if (Rows[was].Hit.Contains(e.Location)) return;   // clicking its own box again closes it
                else OnMouseDown(e);
                return;
            }
            int h = HitTest(e.Location);
            if (h < 0) return;
            var row = Rows[h];
            if (row.Slider) { dragging = h; Capture = true; SlideTo(h, e.X); }
            else if (row.Toggle != null) row.Toggle();
            else if (row.Choices != null) { open = h; hoverItem = -1; Focus(); }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging >= 0) { SlideTo(dragging, e.X); return; }
            if (open >= 0)
            {
                int item = ListItemAt(e.Location);
                Cursor = item >= 0 || HitTest(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
                if (item != hoverItem) { hoverItem = item; Invalidate(); }
                return;
            }
            int h = HitTest(e.Location);
            if (h == hover) return;
            hover = h;
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging < 0) return;
            var r = Rows[dragging];
            dragging = -1;
            Capture = false;
            if (r.Commit != null) r.Commit();
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (dragging < 0) { hover = -1; Invalidate(); } }
    }

    class BarButton
    {
        public string Text;
        public Action Click;
        public bool Right, Disabled;
        public Rectangle Bounds;
    }

    // Bottom strip between two accent rules, with framed buttons: a solid tab on the left edge, bold label inside.
    class BottomBar : Control
    {
        public readonly List<BarButton> Buttons = new List<BarButton>();
        int hover = -1;

        public BottomBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Height = Theme.S(82);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.Paint(e.Graphics, this); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int sp = Theme.S(1), top = Theme.S(12), h = Height - Theme.S(26);
            using (var b = new SolidBrush(Theme.Primary))
            {
                g.FillRectangle(b, 0, top, Width, Theme.S(2));
                g.FillRectangle(b, 0, top + h + Theme.S(2), Width, Theme.S(2));
            }
            // On narrow windows the buttons switch to a smaller font and tighter edges so they never overlap.
            int gap = Theme.S(16), edge = Theme.S(80);
            Font font = Theme.Button;
            Func<Font, int> total = f => Buttons.Sum(bt => Measure(g, bt, 0, top, h, sp, f).Width + gap);
            if (total(font) + 2 * edge > Width) { font = Theme.ButtonSmall; edge = Theme.S(24); gap = Theme.S(10); }

            int lx = edge, rx = Width - edge;
            var right = Buttons.Where(bt => bt.Right).Reverse().ToList();
            foreach (var bt in Buttons.Where(bt => !bt.Right))
            {
                bt.Bounds = Measure(g, bt, lx, top, h, sp, font);
                lx = bt.Bounds.Right + gap;
            }
            foreach (var bt in right)
            {
                var r = Measure(g, bt, 0, top, h, sp, font);
                r.X = rx - r.Width;
                bt.Bounds = r;
                rx = r.X - gap;
            }
            for (int i = 0; i < Buttons.Count; i++) Draw(g, Buttons[i], i == hover, sp, font);
            UiDraw.Scanlines(g, this);
        }

        static Rectangle Measure(Graphics g, BarButton bt, int x, int top, int h, int sp, Font font)
        {
            int tw = Theme.SpacedWidth(g, bt.Text, font, sp);
            int pad = font == Theme.Button ? Theme.S(40) : Theme.S(22);
            return new Rectangle(x, top + Theme.S(2), Theme.S(18) + tw + pad, h);
        }

        static void Draw(Graphics g, BarButton bt, bool hov, int sp, Font font)
        {
            UiDraw.TabButton(g, bt.Bounds, bt.Text, font, hov, bt.Disabled);
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < Buttons.Count; i++) if (Buttons[i].Bounds.Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (h == hover) return;
            hover = h;
            Cursor = h >= 0 && !Buttons[h].Disabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int h = HitTest(e.Location);
            if (h >= 0 && !Buttons[h].Disabled && Buttons[h].Click != null) Buttons[h].Click();
            if (!IsDisposed) Invalidate();
        }
    }

    // ------------------------------------------------------------------ now playing widget

    class NowPlayingInfo
    {
        public string Title = "", Artist = "", Album = "", App = "";
        public bool Playing;
        public TimeSpan Position, Duration;
        public DateTime UpdatedUtc;   // when Position was true (players report it every few seconds, not continuously)
        public Bitmap Art;            // album art or video thumbnail; null when the player gives none
        public float Volume = -1;     // the player app's own volume (0..1), -1 when it isn't making sound right now
        public Bitmap AppIcon;        // the player's program icon as a white glyph (shared; never disposed by the widget)

        // Where the song is now, counting on from the player's last report while it plays.
        public TimeSpan Now
        {
            get
            {
                var p = Position;
                if (Playing && UpdatedUtc.Year > 2000) p += DateTime.UtcNow - UpdatedUtc;
                if (p < TimeSpan.Zero) p = TimeSpan.Zero;
                return Duration > TimeSpan.Zero && p > Duration ? Duration : p;
            }
        }
    }

    // Reads what's playing from the Windows media controls (the ones in the volume flyout), which Spotify, browsers
    // playing YouTube or YouTube Music, and most other players report to. No accounts or keys involved. Polls twice a
    // second on its own thread; the WinRT objects are called through their vtables like the window capture.
    sealed class MediaWatcher : IDisposable
    {
        [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr classId, [In] ref Guid iid, out IntPtr factory);
        [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string s, int length, out IntPtr hstring);
        [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr hstring);
        [DllImport("combase.dll")] static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out int length);
        [DllImport("shcore.dll")] static extern int CreateStreamOverRandomAccessStream(IntPtr stream, [In] ref Guid iid, out IntPtr result);

        static readonly Guid ManagerStaticsId = new Guid("2050c4ee-11a0-57de-aed7-c97c70338245");
        static readonly Guid AsyncInfoId = new Guid("00000036-0000-0000-C000-000000000046");
        static readonly Guid StreamId = new Guid("0000000c-0000-0000-C000-000000000046");
        static readonly Guid RandomAccessStreamId = new Guid("905A0FE1-BC53-11DF-8C49-001E4FC686DA");

        delegate int OutPtrFn(IntPtr self, out IntPtr result);
        delegate int OutIntFn(IntPtr self, out int result);
        delegate int OutLongFn(IntPtr self, out long result);
        delegate int GetAtFn(IntPtr self, int index, out IntPtr result);
        delegate int SeekFn(IntPtr self, long position, out IntPtr result);

        static T Slot<T>(IntPtr obj, int index) where T : class
        {
            return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), index * IntPtr.Size), typeof(T));
        }

        static IntPtr Ptr(IntPtr obj, int slot) { IntPtr r; return Slot<OutPtrFn>(obj, slot)(obj, out r) >= 0 ? r : IntPtr.Zero; }
        static int Int(IntPtr obj, int slot) { int r; return Slot<OutIntFn>(obj, slot)(obj, out r) >= 0 ? r : 0; }
        static long Long(IntPtr obj, int slot) { long r; return Slot<OutLongFn>(obj, slot)(obj, out r) >= 0 ? r : 0; }

        static void Free(IntPtr p) { if (p != IntPtr.Zero) Marshal.Release(p); }

        static string Str(IntPtr obj, int slot)
        {
            IntPtr h = Ptr(obj, slot);
            if (h == IntPtr.Zero) return "";
            int len;
            string s = Marshal.PtrToStringUni(WindowsGetStringRawBuffer(h, out len), len);
            WindowsDeleteString(h);
            return s;
        }

        // Waits for a WinRT async operation and returns its result (IAsyncOperation<T>.GetResults), or zero.
        static IntPtr Await(IntPtr op, int timeoutMs)
        {
            if (op == IntPtr.Zero) return IntPtr.Zero;
            IntPtr info;
            Guid id = AsyncInfoId;
            if (Marshal.QueryInterface(op, ref id, out info) < 0) { Free(op); return IntPtr.Zero; }
            try
            {
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    int status = Int(info, 7);                         // Status: 0 started, 1 completed, 2 canceled, 3 error
                    if (status == 1)
                    {
                        IntPtr result;
                        int hr = Slot<OutPtrFn>(op, 8)(op, out result);    // GetResults
                        if (hr < 0) AsyncError = "results " + hr.ToString("X8");
                        return hr >= 0 ? result : IntPtr.Zero;
                    }
                    if (status != 0 || sw.ElapsedMilliseconds > timeoutMs)
                    {
                        AsyncError = "status " + status + " error " + Int(info, 8).ToString("X8") + " after " + sw.ElapsedMilliseconds + " ms";
                        return IntPtr.Zero;
                    }
                    Thread.Sleep(5);
                }
            }
            finally
            {
                Free(info);
                Free(op);
            }
        }

        readonly Action<NowPlayingInfo> Changed;   // called on the watcher's thread
        readonly Thread thread;
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        IntPtr manager;
        NowPlayingInfo last;
        string artKey;
        Bitmap art;

        public MediaWatcher(Action<NowPlayingInfo> changed)
        {
            Changed = changed;
            thread = new Thread(Run) { IsBackground = true, Name = "Now Playing" };
            thread.Start();
            meterThread = new Thread(RunMeter) { IsBackground = true, Name = "Now Playing meter" };
            meterThread.Start();
        }

        // How loud the playing app is right now (0..1), read 30 times a second for the XP style's visualizer.
        public volatile float Level;
        readonly Thread meterThread;

        void RunMeter()
        {
            List<IAudioMeterInformation> meters = null;
            string metersFor = null;
            var rebuildAt = DateTime.MinValue;
            while (!stop.WaitOne(33))
            {
                try
                {
                    var shown = last;
                    string app = shown != null ? shown.App : "";
                    // The app's sessions come and go (a browser opens new ones per tab), so the list is refreshed.
                    if (meters == null || app != metersFor || DateTime.UtcNow > rebuildAt)
                    {
                        if (meters != null) foreach (var m in meters) Com.Release(m);
                        meters = AppVolume.Meters(AppVolume.ProcessNames(app));
                        metersFor = app;
                        rebuildAt = DateTime.UtcNow.AddSeconds(2);
                    }
                    float peak = 0;
                    foreach (var m in meters)
                    {
                        float p;
                        if (m.GetPeakValue(out p) >= 0) peak = Math.Max(peak, p);
                    }
                    Level = peak;
                }
                catch { meters = null; Level = 0; }
            }
            if (meters != null) foreach (var m in meters) Com.Release(m);
        }

        // Player buttons. The command goes to the player the widget is showing.
        public const int Previous = 17, PlayPause = 20, Next = 16;   // session vtable slots: TrySkipPrevious/TryTogglePlayPause/TrySkipNext
        volatile int command;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        string shownSource;   // SourceAppUserModelId of the session on show

        public void Send(int slot)
        {
            command = slot;
            wake.Set();
        }

        long seekRequest = -1;   // 100 ns ticks into the track, -1 = none (Interlocked)

        // Jumps the player to a point in the track.
        public void Seek(TimeSpan position)
        {
            Interlocked.Exchange(ref seekRequest, Math.Max(0, position.Ticks));
            wake.Set();
        }

        volatile float volumeRequest = -1;

        // Sets the playing app's own volume (0..1); repeated calls while dragging just update the target.
        public void SetVolume(float level)
        {
            volumeRequest = level;
            wake.Set();
        }

        void Run()
        {
            var handles = new WaitHandle[] { stop, wake };
            do
            {
                float vol = volumeRequest;
                volumeRequest = -1;
                if (vol >= 0 && last != null) try { AppVolume.Level(AppVolume.ProcessNames(last.App), vol); } catch { }
                long seek = Interlocked.Exchange(ref seekRequest, -1);
                if (seek >= 0) try { SeekTo(seek); } catch { }
                int cmd = command;
                command = 0;
                if (cmd != 0) try { Execute(cmd); } catch { }
                NowPlayingInfo info = null;
                try { info = Read(); }
                catch { Free(manager); manager = IntPtr.Zero; }
                info = info ?? new NowPlayingInfo();
                if (last == null || Differs(last, info))
                {
                    last = info;
                    Changed(info);
                }
            } while (WaitHandle.WaitAny(handles, 500) != 0);
            Free(manager);
        }

        // TryChangePlaybackPositionAsync (slot 24) takes a position on the player's timeline, which may not start at 0.
        void SeekTo(long ticks)
        {
            if (!EnsureManager()) return;
            IntPtr session = FindSession(true);
            if (session == IntPtr.Zero) return;
            try
            {
                long start = 0;
                IntPtr timeline = Ptr(session, 8);                               // GetTimelineProperties
                if (timeline != IntPtr.Zero) { start = Long(timeline, 6); Free(timeline); }   // StartTime
                IntPtr op;
                if (Slot<SeekFn>(session, 24)(session, start + ticks, out op) >= 0) AwaitDone(op, 1500);
            }
            finally { Free(session); }
            Thread.Sleep(150);
        }

        void Execute(int slot)
        {
            if (!EnsureManager()) return;
            IntPtr session = FindSession(true);
            if (session == IntPtr.Zero) return;
            try { AwaitDone(Ptr(session, slot), 1500); }
            finally { Free(session); }
            Thread.Sleep(150);   // give the player a moment, so the next read shows the new state
        }

        // Waits for an async call whose result isn't needed (the player's yes/no answer).
        static void AwaitDone(IntPtr op, int timeoutMs)
        {
            if (op == IntPtr.Zero) return;
            IntPtr info;
            Guid id = AsyncInfoId;
            if (Marshal.QueryInterface(op, ref id, out info) >= 0)
            {
                var sw = Stopwatch.StartNew();
                while (Int(info, 7) == 0 && sw.ElapsedMilliseconds < timeoutMs) Thread.Sleep(5);
                Free(info);
            }
            Free(op);
        }

        bool EnsureManager()
        {
            if (manager != IntPtr.Zero) return true;
            IntPtr name, statics;
            string cls = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager";
            Com.Check(WindowsCreateString(cls, cls.Length, out name));
            Guid id = ManagerStaticsId;
            try { Com.Check(RoGetActivationFactory(name, ref id, out statics)); }
            finally { WindowsDeleteString(name); }
            try { manager = Await(Ptr(statics, 6), 3000); }                     // RequestAsync
            finally { Free(statics); }
            return manager != IntPtr.Zero;
        }

        // The player to show: one that's playing, else the one on show before (so pausing doesn't switch players),
        // else the one Windows considers current. forCommand: the one on show comes first.
        IntPtr FindSession(bool forCommand)
        {
            IntPtr playing = IntPtr.Zero, shown = IntPtr.Zero;
            IntPtr list = Ptr(manager, 7);                                       // GetSessions
            if (list != IntPtr.Zero)
            {
                try
                {
                    int n = Int(list, 7);                                        // Size
                    for (int i = 0; i < n; i++)
                    {
                        IntPtr s;
                        if (Slot<GetAtFn>(list, 6)(list, i, out s) < 0 || s == IntPtr.Zero) continue;   // GetAt
                        bool keep = false;
                        if (shown == IntPtr.Zero && shownSource != null && Str(s, 6) == shownSource) { shown = s; keep = true; }
                        else if (playing == IntPtr.Zero && Status(s) == 4) { playing = s; keep = true; }
                        if (!keep) Free(s);
                    }
                }
                finally { Free(list); }
            }
            // A playing session wins for display, unless it's the same one; the shown one wins for a button press.
            if (shown != IntPtr.Zero && (forCommand || playing == IntPtr.Zero || Status(shown) == 4))
            {
                Free(playing);
                return shown;
            }
            Free(shown);
            if (playing != IntPtr.Zero) return playing;
            return Ptr(manager, 6);                                              // GetCurrentSession
        }

        static bool Differs(NowPlayingInfo a, NowPlayingInfo b)
        {
            return a.Title != b.Title || a.Artist != b.Artist || a.App != b.App || a.Playing != b.Playing || a.Art != b.Art || a.AppIcon != b.AppIcon ||
                   Math.Abs(a.Volume - b.Volume) > 0.005f ||
                   a.Duration != b.Duration || Math.Abs((a.Now - b.Now).TotalSeconds) > 1.5;
        }

        NowPlayingInfo Read()
        {
            if (!EnsureManager()) return null;
            IntPtr session = FindSession(false);
            if (session == IntPtr.Zero) return null;

            try
            {
                shownSource = Str(session, 6);                                   // SourceAppUserModelId
                var info = new NowPlayingInfo { App = AppName(shownSource), Playing = Status(session) == 4 };
                IntPtr props = Await(Ptr(session, 7), 2000);                    // TryGetMediaPropertiesAsync
                IntPtr thumb = IntPtr.Zero;
                if (props != IntPtr.Zero)
                {
                    try
                    {
                        info.Title = Str(props, 6);
                        info.Artist = Str(props, 9);
                        info.Album = Str(props, 10);
                        thumb = Ptr(props, 15);                                  // Thumbnail
                    }
                    finally { Free(props); }
                }
                IntPtr timeline = Ptr(session, 8);                               // GetTimelineProperties
                if (timeline != IntPtr.Zero)
                {
                    try
                    {
                        long start = Long(timeline, 6), end = Long(timeline, 7), pos = Long(timeline, 10), updated = Long(timeline, 11);
                        info.Duration = TimeSpan.FromTicks(Math.Max(0, end - start));
                        info.Position = TimeSpan.FromTicks(Math.Max(0, pos - start));
                        if (updated > 0) try { info.UpdatedUtc = DateTime.FromFileTimeUtc(updated); } catch { }
                    }
                    finally { Free(timeline); }
                }
                // Album art is only fetched again when the track changes.
                string key = info.App + "|" + info.Title + "|" + info.Artist + "|" + info.Album;
                if (key != artKey)
                {
                    artKey = key;
                    art = thumb != IntPtr.Zero ? LoadArt(thumb) : null;
                    if (art == null && thumb != IntPtr.Zero) artKey = null;     // not ready yet (players set the art a moment later)
                }
                Free(thumb);
                info.Art = art;
                info.AppIcon = IconFor(info.App);
                try { info.Volume = AppVolume.Level(AppVolume.ProcessNames(info.App), -1); } catch { }
                return string.IsNullOrEmpty(info.Title) ? null : info;
            }
            finally { Free(session); }
        }

        readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>();

        // The player's own program icon, turned into a white glyph like the Windows media flyout shows: bright and
        // colored parts become white, dark parts (Spotify's sound waves) drop out. Looked up once per app.
        Bitmap IconFor(string app)
        {
            Bitmap glyph = null;
            if (string.IsNullOrEmpty(app) || icons.TryGetValue(app, out glyph)) return glyph;
            string path = null;
            foreach (var name in AppVolume.ProcessNames(app))
                foreach (var p in Process.GetProcessesByName(name))
                {
                    if (path == null) path = ProcessInfo.ImagePath(p.Id);
                    p.Dispose();
                }
            if (path != null)
                try
                {
                    using (var ic = Icon.ExtractAssociatedIcon(path))
                    using (var src = ic.ToBitmap())
                    {
                        glyph = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                        for (int y = 0; y < src.Height; y++)
                            for (int x = 0; x < src.Width; x++)
                            {
                                var c = src.GetPixel(x, y);
                                float lum = (0.3f * c.R + 0.59f * c.G + 0.11f * c.B) / 255f;
                                int a = (int)(c.A * Math.Min(1f, lum * 2.2f));
                                glyph.SetPixel(x, y, Color.FromArgb(a, 255, 255, 255));
                            }
                    }
                }
                catch { glyph = null; }
            icons[app] = glyph;
            return glyph;
        }

        static int Status(IntPtr session)
        {
            IntPtr playback = Ptr(session, 9);                                   // GetPlaybackInfo
            if (playback == IntPtr.Zero) return 0;
            try { return Int(playback, 7); }                                     // PlaybackStatus: 4 playing, 5 paused
            finally { Free(playback); }
        }

        // "Spotify.exe" -> "Spotify"; "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" -> "ZuneMusic"; browsers by name.
        static string AppName(string aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return "";
            string s = aumid;
            int bang = s.LastIndexOf('!');
            if (bang >= 0) s = s.Substring(bang + 1);
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            foreach (var known in new[] { "Spotify", "Brave", "Chrome", "Edge", "Firefox", "Opera", "Vivaldi", "YouTube Music", "Apple Music", "iTunes", "VLC", "foobar2000", "MusicBee", "Tidal", "Deezer", "Amazon Music" })
                if (aumid.IndexOf(known.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) >= 0) return known;
            int dot = s.LastIndexOf('.');
            if (dot >= 0 && dot < s.Length - 1) s = s.Substring(dot + 1);
            return s;
        }

        static Bitmap LoadArt(IntPtr thumb)
        {
            IntPtr stream = Await(Ptr(thumb, 6), 2000);                          // OpenReadAsync
            if (stream == IntPtr.Zero) { ArtError = "open failed"; return null; }
            IntPtr ras = IntPtr.Zero, com = IntPtr.Zero, count = Marshal.AllocHGlobal(4);
            try
            {
                Guid id = RandomAccessStreamId;
                int hr = Marshal.QueryInterface(stream, ref id, out ras);
                if (hr < 0) { ArtError = "no IRandomAccessStream " + hr.ToString("X8"); return null; }
                id = StreamId;
                hr = CreateStreamOverRandomAccessStream(ras, ref id, out com);
                if (hr < 0) { ArtError = "stream wrap " + hr.ToString("X8"); return null; }
                var s = (System.Runtime.InteropServices.ComTypes.IStream)Marshal.GetObjectForIUnknown(com);
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        var buf = new byte[65536];
                        while (true)
                        {
                            s.Read(buf, buf.Length, count);
                            int n = Marshal.ReadInt32(count);
                            if (n <= 0) break;
                            ms.Write(buf, 0, n);
                            if (ms.Length > 20 << 20) break;
                        }
                        if (ms.Length == 0) return null;
                        ms.Position = 0;
                        using (var img = Image.FromStream(ms)) return Fit(img);
                    }
                }
                finally { Marshal.ReleaseComObject(s); }
            }
            catch (Exception ex) { ArtError = ex.GetType().Name + ": " + ex.Message; return null; }
            finally
            {
                Marshal.FreeHGlobal(count);
                Free(com);
                Free(ras);
                Free(stream);
            }
        }

        // Album art is shown a few hundred pixels across at most, so a bigger picture (a video's full-size thumbnail)
        // is kept no bigger than that.
        static Bitmap Fit(Image img)
        {
            const int Most = 480;   // the shorter side, in pixels
            float k = Most / (float)Math.Min(img.Width, img.Height);
            if (k >= 1) return new Bitmap(img);
            int w = Math.Max(1, (int)Math.Round(img.Width * k)), h = Math.Max(1, (int)Math.Round(img.Height * k));
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            using (var attrs = new ImageAttributes())
            {
                attrs.SetWrapMode(WrapMode.TileFlipXY);   // no dark fringe along the edges
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(img, new Rectangle(0, 0, w, h), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attrs);
            }
            return b;
        }

        public static string ArtError, AsyncError;   // why the last album art / async call failed (for troubleshooting)

        public void Dispose()
        {
            stop.Set();
            thread.Join(3000);
            meterThread.Join(1000);
        }
    }

    // A small always-on-top "Now Playing" card in the Dino look: album art, track, artist, the app it's
    // playing in, and a progress bar. Drag it anywhere; it never takes focus from the app you're using.
    class NowPlayingWidget : Form
    {
        NowPlayingInfo info = new NowPlayingInfo();
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 33 };
        float scroll;        // title marquee offset in pixels
        int hold = 60;       // frames the marquee waits at the start
        int tick;
        Point dragFrom;
        bool dragging;
        readonly Rectangle[] buttons = new Rectangle[3];   // previous, play/pause, skip (placed when painted)
        int hoverButton = -1, pressed = -1, pressFrames;
        public event Action Moved;
        public event Action<int> Command;                  // a MediaWatcher command for the player
        public event Action<float> VolumeChanged;          // the player app's new volume, 0..1
        Rectangle volumeBar;
        bool volumeHover, draggingVolume;
        float localVolume = -1;
        DateTime localUntil;

        // The card has its own size (dragged by a corner) on top of the display scaling; everything is laid out in
        // 520 x 132 units multiplied by it, with fonts to match.
        public const float MinScale = 0.6f, MaxScale = 2.5f;
        float scale = 1;
        Font fHead, fTitle, fBody;
        public event Action Resized;                        // the user finished resizing (save the size)

        int S(int v) { return (int)Math.Round(v * Theme.Scale * scale); }

        public float CardScale
        {
            get { return scale; }
            set
            {
                scale = Math.Max(MinScale, Math.Min(MaxScale, value));
                foreach (var f in new[] { fHead, fTitle, fBody, wTitle, wSmall, wApp, aTitle, aBody, aSmall, xTitle, xArtist, xLabel, xSmall, oTitle, oBody, oSmall, oHead }) if (f != null) f.Dispose();
                fHead = new Font(Theme.TileTitle.FontFamily, Theme.TileTitle.Size * scale, Theme.TileTitle.Style);
                fTitle = new Font(Theme.TileValue.FontFamily, Theme.TileValue.Size * scale, Theme.TileValue.Style);
                fBody = new Font(Theme.Body.FontFamily, Theme.Body.Size * scale, Theme.Body.Style);
                float px = Theme.Scale * scale;
                wTitle = new Font(SegoeLight, 32 * px, GraphicsUnit.Pixel);
                wSmall = new Font("Segoe UI", 15 * px, GraphicsUnit.Pixel);
                wApp = new Font("Segoe UI", 13 * px, GraphicsUnit.Pixel);
                aTitle = new Font("Segoe UI", 20 * px, GraphicsUnit.Pixel);
                aBody = new Font("Segoe UI", 13 * px, GraphicsUnit.Pixel);
                aSmall = new Font("Segoe UI", 11 * px, GraphicsUnit.Pixel);
                xTitle = new Font(SegoeLight, 24 * px, GraphicsUnit.Pixel);
                xArtist = new Font(SegoeLight, 17 * px, GraphicsUnit.Pixel);
                xLabel = new Font("Tahoma", 11 * px, FontStyle.Bold, GraphicsUnit.Pixel);
                xSmall = new Font("Tahoma", 10 * px, GraphicsUnit.Pixel);
                oTitle = new Font("Segoe UI", 19 * px, GraphicsUnit.Pixel);
                oBody = new Font("Segoe UI", 13 * px, GraphicsUnit.Pixel);
                oSmall = new Font("Segoe UI", 11 * px, GraphicsUnit.Pixel);
                oHead = new Font("Segoe UI", 12 * px, FontStyle.Bold, GraphicsUnit.Pixel);
                Size = win ? new Size(S(635), S(174)) : aero ? new Size(S(540), S(152)) : xp ? new Size(S(600), shaded ? S(26) : S(214))
                     : orb ? new Size(S(600), S(190)) : new Size(S(520), S(132));
                ApplyRegion();
                Invalidate();
            }
        }

        // The Windows style is two separate panels with see-through space between them, like the Windows 10 flyout.
        // This window is only the player panel; the volume panel is its own small window (VolumePanel) so it can fade
        // on its own.
        void ApplyRegion()
        {
            var old = Region;
            Region = win ? new Region(WinRight) : null;
            if (old != null) old.Dispose();
            PlacePanel();
        }

        // Windows style: the volume panel is hidden until the mouse is over its spot or the volume changes (the mouse
        // wheel, dragging, or the player or Windows changing it). It fades out 2 seconds after the mouse leaves or the
        // volume settles. The mouse over its spot is looked for every frame, since the panel gets no mouse events while
        // it's hidden. (The Dino style always shows its slider.)
        DateTime volumeUntil;
        float panelFade;       // 0 hidden .. 1 fully shown
        float seenVolume = -1; // the volume last frame, to notice it changing
        VolumePanel panel;
        internal bool panelHover;

        bool VolumeShown { get { return panelHover || draggingVolume || OverPanelSpot || DateTime.UtcNow < volumeUntil; } }

        bool OverPanelSpot
        {
            get { return win && IsHandleCreated && new Rectangle(Left + WinLeft.X, Top + WinLeft.Y, WinLeft.Width, WinLeft.Height).Contains(Cursor.Position); }
        }

        internal void KeepVolume() { volumeUntil = DateTime.UtcNow.AddSeconds(2); }

        // Keeps the volume panel window over its spot beside the player panel.
        void PlacePanel()
        {
            if (!win || !IsHandleCreated)
            {
                if (panel != null) panel.Hide();
                return;
            }
            if (panel == null || panel.IsDisposed) panel = new VolumePanel(this);
            panel.Bounds = new Rectangle(Left + WinLeft.X, Top + WinLeft.Y, WinLeft.Width, WinLeft.Height);
            ApplyPanelFade();
        }

        void ApplyPanelFade()
        {
            if (panel == null || panel.IsDisposed) return;
            if (!win || panelFade <= 0) { if (panel.Visible) panel.Hide(); return; }
            panel.Opacity = userOpacity * 0.93 * panelFade;
            if (!panel.Visible) panel.Show(this);
            panel.Invalidate();
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); PlacePanel(); }

        // Anything that redraws the card (a new volume level, a theme change) redraws the volume panel too.
        protected override void OnInvalidated(InvalidateEventArgs e)
        {
            base.OnInvalidated(e);
            if (panel != null && !panel.IsDisposed && panel.Visible) panel.Invalidate();
        }
        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); if (panel != null) PlacePanel(); }

        // Called by the volume panel window (its own coordinates match WinLeft's, which sits at the card's corner).
        internal void PaintVolumePanel(Graphics g)
        {
            g.Clear(Color.FromArgb(31, 31, 31));
            float vol = ShownVolume;
            var track = WinTrack;
            float v = Math.Max(0, Math.Min(1, vol < 0 ? 0 : vol));
            int thumbH = S(14), thumbTop = track.Y + (int)Math.Round((1 - v) * (track.Height - thumbH));
            using (var b = new SolidBrush(WinGray)) g.FillRectangle(b, track.X, track.Y, track.Width, thumbTop - track.Y);
            using (var b = new SolidBrush(WinBlue)) g.FillRectangle(b, track.X, thumbTop + thumbH, track.Width, track.Bottom - thumbTop - thumbH);
            using (var b = new SolidBrush(Color.White)) g.FillRectangle(b, track.X, thumbTop, track.Width, thumbH);
            string level = vol < 0 ? "–" : ((int)Math.Round(vol * 100)).ToString();
            DrawBaseline(g, level, wSmall, WinLeft.X, WinLeft.Width, S(150), true);
        }

        internal void PanelMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !WinTrackHit.Contains(e.Location)) return;
            draggingVolume = true;
            SlideVolumeVertical(e.Y);
        }

        internal void PanelMouseMove(MouseEventArgs e)
        {
            KeepVolume();
            if (draggingVolume) SlideVolumeVertical(e.Y);
        }

        internal void PanelMouseUp() { draggingVolume = false; KeepVolume(); }

        internal void PanelWheel(MouseEventArgs e) { OnMouseWheel(e); }

        internal bool OnTrack(Point p) { return WinTrackHit.Contains(p); }

        // Which look the card has: the Dino card, a copy of the Windows 10 media and volume flyout, a glossy
        // Frutiger Aero / Windows Vista gadget, a Windows XP-era media player, or a green orb console dashboard.
        bool win, aero, xp, orb;
        Font wTitle, wSmall, wApp, aTitle, aBody, aSmall, xTitle, xArtist, xLabel, xSmall, oTitle, oBody, oSmall, oHead;
        static readonly string SegoeLight = new System.Drawing.Text.InstalledFontCollection().Families.Any(f => f.Name == "Segoe UI Light") ? "Segoe UI Light" : "Segoe UI";

        public const int DinoStyle = 0, WinStyle = 1, AeroStyle = 2, XpStyle = 3, OrbStyle = 4;

        // Styles that draw their own see-through pixels (smooth curved edges, glass, a shape that isn't a rectangle).
        bool Layered { get { return aero || xp || orb; } }

        public int Style
        {
            get { return orb ? OrbStyle : xp ? XpStyle : aero ? AeroStyle : win ? WinStyle : DinoStyle; }
            set
            {
                bool wasLayered = Layered;
                win = value == WinStyle;
                aero = value == AeroStyle;
                xp = value == XpStyle;
                orb = value == OrbStyle;
                shaded = maximized = false;   // the XP card's roll-up and maximize don't carry over to another style
                // The Aero and XP cards draw their own see-through pixels, which needs a differently made window;
                // the others use one see-through level for the whole window.
                if (IsHandleCreated && wasLayered != Layered)
                {
                    Opacity = 1;
                    RecreateHandle();
                }
                ApplyOpacity();
                BackColor = win ? Color.FromArgb(31, 31, 31) : Theme.Bg;
                CardScale = scale;
            }
        }

        // How see-through the card is, from the Options slider (0.2 to 1). The Windows style is a touch see-through
        // on top of that, like the original flyout.
        float userOpacity = 1;
        public const float MinOpacity = 0.2f;

        public float CardOpacity
        {
            get { return userOpacity; }
            set
            {
                userOpacity = Math.Max(MinOpacity, Math.Min(1, value));
                ApplyOpacity();
            }
        }

        void ApplyOpacity()
        {
            if (Layered) RenderLayered();   // the Aero and XP cards' level goes into their own pixels
            else Opacity = userOpacity * (win ? 0.93 : 1);
            ApplyPanelFade();
        }

        public NowPlayingWidget(float scale, int style)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            this.scale = scale;
            Style = style;
            Text = "Now Playing";
            anim.Tick += (s, e) => Animate();
            anim.Start();
            Theme.Changed += OnThemeChanged;
        }

        void OnThemeChanged() { if (IsHandleCreated) BeginInvoke((Action)Invalidate); }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000 | 0x8;   // tool window (no Alt+Tab), no activation, topmost
                if (Layered) cp.ExStyle |= 0x80000;      // layered: the Aero / XP card supplies every pixel's transparency
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (Layered) RenderLayered();
        }

        // The info (and its album art) is shared with the main window, which owns it; the card never disposes it.
        public void SetInfo(NowPlayingInfo next)
        {
            if (next.Title != info.Title) { scroll = 0; hold = 60; }
            info = next;
            Invalidate();
        }

        void Animate()
        {
            tick++;
            if (hold > 0) hold--;
            else scroll += S(1);
            if (pressFrames > 0 && --pressFrames == 0) Invalidate();
            // Windows style: the volume panel fades in quickly (~0.15 s) and out gently (~0.5 s).
            if (win)
            {
                float vol = ShownVolume;   // -1 while the player is silent: starting or stopping isn't a change
                if (vol >= 0 && seenVolume >= 0 && Math.Abs(vol - seenVolume) > 0.004f) KeepVolume();
                seenVolume = vol;
                float target = VolumeShown ? 1 : 0;
                if (panelFade != target)
                {
                    panelFade = target > panelFade ? Math.Min(1, panelFade + 0.25f) : Math.Max(0, panelFade - 0.07f);
                    ApplyPanelFade();
                }
            }
            // Redrawn for the marquee, the equalizer and the clock: every frame while playing, else twice a second.
            // The Windows style has nothing moving, so it only redraws when something changes. The Aero card's
            // bubbles always drift and the XP card's visualizer always moves, so they're redrawn every frame.
            if (xp || orb) UpdateXpBars();   // the XP screen's visualizer, or the orb's burst of light
            else vizEngine.Release();         // the other looks have no visualizer: what it drew with goes
            if (Layered) RenderLayered();
            else if (!win && (info.Playing || tick % 15 == 0)) Invalidate();
        }

        Rectangle ArtRect { get { int m = S(14), tab = S(10); return new Rectangle(tab + m, m, Height - 2 * m, Height - 2 * m); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (Layered) return;   // drawn by RenderLayered straight to the layered window
            if (win) { PaintWindows(g); return; }
            int t = S(2), tab = S(10);
            using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, ClientRectangle);
            using (var b = new SolidBrush(Theme.Dim)) g.FillRectangle(b, 0, 0, tab - S(2), Height);   // solid tab, like the app's buttons
            UiDraw.Box(g, new Rectangle(tab, 0, Width - tab, Height), t, Theme.Primary);

            var art = ArtRect;
            if (info.Art != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                // Center-crop to a square (video thumbnails are wide).
                int side = Math.Min(info.Art.Width, info.Art.Height);
                g.DrawImage(info.Art, art, new Rectangle((info.Art.Width - side) / 2, (info.Art.Height - side) / 2, side, side), GraphicsUnit.Pixel);
            }
            else DrawPlaceholder(g, art);
            UiDraw.Box(g, Rectangle.Inflate(art, S(1), S(1)), S(1), Theme.Faint);

            int x = art.Right + S(16), right = Width - S(16), w = right - x;
            bool any = !string.IsNullOrEmpty(info.Title);

            // Header: equalizer, state and app; the app's volume slider on the right.
            int hy = art.Y + S(2);
            int ex = DrawEqualizer(g, x, hy, fHead.Height);
            float vol = ShownVolume;
            volumeBar = Rectangle.Empty;
            int headRight = right;
            if (any && vol >= 0)
            {
                int iconW = S(16), barW = S(96);
                var icon = new Rectangle(right - barW - S(8) - iconW, hy, iconW, fHead.Height);
                volumeBar = new Rectangle(right - barW, hy + fHead.Height / 2 - S(2), barW, S(4));
                DrawVolume(g, icon, volumeBar, vol);
                headRight = icon.X - S(12);
            }
            string head = !any ? "Nothing Playing" : (info.Playing ? "Now Playing" : "Paused") + (info.App != "" ? "  ·  " + info.App : "");
            Theme.DrawSpaced(g, OptionsPanel.Fit(g, head, fHead, headRight - ex), fHead, any && info.Playing ? Theme.Primary : Theme.Dim, ex, hy, S(1));

            if (any)
            {
                // Title, scrolling when it's too long to fit.
                int ty = hy + fHead.Height + S(8), sp = S(1);
                int tw = Theme.SpacedWidth(g, info.Title, fTitle, sp);
                var clip = new Rectangle(x, ty, w, fTitle.Height + S(2));
                g.SetClip(clip);
                if (tw <= w) Theme.DrawSpaced(g, info.Title, fTitle, Theme.Bright, x, ty, sp);
                else
                {
                    int gap = S(60), loop = tw + gap;
                    if (scroll >= loop) { scroll = 0; hold = 60; }
                    int off = (int)scroll;
                    Theme.DrawSpaced(g, info.Title, fTitle, Theme.Bright, x - off, ty, sp, true);
                    Theme.DrawSpaced(g, info.Title, fTitle, Theme.Bright, x - off + loop, ty, sp, true);
                }
                g.ResetClip();

                string by = info.Artist != "" ? info.Artist : info.Album;
                int ay = ty + fTitle.Height + S(6);
                Theme.DrawSpaced(g, OptionsPanel.Fit(g, by, fBody, w), fBody, Theme.Primary, x, ay, sp);

                // Player buttons, then the progress bar (drag or click it to jump) and times.
                int bs = S(26), row = art.Bottom - bs;
                for (int i = 0; i < 3; i++) buttons[i] = new Rectangle(x + i * (bs + S(4)), row, bs, bs);
                DrawButtons(g);
                int barX = buttons[2].Right + S(14);
                progressBar = Rectangle.Empty;
                if (info.Duration > TimeSpan.Zero)
                {
                    var now = ShownPosition;
                    string times = Time(now) + " / " + Time(info.Duration);
                    // Laid out for the widest time this track can show, so the bar doesn't twitch as digits change.
                    int timesW = Theme.SpacedWidth(g, Time(info.Duration) + " / " + Time(info.Duration), fBody, sp);
                    int by2 = row + (bs - fBody.Height) / 2;
                    Theme.DrawSpaced(g, times, fBody, seeking ? Theme.Bright : Theme.Dim, right - Theme.SpacedWidth(g, times, fBody, sp), by2, sp);
                    var track = new Rectangle(barX, by2 + fBody.Height / 2 - S(2), Math.Max(0, right - timesW - S(14) - barX), S(4));
                    progressBar = track;
                    bool hot = seeking || progressHover;
                    if (hot) track = Rectangle.Inflate(track, 0, S(1));
                    using (var b = new SolidBrush(Theme.Faint)) g.FillRectangle(b, track);
                    int fill = (int)(track.Width * Math.Min(1, now.TotalSeconds / info.Duration.TotalSeconds));
                    using (var b = new SolidBrush(Theme.Primary)) g.FillRectangle(b, track.X, track.Y, fill, track.Height);
                    int kw = hot ? S(5) : S(3);
                    using (var b = new SolidBrush(Theme.Bright)) g.FillRectangle(b, track.X + fill - kw / 2, track.Y - S(4), kw, track.Height + S(8));
                }
                // The card moves only when grabbed by its middle: the album art or the title and artist lines.
                moveArea = new Rectangle(art.X, art.Y, art.Width, art.Height);
                moveBand = new Rectangle(x, ty - S(2), w, row - S(4) - (ty - S(2)));
            }
            else
            {
                moveArea = art;
                moveBand = new Rectangle(x, hy + fHead.Height + S(4), w, art.Bottom - (hy + fHead.Height + S(4)));
            }

            // Corner brackets while the mouse is over the card: grab one to resize.
            if (hovering || corner >= 0)
                using (var b = new SolidBrush(Theme.Bright))
                {
                    int len = Math.Max(8, S(14)), th = Math.Max(2, S(3));
                    foreach (var p in new[] { new Point(0, 0), new Point(Width - len, 0), new Point(0, Height - len), new Point(Width - len, Height - len) })
                    {
                        bool l = p.X == 0, top = p.Y == 0;
                        g.FillRectangle(b, p.X, top ? 0 : Height - th, len, th);
                        g.FillRectangle(b, l ? 0 : Width - th, p.Y, th, len);
                    }
                }

            // Scanlines on the card, but not over the album art.
            g.ExcludeClip(art);
            UiDraw.Scanlines(g, this);
        }

        // Previous, play/pause and skip, as outlined icons that light up under the mouse.
        void DrawButtons(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < 3; i++)
            {
                var r = buttons[i];
                bool hov = i == hoverButton, down = i == pressed && pressFrames > 0;
                if (hov || down) using (var b = new SolidBrush(Color.FromArgb(down ? 110 : 55, Theme.Primary))) g.FillRectangle(b, r);
                UiDraw.Box(g, r, S(1), hov ? Theme.Bright : Theme.Faint);
                Color c = hov ? Theme.Bright : Theme.Primary;
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, s = r.Width * 0.22f;
                using (var b = new SolidBrush(c))
                {
                    if (i == 1 && info.Playing)
                    {
                        g.FillRectangle(b, cx - s, cy - s, s * 0.7f, 2 * s);         // pause: two bars
                        g.FillRectangle(b, cx + s * 0.3f, cy - s, s * 0.7f, 2 * s);
                    }
                    else if (i == 1)
                        g.FillPolygon(b, new[] { new PointF(cx - s * 0.8f, cy - s), new PointF(cx + s, cy), new PointF(cx - s * 0.8f, cy + s) });   // play
                    else
                    {
                        float d = i == 0 ? -1 : 1;                                       // previous points left, skip points right
                        g.FillPolygon(b, new[] { new PointF(cx - d * s * 0.9f, cy - s), new PointF(cx + d * s * 0.6f, cy), new PointF(cx - d * s * 0.9f, cy + s) });
                        g.FillRectangle(b, d > 0 ? cx + s * 0.6f : cx - s * 0.6f - s * 0.35f, cy - s, s * 0.35f, 2 * s);
                    }
                }
            }
            g.SmoothingMode = SmoothingMode.None;
        }

        // While dragging (and for a moment after) the slider shows where the mouse put it, not the last reading.
        float ShownVolume { get { return DateTime.UtcNow < localUntil ? localVolume : info.Volume; } }

        // A speaker with sound waves (fewer as the volume drops, a cross when muted), and the slider bar.
        void DrawVolume(Graphics g, Rectangle icon, Rectangle bar, float vol)
        {
            bool hov = volumeHover || draggingVolume;
            Color c = hov ? Theme.Bright : Theme.Primary;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cy = icon.Y + icon.Height / 2f, s = icon.Height * 0.22f, x0 = icon.X;
            using (var b = new SolidBrush(c))
                g.FillPolygon(b, new[] { new PointF(x0, cy - s * 0.6f), new PointF(x0 + s * 0.8f, cy - s * 0.6f), new PointF(x0 + s * 1.8f, cy - s * 1.5f),
                                        new PointF(x0 + s * 1.8f, cy + s * 1.5f), new PointF(x0 + s * 0.8f, cy + s * 0.6f), new PointF(x0, cy + s * 0.6f) });
            using (var p = new Pen(c, Math.Max(1f, S(1) * 1.4f)))
            {
                float wx = x0 + s * 2.4f;
                if (vol <= 0.001f)
                {
                    g.DrawLine(p, wx, cy - s, wx + s * 1.6f, cy + s);
                    g.DrawLine(p, wx, cy + s, wx + s * 1.6f, cy - s);
                }
                else
                {
                    g.DrawArc(p, wx - s, cy - s, 2 * s, 2 * s, -50, 100);
                    if (vol > 0.5f) g.DrawArc(p, wx - s * 0.4f, cy - s * 1.8f, s * 3.6f, s * 3.6f, -50, 100);
                }
            }
            g.SmoothingMode = SmoothingMode.None;
            using (var b = new SolidBrush(Theme.Faint)) g.FillRectangle(b, bar);
            int fill = (int)(bar.Width * Math.Max(0, Math.Min(1, vol)));
            using (var b = new SolidBrush(Theme.Primary)) g.FillRectangle(b, bar.X, bar.Y, fill, bar.Height);
            var knob = new Rectangle(bar.X + fill - S(4), bar.Y - S(6), S(8), bar.Height + S(12));
            using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, knob);
            UiDraw.Box(g, knob, S(1) + 1, hov ? Theme.Bright : Theme.Primary);
        }

        // The slider's grab area: the bar plus some room above and below it.
        Rectangle VolumeHit { get { return volumeBar.IsEmpty ? Rectangle.Empty : Rectangle.Inflate(volumeBar, S(6), S(10)); } }

        void SlideVolume(int mouseX)
        {
            if (volumeBar.Width <= 0) return;
            localVolume = Math.Max(0, Math.Min(1, (mouseX - volumeBar.X) / (float)volumeBar.Width));
            localUntil = DateTime.UtcNow.AddSeconds(1.5);
            if (VolumeChanged != null) VolumeChanged(localVolume);
            Invalidate();
        }

        // The mouse wheel anywhere on the card nudges the volume by 5%.
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float vol = ShownVolume;
            if (vol < 0 || string.IsNullOrEmpty(info.Title)) return;
            KeepVolume();
            float step = win ? 0.02f : 0.05f;   // the Windows flyout moves in steps of 2
            localVolume = Math.Max(0, Math.Min(1, (float)Math.Round((vol + Math.Sign(e.Delta) * step) / step) * step));
            localUntil = DateTime.UtcNow.AddSeconds(1.5);
            if (VolumeChanged != null) VolumeChanged(localVolume);
            Invalidate();
        }

        int ButtonAt(Point p)
        {
            if (string.IsNullOrEmpty(info.Title)) return -1;
            for (int i = 0; i < 3; i++) if (buttons[i].Contains(p)) return i;
            return -1;
        }

        static string Time(TimeSpan t)
        {
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                                     : string.Format("{0}:{1:00}", (int)t.TotalMinutes, t.Seconds);
        }

        // Three little bars that bounce while music plays and rest when it doesn't. Returns where the text starts.
        int DrawEqualizer(Graphics g, int x, int y, int h)
        {
            int bw = S(3), gap = S(2);
            using (var b = new SolidBrush(info.Playing ? Theme.Primary : Theme.Faint))
                for (int i = 0; i < 3; i++)
                {
                    double v = info.Playing ? 0.35 + 0.65 * Math.Abs(Math.Sin(tick * (0.17 + i * 0.07) + i * 1.3)) : 0.3;
                    int bh = Math.Max(S(2), (int)(h * v));
                    g.FillRectangle(b, x + i * (bw + gap), y + h - bh, bw, bh);
                }
            return x + 3 * (bw + gap) + S(8);
        }

        // No album art: a dark square with the Dino Monitor footprint.
        // ---- Aero style: a glossy Frutiger Aero / Windows Vista gadget (540 x 152 units at 100%). It's drawn into a
        // bitmap with real per-pixel transparency (smooth rounded corners, soft shadow) and handed to Windows as a
        // layered window, 30 times a second so its bubbles drift.

        [DllImport("user32.dll")]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref Point pos, ref Size size, IntPtr src, ref Point srcPos, int key, ref BlendFunction blend, int flags);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BlendFunction { public byte Op, Flags, Alpha, Format; }

        struct Bubble { public float X, R, Speed, Phase, Wobble; }

        static readonly Bubble[] Bubbles = MakeBubbles();

        static Bubble[] MakeBubbles()
        {
            var r = new Random(11);
            var b = new Bubble[10];
            for (int i = 0; i < b.Length; i++)
                b[i] = new Bubble { X = (float)r.NextDouble(), R = 3 + (float)r.NextDouble() * 9, Speed = 5 + (float)r.NextDouble() * 9,
                                    Phase = (float)r.NextDouble() * 300, Wobble = (float)(r.NextDouble() * Math.PI * 2) };
            return b;
        }

        Bitmap reflection;      // the album art's faded mirror image, made once per track and size
        Bitmap reflectionOf;
        int reflectionSize;
        float reflectionTint;

        static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Max(1, radius * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        RectangleF AeroCard { get { return new RectangleF(S(4), S(3), Width - S(8), Height - S(10)); } }

        void RenderLayered()
        {
            if (!Layered || !IsHandleCreated || Width <= 0 || Height <= 0) return;
            using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    if (xp) PaintXp(g); else if (orb) PaintOrb(g); else PaintAero(g);
                }
                IntPtr screen = Native.GetDC(IntPtr.Zero), mem = Native.CreateCompatibleDC(screen);
                IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0)), old = Native.SelectObject(mem, hbmp);
                try
                {
                    var pos = Location;
                    var size = bmp.Size;
                    var src = Point.Empty;
                    var blend = new BlendFunction { Op = 0, Flags = 0, Alpha = (byte)(255 * userOpacity), Format = 1 };   // per-pixel alpha
                    UpdateLayeredWindow(Handle, screen, ref pos, ref size, mem, ref src, 0, ref blend, 2);
                }
                finally
                {
                    Native.SelectObject(mem, old);
                    Native.DeleteObject(hbmp);
                    Native.DeleteDC(mem);
                    Native.ReleaseDC(IntPtr.Zero, screen);
                }
            }
        }

        void PaintAero(Graphics g)
        {
            var card = AeroCard;
            float radius = S(16);
            bool any = !string.IsNullOrEmpty(info.Title);

            // A soft shadow under the card.
            for (int i = 4; i >= 1; i--)
                using (var path = RoundRect(new RectangleF(card.X - i + S(1), card.Y + S(3) - i * 0.5f, card.Width + 2 * i - S(2), card.Height + i), radius + i))
                using (var b = new SolidBrush(Tint(14, 0, 20, 40)))
                    g.FillPath(b, path);

            using (var shape = RoundRect(card, radius))
            {
                // Sky: pale at the top to deep blue at the bottom.
                using (var b = new LinearGradientBrush(card, Color.White, Color.White, 90f))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Tint(200, 238, 255), Tint(100, 198, 244), Tint(30, 142, 214), Tint(8, 84, 158) },
                        Positions = new[] { 0f, 0.42f, 0.72f, 1f }
                    };
                    g.FillPath(b, shape);
                }
                var clip = g.Clip;
                g.SetClip(shape);

                // Green and aqua "aurora" swooshes sweeping up from the bottom.
                using (var swoosh = new GraphicsPath())
                {
                    swoosh.AddBezier(card.X + card.Width * 0.18f, card.Bottom + 2, card.X + card.Width * 0.5f, card.Bottom - card.Height * 0.15f,
                                     card.X + card.Width * 0.72f, card.Y + card.Height * 0.55f, card.Right + 2, card.Y + card.Height * 0.22f);
                    swoosh.AddLine(card.Right + 2, card.Y + card.Height * 0.22f, card.Right + 2, card.Bottom + 2);
                    swoosh.CloseFigure();
                    using (var b = new LinearGradientBrush(card, Tint(120, 150, 230, 90), Tint(150, 0, 170, 140), 20f)) g.FillPath(b, swoosh);
                }
                using (var swoosh = new GraphicsPath())
                {
                    swoosh.AddBezier(card.X + card.Width * 0.38f, card.Bottom + 2, card.X + card.Width * 0.6f, card.Bottom - card.Height * 0.3f,
                                     card.X + card.Width * 0.85f, card.Y + card.Height * 0.48f, card.Right + 2, card.Y + card.Height * 0.4f);
                    using (var p = new Pen(Tint(110, 230, 255, 200), Math.Max(1, S(2)))) g.DrawPath(p, swoosh);
                }

                // Bubbles rising and swaying.
                float t = tick * 0.033f;
                foreach (var bub in Bubbles)
                {
                    float r = bub.R * Theme.Scale * scale, travel = card.Height + 2 * r;
                    float y = card.Bottom + r - ((t * bub.Speed * scale + bub.Phase) % travel);
                    float x = card.X + bub.X * card.Width + (float)Math.Sin(t * 0.8 + bub.Wobble) * S(6);
                    var rect = new RectangleF(x - r, y - r, 2 * r, 2 * r);
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(rect);
                        using (var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb(10, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(90, 255, 255, 255) } })
                            g.FillPath(pg, path);
                    }
                    using (var p = new Pen(Color.FromArgb(120, 255, 255, 255), 1)) g.DrawEllipse(p, rect);
                    using (var b = new SolidBrush(Color.FromArgb(190, 255, 255, 255))) g.FillEllipse(b, x - r * 0.55f, y - r * 0.6f, r * 0.45f, r * 0.32f);
                }

                // The glass: a bright gloss over the top half with a curved lower edge.
                using (var gloss = new GraphicsPath())
                {
                    gloss.AddEllipse(card.X - card.Width * 0.25f, card.Y - card.Height * 0.95f, card.Width * 1.5f, card.Height * 1.42f);
                    using (var b = new LinearGradientBrush(new RectangleF(card.X, card.Y, card.Width, card.Height * 0.5f), Color.FromArgb(150, 255, 255, 255), Color.FromArgb(25, 255, 255, 255), 90f))
                        g.FillPath(b, gloss);
                }
                g.Clip = clip;

                // Edges: a dark outer line and a bright inner one.
                using (var p = new Pen(Tint(200, 10, 50, 100), Math.Max(1, S(1)))) g.DrawPath(p, shape);
                using (var inner = RoundRect(RectangleF.Inflate(card, -S(1) - 0.5f, -S(1) - 0.5f), radius - S(1)))
                using (var p = new Pen(Color.FromArgb(160, 255, 255, 255), 1)) g.DrawPath(p, inner);

                // Brighter corners while the mouse is over the card: drag one to resize.
                if (hovering || corner >= 0)
                    using (var p = new Pen(Color.FromArgb(230, 255, 255, 255), Math.Max(2, S(2))))
                    {
                        float d = radius * 2;
                        g.DrawArc(p, card.X, card.Y, d, d, 180, 90);
                        g.DrawArc(p, card.Right - d, card.Y, d, d, 270, 90);
                        g.DrawArc(p, card.Right - d, card.Bottom - d, d, d, 0, 90);
                        g.DrawArc(p, card.X, card.Bottom - d, d, d, 90, 90);
                    }
            }

            // Album art with a white glass frame and its reflection fading underneath.
            var art = new Rectangle((int)card.X + S(16), (int)card.Y + S(14), S(96), S(96));
            using (var shade = RoundRect(new RectangleF(art.X + S(1), art.Y + S(2), art.Width, art.Height), S(6)))
            using (var b = new SolidBrush(Tint(70, 0, 30, 60))) g.FillPath(b, shade);
            using (var frame = RoundRect(art, S(6)))
            {
                var clip = g.Clip;
                g.SetClip(frame);
                if (info.Art != null)
                {
                    int side = Math.Min(info.Art.Width, info.Art.Height);
                    g.DrawImage(info.Art, art, new Rectangle((info.Art.Width - side) / 2, (info.Art.Height - side) / 2, side, side), GraphicsUnit.Pixel);
                }
                else AeroPlaceholder(g, art);
                using (var b = new LinearGradientBrush(new Rectangle(art.X, art.Y, art.Width, art.Height / 2 + 1), Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(b, art.X, art.Y, art.Width, art.Height / 2);
                g.Clip = clip;
                using (var p = new Pen(Color.FromArgb(230, 255, 255, 255), Math.Max(1.5f, S(2)))) g.DrawPath(p, frame);
            }
            int reflH = Math.Min(S(26), (int)card.Bottom - art.Bottom - S(6));
            if (reflH > 2)
            {
                var refl = Reflection(art.Width, reflH);
                if (refl != null) g.DrawImageUnscaled(refl, art.X, art.Bottom + S(3));
            }

            int x0 = art.Right + S(18), right = (int)card.Right - S(16), w = right - x0;

            // Header: the app's icon and name, and the volume slider on the right.
            int hy = (int)card.Y + S(12), hh = aSmall.Height;
            float vol = ShownVolume;
            volumeBar = Rectangle.Empty;
            int headRight = right;
            if (any && vol >= 0)
            {
                // Inset from the edge so the knob never touches the card's rim, with clear space before the speaker.
                var bar = new Rectangle(right - S(84) - S(4), hy + hh / 2 - S(3), S(84), S(6));
                volumeBar = bar;
                AeroSpeaker(g, bar.X - S(28), hy + hh / 2, vol);
                AeroBar(g, bar, Math.Max(0, Math.Min(1, vol)), false, volumeHover || draggingVolume);
                headRight = bar.X - S(36);
            }
            int hx = x0;
            if (info.AppIcon != null && any)
            {
                // The app's white glyph, tinted navy like the text beside it.
                var cm = new System.Drawing.Imaging.ColorMatrix(new[]
                {
                    new[] { AeroInk.R / 255f, 0, 0, 0, 0f }, new[] { 0, AeroInk.G / 255f, 0, 0, 0f }, new[] { 0, 0, AeroInk.B / 255f, 0, 0f },
                    new[] { 0, 0, 0, 1f, 0 }, new[] { 0, 0, 0, 0, 1f }
                });
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(cm);
                    g.DrawImage(info.AppIcon, new Rectangle(hx, hy + (hh - S(14)) / 2, S(14), S(14)), 0, 0, info.AppIcon.Width, info.AppIcon.Height, GraphicsUnit.Pixel, ia);
                }
                hx += S(18);
            }
            string head = !any ? "Nothing playing" : (info.Playing ? "Now Playing" : "Paused") + (info.App != "" ? "  ·  " + info.App : "");
            AeroText(g, head, aSmall, new RectangleF(hx, hy, Math.Max(1, headRight - hx), hh + 2), AeroInk, false);

            int ty = (int)card.Y + S(32);
            if (any)
            {
                // Title, scrolling when it's too long.
                float tw = g.MeasureString(info.Title, aTitle, PointF.Empty, StringFormat.GenericTypographic).Width;
                var titleBox = new RectangleF(x0, ty, w, aTitle.Height + S(4));
                var clip = g.Clip;
                g.SetClip(titleBox);
                if (tw <= w) AeroText(g, info.Title, aTitle, new RectangleF(x0, ty, w + S(20), aTitle.Height + S(4)), Color.White, true);
                else
                {
                    float gap = S(60), loop = tw + gap;
                    if (scroll >= loop) { scroll = 0; hold = 60; }
                    AeroText(g, info.Title, aTitle, new RectangleF(x0 - scroll, ty, tw + S(10), aTitle.Height + S(4)), Color.White, true);
                    AeroText(g, info.Title, aTitle, new RectangleF(x0 - scroll + loop, ty, tw + S(10), aTitle.Height + S(4)), Color.White, true);
                }
                g.Clip = clip;
                AeroText(g, info.Artist != "" ? info.Artist : info.Album, aBody, new RectangleF(x0, ty + aTitle.Height + S(2), w, aBody.Height + 2),
                         Tint(235, 225, 246, 255), false);
            }

            // Controls: small glass orbs either side of a big blue play/pause orb, then the progress bar and times.
            int rowY = (int)card.Y + S(100);
            buttons[0] = new Rectangle(x0, rowY - S(14), S(28), S(28));
            buttons[1] = new Rectangle(x0 + S(32), rowY - S(19), S(38), S(38));
            buttons[2] = new Rectangle(x0 + S(74), rowY - S(14), S(28), S(28));
            for (int i = 0; i < 3; i++) AeroOrb(g, buttons[i], i, i == 1, any && i == hoverButton, i == pressed && pressFrames > 0, any);
            progressBar = Rectangle.Empty;
            if (any && info.Duration > TimeSpan.Zero)
            {
                var now = ShownPosition;
                string times = Time(now) + " / " + Time(info.Duration);
                float timesW = g.MeasureString(Time(info.Duration) + " / " + Time(info.Duration), aSmall, PointF.Empty, StringFormat.GenericTypographic).Width;
                int bx = buttons[2].Right + S(14);
                var bar = new Rectangle(bx, rowY - S(4), Math.Max(S(20), right - (int)timesW - S(10) - bx), S(8));
                progressBar = bar;
                AeroBar(g, bar, (float)Math.Min(1, now.TotalSeconds / info.Duration.TotalSeconds), true, seeking || progressHover);
                AeroText(g, times, aSmall, new RectangleF(right - timesW - S(2), rowY - hh / 2, timesW + S(6), hh + 2), Color.White, false);
            }

            // The middle (album art, title and artist) moves the card.
            moveArea = art;
            moveBand = new Rectangle(x0, ty - S(2), w, rowY - S(20) - (ty - S(2)));
        }

        // ---- Orb style: an early-2000s green console dashboard (600 x 190 units at 100%). A dark green glass card with
        // a faint wireframe grid curving away behind it and a glowing tube sweeping in from the edge; on the left a big
        // glowing green orb with the chosen visualizer playing inside it, pulsing with the music, and a beveled time
        // box under it; on the right a beveled panel with a cut corner: the "now playing" header (with the visualizer
        // picker), album art, title, artist and album, a lime progress bar, beveled player buttons and the volume.
        // Always green, whatever the accent color.

        float orbPulse;
        static readonly Color OrbLime = Color.FromArgb(170, 232, 64), OrbText = Color.FromArgb(220, 246, 190), OrbDim = Color.FromArgb(120, 172, 76);

        void PaintOrb(Graphics g)
        {
            bool any = !string.IsNullOrEmpty(info.Title);
            float level = info.Playing && LevelSource != null ? Math.Min(1, (float)Math.Sqrt(LevelSource()) * 1.2f) : 0;
            orbPulse += (level - orbPulse) * 0.25f;
            var card = R(4, 4, 592, 182);

            using (var shape = RoundRect(card, U(18)))
            {
                using (var b = new LinearGradientBrush(card, Color.FromArgb(242, 10, 40, 12), Color.FromArgb(242, 3, 16, 5), 90f)) g.FillPath(b, shape);
                var clip = g.Clip;
                g.SetClip(shape);

                // The wireframe: rings and spokes of a huge sphere off to the right, curving away.
                var hub = new PointF(U(640), U(96));
                using (var p = new Pen(Color.FromArgb(38, 120, 200, 70), Math.Max(1, U(1))))
                {
                    for (float r = U(120); r < U(760); r += U(64)) g.DrawEllipse(p, hub.X - r, hub.Y - r * 0.72f, 2 * r, 2 * r * 0.72f);
                    for (int k = 0; k < 18; k++)
                    {
                        double a = Math.PI * (0.5 + k / 17.0);
                        g.DrawLine(p, hub, new PointF(hub.X + (float)Math.Cos(a) * U(800), hub.Y - (float)Math.Sin(a) * U(580)));
                    }
                }

                // A glowing tube sweeping in from the lower left toward the orb.
                using (var tube = new GraphicsPath())
                {
                    tube.AddBezier(U(-20), U(178), U(30), U(196), U(4), U(70), U(58), U(46));
                    using (var p = new Pen(Color.FromArgb(46, 150, 230, 80), U(14)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawPath(p, tube);
                    using (var p = new Pen(Color.FromArgb(150, 190, 250, 110), Math.Max(1, U(1.6f))) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawPath(p, tube);
                }
                g.Clip = clip;
                using (var p = new Pen(Color.FromArgb(150, 120, 200, 50), Math.Max(1, U(1.2f)))) g.DrawPath(p, shape);
            }

            // The orb: a glowing green sphere with the visualizer inside, swelling a little with the music.
            float radius = U(66) * (1 + 0.05f * orbPulse);
            var center = new PointF(U(112), U(90));
            var ball = new RectangleF(center.X - radius, center.Y - radius, 2 * radius, 2 * radius);
            for (int i = 3; i >= 1; i--)
                using (var p = new Pen(Color.FromArgb((int)((16 + 26 * orbPulse) * i), 150, 240, 70), U(4) * i)) g.DrawEllipse(p, RectangleF.Inflate(ball, U(2) * i, U(2) * i));
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(ball);
                using (var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb(255, 130, 220, 60), SurroundColors = new[] { Color.FromArgb(255, 18, 80, 16) } })
                    g.FillPath(pg, path);
                var clip = g.Clip;
                using (var inner = new GraphicsPath())
                {
                    var screen = RectangleF.Inflate(ball, -radius * 0.08f, -radius * 0.08f);
                    inner.AddEllipse(screen);
                    g.SetClip(inner, CombineMode.Intersect);
                    var palette = ColorMix.Palette();
                    if (palette != null) vizEngine.DrawMulti(g, screen, Visualizer, U(1.2f), palette);
                    else vizEngine.Draw(g, screen, Visualizer, U(1.2f), VizColors.XpGreen);
                }
                g.Clip = clip;
                // Glass: a soft shine across the top, darker toward the rim.
                using (var rim = new PathGradientBrush(path) { CenterColor = Color.FromArgb(0, 0, 0, 0), SurroundColors = new[] { Color.FromArgb(150, 4, 30, 4) }, FocusScales = new PointF(0.7f, 0.7f) })
                    g.FillPath(rim, path);
                var shine = new RectangleF(ball.X + ball.Width * 0.18f, ball.Y + ball.Height * 0.06f, ball.Width * 0.5f, ball.Height * 0.3f);
                using (var b = new LinearGradientBrush(new RectangleF(shine.X, shine.Y - 1, shine.Width, shine.Height + 2), Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillEllipse(b, shine);
                using (var p = new Pen(Color.FromArgb(220, OrbLime), Math.Max(1, U(1.6f)))) g.DrawEllipse(p, ball);
            }

            // The time box under the orb: play state and how far into the song.
            var timeBox = R(54, 152, 120, 28);
            OrbBevel(g, timeBox, false, false);
            float gx = timeBox.X + U(14), gy = timeBox.Y + timeBox.Height / 2, gs = U(6);
            using (var b = new SolidBrush(OrbText))
            {
                if (info.Playing)
                {
                    g.FillRectangle(b, gx - gs * 0.8f, gy - gs, gs * 0.6f, 2 * gs);
                    g.FillRectangle(b, gx + gs * 0.2f, gy - gs, gs * 0.6f, 2 * gs);
                }
                else g.FillPolygon(b, new[] { new PointF(gx - gs * 0.7f, gy - gs), new PointF(gx + gs, gy), new PointF(gx - gs * 0.7f, gy + gs) });
            }
            string now = any && info.Duration > TimeSpan.Zero ? Time(ShownPosition) : "--:--";
            using (var fmt = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
            using (var b = new SolidBrush(OrbText))
                g.DrawString(now, oTitle, b, new RectangleF(timeBox.X, timeBox.Y, timeBox.Width - U(12), timeBox.Height), fmt);

            // The panel: beveled, with its top-left corner cut off.
            var panel = R(200, 14, 388, 166);
            float cut = U(16);
            var outline = new[]
            {
                new PointF(panel.X + cut, panel.Y), new PointF(panel.Right, panel.Y), new PointF(panel.Right, panel.Bottom),
                new PointF(panel.X, panel.Bottom), new PointF(panel.X, panel.Y + cut)
            };
            using (var b = new LinearGradientBrush(panel, Color.FromArgb(236, 14, 38, 12), Color.FromArgb(236, 6, 22, 6), 90f)) g.FillPolygon(b, outline);
            using (var p = new Pen(Color.FromArgb(230, 140, 210, 50), Math.Max(1, U(1.5f)))) g.DrawPolygon(p, outline);
            using (var p = new Pen(Color.FromArgb(90, 200, 250, 140), 1))
                g.DrawLines(p, new[] { new PointF(panel.X + U(3), panel.Bottom - U(3)), new PointF(panel.X + U(3), panel.Y + cut + U(1)), new PointF(panel.X + cut + U(1), panel.Y + U(3)), new PointF(panel.Right - U(3), panel.Y + U(3)) });

            // The header strip: what's going on, and the visualizer picker on the right.
            var header = new RectangleF(panel.X + cut, panel.Y + U(4), panel.Width - cut - U(6), U(24));
            using (var b = new SolidBrush(Color.FromArgb(120, 40, 90, 20))) g.FillRectangle(b, header);
            string head = (!any ? "NOTHING PLAYING" : (info.Playing ? "NOW PLAYING" : "PAUSED") + (info.App != "" ? "  ·  " + info.App.ToUpperInvariant() : ""));
            XpText(g, head, oHead, new RectangleF(header.X + U(4), header.Y + U(5), header.Width * 0.55f, U(16)), OrbLime, false);
            string viz = "VISUALIZER: " + VisualizerNames[Math.Max(0, Math.Min(VisualizerNames.Length - 1, Visualizer))].ToUpperInvariant() + "  ▾";
            float vw = g.MeasureString(viz, oSmall, PointF.Empty, StringFormat.GenericTypographic).Width;
            var vbox = new RectangleF(header.Right - vw - U(16), header.Y + U(2), vw + U(12), U(20));
            vizBox = Rectangle.Round(vbox);
            OrbBevel(g, vbox, vizHover, false);
            XpText(g, viz, oSmall, new RectangleF(vbox.X + U(6), vbox.Y + U(4), vw + U(4), U(14)), vizHover ? Color.White : OrbText, false);

            // Album art in a lime frame, then the song.
            var art = R(212, 48, 68, 68);
            if (info.Art != null)
            {
                int side = Math.Min(info.Art.Width, info.Art.Height);
                g.DrawImage(info.Art, art, new RectangleF((info.Art.Width - side) / 2f, (info.Art.Height - side) / 2f, side, side), GraphicsUnit.Pixel);
            }
            else using (var b = new SolidBrush(Color.FromArgb(255, 8, 30, 8))) g.FillRectangle(b, art);
            using (var p = new Pen(Color.FromArgb(220, OrbLime), Math.Max(1, U(1.2f)))) g.DrawRectangle(p, art.X, art.Y, art.Width, art.Height);
            float tx = U(292), tw = U(286);
            if (any)
            {
                XpText(g, info.Title, oTitle, new RectangleF(tx, U(48), tw, oTitle.Height + U(4)), OrbText, true);
                XpText(g, info.Artist, oBody, new RectangleF(tx, U(74), tw, oBody.Height + U(2)), OrbLime, false);
                XpText(g, info.Album, oSmall, new RectangleF(tx, U(94), tw * 0.62f, oSmall.Height + U(2)), OrbDim, false);
            }
            else XpText(g, "Play something in Spotify, YouTube or any player", oBody, new RectangleF(tx, U(56), tw, oBody.Height + U(2)), OrbDim, false);

            // The progress bar (click or drag to jump), with the times above its right end.
            progressBar = Rectangle.Empty;
            if (any && info.Duration > TimeSpan.Zero)
            {
                var bar = R(212, 124, 366, 8);
                progressBar = Rectangle.Round(bar);
                float f = (float)Math.Max(0, Math.Min(1, ShownPosition.TotalSeconds / info.Duration.TotalSeconds));
                OrbTrack(g, bar, f, seeking || progressHover);
                string times = Time(ShownPosition) + " / " + Time(info.Duration);
                using (var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Far })
                using (var b = new SolidBrush(OrbDim))
                    g.DrawString(times, oSmall, b, new RectangleF(U(380), U(96), U(198), U(16)), fmt);
            }

            // Beveled player buttons, and the volume on the right.
            buttons[0] = Rectangle.Round(R(212, 142, 42, 28));
            buttons[1] = Rectangle.Round(R(258, 142, 42, 28));
            buttons[2] = Rectangle.Round(R(304, 142, 42, 28));
            for (int i = 0; i < 3; i++)
            {
                var r = new RectangleF(buttons[i].X, buttons[i].Y, buttons[i].Width, buttons[i].Height);
                OrbBevel(g, r, any && hoverButton == i, pressed == i && pressFrames > 0);
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, s = U(5.5f);
                using (var b = new SolidBrush(any ? (hoverButton == i ? Color.White : OrbText) : OrbDim))
                {
                    if (i == 1 && info.Playing)
                    {
                        g.FillRectangle(b, cx - s * 0.9f, cy - s, s * 0.65f, 2 * s);
                        g.FillRectangle(b, cx + s * 0.25f, cy - s, s * 0.65f, 2 * s);
                    }
                    else if (i == 1) g.FillPolygon(b, new[] { new PointF(cx - s * 0.7f, cy - s), new PointF(cx + s, cy), new PointF(cx - s * 0.7f, cy + s) });
                    else
                    {
                        float d = i == 0 ? -1 : 1;
                        for (int k = 0; k < 2; k++)
                        {
                            float ox = d * (k * s * 0.9f - s * 0.45f);
                            g.FillPolygon(b, new[] { new PointF(cx + ox - d * s * 0.5f, cy - s * 0.8f), new PointF(cx + ox + d * s * 0.5f, cy), new PointF(cx + ox - d * s * 0.5f, cy + s * 0.8f) });
                        }
                        g.FillRectangle(b, d > 0 ? cx + s * 1.0f : cx - s * 1.0f - U(1.6f), cy - s * 0.8f, U(1.6f), s * 1.6f);
                    }
                }
            }
            volumeBar = Rectangle.Empty;
            float vol = ShownVolume;
            if (any && vol >= 0)
            {
                var vb = R(476, 153, 100, 6);
                volumeBar = Rectangle.Round(vb);
                float sx = U(460), sy = vb.Y + vb.Height / 2, ss = U(5);
                using (var b = new SolidBrush(OrbText))
                    g.FillPolygon(b, new[] { new PointF(sx - ss, sy - ss * 0.45f), new PointF(sx - ss * 0.3f, sy - ss * 0.45f), new PointF(sx + ss * 0.5f, sy - ss),
                                             new PointF(sx + ss * 0.5f, sy + ss), new PointF(sx - ss * 0.3f, sy + ss * 0.45f), new PointF(sx - ss, sy + ss * 0.45f) });
                OrbTrack(g, vb, Math.Max(0, Math.Min(1, vol)), volumeHover || draggingVolume);
            }

            // Brighter corners while the mouse is over the card: drag one to resize.
            if (hovering || corner >= 0)
                using (var p = new Pen(Color.FromArgb(230, OrbLime), Math.Max(2, U(2))))
                {
                    float d = U(36);
                    g.DrawArc(p, card.X, card.Y, d, d, 180, 90);
                    g.DrawArc(p, card.Right - d, card.Y, d, d, 270, 90);
                    g.DrawArc(p, card.Right - d, card.Bottom - d, d, d, 0, 90);
                    g.DrawArc(p, card.X, card.Bottom - d, d, d, 90, 90);
                }

            // The orb and the song's details move the card.
            moveArea = Rectangle.Round(ball);
            moveBand = Rectangle.Round(new RectangleF(art.X, art.Y, panel.Right - U(8) - art.X, art.Height));
        }

        // A beveled button or box: dark green with a lighter top edge and a lime outline.
        void OrbBevel(Graphics g, RectangleF r, bool hover, bool down)
        {
            using (var path = RoundRect(r, U(5)))
            {
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), down ? Color.FromArgb(240, 6, 22, 6) : hover ? Color.FromArgb(240, 52, 110, 24) : Color.FromArgb(240, 30, 70, 16),
                                                       down ? Color.FromArgb(240, 24, 60, 14) : Color.FromArgb(240, 6, 24, 6), 90f))
                    g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(hover ? 255 : 210, OrbLime), Math.Max(1, U(1.2f)))) g.DrawPath(p, path);
            }
            using (var p = new Pen(Color.FromArgb(70, 220, 255, 170), 1)) g.DrawLine(p, r.X + U(5), r.Y + U(2), r.Right - U(5), r.Y + U(2));
        }

        // A glowing lime bar in a dark groove, with a knob while the mouse is on it.
        void OrbTrack(Graphics g, RectangleF r, float fraction, bool hot)
        {
            using (var path = RoundRect(r, r.Height / 2))
            {
                using (var b = new SolidBrush(Color.FromArgb(255, 4, 16, 4))) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(160, 90, 150, 40), 1)) g.DrawPath(p, path);
            }
            if (fraction > 0)
            {
                var fill = new RectangleF(r.X, r.Y, Math.Max(r.Height, r.Width * fraction), r.Height);
                using (var glow = RoundRect(RectangleF.Inflate(fill, U(2), U(2)), r.Height))
                using (var b = new SolidBrush(Color.FromArgb(60, OrbLime))) g.FillPath(b, glow);
                using (var path = RoundRect(fill, r.Height / 2))
                using (var b = new LinearGradientBrush(new RectangleF(fill.X, fill.Y - 1, fill.Width, fill.Height + 2), Color.FromArgb(220, 255, 150), Color.FromArgb(90, 170, 30), 90f))
                    g.FillPath(b, path);
            }
            if (hot)
            {
                float kx = r.X + r.Width * fraction, ks = r.Height * 1.9f;
                using (var b = new SolidBrush(OrbText)) g.FillEllipse(b, kx - ks / 2, r.Y + r.Height / 2 - ks / 2, ks, ks);
                using (var p = new Pen(OrbLime, 1)) g.DrawEllipse(p, kx - ks / 2, r.Y + r.Height / 2 - ks / 2, ks, ks);
            }
        }

        // Deep navy, for text and icons on the pale top of the card (as Vista put dark text on light glass).
        static Color AeroInk { get { return Tint(10, 52, 98); } }

        // The Aero colors are designed in blue (the app's default accent hue); they turn round the color wheel with
        // the Accent Hue slider and fade or deepen with Accent Saturation, like the rest of the app. Whites stay white.
        static Color Tint(int r, int g, int b) { return Tint(255, r, g, b); }

        static Color Tint(int a, int r, int g, int b)
        {
            float max = Math.Max(r, Math.Max(g, b)) / 255f, min = Math.Min(r, Math.Min(g, b)) / 255f, delta = max - min;
            if (delta <= 0) return Color.FromArgb(a, r, g, b);
            float h;
            if (max == r / 255f) h = 60 * (((g - b) / 255f / delta) % 6);
            else if (max == g / 255f) h = 60 * ((b - r) / 255f / delta + 2);
            else h = 60 * ((r - g) / 255f / delta + 4);
            var c = Theme.Hsv(h + Theme.Hue - Theme.DefaultHue, Math.Min(1, delta / max * Theme.Saturation), max);
            return Color.FromArgb(a, c);
        }

        // Text with a soft glow under it (blue under white text, white under the navy), cut off with "…" when it
        // doesn't fit (unless it scrolls).
        void AeroText(Graphics g, string s, Font f, RectangleF box, Color color, bool big)
        {
            bool dark = color.GetBrightness() < 0.4f;
            using (var fmt = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                using (var glow = new SolidBrush(dark ? Color.FromArgb(120, 255, 255, 255) : Tint(big ? 120 : 80, 0, 40, 100)))
                    foreach (var o in new[] { new PointF(1, 1), new PointF(-1, 1), new PointF(1, -1), new PointF(0, 2) })
                        g.DrawString(s, f, glow, new RectangleF(box.X + o.X, box.Y + o.Y, box.Width, box.Height), fmt);
                using (var b = new SolidBrush(color)) g.DrawString(s, f, b, box, fmt);
            }
        }

        // A glass orb button: glossy highlight on top, a glow below; the middle one is a big blue orb.
        void AeroOrb(Graphics g, Rectangle r, int kind, bool big, bool hover, bool down, bool enabled)
        {
            var rf = new RectangleF(r.X, r.Y, r.Width, r.Height);
            using (var b = new SolidBrush(Tint(70, 0, 30, 60))) g.FillEllipse(b, rf.X, rf.Y + S(1) + 1, rf.Width, rf.Height);
            Color top = big ? Tint(130, 220, 255) : Tint(245, 252, 255), bottom = big ? Tint(6, 88, 168) : Tint(150, 205, 236);
            using (var b = new LinearGradientBrush(rf, top, bottom, 90f)) g.FillEllipse(b, rf);
            using (var path = new GraphicsPath())   // light gathering at the bottom, like real glass
            {
                path.AddEllipse(rf.X + rf.Width * 0.1f, rf.Y + rf.Height * 0.45f, rf.Width * 0.8f, rf.Height * 0.55f);
                using (var pg = new PathGradientBrush(path) { CenterColor = Tint(big ? 150 : 110, big ? 120 : 255, 230, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } })
                    g.FillPath(pg, path);
            }
            var hl = new RectangleF(rf.X + rf.Width * 0.16f, rf.Y + rf.Height * 0.06f, rf.Width * 0.68f, rf.Height * 0.48f);
            using (var b = new LinearGradientBrush(hl, Color.FromArgb(220, 255, 255, 255), Color.FromArgb(25, 255, 255, 255), 90f)) g.FillEllipse(b, hl);
            if (hover) using (var b = new SolidBrush(Color.FromArgb(55, 255, 255, 255))) g.FillEllipse(b, rf);
            if (down) using (var b = new SolidBrush(Tint(60, 0, 30, 70))) g.FillEllipse(b, rf);
            using (var p = new Pen(Tint(210, 8, 52, 104), 1)) g.DrawEllipse(p, rf);

            float cx = rf.X + rf.Width / 2, cy = rf.Y + rf.Height / 2, s = rf.Width * (big ? 0.2f : 0.19f);
            Color glyph = !enabled ? Color.FromArgb(120, 255, 255, 255) : big ? Color.White : Tint(20, 72, 130);
            using (var sh = new SolidBrush(Tint(big ? 90 : 50, 0, 20, 50)))
            using (var b = new SolidBrush(glyph))
                foreach (var pass in new[] { sh, b })
                {
                    float o = pass == sh ? 1 : 0;
                    if (kind == 1 && info.Playing)
                    {
                        g.FillRectangle(pass, cx - s * 0.9f + o, cy - s + o, s * 0.65f, 2 * s);
                        g.FillRectangle(pass, cx + s * 0.25f + o, cy - s + o, s * 0.65f, 2 * s);
                    }
                    else if (kind == 1)
                        g.FillPolygon(pass, new[] { new PointF(cx - s * 0.7f + o, cy - s + o), new PointF(cx + s + o, cy + o), new PointF(cx - s * 0.7f + o, cy + s + o) });
                    else
                    {
                        float d = kind == 0 ? -1 : 1;
                        g.FillPolygon(pass, new[] { new PointF(cx - d * s * 0.8f + o, cy - s + o), new PointF(cx + d * s * 0.6f + o, cy + o), new PointF(cx - d * s * 0.8f + o, cy + s + o) });
                        g.FillRectangle(pass, (d > 0 ? cx + s * 0.6f : cx - s * 0.6f - s * 0.32f) + o, cy - s + o, s * 0.32f, 2 * s);
                    }
                }
        }

        // A glossy rounded bar: a dark glass groove, filled in Vista green (progress) or pale blue (volume), with a
        // glass knob while the mouse is on it.
        void AeroBar(Graphics g, Rectangle bar, float fraction, bool green, bool hot)
        {
            float rad = bar.Height / 2f;
            using (var groove = RoundRect(bar, rad))
            {
                using (var b = new LinearGradientBrush(bar, Tint(150, 0, 34, 70), Tint(90, 0, 50, 90), 90f)) g.FillPath(b, groove);
                using (var p = new Pen(Color.FromArgb(150, 255, 255, 255), 1)) g.DrawPath(p, groove);
            }
            int fw = (int)Math.Round(bar.Width * fraction);
            if (fw >= 2)
            {
                var fill = new Rectangle(bar.X, bar.Y, fw, bar.Height);
                using (var path = RoundRect(fill, rad))
                {
                    using (var b = new LinearGradientBrush(fill, Color.White, Color.White, 90f))
                    {
                        b.InterpolationColors = green
                            ? new ColorBlend { Colors = new[] { Tint(214, 255, 160), Tint(96, 210, 40), Tint(40, 150, 14) }, Positions = new[] { 0f, 0.5f, 1f } }
                            : new ColorBlend { Colors = new[] { Color.White, Tint(200, 238, 255), Tint(120, 200, 245) }, Positions = new[] { 0f, 0.5f, 1f } };
                        g.FillPath(b, path);
                    }
                    using (var b = new SolidBrush(Color.FromArgb(110, 255, 255, 255))) g.FillRectangle(b, fill.X + rad / 2, fill.Y + 1, Math.Max(0, fill.Width - rad), fill.Height / 2f - 1);
                }
            }
            if (hot)
            {
                float k = bar.Height * 1.9f;
                var knob = new RectangleF(bar.X + fw - k / 2, bar.Y + bar.Height / 2f - k / 2, k, k);
                using (var b = new LinearGradientBrush(knob, Color.White, Tint(150, 210, 240), 90f)) g.FillEllipse(b, knob);
                using (var p = new Pen(Tint(200, 8, 52, 104), 1)) g.DrawEllipse(p, knob);
            }
        }

        // A little navy speaker with sound waves (one, or two above half volume).
        void AeroSpeaker(Graphics g, float x, float cy, float vol)
        {
            float s = S(5);
            using (var b = new SolidBrush(AeroInk))
                g.FillPolygon(b, new[] { new PointF(x, cy - s * 0.55f), new PointF(x + s * 0.8f, cy - s * 0.55f), new PointF(x + s * 1.7f, cy - s * 1.3f),
                                         new PointF(x + s * 1.7f, cy + s * 1.3f), new PointF(x + s * 0.8f, cy + s * 0.55f), new PointF(x, cy + s * 0.55f) });
            // Sound waves as arcs round the speaker's mouth, so they stay inside the icon's 3.5-unit width.
            using (var p = new Pen(AeroInk, Math.Max(1.2f, S(1) * 1.3f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float mx = x + s * 1.7f, r1 = s * 0.8f, r2 = s * 1.55f;
                if (vol > 0.001f) g.DrawArc(p, mx - r1, cy - r1, 2 * r1, 2 * r1, -45, 90);
                if (vol > 0.5f) g.DrawArc(p, mx - r2, cy - r2, 2 * r2, 2 * r2, -45, 90);
            }
        }

        // No album art: an aqua glass tile with the Dino Monitor footprint in white.
        void AeroPlaceholder(Graphics g, Rectangle r)
        {
            using (var b = new LinearGradientBrush(r, Tint(140, 225, 255), Tint(10, 120, 190), 90f)) g.FillRectangle(b, r);
            var pts = new PointF[SkullArt.Foot.Length / 2];
            float u = r.Height * 0.3f;
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(r.X + r.Width / 2f + SkullArt.Foot[2 * i] * u, r.Y + r.Height / 2f + SkullArt.Foot[2 * i + 1] * u);
            using (var path = new GraphicsPath())
            {
                path.AddClosedCurve(pts, 0.35f);
                using (var b = new SolidBrush(Color.FromArgb(230, 255, 255, 255))) g.FillPath(b, path);
            }
        }

        // The album art turned upside down, fading from faint to nothing, as if standing on glass.
        Bitmap Reflection(int width, int height)
        {
            float tint = Theme.Hue * 10 + Theme.Saturation;   // the placeholder tile follows the accent sliders
            if (reflection != null && reflectionOf == info.Art && reflectionSize == width * 10000 + height && (info.Art != null || reflectionTint == tint)) return reflection;
            if (reflection != null) reflection.Dispose();
            reflection = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            reflectionOf = info.Art;
            reflectionSize = width * 10000 + height;
            reflectionTint = tint;
            using (var full = new Bitmap(width, width, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(full))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    var rect = new Rectangle(0, 0, width, width);
                    if (info.Art != null)
                    {
                        int side = Math.Min(info.Art.Width, info.Art.Height);
                        g.DrawImage(info.Art, rect, new Rectangle((info.Art.Width - side) / 2, (info.Art.Height - side) / 2, side, side), GraphicsUnit.Pixel);
                    }
                    else AeroPlaceholder(g, rect);
                }
                for (int y = 0; y < height; y++)
                {
                    float a = 0.38f * (1 - y / (float)height);
                    for (int x = 0; x < width; x++)
                    {
                        var c = full.GetPixel(x, width - 1 - y);
                        reflection.SetPixel(x, y, Color.FromArgb((int)(c.A * a), c.R, c.G, c.B));
                    }
                }
            }
            return reflection;
        }

        // ---- XP style: a Windows XP-era media player (600 x 214 units at 100%): a Luna-blue curved wing on the left,
        // a silver-blue metallic frame round a black screen with green spectrum bars, and a glossy control bar that
        // swoops out underneath. Its colors follow the accent sliders like the Aero card's.

        public Func<float> LevelSource;   // how loud the player is right now (0..1); drives the visualizer
        readonly VizEngine vizEngine = new VizEngine();

        float U(float v) { return v * Theme.Scale * scale; }
        RectangleF R(float x, float y, float w, float h) { return new RectangleF(U(x), U(y), U(w), U(h)); }

        // The screen's visualizer follows the player's real loudness (see VizEngine).
        void UpdateXpBars()
        {
            vizEngine.Update(!info.Playing ? 0 : LevelSource != null ? LevelSource() : 0.4f);
        }

        void PaintXp(Graphics g)
        {
            if (shaded) { PaintXpShade(g); return; }
            bool any = !string.IsNullOrEmpty(info.Title);
            var wing = R(0, 6, 104, 196);
            var frame = R(64, 0, 536, 196);
            var swoop = R(36, 154, 360, 60);
            var screen = R(108, 26, 484, 122);

            using (var wingPath = RoundRect(wing, U(18)))
            using (var framePath = RoundRect(frame, U(10)))
            using (var swoopPath = RoundRect(swoop, U(28)))
            {
                // The frame: brushed silver-blue metal, brighter at the top edge.
                using (var b = new LinearGradientBrush(frame, Color.White, Color.White, 90f))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Tint(240, 245, 253), Tint(200, 215, 238), Tint(172, 192, 226), Tint(212, 225, 244), Tint(150, 172, 212) },
                        Positions = new[] { 0f, 0.1f, 0.5f, 0.88f, 1f }
                    };
                    g.FillPath(b, framePath);
                }
                using (var p = new Pen(Tint(40, 70, 132), Math.Max(1, U(1)))) g.DrawPath(p, framePath);
                using (var inner = RoundRect(RectangleF.Inflate(frame, -U(1.5f), -U(1.5f)), U(9)))
                using (var p = new Pen(Color.FromArgb(170, 255, 255, 255), 1)) g.DrawPath(p, inner);

                // The wing in front of it: deep Luna blue at the edge to pale blue, with a dark rim.
                using (var b = new LinearGradientBrush(wing, Tint(28, 70, 156), Tint(150, 186, 236), 0f)) g.FillPath(b, wingPath);
                using (var p = new Pen(Tint(20, 44, 110), Math.Max(1, U(1)))) g.DrawPath(p, wingPath);

                // The wing's "Now Playing" tab and the app it's playing in, with the Dino footprint where a logo would go.
                // The tab is a glossy capsule sitting inside the wing: a darker glass body, a soft shine across its top
                // half, a white inner edge and a dark rim, with the label in white over a faint shadow.
                var tab = R(9, 31, 87, 23);
                using (var tabPath = RoundRect(tab, tab.Height / 2))
                {
                    using (var b = new LinearGradientBrush(tab, Color.White, Color.White, 90f))
                    {
                        b.InterpolationColors = new ColorBlend
                        {
                            Colors = new[] { Tint(92, 142, 222), Tint(48, 96, 190), Tint(30, 72, 164), Tint(70, 128, 214) },
                            Positions = new[] { 0f, 0.5f, 0.52f, 1f }
                        };
                        g.FillPath(b, tabPath);
                    }
                    var shine = new RectangleF(tab.X + U(3), tab.Y + U(1.5f), tab.Width - U(6), tab.Height * 0.46f);
                    using (var shinePath = RoundRect(shine, shine.Height / 2))
                    using (var b = new LinearGradientBrush(RectangleF.Inflate(shine, 0, 1), Color.FromArgb(150, 255, 255, 255), Color.FromArgb(25, 255, 255, 255), 90f))
                        g.FillPath(b, shinePath);
                    using (var inner = RoundRect(RectangleF.Inflate(tab, -U(1), -U(1)), tab.Height / 2 - U(1)))
                    using (var p = new Pen(Color.FromArgb(90, 255, 255, 255), 1)) g.DrawPath(p, inner);
                    using (var p = new Pen(Tint(18, 42, 104), Math.Max(1, U(1)))) g.DrawPath(p, tabPath);
                }
                var labelBox = new RectangleF(tab.X, tab.Y + (tab.Height - xLabel.GetHeight(g)) / 2, tab.Width, tab.Height);
                using (var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                {
                    using (var b = new SolidBrush(Color.FromArgb(150, 10, 30, 80)))
                        g.DrawString("Now Playing", xLabel, b, new RectangleF(labelBox.X + 1, labelBox.Y + 1, labelBox.Width, labelBox.Height), fmt);
                    g.DrawString("Now Playing", xLabel, Brushes.White, labelBox, fmt);
                }
                // A thin line under it that fades out at both ends.
                var rule = new RectangleF(U(12), U(62), U(84), 1);
                using (var b = new LinearGradientBrush(new RectangleF(rule.X - 1, rule.Y, rule.Width + 2, 1), Color.White, Color.White, 0f))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) },
                        Positions = new[] { 0f, 0.4f, 1f }
                    };
                    g.FillRectangle(b, rule);
                }
                if (any && info.App != "")
                {
                    float ix = U(14);
                    if (info.AppIcon != null) { g.DrawImage(info.AppIcon, ix, U(70), U(14), U(14)); ix += U(18); }
                    XpText(g, info.App, xSmall, new RectangleF(ix, U(71), U(98) - ix, U(14)), Color.White, false);
                }
                DrawVizBox(g);
                var pts = new PointF[SkullArt.Foot.Length / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(U(50) + SkullArt.Foot[2 * i] * U(11), U(132) + SkullArt.Foot[2 * i + 1] * U(11));
                using (var path = new GraphicsPath())
                {
                    path.AddClosedCurve(pts, 0.35f);
                    using (var b = new LinearGradientBrush(path.GetBounds(), Color.White, Tint(170, 205, 250), 90f)) g.FillPath(b, path);
                    using (var p = new Pen(Tint(30, 70, 150), 1)) g.DrawPath(p, path);
                }

                // The control bar swooping out under the screen: glossy blue with a bright top.
                using (var b = new LinearGradientBrush(swoop, Color.White, Color.White, 90f))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Tint(196, 216, 248), Tint(96, 146, 220), Tint(52, 100, 186), Tint(30, 72, 160) },
                        Positions = new[] { 0f, 0.4f, 0.7f, 1f }
                    };
                    g.FillPath(b, swoopPath);
                }
                using (var hl = RoundRect(new RectangleF(swoop.X + U(3), swoop.Y + U(2), swoop.Width - U(6), swoop.Height * 0.42f), U(20)))
                using (var b = new LinearGradientBrush(new RectangleF(swoop.X, swoop.Y, swoop.Width, swoop.Height * 0.45f), Color.FromArgb(140, 255, 255, 255), Color.FromArgb(10, 255, 255, 255), 90f))
                    g.FillPath(b, hl);
                using (var p = new Pen(Tint(22, 52, 120), Math.Max(1, U(1)))) g.DrawPath(p, swoopPath);
            }

            DrawXpWindowButtons(g);

            // The screen: black glass in a dark bezel.
            g.FillRectangle(Brushes.Black, screen);
            using (var p = new Pen(Tint(50, 66, 100), 1)) g.DrawRectangle(p, screen.X - 1, screen.Y - 1, screen.Width + 1, screen.Height + 1);
            var art = R(108 + 484 - 74, 34, 64, 64);
            if (info.Art != null)
            {
                int side = Math.Min(info.Art.Width, info.Art.Height);
                g.DrawImage(info.Art, art, new RectangleF((info.Art.Width - side) / 2f, (info.Art.Height - side) / 2f, side, side), GraphicsUnit.Pixel);
                using (var p = new Pen(Color.FromArgb(120, 120, 120), 1)) g.DrawRectangle(p, art.X, art.Y, art.Width, art.Height);
            }
            float textW = art.X - U(12) - U(120);
            if (any)
            {
                XpText(g, info.Artist != "" ? info.Artist : info.Album, xArtist, new RectangleF(U(120), U(30), textW, xArtist.Height + 2), Color.FromArgb(225, 225, 225), false);
                // Title, scrolling when it's too long.
                float tw = g.MeasureString(info.Title, xTitle, PointF.Empty, StringFormat.GenericTypographic).Width;
                var box = new RectangleF(U(120), U(52), textW, xTitle.Height + U(4));
                var clip = g.Clip;
                g.SetClip(box);
                if (tw <= textW) XpText(g, info.Title, xTitle, new RectangleF(box.X, box.Y, textW + U(20), box.Height), Color.White, true);
                else
                {
                    float gap = U(60), loop = tw + gap;
                    if (scroll >= loop) { scroll = 0; hold = 60; }
                    XpText(g, info.Title, xTitle, new RectangleF(box.X - scroll, box.Y, tw + U(10), box.Height), Color.White, true);
                    XpText(g, info.Title, xTitle, new RectangleF(box.X - scroll + loop, box.Y, tw + U(10), box.Height), Color.White, true);
                }
                g.Clip = clip;
            }
            else XpText(g, "Nothing playing", xArtist, new RectangleF(U(120), U(30), textW, xArtist.Height + 2), Color.FromArgb(200, 200, 200), false);

            // The visualizer strip along the bottom of the screen, in the style picked in the wing.
            // Classic green, or the app's several colors when Colors is set to more than one.
            var palette = ColorMix.Palette();
            if (palette != null) vizEngine.DrawMulti(g, R(118, 98, 466, 36), Visualizer, U(1), palette);
            else vizEngine.Draw(g, R(118, 98, 466, 36), Visualizer, U(1), VizColors.XpGreen);
            // A green status line: the album, and the time on the right.
            var green = Color.FromArgb(90, 225, 90);   // the screen stays classic green whatever the skin color
            using (var b = new SolidBrush(green))
                g.FillPolygon(b, new[] { new PointF(U(120), U(138)), new PointF(U(126), U(141)), new PointF(U(120), U(144)) });
            if (any)
            {
                var now = ShownPosition;
                string times = info.Duration > TimeSpan.Zero ? Time(now) + " / " + Time(info.Duration) : "";
                float timesW = g.MeasureString(times, xSmall, PointF.Empty, StringFormat.GenericTypographic).Width;
                XpText(g, info.Album != "" ? info.Album : info.Playing ? "Playing" : "Paused", xSmall, new RectangleF(U(131), U(135), U(440) - timesW, U(13)), green, false);
                XpText(g, times, xSmall, new RectangleF(U(588) - timesW, U(135), timesW + U(4), U(13)), green, false);
            }

            // Controls on the swoop: a chrome play/pause orb, Previous/Next capsules, the seek bar and the volume.
            buttons[1] = Rectangle.Round(R(60, 164, 42, 42));
            buttons[0] = Rectangle.Round(R(130, 178, 30, 16));
            buttons[2] = Rectangle.Round(R(164, 178, 30, 16));
            XpOrb(g, buttons[1], any && hoverButton == 1, pressed == 1 && pressFrames > 0, any);
            XpCapsule(g, buttons[0], 0, any && hoverButton == 0, pressed == 0 && pressFrames > 0, any);
            XpCapsule(g, buttons[2], 2, any && hoverButton == 2, pressed == 2 && pressFrames > 0, any);
            progressBar = Rectangle.Empty;
            if (any && info.Duration > TimeSpan.Zero)
            {
                var seek = Rectangle.Round(R(130, 162, 250, 7));
                progressBar = seek;
                XpTrack(g, seek, (float)Math.Min(1, ShownPosition.TotalSeconds / info.Duration.TotalSeconds), seeking || progressHover);
            }
            volumeBar = Rectangle.Empty;
            float vol = ShownVolume;
            if (any && vol >= 0)
            {
                var vb = Rectangle.Round(R(232, 184, 82, 6));
                volumeBar = vb;
                XpSpeaker(g, U(210), U(187), vol);
                XpTrack(g, vb, Math.Max(0, Math.Min(1, vol)), volumeHover || draggingVolume);
            }

            // The size grip in the bottom-right corner, XP-style: drag it (or any corner) to resize.
            using (var light = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
            using (var dark = new SolidBrush(Tint(90, 110, 150)))
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col <= row; col++)
                    {
                        float x = U(590) - col * U(4), y = U(188) - (row - col) * U(4);
                        g.FillRectangle(light, x + 1, y + 1, U(2), U(2));
                        g.FillRectangle(dark, x, y, U(2), U(2));
                    }

            // The screen's text and the wing move the card.
            moveArea = Rectangle.Round(R(108, 26, 484, 72));
            moveBand = Rectangle.Round(R(0, 6, 60, 140));
        }

        // ---- The XP card's window buttons, at the right-hand end of the top strip: close (red), minimize (yellow,
        // rolls the card up into a thin bar and back) and maximize (green, the biggest size that fits the screen,
        // centered, and back). They're glossy lights that show their symbol while the mouse is over the card.

        readonly Rectangle[] xpWinButtons = new Rectangle[3];
        int xpWinHover = -1;
        bool shaded, maximized;
        float restoreScale;
        Point restoreAt;
        public event Action CloseRequested;

        int XpWindowButtonAt(Point p)
        {
            if (!xp) return -1;
            for (int i = 0; i < 3; i++) if (Rectangle.Inflate(xpWinButtons[i], S(2), S(3)).Contains(p)) return i;
            return -1;
        }

        void DrawXpWindowButtons(Graphics g)
        {
            var light = new[] { Color.FromArgb(255, 150, 140), Color.FromArgb(255, 236, 150), Color.FromArgb(170, 240, 140) };
            var deep = new[] { Color.FromArgb(205, 38, 30), Color.FromArgb(220, 156, 12), Color.FromArgb(40, 152, 44) };
            var rim = new[] { Color.FromArgb(120, 18, 12), Color.FromArgb(135, 90, 0), Color.FromArgb(18, 90, 22) };
            for (int i = 0; i < 3; i++)
            {
                var r = R(546 + i * 16, 6, 13, 13);
                xpWinButtons[i] = Rectangle.Round(r);
                using (var b = new LinearGradientBrush(RectangleF.Inflate(r, 0, 1), light[i], deep[i], 90f)) g.FillEllipse(b, r);
                if (xpWinHover == i) using (var b = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) g.FillEllipse(b, r);
                using (var p = new Pen(rim[i], Math.Max(1, U(1)))) g.DrawEllipse(p, r);
                using (var b = new SolidBrush(Color.FromArgb(190, 255, 255, 255)))
                    g.FillEllipse(b, r.X + r.Width * 0.24f, r.Y + r.Height * 0.1f, r.Width * 0.52f, r.Height * 0.36f);
                if (!hovering) continue;
                // The symbol: × close, – minimize (or ▾ to roll back down), + maximize (or – to go back).
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2 + U(0.5f), a = r.Width * 0.22f;
                using (var p = new Pen(Color.FromArgb(200, rim[i]), Math.Max(1.2f, U(1.6f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    if (i == 0) { g.DrawLine(p, cx - a, cy - a, cx + a, cy + a); g.DrawLine(p, cx - a, cy + a, cx + a, cy - a); }
                    else if (i == 1 && shaded) g.DrawLines(p, new[] { new PointF(cx - a, cy - a * 0.5f), new PointF(cx, cy + a * 0.6f), new PointF(cx + a, cy - a * 0.5f) });
                    else if (i == 1 || maximized) g.DrawLine(p, cx - a, cy, cx + a, cy);
                    else { g.DrawLine(p, cx - a, cy, cx + a, cy); g.DrawLine(p, cx, cy - a, cx, cy + a); }
                }
            }
        }

        void PressXpWindowButton(int i)
        {
            if (i == 0) { if (CloseRequested != null) BeginInvoke(CloseRequested); return; }
            if (i == 1)
            {
                shaded = !shaded;
                CardScale = scale;   // the top-left corner stays put
            }
            else if (!maximized)
            {
                restoreScale = scale;
                restoreAt = Location;
                var wa = Screen.FromRectangle(Bounds).WorkingArea;
                float fit = Math.Min(wa.Width * 0.96f / (600 * Theme.Scale), wa.Height * 0.96f / (214 * Theme.Scale));
                shaded = false;
                maximized = true;
                CardScale = Math.Min(MaxScale, fit);
                Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
            }
            else
            {
                maximized = false;
                shaded = false;
                CardScale = restoreScale;
                Location = restoreAt;
                if (Moved != null) Moved();
            }
            RenderLayered();
        }

        // Rolled up: one thin bar with the wing's blue end (the footprint and "Now Playing"), the title and artist on
        // the metal, and the window buttons. Dragging the bar moves the card.
        void PaintXpShade(Graphics g)
        {
            for (int i = 0; i < 3; i++) buttons[i] = Rectangle.Empty;
            progressBar = volumeBar = vizBox = Rectangle.Empty;
            var bar = R(0, 0, 600, 25);
            using (var path = RoundRect(bar, U(12)))
            {
                using (var b = new LinearGradientBrush(bar, Color.White, Color.White, 90f))
                {
                    b.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Tint(240, 245, 253), Tint(200, 215, 238), Tint(172, 192, 226), Tint(150, 172, 212) },
                        Positions = new[] { 0f, 0.3f, 0.7f, 1f }
                    };
                    g.FillPath(b, path);
                }
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                var cap = R(0, 0, 104, 25);
                using (var b = new LinearGradientBrush(cap, Tint(28, 70, 156), Tint(120, 165, 230), 0f)) g.FillRectangle(b, cap);
                using (var b = new LinearGradientBrush(new RectangleF(cap.X, cap.Y, cap.Width, cap.Height / 2 + 1), Color.FromArgb(110, 255, 255, 255), Color.FromArgb(10, 255, 255, 255), 90f))
                    g.FillRectangle(b, cap.X, cap.Y, cap.Width, cap.Height / 2);
                using (var p = new Pen(Tint(20, 44, 110), 1)) g.DrawLine(p, cap.Right, cap.Y, cap.Right, cap.Bottom);
                g.Clip = clip;
                using (var p = new Pen(Tint(20, 44, 110), Math.Max(1, U(1)))) g.DrawPath(p, path);
            }
            var pts = new PointF[SkullArt.Foot.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(U(16) + SkullArt.Foot[2 * i] * U(7), U(12.5f) + SkullArt.Foot[2 * i + 1] * U(7));
            using (var path = new GraphicsPath())
            {
                path.AddClosedCurve(pts, 0.35f);
                using (var b = new LinearGradientBrush(path.GetBounds(), Color.White, Tint(170, 205, 250), 90f)) g.FillPath(b, path);
                using (var p = new Pen(Tint(30, 70, 150), 1)) g.DrawPath(p, path);
            }
            float labelY = U(12.5f) - xLabel.GetHeight(g) / 2;
            XpText(g, "Now Playing", xLabel, new RectangleF(U(29) + 1, labelY + 1, U(72), U(16)), Color.FromArgb(150, 10, 30, 80), false);
            XpText(g, "Now Playing", xLabel, new RectangleF(U(29), labelY, U(72), U(16)), Color.White, false);
            bool any = !string.IsNullOrEmpty(info.Title);
            string line = !any ? "Nothing playing" : info.Title + (info.Artist != "" ? "  ·  " + info.Artist : "");
            XpText(g, line, xLabel, new RectangleF(U(114), labelY, U(424), U(16)), any ? Tint(18, 40, 96) : Tint(80, 96, 130), false);
            DrawXpWindowButtons(g);
            moveArea = Rectangle.Round(R(0, 0, 540, 25));
            moveBand = Rectangle.Empty;
        }

        // ---- The XP screen's visualizer picker (the styles themselves are drawn by VizEngine, in classic green)

        public int Visualizer;                 // 0 bars, 1 scope, 2 mirror, 3 LED
        public event Action<int> VisualizerChanged;
        static readonly string[] VisualizerNames = VizEngine.Names;

        // The wing's visualizer picker: a classic XP combo box that opens a menu of the styles.
        Rectangle vizBox;
        bool vizHover;
        ContextMenuStrip vizMenu;

        void DrawVizBox(Graphics g)
        {
            var box = R(12, 92, 86, 17);
            vizBox = Rectangle.Round(box);
            g.FillRectangle(Brushes.White, box);
            using (var p = new Pen(Tint(127, 157, 185), 1)) g.DrawRectangle(p, box.X, box.Y, box.Width, box.Height);
            XpText(g, VisualizerNames[Math.Max(0, Math.Min(VisualizerNames.Length - 1, Visualizer))], xSmall,
                   new RectangleF(box.X + U(4), box.Y + U(3), box.Width - U(20), box.Height - U(2)), Color.FromArgb(20, 20, 20), false);
            var btn = new RectangleF(box.Right - U(15), box.Y + 1, U(14), box.Height - 1);
            using (var b = new LinearGradientBrush(btn, vizHover ? Tint(225, 238, 255) : Tint(200, 220, 255), vizHover ? Tint(150, 185, 245) : Tint(120, 160, 230), 90f)) g.FillRectangle(b, btn);
            using (var p = new Pen(Tint(150, 180, 230), 1)) g.DrawRectangle(p, btn.X, btn.Y, btn.Width - 1, btn.Height - 1);
            float ax = btn.X + btn.Width / 2, ay = btn.Y + btn.Height / 2;
            using (var b = new SolidBrush(Tint(30, 50, 100)))
                g.FillPolygon(b, new[] { new PointF(ax - U(3.5f), ay - U(1.5f)), new PointF(ax + U(3.5f), ay - U(1.5f)), new PointF(ax, ay + U(2)) });
        }

        void ShowVizMenu()
        {
            if (vizMenu == null)
            {
                vizMenu = new ContextMenuStrip();
                for (int i = 0; i < VisualizerNames.Length; i++)
                {
                    var item = new ToolStripMenuItem(VisualizerNames[i]) { Tag = i };
                    item.Click += (s, e) =>
                    {
                        Visualizer = (int)((ToolStripItem)s).Tag;
                        if (VisualizerChanged != null) VisualizerChanged(Visualizer);
                    };
                    vizMenu.Items.Add(item);
                }
            }
            foreach (ToolStripMenuItem item in vizMenu.Items) item.Checked = (int)item.Tag == Visualizer;
            vizMenu.Show(this, new Point(vizBox.X, vizBox.Bottom + 1));
        }

        void XpText(Graphics g, string s, Font f, RectangleF box, Color color, bool big)
        {
            using (var fmt = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var b = new SolidBrush(color))
                g.DrawString(s, f, b, box, fmt);
        }

        // The big round play/pause button: a blue glass center in a chrome ring.
        void XpOrb(Graphics g, Rectangle r, bool hover, bool down, bool enabled)
        {
            var rf = new RectangleF(r.X, r.Y, r.Width, r.Height);
            using (var b = new LinearGradientBrush(rf, Color.White, Tint(110, 130, 170), 90f)) g.FillEllipse(b, rf);   // chrome ring
            using (var p = new Pen(Tint(40, 60, 110), 1)) g.DrawEllipse(p, rf);
            var c = RectangleF.Inflate(rf, -rf.Width * 0.12f, -rf.Height * 0.12f);
            using (var b = new LinearGradientBrush(c, Tint(150, 200, 255), Tint(20, 70, 170), 90f)) g.FillEllipse(b, c);
            using (var b = new LinearGradientBrush(new RectangleF(c.X, c.Y, c.Width, c.Height / 2), Color.FromArgb(200, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 90f))
                g.FillEllipse(b, c.X + c.Width * 0.15f, c.Y + c.Height * 0.04f, c.Width * 0.7f, c.Height * 0.48f);
            if (hover) using (var b = new SolidBrush(Color.FromArgb(55, 255, 255, 255))) g.FillEllipse(b, c);
            if (down) using (var b = new SolidBrush(Color.FromArgb(70, 0, 20, 60))) g.FillEllipse(b, c);
            using (var p = new Pen(Tint(20, 50, 120), 1)) g.DrawEllipse(p, c);
            float cx = c.X + c.Width / 2, cy = c.Y + c.Height / 2, s = c.Width * 0.2f;
            using (var b = new SolidBrush(enabled ? Color.White : Color.FromArgb(140, 255, 255, 255)))
            {
                if (info.Playing)
                {
                    g.FillRectangle(b, cx - s * 0.85f, cy - s, s * 0.6f, 2 * s);
                    g.FillRectangle(b, cx + s * 0.25f, cy - s, s * 0.6f, 2 * s);
                }
                else g.FillPolygon(b, new[] { new PointF(cx - s * 0.65f, cy - s), new PointF(cx + s, cy), new PointF(cx - s * 0.65f, cy + s) });
            }
        }

        // A small rounded capsule button (previous or next) in silver-blue glass.
        void XpCapsule(Graphics g, Rectangle r, int kind, bool hover, bool down, bool enabled)
        {
            var rf = new RectangleF(r.X, r.Y, r.Width, r.Height);
            using (var path = RoundRect(rf, rf.Height / 2))
            {
                using (var b = new LinearGradientBrush(rf, Tint(236, 244, 255), Tint(120, 160, 222), 90f)) g.FillPath(b, path);
                if (hover) using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillPath(b, path);
                if (down) using (var b = new SolidBrush(Color.FromArgb(70, 0, 20, 60))) g.FillPath(b, path);
                using (var p = new Pen(Tint(30, 60, 120), 1)) g.DrawPath(p, path);
            }
            float cx = rf.X + rf.Width / 2, cy = rf.Y + rf.Height / 2, s = rf.Height * 0.28f, d = kind == 0 ? -1 : 1;
            using (var b = new SolidBrush(enabled ? Tint(20, 50, 110) : Tint(110, 130, 170)))
            {
                g.FillPolygon(b, new[] { new PointF(cx - d * s * 0.9f, cy - s), new PointF(cx + d * s * 0.6f, cy), new PointF(cx - d * s * 0.9f, cy + s) });
                g.FillRectangle(b, d > 0 ? cx + s * 0.6f : cx - s * 0.6f - s * 0.4f, cy - s, s * 0.4f, 2 * s);
            }
        }

        // A sunken track with a green fill and a little chrome knob (seek and volume).
        void XpTrack(Graphics g, Rectangle r, float fraction, bool hot)
        {
            using (var b = new LinearGradientBrush(r, Tint(20, 40, 80), Tint(70, 100, 150), 90f)) g.FillRectangle(b, r);
            int fw = (int)Math.Round(r.Width * fraction);
            if (fw > 0)
            {
                var fill = new Rectangle(r.X, r.Y, fw, r.Height);
                using (var b = new LinearGradientBrush(fill, Color.FromArgb(170, 255, 120), Color.FromArgb(30, 160, 30), 90f)) g.FillRectangle(b, fill);
            }
            using (var p = new Pen(Color.FromArgb(150, 255, 255, 255), 1)) g.DrawLine(p, r.X, r.Bottom, r.Right, r.Bottom);
            float k = r.Height * (hot ? 2.2f : 1.8f);
            var knob = new RectangleF(r.X + fw - k / 2, r.Y + r.Height / 2f - k / 2, k, k);
            using (var b = new LinearGradientBrush(knob, Color.White, Tint(130, 150, 190), 90f)) g.FillEllipse(b, knob);
            using (var p = new Pen(Tint(40, 60, 110), 1)) g.DrawEllipse(p, knob);
        }

        // A small white speaker with sound waves (one, or two above half volume).
        void XpSpeaker(Graphics g, float x, float cy, float vol)
        {
            float s = U(4.5f);
            using (var b = new SolidBrush(Color.White))
                g.FillPolygon(b, new[] { new PointF(x, cy - s * 0.55f), new PointF(x + s * 0.8f, cy - s * 0.55f), new PointF(x + s * 1.7f, cy - s * 1.3f),
                                         new PointF(x + s * 1.7f, cy + s * 1.3f), new PointF(x + s * 0.8f, cy + s * 0.55f), new PointF(x, cy + s * 0.55f) });
            using (var p = new Pen(Color.White, Math.Max(1.2f, U(1.3f))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float mx = x + s * 1.7f, r1 = s * 0.8f, r2 = s * 1.55f;
                if (vol > 0.001f) g.DrawArc(p, mx - r1, cy - r1, 2 * r1, 2 * r1, -45, 90);
                if (vol > 0.5f) g.DrawArc(p, mx - r2, cy - r2, 2 * r2, 2 * r2, -45, 90);
            }
        }

        // ---- Windows style: measured off the Windows 10 media flyout (635 x 174 units at 100%).

        Rectangle WinLeft { get { return new Rectangle(0, 0, S(80), S(174)); } }
        Rectangle WinRight { get { return new Rectangle(S(87), 0, S(548), S(174)); } }
        Rectangle WinTrack { get { return new Rectangle(S(33), S(25), S(14), S(98)); } }
        Rectangle WinArt { get { return new Rectangle(S(87 + 405), S(25), S(98), S(98)); } }

        Rectangle WinButton(int i)
        {
            int d = S(50);
            return new Rectangle(S(87 + 36) + i * S(100), S(25), d, d);
        }

        static readonly Color WinGray = Color.FromArgb(106, 106, 106);

        // The volume fill: Windows' dark blue at the default accent hue, turning with the Accent Hue and Saturation sliders.
        static Color WinBlue { get { return Tint(3, 62, 114); } }

        void PaintWindows(Graphics g)
        {
            g.Clear(Color.FromArgb(31, 31, 31));
            bool any = !string.IsNullOrEmpty(info.Title);

            // (The volume panel is drawn by its own window: PaintVolumePanel.)
            // Player panel: three ring buttons, title and artist, album art and the app it's playing in.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < 3; i++)
            {
                var r = WinButton(i);
                bool hov = any && i == hoverButton, down = i == pressed && pressFrames > 0;
                var ring = new RectangleF(r.X + S(1) * 1.25f, r.Y + S(1) * 1.25f, r.Width - S(1) * 2.5f, r.Height - S(1) * 2.5f);
                if (hov || down) using (var b = new SolidBrush(Color.FromArgb(down ? 90 : 45, 255, 255, 255))) g.FillEllipse(b, ring);
                using (var p = new Pen(any ? Color.White : Color.FromArgb(120, 255, 255, 255), Math.Max(1.5f, S(1) * 2.5f))) g.DrawEllipse(p, ring);
                DrawWinGlyph(g, i, r.X + r.Width / 2f, r.Y + r.Height / 2f, any);
            }
            g.SmoothingMode = SmoothingMode.None;

            int tx = WinRight.X + S(36), tw = WinArt.X - S(20) - tx;
            if (any)
            {
                DrawBaseline(g, info.Title, wTitle, tx, tw, S(125), false);
                DrawBaseline(g, info.Artist != "" ? info.Artist : info.Album, wSmall, tx, tw, S(150), false);
            }
            else DrawBaseline(g, "Nothing playing", wTitle, tx, tw, S(125), false);

            var art = WinArt;
            if (info.Art != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                int side = Math.Min(info.Art.Width, info.Art.Height);
                g.DrawImage(info.Art, art, new Rectangle((info.Art.Width - side) / 2, (info.Art.Height - side) / 2, side, side), GraphicsUnit.Pixel);
            }
            else using (var b = new SolidBrush(Color.FromArgb(48, 48, 48))) g.FillRectangle(b, art);

            // App: its icon as a white glyph and its name, centered under the art.
            if (any && info.App != "")
            {
                int icon = info.AppIcon != null ? S(22) : 0, gap = icon > 0 ? S(4) : 0;
                int nameW = TextRenderer.MeasureText(g, info.App, wApp, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                int lx = art.X + (art.Width - icon - gap - nameW) / 2, cy = S(138);
                if (info.AppIcon != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(info.AppIcon, lx, cy - icon / 2, icon, icon);
                }
                TextRenderer.DrawText(g, info.App, wApp, new Point(lx + icon + gap, cy - wApp.Height / 2), Color.White, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }

            // Corner brackets while hovering, so it's clear the corners resize (the only extra over the original).
            if (hovering || corner >= 0)
                using (var b = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                {
                    int len = Math.Max(8, S(12)), th = Math.Max(2, S(2));
                    g.FillRectangle(b, 0, 0, len, th); g.FillRectangle(b, 0, 0, th, len);
                    g.FillRectangle(b, 0, Height - th, len, th); g.FillRectangle(b, 0, Height - len, th, len);
                    g.FillRectangle(b, Width - len, 0, len, th); g.FillRectangle(b, Width - th, 0, th, len);
                    g.FillRectangle(b, Width - len, Height - th, len, th); g.FillRectangle(b, Width - th, Height - len, th, len);
                }
        }

        // Text placed by its baseline (how the flyout lines things up), cut off with "…" past the width.
        static void DrawBaseline(Graphics g, string text, Font f, int x, int width, int baseline, bool center)
        {
            var fam = f.FontFamily;
            float ascent = f.Size * fam.GetCellAscent(f.Style) / fam.GetEmHeight(f.Style);
            int top = baseline - (int)Math.Round(ascent);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                        (center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);
            TextRenderer.DrawText(g, text, f, new Rectangle(x, top, width, f.Height + 4), Color.White, flags);
        }

        // Previous (bar + left triangle), pause (two bars) or play, next (right triangle + bar), 16 units tall.
        void DrawWinGlyph(Graphics g, int i, float cx, float cy, bool any)
        {
            float u = Theme.Scale * scale, h = 8 * u;
            using (var b = new SolidBrush(any ? Color.White : Color.FromArgb(120, 255, 255, 255)))
            {
                if (i == 0)
                {
                    g.FillRectangle(b, cx - 8.5f * u, cy - h, 3.5f * u, 2 * h);
                    g.FillPolygon(b, new[] { new PointF(cx - 2f * u, cy), new PointF(cx + 8.5f * u, cy - h), new PointF(cx + 8.5f * u, cy + h) });
                }
                else if (i == 2)
                {
                    g.FillPolygon(b, new[] { new PointF(cx - 8.5f * u, cy - h), new PointF(cx + 2f * u, cy), new PointF(cx - 8.5f * u, cy + h) });
                    g.FillRectangle(b, cx + 5f * u, cy - h, 3.5f * u, 2 * h);
                }
                else if (info.Playing)
                {
                    g.FillRectangle(b, cx - 6.5f * u, cy - h, 3.5f * u, 2 * h);
                    g.FillRectangle(b, cx + 3f * u, cy - h, 3.5f * u, 2 * h);
                }
                else g.FillPolygon(b, new[] { new PointF(cx - 5f * u, cy - h), new PointF(cx + 8f * u, cy), new PointF(cx - 5f * u, cy + h) });
            }
        }

        int WinButtonAt(Point p)
        {
            if (string.IsNullOrEmpty(info.Title)) return -1;
            for (int i = 0; i < 3; i++)
            {
                var r = WinButton(i);
                float dx = p.X - (r.X + r.Width / 2f), dy = p.Y - (r.Y + r.Height / 2f);
                if (dx * dx + dy * dy <= r.Width * r.Width / 4f) return i;
            }
            return -1;
        }

        Rectangle WinTrackHit { get { return Rectangle.Inflate(WinTrack, S(14), S(8)); } }

        // The middle of the player panel (title, artist, art and app name) moves the card.
        bool WinMoveArea(Point p) { return p.X >= WinRight.X && p.Y >= S(82) && p.Y < WinRight.Bottom - S(10); }

        void SlideVolumeVertical(int mouseY)
        {
            var t = WinTrack;
            int thumbH = S(14);
            localVolume = Math.Max(0, Math.Min(1, 1 - (mouseY - t.Y - thumbH / 2f) / (t.Height - thumbH)));
            localUntil = DateTime.UtcNow.AddSeconds(1.5);
            if (VolumeChanged != null) VolumeChanged(localVolume);
            Invalidate();
        }

        // Seeking: dragging along the progress bar previews the spot; letting go jumps the player there.
        Rectangle progressBar, moveArea, moveBand;
        bool seeking, progressHover;
        TimeSpan seekPreview, seekTarget;
        DateTime seekAt, seekHoldUntil;
        public event Action<TimeSpan> SeekRequested;

        Rectangle ProgressHit { get { return progressBar.IsEmpty ? Rectangle.Empty : Rectangle.Inflate(progressBar, S(4), S(10)); } }

        bool InMoveArea(Point p) { return moveArea.Contains(p) || moveBand.Contains(p); }

        // Where the bar shows the track: the drag preview, or right after a jump the new spot (until the player's own
        // report catches up), else the player's position.
        TimeSpan ShownPosition
        {
            get
            {
                if (seeking) return seekPreview;
                if (DateTime.UtcNow < seekHoldUntil)
                {
                    var p = seekTarget + (info.Playing ? DateTime.UtcNow - seekAt : TimeSpan.Zero);
                    if (Math.Abs((info.Now - p).TotalSeconds) > 1.5) return p;
                    seekHoldUntil = DateTime.MinValue;   // the player has caught up
                }
                return info.Now;
            }
        }

        TimeSpan PositionAt(int mouseX)
        {
            if (progressBar.Width <= 0) return TimeSpan.Zero;
            double f = Math.Max(0, Math.Min(1, (mouseX - progressBar.X) / (double)progressBar.Width));
            return TimeSpan.FromSeconds(info.Duration.TotalSeconds * f);
        }

        // Corner grabbing: 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right, -1 none.
        int corner = -1;
        bool hovering;
        float startScale;
        Rectangle startBounds;
        Point startMouse;

        int CornerAt(Point p)
        {
            int z = Math.Max(10, S(16));
            if (xp && shaded) return -1;   // rolled up: no resizing
            if (xp)
            {
                // The XP card isn't a rectangle: its corners are the wing's top, the frame's right-hand corners and
                // the bottom of the control bar's swoop.
                var pts = new[] { new Point(S(10), S(16)), new Point(Width - S(8), S(8)), new Point(S(54), Height - S(10)), new Point(Width - S(8), S(188)) };
                for (int i = 0; i < 4; i++) if (Math.Abs(p.X - pts[i].X) < z && Math.Abs(p.Y - pts[i].Y) < z) return i;
                return -1;
            }
            bool left = p.X < z, right = p.X >= Width - z, top = p.Y < z, bottom = p.Y >= Height - z;
            if (top && left) return 0;
            if (top && right) return 1;
            if (bottom && left) return 2;
            if (bottom && right) return 3;
            return -1;
        }

        static Cursor CornerCursor(int c) { return c == 0 || c == 3 ? Cursors.SizeNWSE : Cursors.SizeNESW; }

        // Scales the card keeping its shape, with the opposite corner staying put.
        void ResizeFromCorner()
        {
            var m = MousePosition;
            int dx = m.X - startMouse.X, dy = m.Y - startMouse.Y;
            if (corner == 0 || corner == 2) dx = -dx;
            if (corner == 0 || corner == 1) dy = -dy;
            float ratio = ((startBounds.Width + dx) / (float)startBounds.Width + (startBounds.Height + dy) / (float)startBounds.Height) / 2;
            CardScale = startScale * ratio;
            int x = corner == 0 || corner == 2 ? startBounds.Right - Width : startBounds.X;
            int y = corner == 0 || corner == 1 ? startBounds.Bottom - Height : startBounds.Y;
            Location = new Point(x, y);
            Update();
        }

        void DrawPlaceholder(Graphics g, Rectangle r)
        {
            using (var b = new SolidBrush(Color.FromArgb(10, 12, 16))) g.FillRectangle(b, r);
            var pts = new PointF[SkullArt.Foot.Length / 2];
            float u = r.Height * 0.3f;
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(r.X + r.Width / 2f + SkullArt.Foot[2 * i] * u, r.Y + r.Height / 2f + SkullArt.Foot[2 * i + 1] * u);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            {
                path.AddClosedCurve(pts, 0.35f);
                using (var fill = new SolidBrush(Color.FromArgb(60, Theme.Primary))) g.FillPath(fill, path);
                using (var line = new Pen(Theme.Dim, S(2))) g.DrawPath(line, path);
            }
            g.SmoothingMode = SmoothingMode.None;
        }

        void PressButton(int b)
        {
            pressed = b;
            pressFrames = 6;
            if (b == 1)
            {
                // Show the new state right away; the player confirms it a moment later.
                info.Position = info.Now;
                info.UpdatedUtc = DateTime.UtcNow;
                info.Playing = !info.Playing;
            }
            if (Command != null) Command(b == 0 ? MediaWatcher.Previous : b == 1 ? MediaWatcher.PlayPause : MediaWatcher.Next);
            Invalidate();
        }

        // ---- For the VR wrist, which keeps a card of its own that's never shown: the card drawn into a picture
        // (see-through round the Aero, XP and Orb shapes, on black for the others), the part of it that's the card,
        // which player button is at a point (0 previous, 1 play/pause, 2 next, -1 none), and the finger over or on one.
        public Bitmap Snapshot()
        {
            var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                if (!Layered) g.Clear(Color.Black);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                if (xp) PaintXp(g);
                else if (orb) PaintOrb(g);
                else if (aero) PaintAero(g);
                else OnPaint(new PaintEventArgs(g, ClientRectangle));
            }
            return bmp;
        }

        public Rectangle CardArea { get { return win ? WinRight : ClientRectangle; } }

        public int PlayerButtonAt(Point p) { return win ? WinButtonAt(p) : ButtonAt(p); }

        public int HoverButton { get { return hoverButton; } set { hoverButton = value; } }

        public void Press(int b) { PressButton(b); }

        // Corners resize, the bars and buttons do their job, and only the middle (art, title, artist) moves the card
        // (it has no title bar and never takes focus).
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int xw = XpWindowButtonAt(e.Location);
            if (xw >= 0) { PressXpWindowButton(xw); return; }
            int c = CornerAt(e.Location);
            if (c >= 0)
            {
                corner = c;
                maximized = false;   // a size picked by hand replaces the maximized one
                startScale = scale;
                startBounds = Bounds;
                startMouse = MousePosition;
                Capture = true;
                return;
            }
            if (win)
            {
                int wb = WinButtonAt(e.Location);
                if (wb >= 0) PressButton(wb);
                else if (WinMoveArea(e.Location)) { dragging = true; dragFrom = e.Location; Capture = true; }
                return;
            }
            if (VolumeHit.Contains(e.Location))
            {
                draggingVolume = true;
                Capture = true;
                SlideVolume(e.X);
                return;
            }
            int b = ButtonAt(e.Location);
            if (b >= 0)
            {
                PressButton(b);
                return;
            }
            if (ProgressHit.Contains(e.Location))
            {
                seeking = true;
                seekPreview = PositionAt(e.X);
                Capture = true;
                Invalidate();
                return;
            }
            if ((xp || orb) && vizBox.Contains(e.Location)) { ShowVizMenu(); return; }
            if (!InMoveArea(e.Location)) return;
            dragging = true;
            dragFrom = e.Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (corner >= 0) { ResizeFromCorner(); return; }
            if (!hovering) { hovering = true; Invalidate(); }
            if (draggingVolume) { KeepVolume(); if (win) SlideVolumeVertical(e.Y); else SlideVolume(e.X); return; }
            if (seeking) { seekPreview = PositionAt(e.X); Invalidate(); return; }
            if (dragging) { Location = new Point(Location.X + e.X - dragFrom.X, Location.Y + e.Y - dragFrom.Y); return; }
            if (win)
            {
                int wb = WinButtonAt(e.Location), wc = CornerAt(e.Location);
                Cursor = wc >= 0 ? CornerCursor(wc) : wb >= 0 || WinTrackHit.Contains(e.Location) ? Cursors.Hand : WinMoveArea(e.Location) ? Cursors.SizeAll : Cursors.Default;
                if (wb != hoverButton) { hoverButton = wb; Invalidate(); }
                return;
            }
            bool onVolume = VolumeHit.Contains(e.Location), onProgress = ProgressHit.Contains(e.Location);
            bool onViz = (xp || orb) && vizBox.Contains(e.Location);
            if (onViz != vizHover) vizHover = onViz;
            if (onVolume != volumeHover) { volumeHover = onVolume; Invalidate(); }
            if (onProgress != progressHover) { progressHover = onProgress; Invalidate(); }
            int xw = XpWindowButtonAt(e.Location);
            if (xw != xpWinHover) { xpWinHover = xw; Invalidate(); }
            int b = ButtonAt(e.Location), c = CornerAt(e.Location);
            Cursor = xw >= 0 ? Cursors.Hand : c >= 0 ? CornerCursor(c) : b >= 0 || onVolume || onProgress || onViz ? Cursors.Hand : InMoveArea(e.Location) ? Cursors.SizeAll : Cursors.Default;
            if (b != hoverButton) { hoverButton = b; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverButton = xpWinHover = -1;
            volumeHover = progressHover = false;
            hovering = false;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (corner >= 0)
            {
                corner = -1;
                Capture = false;
                if (Resized != null) Resized();
                return;
            }
            if (draggingVolume)
            {
                draggingVolume = false;
                Capture = false;
                Invalidate();
                return;
            }
            if (seeking)
            {
                seeking = false;
                Capture = false;
                seekTarget = PositionAt(e.X);
                seekAt = DateTime.UtcNow;
                seekHoldUntil = seekAt.AddSeconds(3);
                if (SeekRequested != null) SeekRequested(seekTarget);
                Invalidate();
                return;
            }
            if (!dragging) return;
            dragging = false;
            Capture = false;
            if (Moved != null) Moved();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (panel != null) panel.Dispose();
                if (vizMenu != null) vizMenu.Dispose();
                anim.Dispose();
                Theme.Changed -= OnThemeChanged;
                foreach (var f in new[] { fHead, fTitle, fBody, wTitle, wSmall, wApp, aTitle, aBody, aSmall, xTitle, xArtist, xLabel, xSmall }) if (f != null) f.Dispose();
                if (reflection != null) reflection.Dispose();
                vizEngine.Release();
            }
            base.Dispose(disposing);
        }
    }

    // The Windows-style card's volume panel, as its own little window beside the player panel so it can fade out
    // on its own. The card does the drawing and the slider logic; this just passes the mouse along.
    class VolumePanel : Form
    {
        readonly NowPlayingWidget card;

        public VolumePanel(NowPlayingWidget card)
        {
            this.card = card;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(31, 31, 31);
            DoubleBuffered = true;
            Text = "Now Playing Volume";
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000 | 0x8;   // tool window, no activation, topmost
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e) { card.PaintVolumePanel(e.Graphics); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Capture = true;
            card.PanelMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            card.panelHover = true;
            Cursor = card.OnTrack(e.Location) ? Cursors.Hand : Cursors.Default;
            card.PanelMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Capture = false;
            card.PanelMouseUp();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            card.panelHover = false;
            card.KeepVolume();   // the 2 seconds count from when the mouse leaves
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            card.PanelWheel(e);
        }
    }

    // ------------------------------------------------------------------ VR wrist (SteamVR)

    // Talks to SteamVR through Valve's OpenVR library, loaded from SteamVR's own folder (so nothing extra ships with
    // the app). Only the few calls the wrist needs are here: entries of the IVRSystem_026 and IVROverlay_028 function
    // tables, at the positions Valve's openvr_api.cs gives them (github.com/ValveSoftware/openvr, BSD-3 license).
    static class OpenVr
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);
        [DllImport("openvr_api", EntryPoint = "VR_InitInternal2", CallingConvention = CallingConvention.Cdecl)]
        static extern uint InitInternal2(ref int error, int type, [MarshalAs(UnmanagedType.LPStr)] string startupInfo);
        [DllImport("openvr_api", EntryPoint = "VR_ShutdownInternal", CallingConvention = CallingConvention.Cdecl)]
        static extern void ShutdownInternal();
        [DllImport("openvr_api", EntryPoint = "VR_GetGenericInterface", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr GetGenericInterface([MarshalAs(UnmanagedType.LPStr)] string version, ref int error);

        // HmdMatrix34_t: a 3 x 4 matrix, row by row (rotation in the first three columns, position in the last).
        [StructLayout(LayoutKind.Sequential)]
        public struct Matrix34 { public float M0, M1, M2, M3, M4, M5, M6, M7, M8, M9, M10, M11; }

        // TrackedDevicePose_t.
        [StructLayout(LayoutKind.Sequential)]
        public struct Pose
        {
            public Matrix34 DeviceToAbsolute;
            public float VX, VY, VZ, AX, AY, AZ;
            public int TrackingResult;
            [MarshalAs(UnmanagedType.I1)] public bool Valid;
            [MarshalAs(UnmanagedType.I1)] public bool Connected;
        }

        // VREvent_t (64 bytes on Windows; only its type is read).
        [StructLayout(LayoutKind.Sequential)]
        public struct Event { public uint Type, Device; public float Age; public ulong D0, D1, D2, D3, D4, D5; }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GetPosesFn(int origin, float predict, [In, Out] Pose[] poses, uint count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate uint RoleIndexFn(int role);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.I1)] public delegate bool PollEventFn(ref Event e, uint size);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void HapticFn(uint device, uint axis, ushort microseconds);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void AckQuitFn();
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int CreateOverlayFn(IntPtr key, IntPtr name, ref ulong handle);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int OverlayFn(ulong handle);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int OverlayFloatFn(ulong handle, float value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int OverlayUIntFn(ulong handle, uint value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int RelativeFn(ulong handle, uint device, ref Matrix34 transform);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int RawFn(ulong handle, IntPtr buffer, uint width, uint height, uint bytesPerPixel);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int FlagFn(ulong handle, int flag, [MarshalAs(UnmanagedType.I1)] bool on);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int AbsoluteFn(ulong handle, int origin, ref Matrix34 transform);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.I1)] public delegate bool PollOverlayEventFn(ulong handle, ref Event e, uint size);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int MouseScaleFn(ulong handle, ref Vector2 scale);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int FromFileFn(ulong handle, IntPtr path);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int TextureFn(ulong handle, ref Texture texture);

        // Texture_t: a Direct3D 11 texture (type 0) in gamma color space (1), as GDI draws.
        [StructLayout(LayoutKind.Sequential)]
        public struct Texture { public IntPtr Handle; public int Type, ColorSpace; }

        [StructLayout(LayoutKind.Sequential)]
        public struct Vector2 { public float X, Y; }

        public static GetPosesFn GetPoses;
        public static RoleIndexFn RoleIndex;
        public static PollEventFn PollEvent;
        public static HapticFn Haptic;
        public static AckQuitFn AckQuit;
        public static CreateOverlayFn CreateOverlay;
        public static OverlayFn DestroyOverlay, ShowOverlay, HideOverlay;
        public static OverlayFloatFn SetAlpha, SetWidth;
        public static OverlayUIntFn SetSortOrder, SetInputMethod;
        public static RelativeFn SetRelative;
        public static RawFn SetRaw;
        public static FlagFn SetFlag;
        public static AbsoluteFn SetAbsolute;
        public static PollOverlayEventFn PollOverlayEvent;
        public static MouseScaleFn SetMouseScale;
        public static FromFileFn SetFromFile;
        public static TextureFn SetTexture;

        // Raw pixels sent straight to an overlay have to stay under a size SteamVR sets (it turns down bigger ones and
        // keeps the last picture); bigger pictures go as an image file instead.
        public const int RawLimit = 380 * 1024;

        // Shows a picture file on an overlay (the path as UTF-8).
        public static int FromFile(ulong handle, string path)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(path + "\0");
            IntPtr p = Marshal.AllocHGlobal(bytes.Length);
            try { Marshal.Copy(bytes, 0, p, bytes.Length); return SetFromFile(handle, p); }
            finally { Marshal.FreeHGlobal(p); }
        }

        public const uint NoDevice = 0xFFFFFFFF, Hmd = 0, MaxDevices = 64;
        public const int LeftHand = 1, RightHand = 2, Standing = 1, QuitEvent = 700;
        public const uint MouseMove = 300, MouseDown = 301, MouseUp = 302, FocusLeave = 304, ScrollSmooth = 309, LeftButton = 1;
        // The laser from the other hand points at an overlay with this while it's visible (no dashboard needed), and
        // it gets smooth scroll events from the thumbstick.
        public const int InteractiveIfVisible = 65536, SmoothScroll = 131072, MouseInput = 1;

        // The x and y of a mouse or scroll event (VREvent_Mouse_t / VREvent_Scroll_t: two floats first).
        public static float First(Event e) { return BitConverter.ToSingle(BitConverter.GetBytes(e.D0), 0); }
        public static float Second(Event e) { return BitConverter.ToSingle(BitConverter.GetBytes(e.D0), 4); }
        public static uint Button(Event e) { return (uint)(e.D1 & 0xFFFFFFFF); }
        public static bool Running;
        static bool loaded;

        // openvr_api.dll in SteamVR's folder, found through the paths file SteamVR keeps for apps.
        public static string Library()
        {
            try
            {
                string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openvr", "openvrpaths.vrpath");
                var runtime = Web.At(Web.Json(File.ReadAllText(file)), "runtime", 0) as string;
                string dll = Path.Combine(runtime, "bin", IntPtr.Size == 8 ? "win64" : "win32", "openvr_api.dll");
                return File.Exists(dll) ? dll : null;
            }
            catch { return null; }
        }

        public static bool SteamVrRunning { get { var p = Process.GetProcessesByName("vrserver"); foreach (var x in p) x.Dispose(); return p.Length > 0; } }

        static T Fn<T>(IntPtr table, int index) where T : class
        {
            return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, index * IntPtr.Size), typeof(T));
        }

        // Connects as an overlay app. Only called while SteamVR is running: SteamVR is never started from here.
        public static bool Start()
        {
            if (Running) return true;
            try
            {
                if (!loaded)
                {
                    string dll = Library();
                    if (dll == null || LoadLibrary(dll) == IntPtr.Zero) return false;
                    loaded = true;
                }
                int error = 0;
                InitInternal2(ref error, 2, null);   // VRApplication_Overlay
                if (error != 0) return false;
                IntPtr system = GetGenericInterface("FnTable:IVRSystem_026", ref error);
                IntPtr overlay = error == 0 ? GetGenericInterface("FnTable:IVROverlay_028", ref error) : IntPtr.Zero;
                if (error != 0 || system == IntPtr.Zero || overlay == IntPtr.Zero) { ShutdownInternal(); return false; }
                GetPoses = Fn<GetPosesFn>(system, 12);
                RoleIndex = Fn<RoleIndexFn>(system, 18);
                PollEvent = Fn<PollEventFn>(system, 30);
                Haptic = Fn<HapticFn>(system, 39);
                AckQuit = Fn<AckQuitFn>(system, 47);
                CreateOverlay = Fn<CreateOverlayFn>(overlay, 1);
                DestroyOverlay = Fn<OverlayFn>(overlay, 3);
                SetAlpha = Fn<OverlayFloatFn>(overlay, 16);
                SetSortOrder = Fn<OverlayUIntFn>(overlay, 20);
                SetWidth = Fn<OverlayFloatFn>(overlay, 22);
                SetRelative = Fn<RelativeFn>(overlay, 35);
                ShowOverlay = Fn<OverlayFn>(overlay, 43);
                HideOverlay = Fn<OverlayFn>(overlay, 44);
                SetRaw = Fn<RawFn>(overlay, 62);
                SetFlag = Fn<FlagFn>(overlay, 11);
                SetAbsolute = Fn<AbsoluteFn>(overlay, 33);
                PollOverlayEvent = Fn<PollOverlayEventFn>(overlay, 48);
                SetInputMethod = Fn<OverlayUIntFn>(overlay, 50);
                SetMouseScale = Fn<MouseScaleFn>(overlay, 52);
                SetFromFile = Fn<FromFileFn>(overlay, 63);
                SetTexture = Fn<TextureFn>(overlay, 60);
                Running = true;
                return true;
            }
            catch { return false; }
        }

        public static void Stop()
        {
            if (!Running) return;
            Running = false;
            try { ShutdownInternal(); } catch { }
        }
    }

    // Pictures for SteamVR as Direct3D 11 textures on the graphics card. Each overlay keeps one, has its pixels
    // replaced in place and hands it to SteamVR again, which is smooth at any rate. (Sending raw pixels or a picture
    // file instead makes SteamVR build a new texture every time, and the overlay flickers while it does.) The device
    // and textures are reached through their COM function tables, as the media controls are.
    static class Gpu
    {
        [DllImport("d3d11.dll")]
        static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels, uint levels, uint sdkVersion,
                                            out IntPtr device, out int featureLevel, out IntPtr context);

        // D3D11_TEXTURE2D_DESC.
        [StructLayout(LayoutKind.Sequential)]
        struct TextureDesc { public uint Width, Height, MipLevels, ArraySize; public int Format; public uint SampleCount, SampleQuality; public int Usage; public uint BindFlags, CpuAccessFlags, MiscFlags; }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateTexture2DFn(IntPtr self, ref TextureDesc desc, IntPtr initialData, out IntPtr texture);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void UpdateSubresourceFn(IntPtr self, IntPtr resource, uint subresource, IntPtr box, IntPtr data, uint rowPitch, uint depthPitch);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void FlushFn(IntPtr self);

        const int Hardware = 1, BgraSupport = 0x20, SdkVersion = 7, Bgra = 87, ShaderResource = 8;
        const int CreateTexture2DSlot = 5, UpdateSubresourceSlot = 48, FlushSlot = 111;   // ID3D11Device / ID3D11DeviceContext
        static IntPtr device, context;
        static bool tried;

        static T Slot<T>(IntPtr obj, int index) where T : class
        {
            return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), index * IntPtr.Size), typeof(T));
        }

        // A Direct3D 11 device on the main graphics card (made the first time it's wanted).
        public static bool Ready
        {
            get
            {
                if (!tried)
                {
                    tried = true;
                    try
                    {
                        int level;
                        if (D3D11CreateDevice(IntPtr.Zero, Hardware, IntPtr.Zero, BgraSupport, IntPtr.Zero, 0, SdkVersion, out device, out level, out context) < 0)
                            device = context = IntPtr.Zero;
                    }
                    catch { device = context = IntPtr.Zero; }
                }
                return device != IntPtr.Zero;
            }
        }

        // A texture of that size in the BGRA layout GDI draws in (zero if it can't be made).
        public static IntPtr Texture(int width, int height)
        {
            if (!Ready) return IntPtr.Zero;
            var desc = new TextureDesc { Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1, Format = Bgra, SampleCount = 1, BindFlags = ShaderResource };
            IntPtr texture;
            return Slot<CreateTexture2DFn>(device, CreateTexture2DSlot)(device, ref desc, IntPtr.Zero, out texture) < 0 ? IntPtr.Zero : texture;
        }

        // New pixels for a texture: BGRA rows, top first, rowPitch bytes apart.
        public static void Update(IntPtr texture, IntPtr pixels, int rowPitch)
        {
            Slot<UpdateSubresourceFn>(context, UpdateSubresourceSlot)(context, texture, 0, IntPtr.Zero, pixels, (uint)rowPitch, 0);
            Slot<FlushFn>(context, FlushSlot)(context);
        }

        public static void Release(ref IntPtr texture)
        {
            if (texture != IntPtr.Zero) Marshal.Release(texture);
            texture = IntPtr.Zero;
        }
    }

    // A Now Playing card as a hologram on one wrist in SteamVR. It's on the inside or the outside of the wrist, and
    // fades in when that side is turned toward your face (and out when it's turned away). While it shows, SteamVR's own
    // laser comes from the other hand when it points at it: the button under the beam lights up and the trigger presses
    // it. The card is any of the widget's looks, drawn by a copy of the widget that's never shown, then turned into a
    // see-through glowing projection in the accent color: bright parts glow, dark parts fade to nothing, with
    // scanlines, a flicker and a band of light sweeping down it.
    //
    // Turning the wrist toward your face twice quickly opens Dino Visualizer's own window floating in front of you (and
    // twice again closes it): a live picture of it, which the laser clicks, drags and scrolls like a mouse.
    sealed class VrWrist : IDisposable
    {
        public static readonly string[] Names = { "Off", "Left Wrist (Inside)", "Left Wrist (Outside)", "Right Wrist (Inside)", "Right Wrist (Outside)" };
        public static readonly string[] Looks = { "Dino Style", "Windows Style", "Aero Style", "XP Style", "Orb Style" };
        public event Action<int> Command;   // a MediaWatcher command for the player
        public event Action Changed;        // connected to SteamVR or not
        public Func<float> LevelSource;
        public Form App;                    // the main window, shown floating in VR on the double turn
        public float OffsetX, OffsetY, OffsetZ;   // meters, a nudge from its usual spot (settings.ini: vrwristx/y/z)
        public float Roll;                        // degrees the card's turned on the wrist (its Rotate buttons)
        public event Action Adjusted;             // its size or turn was changed from its buttons (save them)
        public static readonly string[] Sizes = { "Small", "Medium", "Large" };
        static readonly float[] SizeWidths = { 0.13f, 0.17f, 0.22f };
        float width = 0.17f;

        // How wide the card is, in meters (it changes straight away).
        public float Width
        {
            get { return width; }
            set { width = value; if (overlay != 0) OpenVr.SetWidth(overlay, width); }
        }

        // Small, Medium or Large: the one nearest the width.
        public int SizeIndex
        {
            get { int best = 0; for (int i = 1; i < SizeWidths.Length; i++) if (Math.Abs(SizeWidths[i] - width) < Math.Abs(SizeWidths[best] - width)) best = i; return best; }
            set { Width = SizeWidths[Math.Max(0, Math.Min(SizeWidths.Length - 1, value))]; }
        }

        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 22 };
        readonly OpenVr.Pose[] poses = new OpenVr.Pose[OpenVr.MaxDevices];
        readonly Stopwatch clock = Stopwatch.StartNew();
        NowPlayingInfo info = new NowPlayingInfo();
        NowPlayingWidget card;
        int place, look, visualizer;
        ulong overlay, window;
        uint attached = OpenVr.NoDevice;
        float alpha;              // 0 hidden .. 1 shown
        bool facing, shown;
        DateTime nextTry, nextFrame, lastRaise;
        IntPtr buffer;
        int bufferSize, texWidth = 1, texHeight = 1;

        public VrWrist() { timer.Tick += (s, e) => Tick(); }

        public bool Connected { get { return overlay != 0; } }

        public int Place
        {
            get { return place; }
            set
            {
                place = value;
                attached = OpenVr.NoDevice;   // placed again on the next frame
                if (place == 0) { Disconnect(); timer.Stop(); }
                else { nextTry = DateTime.MinValue; timer.Start(); }
            }
        }

        public int Look
        {
            get { return look; }
            set { look = value; if (card != null) card.Style = value; }
        }

        public int Visualizer { set { visualizer = value; if (card != null) card.Visualizer = value; } }

        public void SetInfo(NowPlayingInfo next)
        {
            info = next;
            if (card != null) card.SetInfo(next);
        }

        // About 45 times a second while it's on. Until SteamVR turns up it only looks for it every 3 seconds.
        void Tick()
        {
            if (place == 0) return;
            try
            {
                if (!Connected)
                {
                    if (DateTime.UtcNow < nextTry) return;
                    nextTry = DateTime.UtcNow.AddSeconds(3);
                    if (OpenVr.SteamVrRunning && OpenVr.Start()) Connect();
                    return;
                }
                // SteamVR closing: let it go, and look for it again later.
                var e = new OpenVr.Event();
                uint size = (uint)Marshal.SizeOf(typeof(OpenVr.Event));
                while (OpenVr.PollEvent(ref e, size))
                    if (e.Type == OpenVr.QuitEvent)
                    {
                        try { OpenVr.AckQuit(); } catch { }
                        Disconnect();
                        nextTry = DateTime.UtcNow.AddSeconds(10);
                        return;
                    }
                // (Listing the processes takes a moment, so SteamVR having gone without a word is looked for rarely.)
                if (DateTime.UtcNow >= nextTry)
                {
                    nextTry = DateTime.UtcNow.AddSeconds(10);
                    if (!OpenVr.SteamVrRunning) { Disconnect(); return; }
                }
                Step();
            }
            catch { Disconnect(); nextTry = DateTime.UtcNow.AddSeconds(10); }
        }

        void Connect()
        {
            IntPtr key = Marshal.StringToHGlobalAnsi("saosinllc.dinovisualizer.wrist"), name = Marshal.StringToHGlobalAnsi("Dino Visualizer Wrist");
            int error;
            try { error = OpenVr.CreateOverlay(key, name, ref overlay); }
            finally { Marshal.FreeHGlobal(key); Marshal.FreeHGlobal(name); }
            if (error != 0) { overlay = 0; OpenVr.Stop(); return; }
            OpenVr.SetWidth(overlay, Width);
            OpenVr.SetSortOrder(overlay, 10);
            OpenVr.SetAlpha(overlay, 0);
            Pointable(overlay);
            alpha = 0;
            shown = facing = false;
            attached = OpenVr.NoDevice;
            card = new NowPlayingWidget(1.2f, look) { LevelSource = LevelSource, Visualizer = visualizer };
            card.Command += slot => { if (Command != null) Command(slot); };
            card.SetInfo(info);
            // The app's window, for the double turn (hidden until then).
            key = Marshal.StringToHGlobalAnsi("saosinllc.dinovisualizer.window");
            name = Marshal.StringToHGlobalAnsi("Dino Visualizer");
            try { if (OpenVr.CreateOverlay(key, name, ref window) != 0) window = 0; }
            finally { Marshal.FreeHGlobal(key); Marshal.FreeHGlobal(name); }
            if (window != 0)
            {
                OpenVr.SetWidth(window, WindowWidth);
                OpenVr.SetSortOrder(window, 5);
                Pointable(window);
            }
            windowOpen = false;
            if (Changed != null) Changed();
        }

        // SteamVR's laser comes from the other hand when it points at the overlay while it's visible, and its trigger,
        // moves and thumbstick scrolling come back as mouse events.
        static void Pointable(ulong handle)
        {
            OpenVr.SetInputMethod(handle, OpenVr.MouseInput);
            OpenVr.SetFlag(handle, OpenVr.InteractiveIfVisible, true);
            OpenVr.SetFlag(handle, OpenVr.SmoothScroll, true);
        }

        void Disconnect()
        {
            bool was = Connected;
            ReleaseWindowMouse();
            if (overlay != 0) try { OpenVr.DestroyOverlay(overlay); } catch { }
            if (window != 0) try { OpenVr.DestroyOverlay(window); } catch { }
            overlay = window = 0;
            windowOpen = false;
            OpenVr.Stop();
            if (card != null) card.Dispose();
            card = null;
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;
            bufferSize = 0;
            Gpu.Release(ref cardTexture);
            Gpu.Release(ref windowTexture);
            ReleaseWindowPictures();
            if (was && Changed != null) Changed();
        }

        public void Dispose()
        {
            timer.Stop();
            Disconnect();
            timer.Dispose();
        }

        // ---- Where it sits. In the controller's space +X is right, +Y up and -Z where it points. The card is 19 cm
        // behind the controller's middle and 5 cm out from it, up the forearm past the controller's base and strap
        // (SteamVR draws the controllers in front of overlays, and closer in an Index controller covered it). It faces
        // out from the chosen side of the wrist, with its text running along the arm.
        //
        // The card's top points the way the controller's bottom does (-Y): that's what reads right way up when the wrist
        // is turned toward your face, on either side of it (tried in VR on both).
        // roll turns the card round in its own plane on the wrist (degrees, from its Rotate buttons).
        public static OpenVr.Matrix34 Mount(int place, float dx, float dy, float dz, float roll = 0)
        {
            // Facing +X: the inside of the left wrist or the outside of the right one; facing -X: the other two.
            bool plusX = place == 1 || place == 4;
            float s = plusX ? 1 : -1;
            float c = (float)Math.Cos(roll * Math.PI / 180), n = (float)Math.Sin(roll * Math.PI / 180);
            // Columns: the card's right (along the arm, +sZ), its up (-Y), both turned by roll, and its face (±X).
            return new OpenVr.Matrix34
            {
                M0 = 0, M1 = 0, M2 = s, M3 = s * 0.05f + dx,
                M4 = -n, M5 = -c, M6 = 0, M7 = -0.01f + dy,
                M8 = c * s, M9 = -n * s, M10 = 0, M11 = 0.19f + dz
            };
        }

        public static OpenVr.Matrix34 Multiply(OpenVr.Matrix34 a, OpenVr.Matrix34 b)
        {
            return new OpenVr.Matrix34
            {
                M0 = a.M0 * b.M0 + a.M1 * b.M4 + a.M2 * b.M8, M1 = a.M0 * b.M1 + a.M1 * b.M5 + a.M2 * b.M9,
                M2 = a.M0 * b.M2 + a.M1 * b.M6 + a.M2 * b.M10, M3 = a.M0 * b.M3 + a.M1 * b.M7 + a.M2 * b.M11 + a.M3,
                M4 = a.M4 * b.M0 + a.M5 * b.M4 + a.M6 * b.M8, M5 = a.M4 * b.M1 + a.M5 * b.M5 + a.M6 * b.M9,
                M6 = a.M4 * b.M2 + a.M5 * b.M6 + a.M6 * b.M10, M7 = a.M4 * b.M3 + a.M5 * b.M7 + a.M6 * b.M11 + a.M7,
                M8 = a.M8 * b.M0 + a.M9 * b.M4 + a.M10 * b.M8, M9 = a.M8 * b.M1 + a.M9 * b.M5 + a.M10 * b.M9,
                M10 = a.M8 * b.M2 + a.M9 * b.M6 + a.M10 * b.M10, M11 = a.M8 * b.M3 + a.M9 * b.M7 + a.M10 * b.M11 + a.M11
            };
        }

        // How squarely the card faces the head (1 straight at it, 0 edge on, below 0 away) and how far away it is.
        public static float FacingHead(OpenVr.Matrix34 card, OpenVr.Matrix34 head, out float distance)
        {
            float dx = head.M3 - card.M3, dy = head.M7 - card.M7, dz = head.M11 - card.M11;
            distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return distance < 1e-4f ? 0 : (card.M2 * dx + card.M6 * dy + card.M10 * dz) / distance;
        }

        void Step()
        {
            OpenVr.GetPoses(OpenVr.Standing, 0.02f, poses, OpenVr.MaxDevices);
            Pointing();
            if (windowOpen && DateTime.UtcNow >= nextWindowFrame)
            {
                var took = Stopwatch.StartNew();
                UploadWindow();
                nextWindowFrame = DateTime.UtcNow.AddMilliseconds(Math.Max(200, took.Elapsed.TotalMilliseconds * 3));
            }
            uint hand = OpenVr.RoleIndex(place <= 2 ? OpenVr.LeftHand : OpenVr.RightHand);
            if (hand >= OpenVr.MaxDevices || !poses[hand].Valid || !poses[OpenVr.Hmd].Valid) { Fade(false); return; }
            var mount = Mount(place, OffsetX, OffsetY, OffsetZ, Roll);
            if (hand != attached)
            {
                OpenVr.SetRelative(overlay, hand, ref mount);
                attached = hand;
            }
            var world = Multiply(poses[hand].DeviceToAbsolute, mount);
            float distance, toward = FacingHead(world, poses[OpenVr.Hmd].DeviceToAbsolute, out distance);
            // Turned toward the face: within about 40 degrees to show, and it stays until past about 55. Turned toward
            // it twice within 1.2 seconds opens or closes the app's window.
            bool was = facing;
            facing = facing ? toward > 0.57f && distance < 1.0f : toward > 0.77f && distance < 0.85f;
            if (facing && !was)
            {
                if ((DateTime.UtcNow - lastRaise).TotalSeconds < 1.2) { ToggleWindow(); lastRaise = DateTime.MinValue; }
                else lastRaise = DateTime.UtcNow;
            }
            Fade(facing);
            if (alpha > 0 && DateTime.UtcNow >= nextFrame)
            {
                nextFrame = DateTime.UtcNow.AddMilliseconds(80);   // about 12 a second: plenty for the slow shimmer
                Upload();
            }
        }

        // What SteamVR's laser did over the card and the window since the last frame. Its mouse y counts up from the
        // bottom.
        void Pointing()
        {
            var e = new OpenVr.Event();
            uint size = (uint)Marshal.SizeOf(typeof(OpenVr.Event));
            while (OpenVr.PollOverlayEvent(overlay, ref e, size))
            {
                if (e.Type == OpenVr.FocusLeave) { card.HoverButton = stripHover = -1; continue; }
                if (e.Type != OpenVr.MouseMove && e.Type != OpenVr.MouseDown) continue;
                float x = OpenVr.First(e), y = texHeight - OpenVr.Second(e);
                bool click = e.Type == OpenVr.MouseDown && OpenVr.Button(e) == OpenVr.LeftButton && alpha > 0.5f;
                if (y >= cardTexHeight)
                {
                    // The strip under the card.
                    var buttons = StripButtons(texWidth, cardTexHeight, texHeight - cardTexHeight);
                    int at = Array.FindIndex(buttons, r => r.Contains((int)x, (int)y));
                    card.HoverButton = -1;
                    stripHover = at;
                    if (click && at >= 0) StripButton(at);
                    continue;
                }
                stripHover = -1;
                var area = card.CardArea;
                var p = new Point(area.X + (int)(x / texScale), area.Y + (int)(y / texScale));
                int button = card.PlayerButtonAt(p);
                if (e.Type == OpenVr.MouseMove) card.HoverButton = button;
                else if (click && button >= 0) card.Press(button);
            }
            if (window == 0) return;
            while (OpenVr.PollOverlayEvent(window, ref e, size))
                if (windowOpen) WindowPointing(e);
        }

        void Fade(bool show)
        {
            float target = show ? 1 : 0;
            if (alpha == target) return;
            alpha = show ? Math.Min(1, alpha + 0.12f) : Math.Max(0, alpha - 0.08f);
            OpenVr.SetAlpha(overlay, alpha);
            if (alpha > 0 && !shown) { Upload(); OpenVr.ShowOverlay(overlay); shown = true; }
            else if (alpha <= 0 && shown) { OpenVr.HideOverlay(overlay); shown = false; card.HoverButton = stripHover = -1; }
        }

        float texScale = 1;   // card texture pixels per card pixel

        IntPtr cardTexture, windowTexture;   // the overlays' textures on the graphics card
        int cardTexW, cardTexH, windowTexW, windowTexH;

        // The card at full size as a texture; without one (no Direct3D 11) small enough for raw pixels, or as a file.
        void Upload()
        {
            int w = texWidth, h = texHeight;
            var bgra = Picture(card, clock.Elapsed.TotalSeconds, Gpu.Ready ? int.MaxValue : OpenVr.RawLimit, stripHover, windowOpen,
                               out texWidth, out texHeight, out cardTexHeight, out texScale);
            var pin = GCHandle.Alloc(bgra, GCHandleType.Pinned);
            bool sent;
            try { sent = ToGpu(overlay, ref cardTexture, ref cardTexW, ref cardTexH, pin.AddrOfPinnedObject(), texWidth * 4, texWidth, texHeight); }
            finally { pin.Free(); }
            if (!sent)
            {
                var rgba = (byte[])bgra.Clone();
                for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = bgra[i + 2]; rgba[i + 2] = bgra[i]; }
                if (Send(overlay, rgba, texWidth, texHeight, ref buffer, ref bufferSize) != 0)
                    using (var bmp = ToBitmap(bgra, texWidth, texHeight)) ShowFile(overlay, bmp, "vr-wrist");
            }
            if (w != texWidth || h != texHeight)
            {
                var scale = new OpenVr.Vector2 { X = texWidth, Y = texHeight };   // the laser's mouse in pixels
                OpenVr.SetMouseScale(overlay, ref scale);
            }
        }

        // New pixels (BGRA rows, top first) into an overlay's texture, made or remade at that size, and the texture
        // handed to SteamVR. False if there's no Direct3D 11 or SteamVR turned it down.
        static bool ToGpu(ulong handle, ref IntPtr texture, ref int texW, ref int texH, IntPtr pixels, int rowPitch, int width, int height)
        {
            if (!Gpu.Ready) return false;
            if (texture == IntPtr.Zero || texW != width || texH != height)
            {
                Gpu.Release(ref texture);
                texture = Gpu.Texture(width, height);
                texW = width;
                texH = height;
            }
            if (texture == IntPtr.Zero) return false;
            Gpu.Update(texture, pixels, rowPitch);
            var t = new OpenVr.Texture { Handle = texture, Type = 0, ColorSpace = 1 };
            return OpenVr.SetTexture(handle, ref t) == 0;
        }

        // Raw pixels to an overlay; returns SteamVR's error (0 when it took them).
        static int Send(ulong handle, byte[] rgba, int width, int height, ref IntPtr buffer, ref int bufferSize)
        {
            if (bufferSize < rgba.Length)
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                buffer = Marshal.AllocHGlobal(rgba.Length);
                bufferSize = rgba.Length;
            }
            Marshal.Copy(rgba, 0, buffer, rgba.Length);
            return OpenVr.SetRaw(handle, buffer, (uint)width, (uint)height, 4);
        }

        // A picture to an overlay as a PNG file in the temp folder. The name takes turns between two, so SteamVR sees a
        // new file every time and loads it again.
        bool flip;

        void ShowFile(ulong handle, Bitmap bmp, string name)
        {
            string folder = Path.Combine(Path.GetTempPath(), "Dino Visualizer");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, name + (flip ? "-a.png" : "-b.png"));
            flip = !flip;
            bmp.Save(file, ImageFormat.Png);
            OpenVr.FromFile(handle, file);
        }

        static Bitmap ToBitmap(byte[] bgra, int width, int height)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < height; y++) Marshal.Copy(bgra, y * width * 4, data.Scan0 + y * data.Stride, width * 4);
            bmp.UnlockBits(data);
            return bmp;
        }

        // ---- The app's window floating in VR: about 80 cm in front of where you were looking when it opened, level
        // and facing you. It's redrawn several times a second while it's open. On top is a bar of buttons, all simply
        // clicked with the laser: Bring Here (in front of where you're looking now), smaller and bigger, turn left and
        // right, and close. In the window itself the laser's moves, trigger and thumbstick scrolling go to the control
        // under it as mouse messages, so everything works (sliders drag, lists open and scroll).

        public float WindowWidth = 1.0f;     // meters (kept in settings.ini as vrwindowsize when it's resized)
        public event Action<float> WindowResized;
        const int Bar = 64, MaxWindowWidth = 1600;
        static readonly string[] BarLabels = { "Bring Here", "Smaller", "Bigger", "Turn Left", "Turn Right", "✕" };
        const int BringHere = 0, Smaller = 1, Bigger = 2, TurnLeft = 3, TurnRight = 4, CloseWindow = 5;
        bool windowOpen;
        DateTime nextWindowFrame;
        int windowTexWidth = 1, windowTexHeight = 1;
        float windowScale = 1;   // texture pixels per window pixel
        Control mouseOn;         // the control the laser's over (or holding, while the trigger's down)
        bool mouseHeld;
        OpenVr.Matrix34 windowAt;   // where the window is in the room

        public bool WindowOpen { get { return windowOpen; } }

        void ToggleWindow()
        {
            if (window == 0 || App == null) return;
            windowOpen = !windowOpen;
            if (!windowOpen)
            {
                ReleaseWindowMouse();
                OpenVr.HideOverlay(window);
                ReleaseWindowPictures();
                return;
            }
            nextFullDraw = DateTime.MinValue;
            PlaceInFront();
            OpenVr.SetWidth(window, WindowWidth);
            UploadWindow();
            OpenVr.ShowOverlay(window);
        }

        // About 80 cm in front of where you're looking, level and facing you.
        void PlaceInFront()
        {
            var head = poses[OpenVr.Hmd].DeviceToAbsolute;
            float fx = -head.M2, fz = -head.M10, len = (float)Math.Sqrt(fx * fx + fz * fz);   // where you're looking, level
            if (len < 1e-3f) { fx = 0; fz = -1; len = 1; }
            fx /= len;
            fz /= len;
            // Columns: right = up x face, up, and the face pointing back at you.
            float zx = -fx, zz = -fz;
            windowAt = new OpenVr.Matrix34
            {
                M0 = zz, M1 = 0, M2 = zx, M3 = head.M3 + fx * 0.8f,
                M4 = 0, M5 = 1, M6 = 0, M7 = head.M7 - 0.05f,
                M8 = -zx, M9 = 0, M10 = zz, M11 = head.M11 + fz * 0.8f
            };
            OpenVr.SetAbsolute(window, OpenVr.Standing, ref windowAt);
        }

        // The bar's buttons, from the right-hand end: close, then turn, size, and Bring Here.
        static Rectangle[] BarButtons(int width)
        {
            var r = new Rectangle[BarLabels.Length];
            int x = width - 8, size = Bar - 16;
            for (int i = BarLabels.Length - 1; i >= 0; i--)
            {
                int w = i == CloseWindow ? size : i == BringHere ? 190 : 150;
                x -= w;
                r[i] = new Rectangle(x, 8, w, size);
                x -= i == CloseWindow || i == TurnLeft || i == Smaller ? 24 : 8;   // gaps between the groups
            }
            return r;
        }

        // A button on the bar was clicked.
        void BarButton(int button)
        {
            if (button == CloseWindow) { ToggleWindow(); return; }
            if (button == BringHere) PlaceInFront();
            else if (button == TurnLeft || button == TurnRight)
            {
                windowAt = TurnAbout(windowAt, (float)(button == TurnLeft ? 1 : -1) * 15 * (float)Math.PI / 180);   // 15 degrees a click
                OpenVr.SetAbsolute(window, OpenVr.Standing, ref windowAt);
            }
            else
            {
                WindowWidth = Math.Max(0.4f, Math.Min(3f, WindowWidth * (button == Bigger ? 1.15f : 1 / 1.15f)));
                OpenVr.SetWidth(window, WindowWidth);
                if (WindowResized != null) WindowResized(WindowWidth);
            }
        }

        // A pose turned round the vertical through its own middle.
        public static OpenVr.Matrix34 TurnAbout(OpenVr.Matrix34 m, float angle)
        {
            float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
            var r = m;
            r.M0 = c * m.M0 + s * m.M8; r.M1 = c * m.M1 + s * m.M9; r.M2 = c * m.M2 + s * m.M10;
            r.M8 = -s * m.M0 + c * m.M8; r.M9 = -s * m.M1 + c * m.M9; r.M10 = -s * m.M2 + c * m.M10;
            return r;
        }

        // ---- Drawing the window costs a lot (the whole thing at once took 160 ms or more), so it's drawn a part at a
        // time into a picture that's kept: every frame just the part that keeps moving (Live: the page, with the
        // visualizer and lyrics), and everything (the bars and Options too) every 2 seconds or right after the laser
        // clicks or scrolls. Frames are spaced at least 3 times as long as the last one took, so the window never
        // takes more than about a quarter of the app's time.

        public Control Live;            // the part of the window that changes all the time
        Bitmap windowCanvas;            // the window's client area at its own size
        Bitmap windowPicture;           // the bar and the window, at the size it goes to SteamVR
        DateTime nextFullDraw;

        // As a texture; without one (no Direct3D 11) as a picture file, as it's far too big for raw pixels.
        void UploadWindow()
        {
            if (!DrawWindow(DateTime.UtcNow >= nextFullDraw)) return;
            if (DateTime.UtcNow >= nextFullDraw) nextFullDraw = DateTime.UtcNow.AddSeconds(2);
            int w = windowPicture.Width, h = windowPicture.Height;
            var data = windowPicture.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            bool sent;
            try { sent = ToGpu(window, ref windowTexture, ref windowTexW, ref windowTexH, data.Scan0, data.Stride, w, h); }
            finally { windowPicture.UnlockBits(data); }
            if (!sent) ShowFile(window, windowPicture, "vr-window");
            if (w != windowTexWidth || h != windowTexHeight)
            {
                windowTexWidth = w;
                windowTexHeight = h;
                var scale = new OpenVr.Vector2 { X = w, Y = h };
                OpenVr.SetMouseScale(window, ref scale);
            }
        }

        // Brings windowPicture up to date: everything when full (or the size changed), else just the Live part. False
        // while the window's minimized (nothing to show).
        public bool DrawWindow(bool full)
        {
            if (App == null || App.IsDisposed) return false;
            var size = App.ClientSize;
            if (size.Width < 200 || size.Height < 150) return false;
            windowScale = Math.Min(1f, MaxWindowWidth / (float)size.Width);
            int w = (int)(size.Width * windowScale), ch = (int)(size.Height * windowScale), h = ch + Bar;   // the window under the bar
            if (windowCanvas == null || windowCanvas.Size != size)
            {
                if (windowCanvas != null) windowCanvas.Dispose();
                windowCanvas = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
                full = true;
            }
            if (windowPicture == null || windowPicture.Width != w || windowPicture.Height != h)
            {
                if (windowPicture != null) windowPicture.Dispose();
                windowPicture = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                full = true;
            }
            if (full || Live == null)
            {
                foreach (Control c in App.Controls)
                    if (Shown(c)) c.DrawToBitmap(windowCanvas, c.Bounds);
            }
            else
            {
                var at = App.PointToClient(Live.PointToScreen(Point.Empty));
                Live.DrawToBitmap(windowCanvas, new Rectangle(at, Live.Size));
            }
            using (var g = Graphics.FromImage(windowPicture))
            {
                g.InterpolationMode = windowScale < 1 ? InterpolationMode.Bilinear : InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(windowCanvas, new Rectangle(0, Bar, w, ch));
                if (full) DrawBar(g, w);
            }
            return true;
        }

        // The bar above the window: the name, and its buttons (big, plain and labelled, for clicking with the laser).
        static void DrawBar(Graphics g, int w)
        {
            using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, 0, 0, w, Bar);
            using (var b = new SolidBrush(Theme.Primary)) g.FillRectangle(b, 0, Bar - 3, w, 3);
            var buttons = BarButtons(w);
            TextRenderer.DrawText(g, "Dino Visualizer", Theme.Header, new Rectangle(18, 0, Math.Max(0, buttons[BringHere].X - 30), Bar - 3), Theme.Primary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            using (var font = new Font(Theme.Header.FontFamily, Theme.Header.Size * 1.2f, FontStyle.Bold))
                for (int i = 0; i < buttons.Length; i++)
                {
                    using (var b = new SolidBrush(i == CloseWindow ? Theme.Primary : Theme.Faint)) g.FillRectangle(b, buttons[i]);
                    using (var p = new Pen(Theme.Bright, 2)) g.DrawRectangle(p, buttons[i]);
                    TextRenderer.DrawText(g, BarLabels[i], i == CloseWindow ? font : Theme.Header, buttons[i], i == CloseWindow ? Theme.Ink : Theme.Bright,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
        }

        void ReleaseWindowPictures()
        {
            if (windowCanvas != null) windowCanvas.Dispose();
            if (windowPicture != null) windowPicture.Dispose();
            windowCanvas = windowPicture = null;
        }

        // A control's own visible flag (the window itself may be hidden in the tray, and then every control reports
        // itself not visible).
        static bool Shown(Control c) { return GetState == null ? c.Visible : (bool)GetState.Invoke(c, new object[] { 2 }); }

        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        const int WmMouseMove = 0x200, WmLButtonDown = 0x201, WmLButtonUp = 0x202, WmMouseWheel = 0x20A, WmMouseLeave = 0x2A3, MkLButton = 1;
        static readonly System.Reflection.MethodInfo GetState = typeof(Control).GetMethod("GetState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        void WindowPointing(OpenVr.Event e)
        {
            float x = OpenVr.First(e), y = windowTexHeight - OpenVr.Second(e);
            // A click or a scroll may change anything in the window: all of it is drawn again, in a moment.
            if (e.Type != OpenVr.MouseMove)
            {
                nextFullDraw = DateTime.MinValue;
                var soon = DateTime.UtcNow.AddMilliseconds(60);
                if (nextWindowFrame > soon) nextWindowFrame = soon;
            }
            // The laser left the window: whatever it was holding is let go.
            if (e.Type == OpenVr.FocusLeave) { ReleaseWindowMouse(); return; }
            if (e.Type == OpenVr.ScrollSmooth)
            {
                if (mouseOn != null) Message(mouseOn, WmMouseWheel, (int)(OpenVr.Second(e) * 120) << 16, lastPoint, true);
                return;
            }
            if (e.Type != OpenVr.MouseMove && e.Type != OpenVr.MouseDown && e.Type != OpenVr.MouseUp) return;
            if (y < Bar)
            {
                // The bar: its buttons.
                if (e.Type == OpenVr.MouseDown && OpenVr.Button(e) == OpenVr.LeftButton)
                {
                    var buttons = BarButtons(windowTexWidth);
                    for (int i = 0; i < buttons.Length; i++)
                        if (buttons[i].Contains((int)x, (int)y)) { BarButton(i); break; }
                }
                return;
            }
            var p = new Point((int)(x / windowScale), (int)((y - Bar) / windowScale));   // in the window's client area
            lastPoint = p;
            if (e.Type == OpenVr.MouseDown && OpenVr.Button(e) == OpenVr.LeftButton)
            {
                mouseOn = ControlAt(p);
                mouseHeld = true;
                Message(mouseOn, WmLButtonDown, MkLButton, p, false);
            }
            else if (e.Type == OpenVr.MouseUp && mouseHeld)
            {
                mouseHeld = false;
                if (mouseOn != null) Message(mouseOn, WmLButtonUp, 0, p, false);
            }
            else if (e.Type == OpenVr.MouseMove)
            {
                // While held, the control it went down on keeps getting the moves (dragging a slider).
                var on = mouseHeld ? mouseOn : ControlAt(p);
                if (on != mouseOn && mouseOn != null && !mouseOn.IsDisposed) SendMessage(mouseOn.Handle, WmMouseLeave, IntPtr.Zero, IntPtr.Zero);
                mouseOn = on;
                Message(on, WmMouseMove, mouseHeld ? MkLButton : 0, p, false);
            }
        }

        Point lastPoint;

        void ReleaseWindowMouse()
        {
            if (mouseOn != null && !mouseOn.IsDisposed && mouseOn.IsHandleCreated)
            {
                if (mouseHeld) Message(mouseOn, WmLButtonUp, 0, lastPoint, false);
                SendMessage(mouseOn.Handle, WmMouseLeave, IntPtr.Zero, IntPtr.Zero);
            }
            mouseOn = null;
            mouseHeld = false;
        }

        // The innermost control at a point in the window's client area. A control's own visible flag is asked for, as
        // the window itself may be hidden in the tray (then every control reports itself not visible).
        Control ControlAt(Point p)
        {
            Control c = App;
            while (true)
            {
                Control next = null;
                foreach (Control child in c.Controls)
                    if (Shown(child) && child.Bounds.Contains(p)) { next = child; break; }
                if (next == null) return c;
                p = new Point(p.X - next.Left, p.Y - next.Top);
                c = next;
            }
        }

        // A mouse message to a control, at a point in the window's client area (in screen coordinates for the wheel).
        void Message(Control c, int msg, int wParam, Point p, bool screen)
        {
            if (c == null || c.IsDisposed || !c.IsHandleCreated) return;
            var at = screen ? App.PointToScreen(p) : c.PointToClient(App.PointToScreen(p));
            SendMessage(c.Handle, msg, (IntPtr)wParam, (IntPtr)((at.Y << 16) | (at.X & 0xFFFF)));
        }

        // ---- Under the card, a strip of buttons for the wrist itself, clicked with the laser like the card's own:
        // the app's window (open or close it), smaller and bigger, and rotate the card on the wrist.
        static readonly string[] StripLabels = { "Open App", "Smaller", "Bigger", "Rotate L", "Rotate R" };
        const int OpenApp = 0, CardSmaller = 1, CardBigger = 2, RotateLeft = 3, RotateRight = 4;
        int stripHover = -1;
        int cardTexHeight = 1;   // the card part of the texture (the strip's below it)

        static int StripHeight(int width) { return Math.Max(40, width / 9); }

        // The strip's buttons, side by side across its full width.
        static Rectangle[] StripButtons(int width, int top, int height)
        {
            int n = StripLabels.Length, gap = Math.Max(4, width / 120), w = (width - gap * (n + 1)) / n;
            var r = new Rectangle[n];
            for (int i = 0; i < n; i++) r[i] = new Rectangle(gap + i * (w + gap), top + gap, w, height - 2 * gap);
            return r;
        }

        void StripButton(int button)
        {
            if (button == OpenApp) ToggleWindow();
            else if (button == CardSmaller || button == CardBigger) Width = Math.Max(0.08f, Math.Min(0.4f, width * (button == CardBigger ? 1.15f : 1 / 1.15f)));
            else
            {
                Roll = ((Roll + (button == RotateLeft ? 15 : -15)) % 360 + 360) % 360;
                attached = OpenVr.NoDevice;   // mounted again, turned, on the next frame
            }
            if (button != OpenApp && Adjusted != null) Adjusted();
        }

        // The card as a hologram with the strip under it, as BGRA bytes (row by row from the top), the card part no
        // bigger than limit bytes (scale is texture pixels per card pixel). cardHeight is where the strip starts.
        public static byte[] Picture(NowPlayingWidget card, double time, int limit, int hover, bool appOpen, out int width, out int height, out int cardHeight, out float scale)
        {
            var area = card.CardArea;
            scale = (float)Math.Min(1, Math.Sqrt(limit / 4.0 / (area.Width * area.Height)));
            width = Math.Max(1, (int)(area.Width * scale));
            int ch = cardHeight = Math.Max(1, (int)(area.Height * scale));
            height = ch + StripHeight(width);
            var bgra = new byte[width * height * 4];
            DrawStrip(bgra, width, ch, height - ch, hover, appOpen);
            bool layered = card.Style == NowPlayingWidget.AeroStyle || card.Style == NowPlayingWidget.XpStyle || card.Style == NowPlayingWidget.OrbStyle;
            using (var whole = card.Snapshot())
            using (var snap = new Bitmap(width, ch, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(snap))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;   // keeps the see-through edges
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(whole, new Rectangle(0, 0, width, ch), area, GraphicsUnit.Pixel);
                }
                var data = snap.LockBits(new Rectangle(0, 0, width, ch), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var src = new byte[data.Stride * ch];
                Marshal.Copy(data.Scan0, src, 0, src.Length);
                snap.UnlockBits(data);
                Color tint = Theme.Primary;
                float flicker = 0.97f + 0.03f * (float)(Math.Sin(time * 23) * Math.Sin(time * 7.3));   // the faintest shimmer
                float beam = (float)((time * 0.3) % 1.5 - 0.25) * ch;   // a band of light sweeping down every 5 seconds
                float glitchRow = (float)((time * 1.7) % 1) * ch;      // once in a while a few rows slip sideways
                bool glitch = Math.Sin(time * 0.9) > 0.998;
                for (int y = 0; y < ch; y++)
                {
                    float scan = y % 3 == 0 ? 0.55f : 1f;
                    float band = Math.Max(0, 1 - Math.Abs(y - beam) / (ch * 0.07f)) * 0.5f;
                    int shift = glitch && Math.Abs(y - glitchRow) < ch * 0.04f ? (int)(width * 0.02f) : 0;
                    for (int x = 0; x < width; x++)
                    {
                        int sx = Math.Min(width - 1, Math.Max(0, x - shift));
                        int i = y * data.Stride + sx * 4, o = (y * width + x) * 4;
                        float a = layered ? src[i + 3] / 255f : 1f;
                        float l = (0.114f * src[i] + 0.587f * src[i + 1] + 0.299f * src[i + 2]) / 255f;
                        float glow = Math.Min(1, l * 1.25f + band);
                        float white = l * l * 0.5f + band * 0.4f;
                        bgra[o] = (byte)Math.Min(255, tint.B * (0.4f + 0.6f * glow) + 255 * white);
                        bgra[o + 1] = (byte)Math.Min(255, tint.G * (0.4f + 0.6f * glow) + 255 * white);
                        bgra[o + 2] = (byte)Math.Min(255, tint.R * (0.4f + 0.6f * glow) + 255 * white);
                        // Never quite solid, so even the light looks (Aero's sky, XP's silver) stay see-through.
                        bgra[o + 3] = (byte)Math.Min(255, 255 * a * (0.12f + 0.68f * Math.Min(1, l * 1.5f) + band * 0.3f) * scan * flicker);
                    }
                }
            }
            // A glowing frame round the rectangular looks, like the edge of the projection.
            if (!layered)
                for (int x = 0; x < width; x++)
                    for (int y = 0; y < ch; y++)
                        if (x < 2 || y < 2 || x >= width - 2 || y >= ch - 2)
                        {
                            int o = (y * width + x) * 4;
                            bgra[o + 3] = Math.Max(bgra[o + 3], (byte)200);
                        }
            return bgra;
        }

        // The strip's buttons into the picture's rows from top: see-through dark glass with glowing edges and words in
        // the accent color, the one the laser's on lit up.
        static void DrawStrip(byte[] bgra, int width, int top, int height, int hover, bool appOpen)
        {
            using (var strip = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                var buttons = StripButtons(width, 0, height);
                using (var g = Graphics.FromImage(strip))
                using (var font = new Font(Theme.Body.FontFamily, Math.Max(10, Math.Min(height * 0.34f, buttons[0].Width * 0.13f)), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var center = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;   // keeps the see-through around the words
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        bool lit = i == hover;
                        using (var b = new SolidBrush(Color.FromArgb(lit ? 150 : 90, lit ? Theme.Primary : Color.FromArgb(10, 12, 18)))) g.FillRectangle(b, buttons[i]);
                        using (var p = new Pen(Color.FromArgb(220, lit ? Theme.Bright : Theme.Primary), Math.Max(2, height / 20))) g.DrawRectangle(p, buttons[i]);
                        string label = i == OpenApp && appOpen ? "Close App" : StripLabels[i];
                        using (var b = new SolidBrush(lit ? Color.White : Theme.Bright)) g.DrawString(label, font, b, buttons[i], center);
                    }
                }
                var data = strip.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                for (int y = 0; y < height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, bgra, (top + y) * width * 4, width * 4);
                strip.UnlockBits(data);
            }
        }
    }

    // ------------------------------------------------------------------ visualizers

    // The colors a visualizer is drawn in: the XP widget's classic green screen, or the app's accent color.
    class VizColors
    {
        public Color Top, Bottom, Cap, Mid, Off, Hot, Glow, Axis;

        public static readonly VizColors XpGreen = new VizColors
        {
            Top = Color.FromArgb(170, 255, 110), Bottom = Color.FromArgb(24, 150, 24), Cap = Color.FromArgb(210, 255, 170),
            Mid = Color.FromArgb(90, 225, 60), Off = Color.FromArgb(18, 50, 18), Hot = Color.FromArgb(255, 80, 60),
            Glow = Color.FromArgb(90, 255, 90), Axis = Color.FromArgb(90, 225, 90)
        };

        public static VizColors Accent()
        {
            return new VizColors
            {
                Top = Theme.Bright, Bottom = Theme.Faint, Cap = Color.FromArgb(235, 240, 255), Mid = Theme.Primary,
                Off = Theme.Hsv(Theme.Hue, Math.Min(1, 0.8f * Theme.Saturation), 0.13f), Hot = Theme.Rec, Glow = Theme.Primary, Axis = Theme.Faint
            };
        }
    }

    // The visualizers, shared by the main window and the XP widget's screen strip: spectrum bars with peak caps, a
    // scrolling oscilloscope, bars mirrored from a center line, and an LED meter. Windows doesn't hand out the actual
    // frequencies, so they follow how loud the player is right now, spread over a shifting pattern.
    sealed class VizEngine
    {
        public static readonly string[] Names =
        {
            "Bars", "Scope", "Mirror", "LED", "Wave", "Dots", "Ripples", "Radial", "Orb", "Waterfall", "Sparks",
            "Starfield", "Lissajous", "Skyline", "VU Meters", "Helix", "Mountains", "Rain", "Circle Scope", "Earth", "Raining Blood"
        };
        public const int EarthStyle = 19, BloodStyle = 20;
        public const int BarCount = 48;
        readonly float[] bars = new float[BarCount], peaks = new float[BarCount];
        readonly Random rnd = new Random(5);
        int tick;
        float level, smooth;   // this frame's loudness (0..1, square-rooted) and a slower-moving copy of it

        // Called about 30 times a second with the player's raw loudness (0..1; 0 when nothing plays). The bars jump up
        // and fall back slowly, with peak caps that drift down; lower notes are a little taller.
        public void Update(float raw)
        {
            tick++;
            level = Math.Min(1, (float)Math.Sqrt(Math.Max(0, raw)) * 1.15f);   // square root: quiet speech still shows
            smooth += (level - smooth) * 0.15f;
            float t = tick * 0.033f;
            for (int i = 0; i < BarCount; i++)
            {
                float n = 0.5f + 0.5f * (float)(Math.Sin(t * (2.3 + i * 0.41) + i * 1.7) * Math.Cos(t * (1.1 + i * 0.13) + i * 0.6));
                float target = level * (0.25f + 0.75f * n) * (1 - 0.35f * i / BarCount);
                bars[i] = target > bars[i] ? bars[i] + (target - bars[i]) * 0.6f : Math.Max(target, bars[i] - 0.035f);
                peaks[i] = Math.Max(peaks[i] - 0.012f, bars[i]);
            }
            UpdateScope(raw);
            UpdateMotion();
            ReleaseUnused();
        }

        // ---- What the newer styles keep from frame to frame. Positions are 0..1 across and down the drawing area, so
        // the same state draws into the small XP strip and the big window alike.

        const int History = 120;                                // Waterfall: the last 4 seconds of bars
        readonly float[,] history = new float[History, BarCount];
        int historyHead;
        readonly float[][] layers = { new float[BarCount], new float[BarCount], new float[BarCount] };   // Mountains
        static readonly float[] LayerRates = { 0.5f, 0.15f, 0.05f };

        class Spark { public float X, Y, VX, VY, Life; }
        class Ring { public float Age, Strength; }
        class Star { public float X, Y, Z, PZ; }
        class Drop { public float X, Y, Speed, Len; }
        readonly List<Spark> sparks = new List<Spark>();
        readonly List<Ring> rings = new List<Ring>();
        readonly Star[] stars = new Star[150];
        readonly Drop[] drops = new Drop[70];
        int lastRing = -100;
        float lissPhase, helixPhase, vuLeft, vuRight;

        float R() { return (float)rnd.NextDouble(); }

        void UpdateMotion()
        {
            for (int i = 0; i < BarCount; i++)
            {
                history[historyHead, i] = bars[i];
                for (int k = 0; k < layers.Length; k++) layers[k][i] += (bars[i] - layers[k][i]) * LayerRates[k];
            }
            historyHead = (historyHead + 1) % History;

            // Sparks: shot up from the bottom, more of them on beats, falling back under gravity.
            int spawn = (int)(level * 2 + scopeHit * 10 + R());
            for (int i = 0; i < spawn && sparks.Count < 400 && level > 0.02f; i++)
                sparks.Add(new Spark { X = R(), Y = 1, VX = (R() - 0.5f) * 0.01f, VY = -(0.015f + R() * 0.03f) * (0.6f + level), Life = 1 });
            foreach (var s in sparks) { s.X += s.VX; s.Y += s.VY; s.VY += 0.0009f; s.Life -= 0.012f; }
            sparks.RemoveAll(s => s.Life <= 0 || s.Y > 1.05f);

            // Ripples: a strong ring on each beat, a soft one now and then while the music plays.
            if (scopeHit > 0.3f && tick - lastRing > 6) { rings.Add(new Ring { Strength = Math.Min(1, scopeHit) }); lastRing = tick; }
            else if (level > 0.05f && tick - lastRing > 24) { rings.Add(new Ring { Strength = level * 0.5f }); lastRing = tick; }
            foreach (var r in rings) r.Age += 0.012f + smooth * 0.01f;
            rings.RemoveAll(r => r.Age >= 1);

            // Starfield: stars rush toward you, faster when it's loud and in a burst on beats.
            float speed = 0.004f + level * 0.03f + scopeHit * 0.02f;
            for (int i = 0; i < stars.Length; i++)
            {
                var s = stars[i];
                if (s == null || s.Z - speed < 0.05f)
                {
                    stars[i] = new Star { X = R() * 2 - 1, Y = R() * 2 - 1, Z = s == null ? 0.05f + R() * 0.95f : 1 };
                    stars[i].PZ = stars[i].Z;
                    continue;
                }
                s.PZ = s.Z;
                s.Z -= speed;
            }

            // Rain: streaks falling, faster with the music.
            for (int i = 0; i < drops.Length; i++)
            {
                var d = drops[i];
                if (d == null || d.Y - d.Len > 1)
                {
                    drops[i] = new Drop { X = R(), Y = d == null ? R() : -R() * 0.3f, Speed = 0.006f + R() * 0.014f, Len = 0.1f + R() * 0.25f };
                    continue;
                }
                d.Y += d.Speed * (0.25f + level * 2);
            }

            UpdateBlood();

            // Earth: it turns (faster when it's loud), its clouds drift a little faster still, and storms flash on the
            // dark side on beats.
            earthSpin += 0.0012f + 0.0045f * smooth;
            cloudSpin += 0.0015f + 0.0052f * smooth;
            if (scopeHit > 0.45f && flashes.Count < 6 && R() < 0.4f)
                flashes.Add(new Flash { X = R() * 1.2f - 0.6f, Y = -0.14f - R() * 0.3f, Life = 1 });
            foreach (var f in flashes) f.Life -= 0.1f;
            flashes.RemoveAll(f => f.Life <= 0);

            lissPhase += 0.015f + smooth * 0.04f;
            helixPhase += 0.08f + smooth * 0.15f;

            // VU needles: the low half of the bars on the left meter, the high half (lifted a bit) on the right.
            float lo = 0, hi = 0;
            for (int i = 0; i < BarCount / 2; i++) { lo += bars[i]; hi += bars[i + BarCount / 2]; }
            lo = Math.Min(1, lo / (BarCount / 2) * 1.1f);
            hi = Math.Min(1, hi / (BarCount / 2) * 1.35f);
            vuLeft += (lo - vuLeft) * (lo > vuLeft ? 0.35f : 0.08f);
            vuRight += (hi - vuRight) * (hi > vuRight ? 0.35f : 0.08f);
        }

        // The Scope trace: a scrolling buffer, newest on the right, about 2 seconds across. Its loudness is the raw
        // meter reading over an automatic gain (so each song uses the full height, quiet parts flat and loud parts
        // tall), split into a slow wide wave that follows the overall level (the lows) and fast jagged spikes when the
        // sound suddenly jumps above that level (the hits).
        readonly float[] scope = new float[240];
        int scopeHead;
        float scopeGain = 0.05f, scopeSlow, scopeHit, scopePhaseLow, scopePhaseHigh;
        readonly Random scopeRnd = new Random(3);

        void UpdateScope(float raw)
        {
            scopeGain = Math.Max(Math.Max(raw, 0.18f), scopeGain * 0.996f);   // the floor keeps quiet passages small
            float level = Math.Min(1, raw / scopeGain);
            scopeSlow += (level - scopeSlow) * 0.08f;
            scopeHit = Math.Max(Math.Max(0, level - scopeSlow) * 1.8f, scopeHit * 0.6f);   // hits flash and fade fast
            for (int k = 0; k < 4; k++)
            {
                scopePhaseLow += 0.16f + level * 0.2f;
                scopePhaseHigh += 0.75f + scopeHit * 1.5f;
                float low = level * 0.7f * (float)Math.Sin(scopePhaseLow);
                float high = (0.06f * level + scopeHit) * (float)Math.Sin(scopePhaseHigh) * (0.55f + 0.45f * (float)scopeRnd.NextDouble());
                scope[scopeHead] = Math.Max(-1, Math.Min(1, low + high));
                scopeHead = (scopeHead + 1) % scope.Length;
            }
        }

        // Draws one style into r. unit is the size of one design pixel (the widget's scale, or bigger for the window).
        public void Draw(Graphics g, RectangleF r, int style, float unit, VizColors c)
        {
            var clip = g.Clip;
            var smoothing = g.SmoothingMode;
            g.SetClip(r, CombineMode.Intersect);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            switch (style)
            {
                case 1: DrawScope(g, r, unit, c); break;
                case 2: DrawMirror(g, r, unit, c); break;
                case 3: DrawLed(g, r, unit, c); break;
                case 4: DrawWave(g, r, unit, c); break;
                case 5: DrawDots(g, r, unit, c); break;
                case 6: DrawRipples(g, r, unit, c); break;
                case 7: DrawRadial(g, r, unit, c); break;
                case 8: DrawOrb(g, r, unit, c); break;
                case 9: DrawWaterfall(g, r, c); break;
                case 10: DrawSparks(g, r, unit, c); break;
                case 11: DrawStarfield(g, r, unit, c); break;
                case 12: DrawLissajous(g, r, unit, c); break;
                case 13: DrawSkyline(g, r, unit, c); break;
                case 14: DrawVu(g, r, unit, c); break;
                case 15: DrawHelix(g, r, unit, c); break;
                case 16: DrawMountains(g, r, unit, c); break;
                case 17: DrawRain(g, r, unit, c); break;
                case 18: DrawCircleScope(g, r, unit, c); break;
                case BloodStyle: DrawBlood(g, r, unit); break;
                case EarthStyle:
                    if (earth == null) earth = new EarthScene(this);
                    earth.Draw(g, r, unit);
                    earthAt = tick;
                    break;
                default: DrawBars(g, r, unit, c); break;
            }
            g.SmoothingMode = smoothing;
            g.Clip = clip;
        }

        // ---- Several colors at once. The style is drawn in shades of gray into a buffer, then every column is tinted
        // with the palette's color at that point across (blending smoothly between the colors), so all the styles work
        // in any number of colors. The brightest parts keep a little white sparkle.

        static readonly VizColors Mono = new VizColors
        {
            Top = Color.White, Bottom = Color.FromArgb(70, 70, 70), Cap = Color.White, Mid = Color.FromArgb(200, 200, 200),
            Off = Color.FromArgb(34, 34, 34), Hot = Color.White, Glow = Color.FromArgb(235, 235, 235), Axis = Color.FromArgb(110, 110, 110)
        };
        Bitmap multi;
        int[] multiRow;
        int earthAt, multiAt;   // when the Earth and the several-colors buffer were last drawn (frames)

        // Lets go of what only drawing needs: the Earth (and so its maps, if no other Earth is showing) and the
        // several-colors buffer. They're made again the next time they're drawn.
        public void Release()
        {
            if (earth != null) earth.Dispose();
            earth = null;
            if (multi != null) multi.Dispose();
            multi = null;
            multiRow = null;
        }

        // Called every frame: what hasn't been drawn for five seconds (another style chosen, the window hidden) goes.
        void ReleaseUnused()
        {
            if (earth != null && tick - earthAt > 150)
            {
                earth.Dispose();
                earth = null;
            }
            if (multi != null && tick - multiAt > 150)
            {
                multi.Dispose();
                multi = null;
                multiRow = null;
            }
        }

        public void DrawMulti(Graphics g, RectangleF r, int style, float unit, Color[] palette)
        {
            int w = (int)Math.Ceiling(r.Width), h = (int)Math.Ceiling(r.Height);
            // Earth and Raining Blood keep their own colors.
            if (w < 2 || h < 2 || palette == null || palette.Length < 2 || style == EarthStyle || style == BloodStyle) { Draw(g, r, style, unit, VizColors.Accent()); return; }
            multiAt = tick;
            if (multi == null || multi.Width != w || multi.Height != h)
            {
                if (multi != null) multi.Dispose();
                multi = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                multiRow = new int[w];
            }
            using (var mg = Graphics.FromImage(multi))
            {
                mg.Clear(Color.Transparent);
                Draw(mg, new RectangleF(0, 0, w, h), style, unit, Mono);
            }
            // The tint for each column.
            var tr = new int[w];
            var tg = new int[w];
            var tb = new int[w];
            for (int x = 0; x < w; x++)
            {
                float t = x / (float)(w - 1) * (palette.Length - 1);
                int i = Math.Min(palette.Length - 2, (int)t);
                float f = t - i;
                Color a = palette[i], b = palette[i + 1];
                tr[x] = (int)(a.R + (b.R - a.R) * f);
                tg[x] = (int)(a.G + (b.G - a.G) * f);
                tb[x] = (int)(a.B + (b.B - a.B) * f);
            }
            var area = new Rectangle(0, 0, w, h);
            var data = multi.LockBits(area, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                // A row at a time, so the buffer is one row rather than the whole picture.
                var row = multiRow;
                for (int y = 0; y < h; y++)
                {
                    IntPtr line = data.Scan0 + y * data.Stride;
                    Marshal.Copy(line, row, 0, w);
                    for (int x = 0; x < w; x++)
                    {
                        int p = row[x], alpha = (p >> 24) & 255;
                        if (alpha == 0) continue;
                        int lum = (p >> 16) & 255;   // the gray level
                        int nr = tr[x] * lum / 255, ng = tg[x] * lum / 255, nb = tb[x] * lum / 255;
                        if (lum > 215)
                        {
                            int k = (lum - 215) * 140 / 40;   // up to about half way to white
                            nr += (255 - nr) * k / 255; ng += (255 - ng) * k / 255; nb += (255 - nb) * k / 255;
                        }
                        row[x] = (alpha << 24) | (nr << 16) | (ng << 8) | nb;
                    }
                    Marshal.Copy(row, 0, line, w);
                }
            }
            finally { multi.UnlockBits(data); }
            g.DrawImage(multi, new RectangleF(r.X, r.Y, w, h));
        }

        void DrawBars(Graphics g, RectangleF viz, float u, VizColors c)
        {
            float bw = viz.Width / BarCount;
            for (int i = 0; i < BarCount; i++)
            {
                float h = Math.Max(u, bars[i] * viz.Height), x = viz.X + i * bw;
                var bar = new RectangleF(x, viz.Bottom - h, Math.Max(1, bw - 2 * u), h);
                using (var b = new LinearGradientBrush(new RectangleF(bar.X, viz.Y - 1, bar.Width, viz.Height + 2), c.Top, c.Bottom, 90f)) g.FillRectangle(b, bar);
                float py = viz.Bottom - peaks[i] * viz.Height - 2 * u;
                using (var b = new SolidBrush(c.Cap)) g.FillRectangle(b, x, py, Math.Max(1, bw - 2 * u), 1.5f * u);
            }
        }

        // An oscilloscope: the scrolling trace, oldest on the left, glowing brighter on hits.
        void DrawScope(Graphics g, RectangleF viz, float u, VizColors c)
        {
            float cy = viz.Y + viz.Height / 2, half = viz.Height * 0.48f;
            using (var p = new Pen(Color.FromArgb(50, c.Axis), 1)) g.DrawLine(p, viz.X, cy, viz.Right, cy);
            int n = scope.Length;
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
                pts[i] = new PointF(viz.X + viz.Width * i / (n - 1), cy - scope[(scopeHead + i) % n] * half);
            int glowAlpha = (int)Math.Min(160, 60 + scopeHit * 160);
            using (var glow = new Pen(Color.FromArgb(glowAlpha, c.Glow), u * (4 + 3 * Math.Min(1, scopeHit))) { LineJoin = LineJoin.Round }) g.DrawLines(glow, pts);
            using (var line = new Pen(c.Top, Math.Max(1.2f, 1.4f * u)) { LineJoin = LineJoin.Round }) g.DrawLines(line, pts);
        }

        // Bars mirrored up and down from the middle line, fading toward their tips.
        void DrawMirror(Graphics g, RectangleF viz, float u, VizColors c)
        {
            float bw = viz.Width / BarCount, cy = viz.Y + viz.Height / 2, half = viz.Height / 2;
            for (int i = 0; i < BarCount; i++)
            {
                float h = Math.Max(u, bars[i] * half), x = viz.X + i * bw, w = Math.Max(1, bw - 2 * u);
                using (var b = new LinearGradientBrush(new RectangleF(x, viz.Y - 1, w, half + 2), Color.FromArgb(60, c.Top), c.Top, 90f)) g.FillRectangle(b, x, cy - h, w, h);
                using (var b = new LinearGradientBrush(new RectangleF(x, cy - 1, w, half + 2), c.Bottom, Color.FromArgb(40, c.Bottom), 90f)) g.FillRectangle(b, x, cy, w, h);
            }
            using (var p = new Pen(Color.FromArgb(160, c.Cap), 1)) g.DrawLine(p, viz.X, cy, viz.Right, cy);
        }

        // An old hi-fi meter: columns of LED segments, dark when off, the top segment red.
        void DrawLed(Graphics g, RectangleF viz, float u, VizColors c)
        {
            const int columns = 32;
            int rows = Math.Max(8, Math.Min(24, (int)Math.Round(viz.Height / (u * 4.5f))));
            float cw = viz.Width / columns, rh = viz.Height / rows;
            using (var off = new SolidBrush(c.Off))
            using (var lit = new SolidBrush(c.Top))
            using (var mid = new SolidBrush(c.Mid))
            using (var hot = new SolidBrush(c.Hot))
                for (int col = 0; col < columns; col++)
                {
                    // Each column takes the loudest of the bars under it.
                    int from = col * BarCount / columns, to = (col + 1) * BarCount / columns;
                    float v = 0, peak = 0;
                    for (int i = from; i < Math.Max(from + 1, to); i++) { v = Math.Max(v, bars[i]); peak = Math.Max(peak, peaks[i]); }
                    int litRows = (int)Math.Round(v * rows), peakRow = Math.Min(rows - 1, (int)Math.Round(peak * rows) - 1);
                    for (int r = 0; r < rows; r++)
                    {
                        var cell = new RectangleF(viz.X + col * cw + u, viz.Bottom - (r + 1) * rh + u, cw - 2 * u, rh - 1.5f * u);
                        Brush b = r < litRows ? (r == rows - 1 ? hot : r >= rows - 3 ? lit : mid) : r == peakRow ? (r == rows - 1 ? hot : lit) : off;
                        g.FillRectangle(b, cell);
                    }
                }
        }

        // ---- The newer styles

        // The loudest bar in a share of the spectrum (for styles with fewer columns than bars).
        float BarAt(int index, int count)
        {
            int from = index * BarCount / count, to = Math.Max(from + 1, (index + 1) * BarCount / count);
            float v = 0;
            for (int i = from; i < to; i++) v = Math.Max(v, bars[i]);
            return v;
        }

        // A bar around the full circle: the spectrum runs up one side and back down the other.
        float BarRound(int index, int count)
        {
            int half = count / 2, i = index < half ? index : count - 1 - index;
            return BarAt(i, half);
        }

        // Round styles fill the area as a circle, or as a flattened oval in a wide, short strip like the XP screen.
        static void Oval(RectangleF r, out float cx, out float cy, out float rx, out float ry)
        {
            cx = r.X + r.Width / 2;
            cy = r.Y + r.Height / 2;
            ry = r.Height / 2;
            rx = Math.Min(r.Width / 2, Math.Max(ry, r.Width * 0.16f));
        }

        static Color Ramp(Color[] colors, float v)
        {
            v = Math.Max(0, Math.Min(1, v)) * (colors.Length - 1);
            int i = Math.Min(colors.Length - 2, (int)v);
            float f = v - i;
            Color a = colors[i], b = colors[i + 1];
            return Color.FromArgb((int)(a.A + (b.A - a.A) * f), (int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));
        }

        // A filled, smooth wave through the bar tops, with a glowing edge.
        void DrawWave(Graphics g, RectangleF viz, float u, VizColors c)
        {
            var pts = new PointF[BarCount];
            for (int i = 0; i < BarCount; i++)
            {
                float v = (bars[Math.Max(0, i - 1)] + 2 * bars[i] + bars[Math.Min(BarCount - 1, i + 1)]) / 4;
                pts[i] = new PointF(viz.X + viz.Width * i / (BarCount - 1), viz.Bottom - Math.Max(u, v * viz.Height * 0.95f));
            }
            using (var path = new GraphicsPath())
            {
                path.AddCurve(pts, 0.5f);
                path.AddLine(viz.Right, viz.Bottom, viz.X, viz.Bottom);
                path.CloseFigure();
                using (var b = new LinearGradientBrush(new RectangleF(viz.X, viz.Y - 1, viz.Width, viz.Height + 2), Color.FromArgb(210, c.Top), Color.FromArgb(25, c.Bottom), 90f))
                    g.FillPath(b, path);
            }
            using (var glow = new Pen(Color.FromArgb(90, c.Glow), 3 * u) { LineJoin = LineJoin.Round }) g.DrawCurve(glow, pts, 0.5f);
            using (var line = new Pen(c.Cap, Math.Max(1, 1.2f * u)) { LineJoin = LineJoin.Round }) g.DrawCurve(line, pts, 0.5f);
        }

        // A dot-matrix display: columns of round lights, a bright dot riding on each peak.
        void DrawDots(Graphics g, RectangleF viz, float u, VizColors c)
        {
            int columns = Math.Max(16, Math.Min(BarCount, (int)(viz.Width / (6 * u)))), rows = Math.Max(4, (int)(viz.Height / (6 * u)));
            float cw = viz.Width / columns, rh = viz.Height / rows, d = Math.Min(cw, rh) * 0.72f;
            using (var off = new SolidBrush(c.Off))
                for (int col = 0; col < columns; col++)
                {
                    float v = BarAt(col, columns), peak = 0;
                    for (int i = col * BarCount / columns; i < Math.Max(col * BarCount / columns + 1, (col + 1) * BarCount / columns); i++) peak = Math.Max(peak, peaks[i]);
                    int lit = (int)Math.Round(v * rows), peakRow = Math.Min(rows - 1, (int)Math.Round(peak * rows) - 1);
                    for (int row = 0; row < rows; row++)
                    {
                        float x = viz.X + col * cw + (cw - d) / 2, y = viz.Bottom - (row + 1) * rh + (rh - d) / 2;
                        if (row < lit)
                            using (var b = new SolidBrush(row == rows - 1 ? c.Hot : row >= rows * 2 / 3 ? c.Top : c.Mid)) g.FillEllipse(b, x, y, d, d);
                        else if (row == peakRow)
                            using (var b = new SolidBrush(c.Cap)) g.FillEllipse(b, x, y, d, d);
                        else g.FillEllipse(off, x, y, d, d);
                    }
                }
        }

        // Rings spreading out from the middle on every beat, round a glowing center that swells with the music.
        void DrawRipples(Graphics g, RectangleF viz, float u, VizColors c)
        {
            float cx, cy, rx, ry;
            Oval(viz, out cx, out cy, out rx, out ry);
            // Rings grow out to the corners: circles in the window, wide ovals in a short strip.
            bool round = rx == ry;
            float reach = (float)Math.Sqrt(viz.Width * viz.Width + viz.Height * viz.Height) / 2;
            float maxX = round ? reach : viz.Width / 2, maxY = round ? reach : viz.Height * 0.6f;
            foreach (var ring in rings)
            {
                float wx = ring.Age * maxX, wy = ring.Age * maxY;
                int a = (int)(255 * (1 - ring.Age) * Math.Min(1, 0.35f + ring.Strength));
                using (var p = new Pen(Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.Top), u * (1 + 3 * ring.Strength * (1 - ring.Age))))
                    g.DrawEllipse(p, cx - wx, cy - wy, 2 * wx, 2 * wy);
            }
            float core = Math.Min(rx, ry) * (0.18f + 0.3f * smooth);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - core * 2, cy - core * 2, core * 4, core * 4);
                using (var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb(200, c.Glow), SurroundColors = new[] { Color.FromArgb(0, c.Glow) } })
                    g.FillPath(pg, path);
            }
            using (var b = new SolidBrush(c.Cap)) g.FillEllipse(b, cx - core / 2, cy - core / 2, core, core);
        }

        // Spokes all round a ring, each one a bar, the ring itself pulsing.
        void DrawRadial(Graphics g, RectangleF viz, float u, VizColors c)
        {
            float cx, cy, rx, ry;
            Oval(viz, out cx, out cy, out rx, out ry);
            const int n = 96;
            float inner = 0.46f * (1 + 0.1f * smooth);
            using (var p = new Pen(Color.FromArgb(120, c.Axis), u)) g.DrawEllipse(p, cx - rx * inner, cy - ry * inner, 2 * rx * inner, 2 * ry * inner);
            float width = Math.Max(1, (float)(2 * Math.PI * Math.Min(rx, ry) * inner / n * 0.6));
            for (int i = 0; i < n; i++)
            {
                double a = -Math.PI / 2 + 2 * Math.PI * i / n;
                float v = BarRound(i, n), cos = (float)Math.Cos(a), sin = (float)Math.Sin(a), outer = inner + 0.52f * Math.Max(0.03f, v);
                using (var p = new Pen(Ramp(new[] { c.Mid, c.Top, c.Cap }, v), width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(p, cx + cos * rx * inner, cy + sin * ry * inner, cx + cos * rx * outer, cy + sin * ry * outer);
            }
        }

        // A glowing blob whose outline is the spectrum, swelling with the music.
        void DrawOrb(Graphics g, RectangleF viz, float u, VizColors c)
        {
            float cx, cy, rx, ry;
            Oval(viz, out cx, out cy, out rx, out ry);
            const int n = 96;
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                double a = -Math.PI / 2 + 2 * Math.PI * i / n + tick * 0.004;
                float k = 0.32f + 0.12f * smooth + 0.5f * BarRound(i, n);
                pts[i] = new PointF(cx + (float)Math.Cos(a) * rx * k, cy + (float)Math.Sin(a) * ry * k);
            }
            using (var path = new GraphicsPath())
            {
                path.AddClosedCurve(pts, 0.5f);
                using (var pg = new PathGradientBrush(path) { CenterPoint = new PointF(cx, cy), CenterColor = Color.FromArgb(230, c.Cap), SurroundColors = new[] { Color.FromArgb(110, c.Bottom) } })
                    g.FillPath(pg, path);
                using (var glow = new Pen(Color.FromArgb((int)Math.Min(170, 60 + scopeHit * 160), c.Glow), u * (3 + 3 * Math.Min(1, scopeHit)))) g.DrawPath(glow, path);
                using (var p = new Pen(c.Top, Math.Max(1, 1.3f * u))) g.DrawPath(p, path);
            }
        }

        // A spectrogram: the last few seconds scrolling to the left, low notes at the bottom, louder brighter.
        void DrawWaterfall(Graphics g, RectangleF viz, VizColors c)
        {
            int bins = Math.Max(8, Math.Min(BarCount, (int)(viz.Height / 3)));
            var ramp = new[] { c.Off, c.Bottom, c.Mid, c.Top, c.Cap };
            var px = new int[History * bins];
            for (int col = 0; col < History; col++)
            {
                int h = (historyHead + col) % History;   // oldest on the left
                for (int b = 0; b < bins; b++)
                {
                    int from = b * BarCount / bins, to = Math.Max(from + 1, (b + 1) * BarCount / bins);
                    float v = 0;
                    for (int i = from; i < to; i++) v = Math.Max(v, history[h, i]);
                    px[(bins - 1 - b) * History + col] = Ramp(ramp, v).ToArgb();
                }
            }
            using (var bmp = new Bitmap(History, bins, PixelFormat.Format32bppArgb))
            {
                var data = bmp.LockBits(new Rectangle(0, 0, History, bins), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                for (int row = 0; row < bins; row++) Marshal.Copy(px, row * History, data.Scan0 + row * data.Stride, History);
                bmp.UnlockBits(data);
                var mode = g.InterpolationMode;
                var offset = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bmp, viz);
                g.InterpolationMode = mode;
                g.PixelOffsetMode = offset;
            }
        }

        // Sparks shooting up from the bottom, more on every beat, arcing back down as they fade.
        void DrawSparks(Graphics g, RectangleF viz, float u, VizColors c)
        {
            foreach (var s in sparks)
            {
                float x = viz.X + s.X * viz.Width, y = viz.Y + s.Y * viz.Height, r = u * (0.8f + 1.2f * s.Life);
                int a = (int)(255 * Math.Max(0, s.Life));
                using (var b = new SolidBrush(Color.FromArgb(a / 3, c.Glow))) g.FillEllipse(b, x - r * 2, y - r * 2, r * 4, r * 4);
                using (var b = new SolidBrush(Color.FromArgb(a, s.Life > 0.6f ? c.Cap : c.Top))) g.FillEllipse(b, x - r, y - r, 2 * r, 2 * r);
            }
            using (var p = new Pen(Color.FromArgb(120, c.Axis), u)) g.DrawLine(p, viz.X, viz.Bottom - u, viz.Right, viz.Bottom - u);
        }

        // Flying through stars: streaks rushing out from the middle, faster when it's loud.
        void DrawStarfield(Graphics g, RectangleF viz, float u, VizColors c)
        {
            float cx = viz.X + viz.Width / 2, cy = viz.Y + viz.Height / 2, fx = viz.Width / 2, fy = viz.Height / 2;
            foreach (var s in stars)
            {
                if (s == null) continue;
                float x1 = cx + s.X / s.PZ * fx * 0.5f, y1 = cy + s.Y / s.PZ * fy * 0.5f;
                float x2 = cx + s.X / s.Z * fx * 0.5f, y2 = cy + s.Y / s.Z * fy * 0.5f;
                float near = 1 - s.Z;
                int a = (int)(60 + 195 * near);
                using (var p = new Pen(Color.FromArgb(a, near > 0.7f ? c.Cap : c.Top), Math.Max(1, u * (0.5f + 2 * near))) { EndCap = LineCap.Round })
                    g.DrawLine(p, x1, y1, x2 + (x2 == x1 && y2 == y1 ? 0.5f : 0), y2);
            }
        }

        // A Lissajous figure, like an oscilloscope in X-Y mode: it opens up with the music and shivers on beats.
        void DrawLissajous(Graphics g, RectangleF viz, float u, VizColors c)
        {
            const int n = 360;
            float cx = viz.X + viz.Width / 2, cy = viz.Y + viz.Height / 2, amp = 0.25f + 0.7f * smooth;
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                double t = 2 * Math.PI * i / (n - 1);
                float jitter = scopeHit * 0.08f * (float)Math.Sin(17 * t + tick * 0.7);
                pts[i] = new PointF(cx + (float)Math.Sin(3 * t + lissPhase) * (viz.Width / 2 * 0.92f) * (amp + jitter),
                                    cy + (float)Math.Sin(2 * t) * (viz.Height / 2 * 0.9f) * (amp + jitter));
            }
            using (var glow = new Pen(Color.FromArgb((int)Math.Min(160, 60 + scopeHit * 160), c.Glow), u * (3 + 3 * Math.Min(1, scopeHit))) { LineJoin = LineJoin.Round }) g.DrawLines(glow, pts);
            using (var line = new Pen(c.Top, Math.Max(1.2f, 1.4f * u)) { LineJoin = LineJoin.Round }) g.DrawLines(line, pts);
        }

        // A city skyline: each bar a building with lit windows, mirrored in the water below.
        void DrawSkyline(Graphics g, RectangleF viz, float u, VizColors c)
        {
            const int n = 24;
            float horizon = viz.Y + viz.Height * 0.74f, cw = viz.Width / n, room = horizon - viz.Y;
            for (int i = 0; i < n; i++)
            {
                float v = Math.Max(0.04f, BarAt(i, n)), h = v * room, x = viz.X + i * cw + u, w = cw - 2 * u;
                var b = new RectangleF(x, horizon - h, w, h);
                using (var br = new LinearGradientBrush(new RectangleF(x, viz.Y - 1, w, room + 2), c.Top, c.Bottom, 90f)) g.FillRectangle(br, b);
                // Windows: small lights in a grid, a few switching on and off over time.
                float step = Math.Max(3 * u, 3);
                using (var lit = new SolidBrush(Color.FromArgb(170, c.Cap)))
                    for (float wy = b.Y + step * 0.6f; wy < horizon - step * 0.5f; wy += step)
                        for (float wx = x + step * 0.5f; wx < x + w - step * 0.4f; wx += step)
                            if (((int)(wx * 7 + wy * 13) + tick / 20 + i) % 5 < 2) g.FillRectangle(lit, wx, wy, Math.Max(1, u), Math.Max(1, u));
                float rh = Math.Min(viz.Bottom - horizon, h * 0.45f);
                using (var br = new LinearGradientBrush(new RectangleF(x, horizon - 1, w, viz.Bottom - horizon + 2), Color.FromArgb(90, c.Top), Color.FromArgb(0, c.Bottom), 90f))
                    g.FillRectangle(br, x, horizon + u, w, rh);
            }
            using (var p = new Pen(c.Axis, u)) g.DrawLine(p, viz.X, horizon, viz.Right, horizon);
        }

        // A pair of analog VU meters with swinging needles: the lows on the left, the highs on the right.
        void DrawVu(Graphics g, RectangleF viz, float u, VizColors c)
        {
            float gap = viz.Width * 0.04f, w = (viz.Width - gap) / 2;
            DrawMeter(g, new RectangleF(viz.X, viz.Y, w, viz.Height), vuLeft, u, c);
            DrawMeter(g, new RectangleF(viz.X + w + gap, viz.Y, w, viz.Height), vuRight, u, c);
        }

        static void DrawMeter(Graphics g, RectangleF r, float v, float u, VizColors c)
        {
            float px = r.X + r.Width / 2, py = r.Bottom - u * 2;
            // The scale is the top third of a circle (210° to 330°): as tall as its radius, 1.73 radii wide.
            float radius = Math.Max(4, Math.Min(r.Width * 0.48f / 0.866f, r.Height - u * 4));
            var arc = new RectangleF(px - radius, py - radius, 2 * radius, 2 * radius);
            using (var p = new Pen(c.Axis, Math.Max(1, 1.5f * u))) g.DrawArc(p, arc, 210, 96);
            using (var p = new Pen(c.Hot, Math.Max(1.5f, 2.5f * u))) g.DrawArc(p, arc, 306, 24);
            for (int i = 0; i <= 10; i++)
            {
                double a = (210 + 12 * i) * Math.PI / 180;
                float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a), inner = i % 5 == 0 ? 0.84f : 0.9f;
                using (var p = new Pen(i >= 8 ? c.Hot : c.Mid, Math.Max(1, u)))
                    g.DrawLine(p, px + cos * radius * inner, py + sin * radius * inner, px + cos * radius, py + sin * radius);
            }
            double na = (210 + 120 * Math.Max(0, Math.Min(1, v))) * Math.PI / 180;
            using (var p = new Pen(c.Cap, Math.Max(1.2f, 1.6f * u)) { EndCap = LineCap.Round })
                g.DrawLine(p, px, py, px + (float)Math.Cos(na) * radius * 0.97f, py + (float)Math.Sin(na) * radius * 0.97f);
            float d = Math.Max(3, 4 * u);
            using (var b = new SolidBrush(c.Top)) g.FillEllipse(b, px - d / 2, py - d / 2, d, d);
        }

        // A turning double helix whose strands spread apart with the spectrum, joined by rungs.
        void DrawHelix(Graphics g, RectangleF viz, float u, VizColors c)
        {
            const int n = 64;
            float cy = viz.Y + viz.Height / 2;
            var a = new PointF[n];
            var b = new PointF[n];
            var depth = new float[n];
            for (int j = 0; j < n; j++)
            {
                float x = viz.X + viz.Width * j / (n - 1);
                float amp = viz.Height * 0.44f * (0.25f + 0.75f * BarAt(j, n));
                double angle = j * 0.32 + helixPhase;
                float s = (float)Math.Sin(angle);
                a[j] = new PointF(x, cy + s * amp);
                b[j] = new PointF(x, cy - s * amp);
                depth[j] = (float)Math.Cos(angle);
            }
            using (var rung = new Pen(Color.FromArgb(110, c.Mid), Math.Max(1, u)))
                for (int j = 0; j < n; j += 3) g.DrawLine(rung, a[j], b[j]);
            using (var p = new Pen(Color.FromArgb(150, c.Mid), Math.Max(1, 1.2f * u))) g.DrawCurve(p, b, 0.5f);
            using (var p = new Pen(c.Top, Math.Max(1, 1.4f * u))) g.DrawCurve(p, a, 0.5f);
            for (int j = 0; j < n; j += 2)
            {
                float da = u * (1.2f + 1.4f * (depth[j] + 1) / 2), db = u * (1.2f + 1.4f * (1 - depth[j]) / 2);
                using (var br = new SolidBrush(depth[j] > 0 ? c.Cap : c.Mid)) g.FillEllipse(br, a[j].X - da, a[j].Y - da, 2 * da, 2 * da);
                using (var br = new SolidBrush(depth[j] <= 0 ? c.Cap : c.Mid)) g.FillEllipse(br, b[j].X - db, b[j].Y - db, 2 * db, 2 * db);
            }
        }

        // Three ranges of hills, the far ones following the music more slowly, like a landscape scrolling past.
        void DrawMountains(Graphics g, RectangleF viz, float u, VizColors c)
        {
            var fills = new[] { Color.FromArgb(235, c.Top), Color.FromArgb(180, c.Mid), Color.FromArgb(120, c.Bottom) };
            var scales = new[] { 0.6f, 0.8f, 1f };
            for (int k = layers.Length - 1; k >= 0; k--)
            {
                var pts = new PointF[BarCount];
                for (int i = 0; i < BarCount; i++)
                    pts[i] = new PointF(viz.X + viz.Width * i / (BarCount - 1), viz.Bottom - Math.Max(u, layers[k][(i + k * 7) % BarCount] * viz.Height * scales[k] * 0.95f));
                using (var path = new GraphicsPath())
                {
                    path.AddCurve(pts, 0.5f);
                    path.AddLine(viz.Right, viz.Bottom, viz.X, viz.Bottom);
                    path.CloseFigure();
                    using (var b = new LinearGradientBrush(new RectangleF(viz.X, viz.Y - 1, viz.Width, viz.Height + 2), fills[k], Color.FromArgb(fills[k].A / 3, c.Bottom), 90f))
                        g.FillPath(b, path);
                }
                if (k == 0) using (var p = new Pen(c.Cap, Math.Max(1, u))) g.DrawCurve(p, pts, 0.5f);
            }
        }

        // Digital rain: streaks falling with bright heads, faster when it's loud.
        void DrawRain(Graphics g, RectangleF viz, float u, VizColors c)
        {
            foreach (var d in drops)
            {
                if (d == null) continue;
                float x = viz.X + d.X * viz.Width, y2 = viz.Y + d.Y * viz.Height, y1 = y2 - d.Len * viz.Height;
                if (y2 - y1 < 1) continue;
                using (var b = new LinearGradientBrush(new RectangleF(x - 1, y1 - 1, 2, y2 - y1 + 2), Color.FromArgb(0, c.Top), Color.FromArgb(220, c.Top), 90f))
                using (var p = new Pen(b, Math.Max(1, 1.4f * u)))
                    g.DrawLine(p, x, y1, x, y2);
                float hd = Math.Max(2, 2.2f * u);
                using (var b = new SolidBrush(c.Cap)) g.FillEllipse(b, x - hd / 2, y2 - hd / 2, hd, hd);
            }
        }

        // The Scope trace wrapped round a circle: a ring that bulges and spikes with the music.
        void DrawCircleScope(Graphics g, RectangleF viz, float u, VizColors c)
        {
            float cx, cy, rx, ry;
            Oval(viz, out cx, out cy, out rx, out ry);
            using (var p = new Pen(Color.FromArgb(60, c.Axis), u)) g.DrawEllipse(p, cx - rx * 0.62f, cy - ry * 0.62f, rx * 1.24f, ry * 1.24f);
            int n = scope.Length;
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                double a = -Math.PI / 2 + 2 * Math.PI * i / n;
                float k = 0.62f + 0.34f * scope[(scopeHead + i) % n];
                pts[i] = new PointF(cx + (float)Math.Cos(a) * rx * k, cy + (float)Math.Sin(a) * ry * k);
            }
            using (var glow = new Pen(Color.FromArgb((int)Math.Min(160, 60 + scopeHit * 160), c.Glow), u * (4 + 3 * Math.Min(1, scopeHit))) { LineJoin = LineJoin.Round }) g.DrawPolygon(glow, pts);
            using (var line = new Pen(c.Top, Math.Max(1.2f, 1.4f * u)) { LineJoin = LineJoin.Round }) g.DrawPolygon(line, pts);
        }

        // ---- Raining Blood: blood soaks the top edge and swells into drips that let go; drops rain down, faster and
        // thicker with the music and in a downpour on beats, and splash into a pool at the bottom whose surface waves
        // with the spectrum (low notes on the left) and ripples where they land. A dark red glow rises from the pool
        // with the loudness. Always blood red: the accent and color options don't touch it. Positions are 0..1 across
        // and down, so it fits any size.

        class BloodDrop
        {
            public float X, Y, VY, Size;
            public bool Landed;   // it has hit the pool and is sinking into it
            public float Under;   // how far down it sinks before it's gone for good
        }
        class BloodSplash { public float X, Y, VX, VY, Life, Size; }
        class Ripple { public float X, Age, Strength; }
        class Drip { public float X, Length, Limit, Speed; }
        readonly List<BloodDrop> blood = new List<BloodDrop>();
        readonly List<BloodSplash> splashes = new List<BloodSplash>();
        readonly List<Ripple> ripples = new List<Ripple>();
        Drip[] drips;
        float poolLevel = 0.12f;

        static readonly Color BloodDeep = Color.FromArgb(62, 0, 6), BloodMid = Color.FromArgb(150, 8, 18), BloodBright = Color.FromArgb(206, 24, 32);

        // How high the pool stands at x (0..1 of the height): its level, the spectrum, and the ripples.
        float PoolHeight(float x)
        {
            // Blood is thick, so the spectrum moves it in broad, smooth swells (each bar averaged with its neighbors).
            float f = Math.Max(0, Math.Min(1, x)) * (BarCount - 1);
            int i = (int)f;
            Func<int, float> wide = k =>
            {
                float sum = 0;
                for (int j = -3; j <= 3; j++) sum += bars[Math.Max(0, Math.Min(BarCount - 1, k + j))];
                return sum / 7;
            };
            float bar = wide(i) + (wide(Math.Min(BarCount - 1, i + 1)) - wide(i)) * (f - i);
            float h = poolLevel + 0.11f * bar;
            foreach (var r in ripples)
            {
                float dx = x - r.X;
                h += (1 - r.Age) * r.Strength * 0.018f * (float)(Math.Cos(dx * 45 - r.Age * 12) * Math.Exp(-Math.Abs(dx) * 16));
            }
            return h;
        }

        void UpdateBlood()
        {
            if (drips == null)
            {
                drips = new Drip[14];
                for (int i = 0; i < drips.Length; i++)
                    drips[i] = new Drip { X = (i + 0.2f + R() * 0.6f) / drips.Length, Length = R() * 0.2f, Limit = 0.15f + R() * 0.25f, Speed = 0.0006f + R() * 0.0012f };
            }
            poolLevel += ((0.1f + 0.12f * smooth) - poolLevel) * 0.05f;

            // New drops: a slow drip-drip when it's quiet, rain with the music, a downpour on beats.
            int count = level <= 0.02f ? (R() < 0.04f ? 1 : 0) : (int)(level * 2.5f + scopeHit * 12 + R() * (0.3f + level));
            for (int i = 0; i < count && blood.Count < 260; i++)
                blood.Add(new BloodDrop { X = R(), Y = -0.05f * R(), VY = (0.008f + R() * 0.012f) * (0.7f + level), Size = 0.4f + R() * 0.6f + (scopeHit > 0.4f ? R() * 0.8f : 0) });

            // The drips swell (quickly on beats) and let go of a big drop when they're too heavy.
            foreach (var d in drips)
            {
                d.Length += d.Speed * (0.3f + level * 2.5f) + (scopeHit > 0.5f ? d.Limit * 0.08f : 0);
                if (d.Length < d.Limit) continue;
                if (blood.Count < 260) blood.Add(new BloodDrop { X = d.X, Y = d.Length, VY = 0.004f, Size = 1.3f });
                d.Length = 0.02f;
                d.Limit = 0.15f + R() * 0.25f;
            }

            // Falling, and landing in the pool with a splash and a ripple. A drop doesn't vanish when it lands: it
            // carries on down into the pool, slowing as it pushes into the thick blood, and the pool (drawn over it)
            // closes over it as it goes under.
            for (int i = blood.Count - 1; i >= 0; i--)
            {
                var d = blood[i];
                if (d.Landed)
                {
                    d.VY = Math.Max(0.0015f, d.VY * 0.9f);
                    d.Y += d.VY;
                    if (d.Y > d.Under) blood.RemoveAt(i);
                    continue;
                }
                d.VY += 0.0011f;
                d.Y += d.VY;
                float surface = 1 - PoolHeight(d.X);
                if (d.Y < surface) continue;
                // It meets the surface here (not wherever this frame's step overshot to), and the thick blood takes
                // most of its speed at once, so it's seen pushing in over several frames rather than gone in one.
                d.Landed = true;
                d.Y = surface;
                d.VY = Math.Min(d.VY, 0.01f);
                d.Under = surface + 0.05f + 0.03f * d.Size;
                // A few small, heavy droplets thrown up a little way (blood is too thick to spray far).
                int n = (int)(d.Size * 2.2f + R());
                for (int k = 0; k < n && splashes.Count < 120; k++)
                    splashes.Add(new BloodSplash { X = d.X, Y = surface, VX = (R() - 0.5f) * 0.007f, VY = -(0.004f + R() * 0.008f) * d.Size, Life = 1, Size = 0.25f + R() * 0.4f });
                if (ripples.Count < 24) ripples.Add(new Ripple { X = d.X, Strength = Math.Min(1, 0.4f * d.Size) });
            }
            for (int i = splashes.Count - 1; i >= 0; i--)
            {
                var s = splashes[i];
                s.X += s.VX;
                s.Y += s.VY;
                s.VY += 0.0014f;
                s.Life -= 0.06f;
                if (s.Life <= 0 || (s.VY > 0 && s.Y > 1 - PoolHeight(s.X))) splashes.RemoveAt(i);
            }
            foreach (var r in ripples) r.Age += 0.02f;
            ripples.RemoveAll(r => r.Age >= 1);
        }

        // A drop of blood: a teardrop pointing up (its round part centered on the origin, radius r, a tail above),
        // dark crimson, lighter inside where light comes through, with a sharp highlight and a faint red rim light.
        // A falling drop of blood, as real drops look: not a teardrop but a liquid ball, the bigger ones flattened a
        // little underneath by the air (flat is 0..1). Centered on the origin, radius r. Dark round the edge, glowing
        // red low down where the light comes through it, a sharp highlight up top, and when it's moving fast a soft
        // vertical blur above it (blur is how far, in pixels).
        static void BloodSphere(Graphics g, float r, float flat, float blur)
        {
            float w = r * (1 + 0.12f * flat), h = r * (1 - 0.14f * flat);   // half width and half height
            if (blur > r * 0.5f)
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(-w * 0.85f, -h - blur, w * 1.7f, blur + h * 1.4f);
                    using (var b = new LinearGradientBrush(new PointF(0, -h - blur - 1), new PointF(0, h * 0.4f + 1), Color.FromArgb(0, 90, 0, 8), Color.FromArgb(90, 90, 0, 8)))
                        g.FillPath(b, path);
                }
            using (var path = new GraphicsPath())
            {
                // Flatter on the bottom than the top.
                path.AddBezier(-w, h * 0.1f, -w, -h * 0.75f, -w * 0.55f, -h, 0, -h);
                path.AddBezier(0, -h, w * 0.55f, -h, w, -h * 0.75f, w, h * 0.1f);
                path.AddBezier(w, h * 0.1f, w, h * 0.7f, w * 0.6f, h, 0, h);
                path.AddBezier(0, h, -w * 0.6f, h, -w, h * 0.7f, -w, h * 0.1f);
                path.CloseFigure();
                using (var pg = new PathGradientBrush(path) { CenterPoint = new PointF(w * 0.08f, h * 0.38f), CenterColor = Color.FromArgb(172, 16, 22), SurroundColors = new[] { Color.FromArgb(40, 0, 4) } })
                {
                    pg.FocusScales = new PointF(0.25f, 0.2f);
                    g.FillPath(pg, path);
                }
            }
            if (r < 1.1f) return;
            // The light coming through: a bright red crescent along the bottom.
            using (var p = new Pen(Color.FromArgb(110, 235, 46, 46), Math.Max(0.7f, r * 0.16f))) g.DrawArc(p, -w * 0.72f, -h * 0.72f, w * 1.44f, h * 1.44f, 25, 130);
            // The reflection of the light: a soft sheen and a sharp, small highlight up and to the left.
            using (var b = new SolidBrush(Color.FromArgb(55, 255, 220, 220))) g.FillEllipse(b, -w * 0.62f, -h * 0.74f, w * 0.7f, h * 0.55f);
            using (var b = new SolidBrush(Color.FromArgb(235, 255, 240, 240))) g.FillEllipse(b, -w * 0.46f, -h * 0.58f, w * 0.26f, h * 0.22f);
        }

        void DrawBlood(Graphics g, RectangleF viz, float u)
        {
            // A dim, murky red glow rising from the pool, a little stronger when it's loud.
            float glowH = viz.Height * (0.4f + 0.3f * smooth);
            using (var b = new LinearGradientBrush(new RectangleF(viz.X, viz.Bottom - glowH - 1, viz.Width, glowH + 2), Color.FromArgb(0, 60, 0, 4), Color.FromArgb((int)(35 + 60 * smooth), 70, 0, 6), 90f))
                g.FillRectangle(b, viz.X, viz.Bottom - glowH, viz.Width, glowH);

            // The wet film along the top: its wavy lower edge (the film itself is drawn over the drips, below).
            const int edgePoints = 40;
            Func<float, float> edgeAt = fx => viz.Y + viz.Height * (0.024f + 0.012f * (float)Math.Sin(fx * 31 + 1.3) + 0.008f * (float)Math.Sin(fx * 73));
            var edge = new PointF[edgePoints];
            for (int k = 0; k < edgePoints; k++)
            {
                float fx = k / (edgePoints - 1f);
                edge[k] = new PointF(viz.X + fx * viz.Width, edgeAt(fx));
            }

            // The drips: a thick film thinning to a narrow neck, then a heavy glossy bead that grows until it lets go.
            // Each starts a little way up under the film, so it runs out from beneath its edge.
            foreach (var d in drips ?? new Drip[0])
            {
                float x = viz.X + d.X * viz.Width, top = edgeAt(d.X) - u * 4;
                float len = Math.Max(u * 3, d.Length * viz.Height), w = u * 4.5f, bead = u * (2 + 2.6f * Math.Min(1, d.Length / d.Limit)), neck = Math.Max(u * 0.6f, bead * 0.28f);
                // One shape from the film down: thinning to a neck, then swelling smoothly into the heavy end that will
                // let go, so the stream and its end are one piece of liquid rather than a drop stuck on a line.
                float cy = top + len - bead * 0.2f;                                   // the swelling's middle
                float neckY = Math.Max(top + (cy - top) * 0.35f, cy - bead * 2.2f);   // where the stream is thinnest
                float swell = (cy - neckY) * 0.6f;
                using (var path = new GraphicsPath())
                {
                    path.AddBezier(x - w, top, x - w * 0.25f, top + (neckY - top) * 0.3f, x - neck, top + (neckY - top) * 0.6f, x - neck, neckY);
                    path.AddBezier(x - neck, neckY, x - neck, neckY + swell, x - bead, cy - bead * 0.8f, x - bead, cy);
                    path.AddArc(x - bead, cy - bead, bead * 2, bead * 2, 180, -180);
                    path.AddBezier(x + bead, cy, x + bead, cy - bead * 0.8f, x + neck, neckY + swell, x + neck, neckY);
                    path.AddBezier(x + neck, neckY, x + neck, top + (neckY - top) * 0.6f, x + w * 0.25f, top + (neckY - top) * 0.3f, x + w, top);
                    path.CloseFigure();
                    // The same glossy shading all the way down, lit from the left.
                    using (var b = new LinearGradientBrush(new RectangleF(x - bead - 1, top, bead * 2 + 2, cy + bead - top + 1), Color.FromArgb(40, 0, 4), Color.FromArgb(40, 0, 4), 0f))
                    {
                        b.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(40, 0, 4), Color.FromArgb(130, 8, 14), Color.FromArgb(70, 2, 8), Color.FromArgb(36, 0, 4) }, Positions = new[] { 0f, 0.42f, 0.6f, 1f } };
                        g.FillPath(b, path);
                    }
                }
                // One highlight running down the stream and on round the swelling, and a soft glint low on it.
                using (var p = new Pen(Color.FromArgb(120, 255, 170, 170), Math.Max(0.7f, u * 0.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var line = new GraphicsPath())
                {
                    line.AddBezier(x - neck * 0.4f, top + (neckY - top) * 0.3f, x - neck * 0.4f, neckY + swell * 0.5f, x - bead * 0.5f, cy - bead * 0.9f, x - bead * 0.55f, cy - bead * 0.15f);
                    g.DrawPath(p, line);
                }
                if (bead >= 1.5f)
                    using (var b = new SolidBrush(Color.FromArgb(150, 255, 225, 225))) g.FillEllipse(b, x - bead * 0.5f, cy - bead * 0.1f, bead * 0.26f, bead * 0.3f);
            }

            // The film over the tops of the drips: thick, dark and glossy, catching the light along its lower edge.
            using (var band = new GraphicsPath())
            {
                band.AddLine(viz.X, viz.Y - 1, viz.Right, viz.Y - 1);
                band.AddCurve(edge.Reverse().ToArray(), 0.4f);
                band.CloseFigure();
                using (var b = new LinearGradientBrush(new RectangleF(viz.X, viz.Y - 2, viz.Width, viz.Height * 0.06f + 4), Color.FromArgb(30, 0, 3), Color.FromArgb(110, 4, 10), 90f)) g.FillPath(b, band);
            }
            using (var p = new Pen(Color.FromArgb(110, 255, 150, 150), Math.Max(0.8f, u * 0.6f)))
                g.DrawCurve(p, edge.Select(e => new PointF(e.X, e.Y - u * 1.2f)).ToArray(), 0.4f);

            // The falling drops: liquid balls (the big ones flattened a little), blurred by their speed.
            foreach (var d in blood)
            {
                float x = viz.X + d.X * viz.Width, y = viz.Y + d.Y * viz.Height, r = u * (0.9f + 1.4f * d.Size);
                float speed = d.VY * viz.Height;
                var state = g.Save();
                g.TranslateTransform(x, y);
                BloodSphere(g, r, Math.Min(1, Math.Max(0, d.Size - 0.5f)), speed * 1.8f);
                g.Restore(state);
            }

            // The pool: dark and glossy, its surface the spectrum, rippling where the drops land.
            const int surfacePoints = 96;
            var surface = new PointF[surfacePoints + 1];
            float highest = viz.Bottom;
            for (int k = 0; k <= surfacePoints; k++)
            {
                float fx = k / (float)surfacePoints;
                surface[k] = new PointF(viz.X + fx * viz.Width, viz.Bottom - PoolHeight(fx) * viz.Height);
                highest = Math.Min(highest, surface[k].Y);
            }
            using (var pool = new GraphicsPath())
            {
                pool.AddCurve(surface, 0.4f);
                pool.AddLine(viz.Right, viz.Bottom + 1, viz.X, viz.Bottom + 1);
                pool.CloseFigure();
                using (var b = new LinearGradientBrush(new RectangleF(viz.X, highest - 1, viz.Width, viz.Bottom - highest + 3), Color.FromArgb(104, 4, 10), Color.FromArgb(22, 0, 3), 90f))
                {
                    b.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(118, 6, 12), Color.FromArgb(70, 1, 7), Color.FromArgb(22, 0, 3) }, Positions = new[] { 0f, 0.35f, 1f } };
                    g.FillPath(b, pool);
                }
                // Reflections drifting on the surface: soft, wide highlights just under the edge.
                var clip = g.Clip;
                g.SetClip(pool, CombineMode.Intersect);
                for (int k = 0; k < 6; k++)
                {
                    float fx = (k * 0.173f + tick * 0.0007f * (k % 2 == 0 ? 1 : -1)) % 1;
                    if (fx < 0) fx += 1;
                    float sx = viz.X + fx * viz.Width, sy = viz.Bottom - PoolHeight(fx) * viz.Height + u * 2.5f;
                    var rect = new RectangleF(sx - viz.Width * 0.05f, sy, viz.Width * 0.1f, Math.Max(2, u * 2.4f));
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(rect);
                        using (var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb(70, 255, 200, 200), SurroundColors = new[] { Color.FromArgb(0, 255, 200, 200) } })
                            g.FillPath(pg, path);
                    }
                }
                // Drops that have gone in: a darker shadow just under the surface where each one is sinking,
                // fading as it goes deeper.
                foreach (var d in blood)
                {
                    if (!d.Landed) continue;
                    float top = viz.Bottom - PoolHeight(d.X) * viz.Height, y = viz.Y + d.Y * viz.Height, bottom = viz.Y + d.Under * viz.Height;
                    float fade = 1 - Math.Min(1, Math.Max(0, (y - top) / Math.Max(1, bottom - top)));
                    float r = u * (0.9f + 1.4f * d.Size) * 1.3f;
                    if (fade <= 0) continue;
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(viz.X + d.X * viz.Width - r, y - r * 0.8f, 2 * r, 1.6f * r);
                        using (var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(150 * fade), 18, 0, 3), SurroundColors = new[] { Color.FromArgb(0, 18, 0, 3) } })
                            g.FillPath(pg, path);
                    }
                }
                g.Clip = clip;
            }
            // The surface: a darker line where it meets the air, and a bright sheen just above it.
            using (var p = new Pen(Color.FromArgb(200, 30, 0, 4), Math.Max(1, u * 1.2f))) g.DrawCurve(p, surface, 0.4f);
            using (var p = new Pen(Color.FromArgb(130, 255, 170, 170), Math.Max(0.7f, u * 0.55f)))
                g.DrawCurve(p, surface.Select(s => new PointF(s.X, s.Y + u * 1.1f)).ToArray(), 0.4f);

            // Splashes: droplets thrown up from the pool, stretched along the way they're flying.
            foreach (var s in splashes)
            {
                float x = viz.X + s.X * viz.Width, y = viz.Y + s.Y * viz.Height, r = u * (0.6f + 1.0f * s.Size) * (0.5f + 0.5f * Math.Max(0, s.Life));
                if (r < 0.4f) continue;
                var state = g.Save();
                g.TranslateTransform(x, y);
                g.RotateTransform((float)(Math.Atan2(s.VY * viz.Height, s.VX * viz.Width) * 180 / Math.PI) - 90);   // the blur trails behind
                BloodSphere(g, r, 0, (float)Math.Sqrt(s.VX * viz.Width * s.VX * viz.Width + s.VY * viz.Height * s.VY * viz.Height) * 1.5f);
                g.Restore(state);
            }
        }

        // ---- Earth: the planet from space, in the spirit of the old console music visualizers but made from
        // scratch here. A slowly turning globe (continents from a rough world map with noise-made coasts, deserts,
        // forests and ice by latitude, swirling cloud bands), the sun glinting off the sea, a blue atmosphere glowing at
        // the edge, city lights on the dark side and a field of stars. The music drives it: the glint and the
        // atmosphere brighten with the loudness, rays of light rise from the sunlit edge as a spectrum (low notes on
        // the left), rings of light leave the planet on beats, storms flash on the dark side, and it turns faster when
        // the music is loud. In a short, wide strip (the XP screen) it becomes a horizon seen from low orbit, the
        // surface rolling over the edge. Its colors are its own; the color options don't tint it.

        float earthSpin, cloudSpin;
        class Flash { public float X, Y, Life; }   // a storm's flash, on the planet's disc (-1..1 across and up)
        readonly List<Flash> flashes = new List<Flash>();
        EarthScene earth;

        sealed class EarthScene
        {
            const int TexW = 2048, TexH = 1024;
            readonly VizEngine v;

            public EarthScene(VizEngine v)
            {
                this.v = v;
                Maps.Join();
            }

            // It's no longer shown: its picture goes, and the maps too once no Earth is left.
            public void Dispose()
            {
                if (picture != null) picture.Dispose();
                picture = null;
                Maps.Leave();
            }

            // ---- The planet's maps (longitude across, latitude down): surface colors, clouds and city lights. They're
            // NASA's satellite maps of the real Earth (Blue Marble land and clouds, Black Marble city lights; built into
            // the program from the earth folder), or, if those aren't there, a planet generated here. They come in full,
            // half and quarter size; each size is made in the background the first time an Earth on screen needs it (a
            // small planet only needs the quarter size), shared by every Earth, and let go once no Earth is left. Until
            // it's ready the planet is plain blue.

            sealed class Level
            {
                public int W, H;
                public int[] Surface;               // RGB; the top byte is 255 for open water (it catches the sun), 0 for land and ice
                public byte[] Clouds, Lights;
            }

            static class Maps
            {
                public const int Sizes = 3;         // full size, then each half the size of the one before
                static readonly Level[] levels = new Level[Sizes];
                static readonly bool[] making = new bool[Sizes], failed = new bool[Sizes];
                static readonly object sync = new object();
                static int users;                   // Earths that exist right now

                public static void Join() { lock (sync) users++; }

                public static void Leave()
                {
                    lock (sync)
                    {
                        if (--users > 0) return;
                        for (int k = 0; k < Sizes; k++) levels[k] = null;
                    }
                    Ram.Trim();
                }

                // The maps at size k (0 full, 1 half, 2 quarter), or null while they're being made.
                public static Level Get(int k)
                {
                    lock (sync)
                    {
                        if (levels[k] != null || making[k] || failed[k]) return levels[k];
                        making[k] = true;
                    }
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Level made = null;
                        try { made = FromPhotos(k) ?? Generate(k); }
                        catch { }
                        lock (sync)
                        {
                            making[k] = false;
                            if (made == null) failed[k] = true;
                            else if (users > 0) levels[k] = made;
                        }
                        Ram.Trim();   // reading the pictures took a lot of memory for a moment
                    });
                    return null;
                }

                // NASA's maps at size k, from the program's built-in pictures (one picture at a time, read a row at a
                // time); null if they aren't built in.
                static Level FromPhotos(int k)
                {
                    int f = 1 << k, w = TexW >> k, h = TexH >> k, n = f * f;
                    var lv = new Level { W = w, H = h, Surface = new int[w * h], Clouds = new byte[w * h], Lights = new byte[w * h] };
                    // The surface. The map's sea is a flat dark navy with turquoise shallows; it becomes a brighter, deeper
                    // blue here (keeping the shallows) and is marked as water so the sun can glint off it. That's sorted
                    // out at full size, then each pixel of a smaller size is the average of the ones it covers (water
                    // where most of them are).
                    int[] sr = new int[w], sg = new int[w], sb = new int[w], wet = new int[w];
                    bool found = Rows("EarthLand.jpg", TexW, TexH, (y, px) =>
                    {
                        for (int x = 0; x < TexW; x++)
                        {
                            int p = px[x], r = (p >> 16) & 255, g = (p >> 8) & 255, b = p & 255, o = x >> k;
                            bool water = b > r + 15 && b > g + 8;
                            int c = water ? Mix(Rgb(10, 62, 152), Rgb(38, 150, 200), Clamp01((g - 25) / 110f)) : Shade(p & 0xFFFFFF, 1.08f);
                            sr[o] += (c >> 16) & 255;
                            sg[o] += (c >> 8) & 255;
                            sb[o] += c & 255;
                            if (water) wet[o]++;
                        }
                        if ((y & (f - 1)) != f - 1) return;
                        int row = (y >> k) * w;
                        for (int x = 0; x < w; x++)
                        {
                            lv.Surface[row + x] = (2 * wet[x] >= n ? unchecked((int)0xFF000000) : 0) | Rgb(sr[x] / n, sg[x] / n, sb[x] / n);
                            sr[x] = sg[x] = sb[x] = wet[x] = 0;
                        }
                    });
                    // Clouds and city lights: their brightness, stretched (read straight at this size).
                    found = found && Rows("EarthClouds.jpg", w, h, (y, px) =>
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int c = px[x];
                            lv.Clouds[y * w + x] = (byte)Math.Max(0, Math.Min(255, ((((c >> 16) & 255) + ((c >> 8) & 255) + (c & 255)) / 3 - 18) * 13 / 10));
                        }
                    });
                    found = found && Rows("EarthLights.jpg", w, h, (y, px) =>
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int c = px[x];
                            lv.Lights[y * w + x] = (byte)Math.Max(0, Math.Min(255, ((((c >> 16) & 255) + ((c >> 8) & 255) + (c & 255)) / 3 - 30) * 14 / 10));
                        }
                    });
                    return found ? lv : null;
                }

                // A built-in picture at the given size (resized smoothly if it isn't that size, wrapping round at the
                // 180th meridian so there's no seam), handed over a row at a time; false if it isn't built in.
                static bool Rows(string name, int w, int h, Action<int, int[]> row)
                {
                    var s = typeof(EarthScene).Assembly.GetManifestResourceStream(name);
                    if (s == null) return false;
                    using (s)
                    using (var img = Image.FromStream(s))
                    {
                        var src = img as Bitmap;
                        Bitmap sized = null;
                        try
                        {
                            if (src == null || img.Width != w || img.Height != h)
                            {
                                src = sized = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                                using (var g = Graphics.FromImage(sized))
                                using (var attrs = new ImageAttributes())
                                {
                                    attrs.SetWrapMode(WrapMode.TileFlipXY);
                                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                    g.PixelOffsetMode = PixelOffsetMode.Half;
                                    g.DrawImage(img, new Rectangle(0, 0, w, h), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attrs);
                                }
                            }
                            var px = new int[w];
                            for (int y = 0; y < h; y++)
                            {
                                var data = src.LockBits(new Rectangle(0, y, w, 1), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                                try { Marshal.Copy(data.Scan0, px, 0, w); }
                                finally { src.UnlockBits(data); }
                                row(y, px);
                            }
                        }
                        finally { if (sized != null) sized.Dispose(); }
                    }
                    return true;
                }

                // Where the land is, roughly: for each 5° band of latitude from the North Pole down, the longitudes
                // (west to east, in degrees) that are land. Build adds the detail along the coasts.
                static readonly int[][] Land =
                {
                    new int[0],                                                                          // 85-90 N
                    new[] { -90, -20, 45, 65, 90, 105 },                                                 // 80-85 N
                    new[] { -125, -18, 10, 30, 50, 68, 90, 115, 135, 150 },                              // 75-80 N
                    new[] { -125, -68, -58, -20, 52, 62, 68, 82, 85, 135, 138, 150 },                    // 70-75 N
                    new[] { -165, -62, -54, -32, 12, 180, -180, -170 },                                  // 65-70 N
                    new[] { -166, -94, -78, -60, -50, -40, -24, -13, 5, 17, 22, 180, -180, -172 },       // 60-65 N
                    new[] { -162, -152, -136, -94, -77, -61, -6, -2, 6, 18, 22, 138, 156, 163 },         // 55-60 N
                    new[] { -132, -56, -10, -6, -5, 1, 4, 141, 142, 144, 156, 160 },                     // 50-55 N
                    new[] { -125, -66, -59, -53, -4, 140, 142, 144 },                                    // 45-50 N
                    new[] { -124, -70, -66, -61, -9, 3, 7, 13, 15, 28, 40, 47, 54, 130, 140, 146 },      // 40-45 N
                    new[] { -123, -76, -9, -1, 8, 10, 12, 17, 20, 23, 26, 122, 126, 129, 136, 141 },     // 35-40 N
                    new[] { -117, -80, -10, 23, 34, 122, 129, 140 },                                     // 30-35 N
                    new[] { -115, -112, -110, -97, -83, -80, -15, 49, 55, 122 },                         // 25-30 N
                    new[] { -112, -109, -106, -97, -85, -74, -17, 37, 39, 60, 68, 117, 120, 122 },       // 20-25 N
                    new[] { -105, -87, -74, -68, -17, 40, 42, 55, 73, 85, 94, 108, 119, 123 },           // 15-20 N
                    new[] { -92, -83, -75, -62, -17, 43, 74, 80, 98, 109, 120, 126 },                    // 10-15 N
                    new[] { -84, -58, -13, 50, 76, 82, 98, 103, 105, 107, 116, 118, 122, 126 },          // 5-10 N
                    new[] { -80, -50, 9, 46, 95, 104, 109, 119, 120, 125, 127, 129 },                    // 0-5 N
                    new[] { -81, -36, 9, 41, 100, 106, 110, 117, 119, 124, 131, 151 },                   // 0-5 S
                    new[] { -80, -35, 12, 40, 105, 125, 137, 150 },                                      // 5-10 S
                    new[] { -77, -38, 13, 41, 44, 51, 126, 136, 141, 144 },                              // 10-15 S
                    new[] { -71, -39, 12, 38, 44, 50, 122, 146 },                                        // 15-20 S
                    new[] { -70, -41, 14, 36, 43, 48, 114, 151 },                                        // 20-25 S
                    new[] { -71, -48, 15, 33, 113, 153 },                                                // 25-30 S
                    new[] { -72, -53, 18, 29, 115, 124, 134, 152 },                                      // 30-35 S
                    new[] { -73, -57, 140, 150, 173, 179 },                                              // 35-40 S
                    new[] { -74, -64, 145, 149, 167, 174 },                                              // 40-45 S
                    new[] { -75, -66, 167, 169 },                                                        // 45-50 S
                    new[] { -75, -68, -61, -58 },                                                        // 50-55 S
                    new int[0],                                                                          // 55-60 S
                    new[] { -63, -56 },                                                                  // 60-65 S
                    new[] { -68, -60, 10, 165 },                                                         // 65-70 S
                    new[] { -100, -62, -15, 165 },                                                       // 70-75 S
                    new[] { -180, 180 },                                                                 // 75-80 S
                    new[] { -180, 180 },                                                                 // 80-85 S
                    new[] { -180, 180 },                                                                 // 85-90 S
                };

                // The generated planet's maps at size `size` (0 full, 1 half, 2 quarter).
                static Level Generate(int size)
                {
                    // The rough map as a grid of 5° cells (1 = land), read smoothly in between.
                    var grid = new float[36, 72];
                    for (int r = 0; r < 36; r++)
                        for (int c = 0; c < 72; c++)
                        {
                            float lon = -177.5f + 5 * c;
                            for (int k = 0; k + 1 < Land[r].Length; k += 2)
                                if (lon >= Land[r][k] && lon <= Land[r][k + 1]) grid[r, c] = 1;
                        }

                    int texW = TexW >> size, texH = TexH >> size;
                    var surface = new int[texW * texH];
                    var clouds = new byte[texW * texH];
                    var lights = new byte[texW * texH];
                    for (int y = 0; y < texH; y++)
                    {
                        float latDeg = 90 - (y + 0.5f) * 180f / texH, alat = Math.Abs(latDeg);
                        double lat = latDeg * Math.PI / 180;
                        float cosLat = (float)Math.Cos(lat), sinLat = (float)Math.Sin(lat);
                        for (int x = 0; x < texW; x++)
                        {
                            float lonDeg = -180 + (x + 0.5f) * 360f / texW;
                            double lon = lonDeg * Math.PI / 180;
                            // The point on the globe, so the noise has no seams or pinches at the poles.
                            float px = cosLat * (float)Math.Sin(lon), py = sinLat, pz = cosLat * (float)Math.Cos(lon);
                            int i = y * texW + x;

                            // The noise only roughens the coasts; well inland or far out at sea it can't make lakes or islands.
                            float coast = Fbm(px * 3.2f, py * 3.2f, pz * 3.2f, 5, 1);
                            float rough = Sample(grid, latDeg, lonDeg);
                            float height = rough - 0.5f + coast * 0.45f * (1 - 0.7f * Math.Abs(2 * rough - 1));
                            float detail = Fbm(px * 9, py * 9, pz * 9, 4, 2);
                            bool water = height <= 0, frozen = false;
                            float desert = 0;
                            int color;
                            if (water)
                            {
                                // Turquoise shallows along the coasts, deep blue further out; pack ice near the poles.
                                frozen = latDeg > 79 + detail * 6 || latDeg < -68 + detail * 4;
                                color = frozen ? Rgb(222, 236, 246) : Mix(Rgb(30, 140, 210), Rgb(10, 66, 158), Clamp01(-height * 4 + 0.1f));
                            }
                            else color = LandColor(latDeg, lonDeg, px, py, pz, detail, out desert, out frozen);
                            surface[i] = (water && !frozen ? unchecked((int)0xFF000000) : 0) | (color & 0xFFFFFF);

                            // Clouds: swirled noise, thicker along the storm tracks and the equator, thinner over the
                            // dry subtropics, with fine wisps.
                            float wx = Fbm(px * 1.6f + 3.1f, py * 1.6f, pz * 1.6f, 4, 11);
                            float wy = Fbm(px * 1.6f, py * 1.6f + 5.7f, pz * 1.6f, 4, 12);
                            float wz = Fbm(px * 1.6f, py * 1.6f, pz * 1.6f + 9.2f, 4, 13);
                            float qx = px + wx * 0.9f, qy = py + wy * 0.9f, qz = pz + wz * 0.9f;
                            float cloud = Fbm(qx * 2.8f, qy * 2.8f, qz * 2.8f, 6, 14)
                                        + 0.16f * Bell(alat, 55, 12) + 0.14f * Bell(latDeg, 5, 7) - 0.14f * Bell(alat, 25, 9);
                            float cover = Smooth(-0.12f, 0.22f, cloud) * (0.8f + 0.2f * Fbm(px * 14, py * 14, pz * 14, 2, 15));
                            clouds[i] = (byte)(255 * Clamp01(cover));

                            // City lights: clustered specks on land, most in the busy northern mid-latitudes, none on
                            // ice or in the big deserts.
                            if (!water && !frozen && desert < 0.6f && alat < 62)
                            {
                                float people = latDeg > 20 && latDeg < 60 ? 0.12f : latDeg > -40 && latDeg <= 20 ? 0 : -0.08f;
                                float city = Fbm(px * 22, py * 22, pz * 22, 3, 21) + people - 0.12f;
                                if (city > 0) lights[i] = (byte)Math.Min(255, city * 900 * (0.5f + 0.25f * (Hash(x << size, y << size, 0, 33) + 1)));
                            }
                        }
                    }
                    return new Level { W = texW, H = texH, Surface = surface, Clouds = clouds, Lights = lights };
                }

                static int LandColor(float latDeg, float lonDeg, float px, float py, float pz, float detail, out float desert, out bool frozen)
                {
                    float alat = Math.Abs(latDeg);
                    desert = 0;
                    // Ice: Antarctica, Greenland and the far north.
                    frozen = latDeg < -60 || (lonDeg > -56 && lonDeg < -18 && latDeg > 59) || latDeg > 71 + detail * 6;
                    if (frozen) return Shade(Rgb(236, 242, 250), 0.92f + detail * 0.12f);
                    // Plants by latitude: rainforest, grassland and woods, the dark northern forests, then tundra.
                    int c = Mix(Rgb(26, 94, 42), Rgb(78, 116, 56), Smooth(10, 24, alat));
                    c = Mix(c, Rgb(36, 74, 50), Smooth(46, 56, alat));
                    c = Mix(c, Rgb(116, 116, 92), Smooth(60, 67, alat));
                    // Deserts: the dry belts, strongest where the real ones are.
                    float dry = 0.25f * Smooth(10, 18, alat) * (1 - Smooth(34, 42, alat)) + Fbm(px * 2.5f + 7, py * 2.5f, pz * 2.5f, 3, 9) * 0.4f;
                    if (In(latDeg, lonDeg, 14, 34, -17, 75)) dry += 0.55f;      // Sahara, Arabia, Persia, Thar
                    if (In(latDeg, lonDeg, -32, -18, 117, 145)) dry += 0.5f;    // Australia
                    if (In(latDeg, lonDeg, -28, -17, 11, 26)) dry += 0.35f;     // Namib and Kalahari
                    if (In(latDeg, lonDeg, 36, 47, 75, 112)) dry += 0.45f;      // Taklamakan and Gobi
                    if (In(latDeg, lonDeg, 24, 38, -118, -103)) dry += 0.35f;   // the American Southwest
                    if (In(latDeg, lonDeg, -27, -15, -72, -67)) dry += 0.4f;    // Atacama
                    desert = Clamp01((dry - 0.45f) * 3);
                    c = Mix(c, Mix(Rgb(206, 180, 136), Rgb(176, 142, 102), Clamp01(detail + 0.5f)), desert);
                    return Shade(c, 0.82f + detail * 0.35f);
                }

                static float Sample(float[,] grid, float latDeg, float lonDeg)
                {
                    float fr = (87.5f - latDeg) / 5, fc = (lonDeg + 177.5f) / 5;
                    int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
                    float tr = fr - r0, tc = fc - c0;
                    Func<int, int, float> cell = (r, c) => grid[Math.Max(0, Math.Min(35, r)), ((c % 72) + 72) % 72];
                    return Lerp(Lerp(cell(r0, c0), cell(r0, c0 + 1), tc), Lerp(cell(r0 + 1, c0), cell(r0 + 1, c0 + 1), tc), tr);
                }

                static bool In(float lat, float lon, float lat0, float lat1, float lon0, float lon1) { return lat >= lat0 && lat <= lat1 && lon >= lon0 && lon <= lon1; }

                static float Bell(float x, float middle, float width) { float d = (x - middle) / width; return (float)Math.Exp(-d * d); }

                // A random value from -1 to 1 for a whole-number point.
                public static float Hash(int x, int y, int z, int seed)
                {
                    unchecked
                    {
                        int h = x * 374761393 + y * 668265263 + z * 1274126177 + seed * 1442695041;
                        h = (h ^ (h >> 13)) * 1274126177;
                        h ^= h >> 16;
                        return (h & 0xFFFF) / 32767.5f - 1;
                    }
                }

                // Smooth noise in 3D: random values at whole-number points, blended in between.
                static float Noise(float x, float y, float z, int seed)
                {
                    int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y), iz = (int)Math.Floor(z);
                    float fx = x - ix, fy = y - iy, fz = z - iz;
                    float ux = fx * fx * (3 - 2 * fx), uy = fy * fy * (3 - 2 * fy), uz = fz * fz * (3 - 2 * fz);
                    float a = Lerp(Hash(ix, iy, iz, seed), Hash(ix + 1, iy, iz, seed), ux);
                    float b = Lerp(Hash(ix, iy + 1, iz, seed), Hash(ix + 1, iy + 1, iz, seed), ux);
                    float c = Lerp(Hash(ix, iy, iz + 1, seed), Hash(ix + 1, iy, iz + 1, seed), ux);
                    float d = Lerp(Hash(ix, iy + 1, iz + 1, seed), Hash(ix + 1, iy + 1, iz + 1, seed), ux);
                    return Lerp(Lerp(a, b, uy), Lerp(c, d, uy), uz);
                }

                // Layers of noise, each twice as fine and half as strong: natural-looking detail, about -0.7 to 0.7.
                static float Fbm(float x, float y, float z, int octaves, int seed)
                {
                    float sum = 0, amp = 0.5f, norm = 0;
                    for (int o = 0; o < octaves; o++)
                    {
                        sum += Noise(x, y, z, seed + o * 101) * amp;
                        norm += amp;
                        amp *= 0.5f;
                        x *= 2.03f;
                        y *= 2.03f;
                        z *= 2.03f;
                    }
                    return sum / norm;
                }
            }

            static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
            static float Clamp01(float x) { return x < 0 ? 0 : x > 1 ? 1 : x; }
            static float Smooth(float e0, float e1, float x) { float t = Clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
            static int Rgb(int r, int g, int b) { return (r << 16) | (g << 8) | b; }

            static int Mix(int a, int b, float t)
            {
                t = Clamp01(t);
                return Rgb((int)Lerp((a >> 16) & 255, (b >> 16) & 255, t), (int)Lerp((a >> 8) & 255, (b >> 8) & 255, t), (int)Lerp(a & 255, b & 255, t));
            }

            static int Shade(int c, float k)
            {
                return Rgb(Math.Min(255, (int)(((c >> 16) & 255) * k)), Math.Min(255, (int)(((c >> 8) & 255) * k)), Math.Min(255, (int)((c & 255) * k)));
            }

            static int Pack(int r, int g, int b)
            {
                r = r < 0 ? 0 : r > 255 ? 255 : r;
                g = g < 0 ? 0 : g > 255 ? 255 : g;
                b = b < 0 ? 0 : b > 255 ? 255 : b;
                return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
            }

            static float[] Unit(float x, float y, float z) { float l = (float)Math.Sqrt(x * x + y * y + z * z); return new[] { x / l, y / l, z / l }; }

            // ---- Per size: which spot on the planet each pixel of the picture shows, and how it's lit. The sun stays
            // put relative to the view, so this is worked out once; each frame just turns the maps underneath.

            int width = -1, height = -1;          // the screen size it was laid out for
            int bw, bh;                            // the picture's size (smaller than the screen when that's big)
            bool horizon;
            float cx, cy, radius;                  // the planet, in picture pixels
            float[] sun, half;                     // toward the sun, and halfway between it and the viewer (where it glints)
            Level level;                           // the map size this layout reads (null until the maps are ready)
            int wanted;                            // which map size that is
            int count;                             // pixels in the picture (the tables below can be longer)
            int[] lonAt;                           // map column x 256, before turning
            short[] rowAt;                         // map row (the upper of the two blended); -1 for space
            byte[] rowMix;                         // how far toward the next row down, 0..255
            byte[] dayAt, specAt, glintAt, rimAt, coverAt, haloAt;
            int[] starAt;
            byte[] starLevel;
            float[] starPhase;
            int[] pixels;
            Bitmap picture;

            void Layout(int w, int h)
            {
                width = w;
                height = h;
                horizon = w > h * 4;
                float scale = Math.Min(1, (float)Math.Sqrt(150000.0 / ((double)w * h)));   // at most about 150,000 pixels a frame
                bw = Math.Max(8, (int)(w * scale));
                bh = Math.Max(8, (int)(h * scale));
                if (horizon)
                {
                    // Seen from low orbit: a huge planet whose edge arcs gently across the strip.
                    radius = bw * 1.6f;
                    cx = bw / 2f;
                    cy = bh * 0.30f + radius;
                }
                else
                {
                    // The whole top of the globe, its lower part running off the bottom into the night.
                    radius = Math.Min(bh * 0.64f, bw * 0.46f);
                    cx = bw / 2f;
                    cy = bh * 0.70f;
                }
                // The planet's tilt in view: north tipped toward you; or, for the horizon, its axis lying across so
                // the turning carries the surface over the edge.
                float roll = horizon ? -(float)Math.PI / 2 : 0.32f, pitch = horizon ? 0 : 0.40f;
                float cr = (float)Math.Cos(-roll), sr = (float)Math.Sin(-roll), cp = (float)Math.Cos(-pitch), sp = (float)Math.Sin(-pitch);
                sun = horizon ? Unit(-0.15f, 0.55f, 0.82f) : Unit(-0.10f, 0.85f, 0.52f);
                half = Unit(sun[0], sun[1], sun[2] + 1);
                float flat = (float)Math.Sqrt(sun[0] * sun[0] + sun[1] * sun[1]), lx = sun[0] / flat, ly = sun[1] / flat;
                float glowWidth = Math.Min(radius * 0.045f, horizon ? bh * 0.12f : radius) + 1;

                // The finest map that isn't much finer than the picture (finer would only shimmer as it turns).
                float perPixel = TexW / 2f / (2 * radius);   // map columns per picture pixel across the planet's face
                wanted = 0;
                while (wanted < Maps.Sizes - 1 && perPixel > 1.6f) { wanted++; perPixel /= 2; }
                level = Maps.Get(wanted);
                int texW = level != null ? level.W : TexW, texH = level != null ? level.H : TexH;

                // The per-pixel tables are kept between sizes (the picture is never much over 150,000 pixels), so
                // resizing the window doesn't keep making new ones.
                int n = count = bw * bh;
                if (pixels == null || pixels.Length < n)
                {
                    lonAt = new int[n];
                    rowAt = new short[n];
                    rowMix = new byte[n];
                    dayAt = new byte[n];
                    specAt = new byte[n];
                    glintAt = new byte[n];
                    rimAt = new byte[n];
                    coverAt = new byte[n];
                    haloAt = new byte[n];
                    pixels = new int[n];
                }
                if (picture != null) picture.Dispose();
                picture = new Bitmap(bw, bh, PixelFormat.Format32bppArgb);
                var stars = new List<int>();
                var rnd = new Random(11);
                for (int y = 0; y < bh; y++)
                    for (int x = 0; x < bw; x++)
                    {
                        int i = y * bw + x;
                        float sx = (x + 0.5f - cx) / radius, sy = (cy - y - 0.5f) / radius;
                        float d = (float)Math.Sqrt(sx * sx + sy * sy);
                        float inside = (1 - d) * radius + 0.5f;   // pixels inside the planet's edge
                        // The atmosphere's glow just past the edge, brightest on the sunlit side.
                        float side = d > 0 ? (sx * lx + sy * ly) / d : 0;
                        float glow = (float)Math.Exp(-Math.Max(0, d - 1) * radius / glowWidth) * (0.1f + 0.9f * Math.Max(0, side));
                        haloAt[i] = (byte)(255 * Math.Min(1, glow));
                        if (inside <= 0)
                        {
                            rowAt[i] = -1;
                            if (rnd.NextDouble() < 1 / 700.0) stars.Add(i);
                            continue;
                        }
                        coverAt[i] = (byte)(255 * Math.Min(1, inside));
                        if (d > 1) { sx /= d; sy /= d; }
                        float z = (float)Math.Sqrt(Math.Max(0, 1 - sx * sx - sy * sy));
                        // Into the planet's own frame (undo the view's roll, then its pitch) to find the map position.
                        float x1 = sx * cr - sy * sr, y1 = sx * sr + sy * cr;
                        float y2 = y1 * cp - z * sp, z2 = y1 * sp + z * cp;
                        double lat = Math.Asin(Math.Max(-1, Math.Min(1, y2))), lon = Math.Atan2(x1, z2);
                        // The map position, measured from pixel centers so neighboring pixels blend smoothly.
                        double row = (Math.PI / 2 - lat) / Math.PI * texH - 0.5;
                        int row0 = Math.Max(0, Math.Min(texH - 2, (int)Math.Floor(row)));
                        rowAt[i] = (short)row0;
                        rowMix[i] = (byte)(255 * Math.Max(0, Math.Min(1, row - row0)));
                        lonAt[i] = (int)(((lon + Math.PI) / (2 * Math.PI) * texW - 0.5) * 256);
                        float lit = sx * sun[0] + sy * sun[1] + z * sun[2];
                        dayAt[i] = (byte)(255 * Smooth(-0.16f, 0.32f, lit));
                        float toward = Math.Max(0, sx * half[0] + sy * half[1] + z * half[2]);
                        specAt[i] = (byte)(255 * Math.Pow(toward, 110));
                        glintAt[i] = (byte)(255 * Math.Pow(toward, 9));
                        // The atmosphere thickens toward the edge (in the low-orbit view only right at the horizon).
                        rimAt[i] = (byte)(255 * Math.Pow(1 - z, horizon ? 7 : 2.4));
                    }
                starAt = stars.ToArray();
                starLevel = new byte[starAt.Length];
                starPhase = new float[starAt.Length];
                for (int k = 0; k < starAt.Length; k++)
                {
                    starLevel[k] = (byte)rnd.Next(60, 230);
                    starPhase[k] = (float)(rnd.NextDouble() * Math.PI * 2);
                }
            }

            public void Draw(Graphics g, RectangleF r, float unit)
            {
                int w = (int)Math.Ceiling(r.Width), h = (int)Math.Ceiling(r.Height);
                if (w < 8 || h < 8) return;
                if (w != width || h != height || (level == null && Maps.Get(wanted) != null)) Layout(w, h);
                Render();
                var mode = g.InterpolationMode;
                var offset = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var attrs = new ImageAttributes())
                {
                    attrs.SetWrapMode(WrapMode.TileFlipXY);   // no dark fringe along the picture's edges when it's stretched
                    g.DrawImage(picture, new[] { new PointF(r.X, r.Y), new PointF(r.Right, r.Y), new PointF(r.X, r.Bottom) },
                                new RectangleF(0, 0, bw, bh), GraphicsUnit.Pixel, attrs);
                }
                g.InterpolationMode = mode;
                g.PixelOffsetMode = offset;
                Overlays(g, r, unit);
            }

            // One frame of the planet into the picture.
            void Render()
            {
                var lv = level;
                bool ready = lv != null;
                int texW = ready ? lv.W : TexW, wrap = texW - 1;
                int[] surface = ready ? lv.Surface : null;
                byte[] clouds = ready ? lv.Clouds : null, lights = ready ? lv.Lights : null;
                int spin = (int)(v.earthSpin / (2 * Math.PI) % 1 * texW * 256), cloudTurn = (int)(v.cloudSpin / (2 * Math.PI) % 1 * texW * 256);
                int shadowShift = Math.Max(1, texW / 700);   // the clouds' shadows fall a little to one side
                float hit = Math.Min(1, v.scopeHit), loud = v.smooth;
                // How the music lights it: the sun's glint, the atmosphere at the edge and around it, and the cities.
                int glintK = (int)(256 * (0.45f + 0.55f * loud + 0.7f * hit));
                int rimK = (int)(256 * (0.6f + 0.5f * loud + 0.35f * hit));
                int haloK = (int)(256 * (0.55f + 0.8f * loud + 0.6f * hit));
                int cityK = (int)(256 * (0.5f + 0.8f * loud));
                for (int i = 0; i < count; i++)
                {
                    int halo = haloAt[i] * haloK >> 8;
                    int sr = 2 + (halo * 110 >> 8), sg = 4 + (halo * 190 >> 8), sb = 10 + (halo * 245 >> 8);   // space, with the glow past the edge
                    int row = rowAt[i];
                    if (row < 0) { pixels[i] = Pack(sr, sg, sb); continue; }
                    // The surface, blended from the four nearest map pixels (and how much of that is water).
                    int fv = rowMix[i], r0 = row * texW, r1 = r0 + texW;
                    int lu = lonAt[i] - spin, u0 = (lu >> 8) & wrap, u1 = (u0 + 1) & wrap, fu = lu & 255;
                    int rr, gg, bb, wet;
                    if (ready)
                    {
                        int s00 = surface[r0 + u0], s01 = surface[r0 + u1], s10 = surface[r1 + u0], s11 = surface[r1 + u1];
                        rr = Bi((s00 >> 16) & 255, (s01 >> 16) & 255, (s10 >> 16) & 255, (s11 >> 16) & 255, fu, fv);
                        gg = Bi((s00 >> 8) & 255, (s01 >> 8) & 255, (s10 >> 8) & 255, (s11 >> 8) & 255, fu, fv);
                        bb = Bi(s00 & 255, s01 & 255, s10 & 255, s11 & 255, fu, fv);
                        wet = Bi(s00 < 0 ? 255 : 0, s01 < 0 ? 255 : 0, s10 < 0 ? 255 : 0, s11 < 0 ? 255 : 0, fu, fv);
                    }
                    else { rr = 16; gg = 96; bb = 176; wet = 255; }
                    int day = dayAt[i], cloud = 0, shadow = 0;
                    if (ready)
                    {
                        int cl = lonAt[i] - cloudTurn, c0 = (cl >> 8) & wrap, c1 = (c0 + 1) & wrap, fc = cl & 255;
                        cloud = Bi(clouds[r0 + c0], clouds[r0 + c1], clouds[r1 + c0], clouds[r1 + c1], fc, fv);
                        shadow = clouds[r0 + ((c0 + shadowShift) & wrap)];
                    }
                    // Sunlight (a little on the night side too), dimmed where clouds cast their shadows.
                    int light = (14 + (day * 242 >> 8)) * (255 - ((shadow * day >> 9) * 3 >> 2)) >> 8;
                    rr = rr * light >> 8;
                    gg = gg * light >> 8;
                    bb = bb * light >> 8;
                    if (wet > 0)
                    {
                        // Open water: the sun glints off it.
                        int glint = ((specAt[i] * glintK >> 8) + (glintAt[i] * glintK >> 10)) * wet >> 8;
                        rr += glint * 230 >> 8;
                        gg += glint * 242 >> 8;
                        bb += glint;
                    }
                    if (ready)
                    {
                        int city = lights[r0 + u0];
                        if (city > 0)
                        {
                            // Warm city lights on the night side, hidden under clouds.
                            int glow = ((city * (255 - day) >> 8) * (255 - cloud) >> 8) * cityK >> 8;
                            rr += glow;
                            gg += glow * 205 >> 8;
                            bb += glow * 130 >> 8;
                        }
                        if (cloud > 0)
                        {
                            int c = 18 + (day * 240 >> 8);
                            rr += ((c * 236 >> 8) - rr) * cloud >> 8;
                            gg += ((c * 246 >> 8) - gg) * cloud >> 8;
                            bb += (c - bb) * cloud >> 8;
                        }
                    }
                    // The atmosphere: a sunlit blue haze over the whole day side, thickening toward the edge.
                    int haze = Math.Min(255, ((rimAt[i] * (36 + (day * 220 >> 8)) >> 8) + (day * 26 >> 8)) * rimK >> 8);
                    rr += (150 - rr) * haze >> 8;
                    gg += (214 - gg) * haze >> 8;
                    bb += (255 - bb) * haze >> 8;
                    int cover = coverAt[i];
                    if (cover < 255)
                    {
                        rr = (rr * cover + sr * (255 - cover)) >> 8;
                        gg = (gg * cover + sg * (255 - cover)) >> 8;
                        bb = (bb * cover + sb * (255 - cover)) >> 8;
                    }
                    pixels[i] = Pack(rr, gg, bb);
                }
                // Twinkling stars.
                float time = v.tick * 0.05f;
                for (int k = 0; k < starAt.Length; k++)
                {
                    int i = starAt[k], p = pixels[i];
                    int twinkle = (int)(starLevel[k] * (0.6f + 0.4f * (float)Math.Sin(time * (1 + k % 5 * 0.3f) + starPhase[k])) + loud * 50);
                    pixels[i] = Pack(((p >> 16) & 255) + twinkle, ((p >> 8) & 255) + twinkle, (p & 255) + twinkle);
                }
                var data = picture.LockBits(new Rectangle(0, 0, bw, bh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try { for (int y = 0; y < bh; y++) Marshal.Copy(pixels, y * bw, data.Scan0 + y * data.Stride, bw); }
                finally { picture.UnlockBits(data); }
            }

            // On top, at full sharpness: the spectrum rays, the beat rings, the sun's bloom and the storm flashes.
            void Overlays(Graphics g, RectangleF r, float unit)
            {
                float fx = r.Width / bw, fy = r.Height / bh;
                float ex = r.X + cx * fx, ey = r.Y + cy * fy, rx = radius * fx, ry = radius * fy;
                float hit = Math.Min(1, v.scopeHit), loud = v.smooth;
                var smoothing = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // The spectrum as a glowing band of light along the sunlit edge, like the atmosphere swelling with the
                // music: many soft, overlapping rays whose heights follow the bars (low notes on the left), fading
                // out toward both ends of the arc, with a fine bright line along the top.
                double sunAngle = Math.Atan2(sun[1], sun[0]);
                double span = horizon ? Math.Asin(Math.Min(1, r.Width / 2 / rx)) * 0.96 : 1.25;
                float room = horizon ? (ey - ry - r.Y) * 0.95f : rx * 0.26f;
                const int Rays = 144;
                float width = Math.Max(1.5f * unit, (float)(rx * span * 2 / (Rays - 1) * 1.7));
                var tips = new PointF[Rays];
                for (int k = 0; k < Rays; k++)
                {
                    float f = k * (BarCount - 1f) / (Rays - 1);
                    int b0 = (int)f;
                    float bar = Lerp(v.bars[b0], v.bars[Math.Min(BarCount - 1, b0 + 1)], f - b0);
                    float taper = (float)Math.Sin(Math.PI * (k + 0.5) / Rays);
                    double a = sunAngle + span * (1 - 2.0 * k / (Rays - 1));
                    float dx = (float)Math.Cos(a), dy = -(float)Math.Sin(a);
                    var start = new PointF(ex + dx * rx * 1.003f, ey + dy * ry * 1.003f);
                    float len = Math.Max(1.5f, room * bar * (0.35f + 0.65f * taper));
                    var end = new PointF(start.X + dx * len, start.Y + dy * len);
                    tips[k] = end;
                    var past = new PointF(end.X + dx * 2, end.Y + dy * 2);   // the fade ends just past the ray, so it never wraps round
                    using (var b = new LinearGradientBrush(start, past, Color.FromArgb((int)(30 + 90 * taper), 175, 232, 255), Color.FromArgb(0, 120, 200, 255)))
                    using (var p = new Pen(b, width)) g.DrawLine(p, start, end);
                }
                using (var p = new Pen(Color.FromArgb(120, 205, 240, 255), Math.Max(1, unit)) { LineJoin = LineJoin.Round }) g.DrawCurve(p, tips, 0.5f);

                // Faint rings of light leaving the planet on the strongest beats.
                if (!horizon)
                    foreach (var ring in v.rings)
                    {
                        if (ring.Strength < 0.35f) continue;
                        float grow = rx * 0.5f * ring.Age;
                        int alpha = (int)(75 * (1 - ring.Age) * Math.Min(1, ring.Strength));
                        using (var p = new Pen(Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), 160, 225, 255), unit * (0.8f + 1.5f * ring.Strength * (1 - ring.Age))))
                            g.DrawEllipse(p, ex - rx - grow, ey - ry - grow, 2 * (rx + grow), 2 * (ry + grow));
                    }

                // The sun's bloom where it glints, swelling with the music.
                var glint = new PointF(ex + half[0] * rx, ey - half[1] * ry);
                float bloom = (horizon ? r.Height * 0.35f : rx * 0.2f) * (1 + 0.8f * loud + 1.2f * hit);
                Glow(g, glint, bloom * 2.2f, (int)(40 + 40 * loud + 50 * hit), Color.White);
                Glow(g, glint, bloom, (int)(110 + 70 * loud + 70 * hit), Color.White);

                // Storms flashing on the dark side.
                if (!horizon)
                    foreach (var f in v.flashes)
                    {
                        float strength = f.Life * ((v.tick & 1) == 0 ? 1 : 0.55f);
                        Glow(g, new PointF(ex + f.X * rx, ey - f.Y * ry), rx * 0.07f * (0.6f + f.Life), (int)(200 * strength), Color.FromArgb(215, 230, 255));
                    }
                g.SmoothingMode = smoothing;
            }

            // A blend of four values (top-left, top-right, bottom-left, bottom-right) by fu across and fv down (0..255).
            static int Bi(int a, int b, int c, int d, int fu, int fv)
            {
                int top = a + ((b - a) * fu >> 8), bottom = c + ((d - c) * fu >> 8);
                return top + ((bottom - top) * fv >> 8);
            }

            static void Glow(Graphics g, PointF at, float radius, int alpha, Color color)
            {
                if (radius < 1 || alpha <= 0) return;
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(at.X - radius, at.Y - radius, radius * 2, radius * 2);
                    using (var b = new PathGradientBrush(path) { CenterColor = Color.FromArgb(Math.Min(255, alpha), color), SurroundColors = new[] { Color.FromArgb(0, color) } })
                        g.FillPath(b, path);
                }
            }
        }
    }

    // Finds beats in the player's loudness meter (read 30 times a second). Music is usually mixed loud and steady, so
    // its peak level hardly moves (often just a few percent); a beat is a reading that rises clearly above the last
    // second and a half, measured against how much the level normally wobbles.
    sealed class BeatDetector
    {
        readonly float[] history = new float[45];
        int count, head;
        float previous;

        public bool Next(float level)
        {
            bool beat = false;
            if (count >= 15 && level > 0.02f)
            {
                float mean = 0;
                for (int i = 0; i < count; i++) mean += history[i];
                mean /= count;
                float spread = 0;
                for (int i = 0; i < count; i++) spread += (history[i] - mean) * (history[i] - mean);
                spread = (float)Math.Sqrt(spread / count);
                beat = level > previous && level > mean + Math.Max(0.005f, 2 * spread);
            }
            history[head] = level;
            head = (head + 1) % history.Length;
            count = Math.Min(count + 1, history.Length);
            previous = level;
            return beat;
        }
    }

    // How many colors the visualizers use at once: one (the accent color), 2 to 6 colors spread round the color wheel
    // from the accent hue (or at random places on it, with Random Colors on), or the whole rainbow. They follow the
    // accent hue, so Hue Cycle turns them all.
    static class ColorMix
    {
        public static readonly string[] Names = { "1 Color", "2 Colors", "3 Colors", "4 Colors", "6 Colors", "Rainbow" };
        static readonly int[] Counts = { 1, 2, 3, 4, 6, 0 };
        public static int Index;
        public static bool Random;
        static readonly Random rnd = new Random();
        static readonly float[] jitter = new float[6];

        // New random places on the color wheel (each color stays in its own slice, so they never all look alike).
        public static void Reroll()
        {
            for (int i = 1; i < jitter.Length; i++) jitter[i] = (float)(rnd.NextDouble() - 0.5);
        }

        // The colors left to right, or null for the single accent color.
        public static Color[] Palette()
        {
            int n = Counts[Math.Max(0, Math.Min(Counts.Length - 1, Index))];
            if (n == 1) return null;
            float sat = Math.Min(1, 0.8f * Theme.Saturation);
            if (n == 0) return Enumerable.Range(0, 7).Select(i => Theme.Hsv(Theme.Hue + 60 * i, sat, 1)).ToArray();   // all the way round
            var colors = new Color[n];
            for (int i = 0; i < n; i++)
            {
                float slice = 360f / n;
                colors[i] = Theme.Hsv(Theme.Hue + i * slice + (Random ? jitter[i] * slice * 0.8f : 0), sat, 1);
            }
            return colors;
        }
    }

    // ------------------------------------------------------------------ equalizer

    // A 10-band graphic equalizer for everything you hear. Windows doesn't let one app change another app's sound, so
    // this drives Equalizer APO, a free audio effect that plugs into Windows' audio: the bands are written into
    // DinoVisualizer.txt in its config folder, its config.txt includes that file (one line, added once; the rest of
    // config.txt is left alone), and Equalizer APO picks up every change live. The sound stays equalized after this
    // app closes, until the equalizer is turned off.
    static class Equalizer
    {
        public static readonly int[] Bands = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
        public static readonly string[] BandNames = { "31 Hz", "62 Hz", "125 Hz", "250 Hz", "500 Hz", "1 kHz", "2 kHz", "4 kHz", "8 kHz", "16 kHz" };

        // What each band is in plain words: a short name under its slider, and what it changes (shown while the
        // mouse is over it).
        public static readonly string[] BandTitles = { "Rumble", "Bass", "Punch", "Warmth", "Body", "Vocals", "Clarity", "Presence", "Treble", "Air" };
        public static readonly string[] BandHelp =
        {
            "the sub-bass you feel more than hear: movie booms, 808s, organ pedals. Small speakers can barely play it.",
            "the thump of the kick drum and the low notes of the bass. Raise for more bass; lower if it's boomy.",
            "the punch and weight of the bass guitar and drums. Too much sounds boomy or bloated.",
            "warmth and fullness. Too much sounds muddy; a small cut often makes everything clearer.",
            "the body of most instruments. Too much sounds boxy or like it's in a cardboard tube.",
            "the center of voices, guitars and pianos. Raise to bring vocals forward; too much sounds nasal.",
            "clarity and bite. Raise to make voices and guitars cut through; too much sounds harsh.",
            "detail and edge, like the 's' and 't' in words and the pick on strings. Too much is tiring to listen to.",
            "crispness: cymbals, hi-hats and sparkle. Raise for brightness; too much sounds hissy.",
            "the faint shimmer at the very top. Subtle, and easiest to hear on good headphones."
        };

        // The bands in three groups, for the labels under the sliders: first band, last band, name.
        public static readonly int[][] Groups = { new[] { 0, 2 }, new[] { 3, 6 }, new[] { 7, 9 } };
        public static readonly string[] GroupNames = { "Bass", "Mids", "Treble" };
        public const float MaxGain = 12;
        public const string FileName = "DinoVisualizer.txt";
        public const string Download = "https://sourceforge.net/projects/equalizerapo/";

        public static readonly string[] PresetNames =
        {
            "Flat", "Bass Boost", "Treble Boost", "Bass + Treble", "Vocal", "Rock", "Pop", "Electronic", "Hip-Hop",
            "Jazz", "Classical", "Acoustic", "Loudness", "Podcast"
        };
        static readonly float[][] Presets =
        {
            new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            new float[] { 6, 5.5f, 4.5f, 3, 1, 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 0, 0, 1, 2.5f, 4, 5, 6 },
            new float[] { 5, 4, 2.5f, 0.5f, -1, -1.5f, 0, 2, 3.5f, 4.5f },
            new float[] { -2, -2, -1, 1, 3, 4, 3.5f, 2, 0.5f, -1 },
            new float[] { 4.5f, 3.5f, 2, 0.5f, -1, -0.5f, 1.5f, 3, 4, 4.5f },
            new float[] { -1, 0, 1.5f, 3, 4, 3, 1.5f, 0, -0.5f, -1 },
            new float[] { 5, 4.5f, 1.5f, 0, -1.5f, 1, 0.5f, 1.5f, 4, 5 },
            new float[] { 5.5f, 5, 2.5f, 3, -0.5f, -0.5f, 1, 0, 1.5f, 2.5f },
            new float[] { 3.5f, 2.5f, 1, 2, -1.5f, -1.5f, 0, 1.5f, 3, 3.5f },
            new float[] { 4, 3, 2.5f, 2, -1, -1, 0, 2, 3, 3.5f },
            new float[] { 4, 4, 3, 1, 1.5f, 1, 2.5f, 3, 2.5f, 1.5f },
            new float[] { 6, 4, 1, 0, -1, 0, 0, 1, 3, 4 },
            new float[] { -6, -4, -1, 1, 2.5f, 3.5f, 3, 1.5f, 0, -1 },
        };

        public static readonly float[] Gains = new float[10];
        public static bool Enabled = true;
        public static string Problem;   // why the last change couldn't be applied, or null
        static string folder;
        static DateTime checkedAt;

        // Equalizer APO's config folder, or null while it isn't installed (looked up again every few seconds, so it
        // starts working as soon as it's installed).
        public static string ConfigFolder
        {
            get
            {
                if ((DateTime.UtcNow - checkedAt).TotalSeconds > 3)
                {
                    checkedAt = DateTime.UtcNow;
                    folder = Find();
                }
                return folder;
            }
        }

        public static bool Installed { get { return ConfigFolder != null; } }

        static string Find()
        {
            try
            {
                using (var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
                using (var k = hklm.OpenSubKey(@"SOFTWARE\EqualizerAPO"))
                {
                    var p = k == null ? null : k.GetValue("ConfigPath") as string;
                    if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
                }
            }
            catch { }
            foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            {
                if (string.IsNullOrEmpty(root)) continue;
                string d = Path.Combine(root, "EqualizerAPO", "config");
                if (Directory.Exists(d)) return d;
            }
            return null;
        }

        // The preset the bands match, or -1 for a custom setting.
        public static int PresetIndex
        {
            get
            {
                for (int p = 0; p < Presets.Length; p++)
                {
                    bool same = true;
                    for (int i = 0; i < Gains.Length && same; i++) same = Math.Abs(Gains[i] - Presets[p][i]) < 0.05f;
                    if (same) return p;
                }
                return -1;
            }
        }

        public static void UsePreset(int p) { Array.Copy(Presets[p], Gains, Gains.Length); }

        public static void Load()
        {
            var parts = Settings.Str("eq", "").Split(',').Select(s => Util.Num(s)).ToArray();
            if (parts.Length == Gains.Length && parts.All(v => !double.IsNaN(v)))
                for (int i = 0; i < Gains.Length; i++) Gains[i] = (float)Math.Max(-MaxGain, Math.Min(MaxGain, parts[i]));
            Enabled = Settings.Num("eqon", 1) != 0;
        }

        public static void Save()
        {
            Settings.Set("eq", string.Join(",", Gains.Select(v => v.ToString("0.0", CultureInfo.InvariantCulture)).ToArray()));
            Settings.Set("eqon", Enabled ? 1 : 0);
        }

        // Writes the bands for Equalizer APO. Boosts get the same amount of headroom taken off first (the preamp), so
        // they don't clip.
        public static void Apply()
        {
            string dir = ConfigFolder;
            if (dir == null) return;
            try
            {
                var inv = CultureInfo.InvariantCulture;
                var text = new System.Text.StringBuilder();
                text.AppendLine("# Written by Dino Visualizer's equalizer; it's replaced on every change. Use the app to change it.");
                if (Enabled && Gains.Any(v => Math.Abs(v) >= 0.05f))
                {
                    float max = Gains.Max();
                    if (max > 0) text.AppendLine("Preamp: -" + max.ToString("0.0", inv) + " dB");
                    text.AppendLine("GraphicEQ: " + string.Join("; ", Bands.Select((f, i) => f + " " + Gains[i].ToString("0.0", inv)).ToArray()));
                }
                File.WriteAllText(Path.Combine(dir, FileName), text.ToString());
                string main = Path.Combine(dir, "config.txt");
                string current = File.Exists(main) ? File.ReadAllText(main) : "";
                if (current.IndexOf("Include: " + FileName, StringComparison.OrdinalIgnoreCase) < 0)
                    File.AppendAllText(main, (current.Length > 0 && !current.EndsWith("\n") ? "\r\n" : "") + "Include: " + FileName + "\r\n");
                Problem = null;
            }
            catch (UnauthorizedAccessException) { Problem = "Windows won't let this app write to " + dir; }
            catch (Exception ex) { Problem = "Couldn't save the equalizer: " + ex.Message; }
        }
    }

    // ------------------------------------------------------------------ lyrics

    class LyricLine
    {
        public TimeSpan Time;
        public string Text;
    }

    // The lyrics found for one song: timed lines (synced), plain lines, or why there are none.
    sealed class LyricsResult
    {
        public readonly List<LyricLine> Lines = new List<LyricLine>();
        public bool Synced, Instrumental, Searching;
        public string Problem;      // "notfound" or "offline"
        public bool Retry;          // the service was busy or unreachable for part of the lookup: try again later
        public DateTime FailedAt;
    }

    // Looks up the playing song's lyrics on LRCLIB (lrclib.net), a free, open lyrics database that needs no account or
    // key. Synced lyrics come with a time for every line, so the Lyrics tab can follow the song. Each song is looked up
    // once, on a background thread, and the last few dozen are remembered; only the song's title, artist, album and
    // length are sent, and only while the lyrics are on screen (the Lyrics tab, or Lyrics + Visualizer) or going to the
    // VRChat chatbox.
    static class Lyrics
    {
        static readonly Dictionary<string, LyricsResult> found = new Dictionary<string, LyricsResult>();
        static readonly Queue<string> remembered = new Queue<string>();   // found's songs, oldest first
        static readonly object sync = new object();
        public static event Action Changed;   // a lookup finished (raised on a background thread)

        const string Api = "https://lrclib.net/api/";
        const string Agent = "DinoVisualizer/1.0 (Windows desktop app)";

        static string Key(NowPlayingInfo info) { return info.App + "|" + info.Artist + "|" + info.Title; }

        // The lyrics for a song: what's known so far, starting a lookup if it hasn't been looked up (or a failed one is
        // due another try).
        public static LyricsResult For(NowPlayingInfo info)
        {
            if (string.IsNullOrEmpty(info.Title)) return null;
            string key = Key(info);
            lock (sync)
            {
                LyricsResult r;
                if (found.TryGetValue(key, out r) && !(r.Retry && (DateTime.UtcNow - r.FailedAt).TotalSeconds > 30)) return r;
                // Only the last few dozen songs are kept.
                if (r == null)
                {
                    remembered.Enqueue(key);
                    while (remembered.Count > 40) found.Remove(remembered.Dequeue());
                }
                r = new LyricsResult { Searching = true };
                found[key] = r;
                string title = info.Title, artist = info.Artist, album = info.Album, app = info.App;
                int seconds = (int)Math.Round(info.Duration.TotalSeconds);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    var result = Look(title, artist, album, app, seconds);
                    lock (sync) if (found.ContainsKey(key)) found[key] = result;
                    var changed = Changed;
                    if (changed != null) changed();
                });
                return r;
            }
        }

        static LyricsResult Look(string title, string artist, string album, string app, int seconds)
        {
            var result = new LyricsResult();
            try
            {
                string t, a;
                Clean(title, artist, app, out t, out a);
                // The exact match first (it needs the length to be right), then searches that are a little looser.
                // Each request either answers (found or not) or fails (busy, unreachable); a failed one doesn't stop
                // the next, looser one being tried.
                Dictionary<string, object> hit = null;
                bool failed = false, answered = false;
                if (seconds > 0 && a != "")
                    hit = Fetch(Api + "get?track_name=" + Q(t) + "&artist_name=" + Q(a) + (album != "" ? "&album_name=" + Q(album) : "") + "&duration=" + seconds, ref failed, ref answered) as Dictionary<string, object>;
                if (hit == null && a != "") hit = Best(Fetch(Api + "search?track_name=" + Q(t) + "&artist_name=" + Q(a), ref failed, ref answered), seconds);
                if (hit == null) hit = Best(Fetch(Api + "search?q=" + Q((a + " " + Regex.Replace(t, @"\s*[\(\[]?\b(feat|ft)\..*$", "", RegexOptions.IgnoreCase)).Trim()), ref failed, ref answered), seconds);
                if (hit == null)
                {
                    result.Problem = answered ? "notfound" : "offline";
                    result.Retry = failed;
                    result.FailedAt = DateTime.UtcNow;
                    return result;
                }
                object v;
                result.Instrumental = hit.TryGetValue("instrumental", out v) && v is bool && (bool)v;
                string synced = hit.TryGetValue("syncedLyrics", out v) ? v as string : null;
                string plain = hit.TryGetValue("plainLyrics", out v) ? v as string : null;
                if (!string.IsNullOrEmpty(synced)) { ParseSynced(synced, result.Lines); result.Synced = result.Lines.Count > 0; }
                if (!result.Synced && !string.IsNullOrEmpty(plain))
                    foreach (var line in plain.Replace("\r", "").Split('\n')) result.Lines.Add(new LyricLine { Text = line.Trim() });
                if (result.Lines.Count == 0 && !result.Instrumental) { result.Problem = "notfound"; result.FailedAt = DateTime.UtcNow; }
            }
            catch { result.Problem = "offline"; result.Retry = true; result.FailedAt = DateTime.UtcNow; }
            return result;
        }

        // Song titles from YouTube in a browser are usually "Artist - Song (Official Video)" posted by the artist's
        // channel; Spotify's can end in "- Remastered 2011". Both are trimmed to the plain song name for the lookup.
        public static void Clean(string title, string artist, string app, out string t, out string a)
        {
            t = Regex.Replace(title, @"\s*[\(\[][^\)\]]*\b(official|video|audio|lyrics?|visuali[sz]er|hd|4k|mv|m/v|explicit|clean)\b[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+-\s+((\d{4}\s+)?remaster(ed)?|live|mono|stereo|radio edit|single version)\b.*$", "", RegexOptions.IgnoreCase).Trim();
            a = Regex.Replace(artist ?? "", @"(\s*-\s*Topic|VEVO)$", "", RegexOptions.IgnoreCase).Trim();
            bool browser = new[] { "Brave", "Chrome", "Edge", "Firefox", "Opera", "Vivaldi" }.Contains(app);
            int dash = t.IndexOf(" - ");
            if (dash > 0 && (browser || a == ""))
            {
                a = t.Substring(0, dash).Trim();
                t = t.Substring(dash + 3).Trim();
            }
            t = t.Trim('"', ' ');
        }

        static string Q(string s) { return Uri.EscapeDataString(s ?? ""); }

        // The search result with words, preferring synced ones and ones whose length matches the song.
        static Dictionary<string, object> Best(object list, int seconds)
        {
            var items = list as object[];
            if (items == null) return null;
            Dictionary<string, object> best = null;
            int bestScore = int.MinValue;
            foreach (var o in items)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                object v;
                bool synced = d.TryGetValue("syncedLyrics", out v) && !string.IsNullOrEmpty(v as string);
                bool plain = d.TryGetValue("plainLyrics", out v) && !string.IsNullOrEmpty(v as string);
                bool instrumental = d.TryGetValue("instrumental", out v) && v is bool && (bool)v;
                if (!synced && !plain && !instrumental) continue;
                double length = d.TryGetValue("duration", out v) && v != null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : 0;
                int score = (synced ? 4 : plain ? 2 : 0) + (seconds > 0 && length > 0 && Math.Abs(length - seconds) <= 3 ? 5 : 0);
                if (score > bestScore) { best = d; bestScore = score; }
            }
            return best;
        }

        // GET a URL and read its JSON; null for "not found". failed is set when the service is busy or can't be
        // reached, answered when it gave a real answer (including "not found").
        static object Fetch(string url, ref bool failed, ref bool answered)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = Agent;
            req.Timeout = 10000;
            req.ReadWriteTimeout = 10000;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8))
                {
                    if ((resp.ContentType ?? "").IndexOf("json", StringComparison.OrdinalIgnoreCase) < 0) { failed = true; return null; }
                    answered = true;
                    return new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 8 << 20 }.DeserializeObject(reader.ReadToEnd());
                }
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp == null || (int)resp.StatusCode >= 500 || (int)resp.StatusCode == 429) failed = true;
                else answered = true;   // 404: it's not in the database
                return null;
            }
        }

        // LRC lines: "[mm:ss.xx] words", sometimes with several times in front of one line.
        static void ParseSynced(string lrc, List<LyricLine> lines)
        {
            var stamp = new Regex(@"\[(\d+):(\d+(?:\.\d+)?)\]");
            foreach (var raw in lrc.Replace("\r", "").Split('\n'))
            {
                var ms = stamp.Matches(raw);
                if (ms.Count == 0) continue;
                string text = raw.Substring(ms[ms.Count - 1].Index + ms[ms.Count - 1].Length).Trim();
                foreach (Match m in ms)
                    lines.Add(new LyricLine
                    {
                        Time = TimeSpan.FromMinutes(int.Parse(m.Groups[1].Value)) + TimeSpan.FromSeconds(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)),
                        Text = text
                    });
            }
            lines.Sort((x, y) => x.Time.CompareTo(y.Time));
        }
    }

    // What's known about the playing song and its artist, for the Info tab.
    sealed class SongDetails
    {
        public bool Searching, Offline, Retry;
        public DateTime FailedAt;
        // From the iTunes catalog (Apple Music's).
        public string Track, Artist, Album, Genre;
        public DateTime Released;               // MinValue when it isn't known
        public int TrackNumber, TrackCount;
        public TimeSpan Length;
        // From Wikipedia.
        public string SongDescription, SongAbout, SongPage;
        public ArtistDetails About;             // null when the artist has no article
        public bool Found { get { return Track != null || SongAbout != null || About != null; } }
    }

    sealed class ArtistDetails
    {
        public string Name, Description, About, Page;
        public Bitmap Picture;
    }

    // Looks up the playing song in the iTunes catalog (album, release date, genre, track number, length) and the song
    // and its artist on Wikipedia (a short description and the opening of the article, and the artist's picture). Both
    // are free and need no account or key. Each song is looked up once, on a background thread, only while the Info
    // tab is open; only the song's title and artist are sent. The last few dozen songs and artists are remembered.
    static class SongInfo
    {
        static readonly Dictionary<string, SongDetails> songs = new Dictionary<string, SongDetails>();
        static readonly Queue<string> songOrder = new Queue<string>();
        static readonly Dictionary<string, ArtistDetails> artists = new Dictionary<string, ArtistDetails>();
        static readonly Queue<string> artistOrder = new Queue<string>();
        static readonly object sync = new object();
        public static event Action Changed;   // a lookup finished (raised on a background thread)

        public static SongDetails For(NowPlayingInfo info)
        {
            if (string.IsNullOrEmpty(info.Title)) return null;
            string key = info.App + "|" + info.Artist + "|" + info.Title;
            lock (sync)
            {
                SongDetails d;
                if (songs.TryGetValue(key, out d) && !(d.Retry && (DateTime.UtcNow - d.FailedAt).TotalSeconds > 30)) return d;
                if (d == null)
                {
                    songOrder.Enqueue(key);
                    while (songOrder.Count > 40) songs.Remove(songOrder.Dequeue());
                }
                d = new SongDetails { Searching = true };
                songs[key] = d;
                string title = info.Title, artist = info.Artist, album = info.Album, app = info.App;
                int seconds = (int)Math.Round(info.Duration.TotalSeconds);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    var result = Look(title, artist, album, app, seconds);
                    lock (sync) if (songs.ContainsKey(key)) songs[key] = result;
                    var changed = Changed;
                    if (changed != null) changed();
                });
                return d;
            }
        }

        static SongDetails Look(string title, string artist, string album, string app, int seconds)
        {
            var d = new SongDetails();
            bool reached = false;
            try
            {
                string t, a;
                Lyrics.Clean(title, artist, app, out t, out a);
                string bare = Regex.Replace(t, @"\s*[\(\[]?\b(feat|ft)\..*$", "", RegexOptions.IgnoreCase).Trim();
                Catalog(d, bare, a, album, seconds, ref reached);
                string name = d.Artist ?? a;
                if (name != "")
                {
                    d.About = Artist(name, ref reached);
                    Song(d, bare, name, ref reached);
                }
            }
            catch { }
            if (!d.Found && !reached)
            {
                d.Offline = d.Retry = true;
                d.FailedAt = DateTime.UtcNow;
            }
            return d;
        }

        // The song in the iTunes catalog: the same name by the same artist, and not a live, acoustic, instrumental or
        // karaoke version (unless that's what's playing). The one on the album the player names comes first, then
        // the one whose length matches, then the earliest release.
        static void Catalog(SongDetails d, string title, string artist, string album, int seconds, ref bool reached)
        {
            int status;
            var reply = Web.Send("GET", "https://itunes.apple.com/search?media=music&entity=song&limit=25&term=" + Q((artist + " " + title).Trim()), null, null, null, out status);
            if (status != 0) reached = true;
            var items = Web.At(reply, "results") as object[];
            if (items == null) return;
            string want = Plain(title), by = Plain(artist);
            bool versionWanted = Regex.IsMatch(title, @"\b(live|acoustic|instrumental|karaoke|remix)\b", RegexOptions.IgnoreCase);
            object best = null;
            int bestScore = int.MinValue;
            DateTime bestDate = DateTime.MaxValue;
            foreach (var item in items)
            {
                if (Plain(Web.Str(item, "trackName")) != want) continue;
                string who = Plain(Web.Str(item, "artistName"));
                if (by != "" && !who.Contains(by) && !by.Contains(who)) continue;
                string onAlbum = Web.Str(item, "collectionName") ?? "";
                if (!versionWanted && Regex.IsMatch(onAlbum + " " + Web.Str(item, "trackName"), @"\b(live|acoustic|instrumental|karaoke|remix|tribute)\b", RegexOptions.IgnoreCase)) continue;
                int score = 0;
                long ms = Web.Num(item, "trackTimeMillis");
                if (!string.IsNullOrEmpty(album) && Plain(onAlbum) == Plain(album)) score += 8;
                if (seconds > 0 && ms > 0 && Math.Abs(ms / 1000.0 - seconds) <= 3) score += 5;
                DateTime date;
                if (!DateTime.TryParse(Web.Str(item, "releaseDate"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out date)) date = DateTime.MaxValue;
                if (score > bestScore || (score == bestScore && date < bestDate)) { best = item; bestScore = score; bestDate = date; }
            }
            if (best == null) return;
            d.Track = Web.Str(best, "trackName");
            d.Artist = Web.Str(best, "artistName");
            d.Album = Web.Str(best, "collectionName");
            d.Genre = Web.Str(best, "primaryGenreName");
            d.Released = bestDate == DateTime.MaxValue ? DateTime.MinValue : bestDate;
            d.TrackNumber = (int)Web.Num(best, "trackNumber");
            d.TrackCount = (int)Web.Num(best, "trackCount");
            d.Length = TimeSpan.FromMilliseconds(Web.Num(best, "trackTimeMillis"));
        }

        // The artist's Wikipedia article: under their name, or "(band)", "(musician)" and so on when the name alone
        // is something else (Nirvana). The article has to be about music.
        static ArtistDetails Artist(string name, ref bool reached)
        {
            string key = Plain(name);
            lock (sync)
            {
                ArtistDetails known;
                if (artists.TryGetValue(key, out known)) return known;
            }
            ArtistDetails found = null;
            foreach (var suffix in new[] { "", " (band)", " (musician)", " (singer)", " (rapper)", " (group)" })
            {
                var page = Wiki(name + suffix, ref reached);
                if (page == null) continue;
                string desc = Web.Str(page, "description") ?? "", text = Web.Str(page, "extract") ?? "";
                string firstLine = text.Length > 240 ? text.Substring(0, 240) : text;
                if (!Regex.IsMatch(desc == "" ? firstLine : desc, @"\b(band|singer|musician|rapper|group|duo|trio|dj|producer|songwriter|composer|vocalist|guitarist|drummer|pianist|orchestra|ensemble|musical|music)\b", RegexOptions.IgnoreCase)) continue;
                string title = Regex.Replace(Web.Str(page, "title") ?? name, @"\s*\((band|musician|singer|rapper|group)\)$", "");
                found = new ArtistDetails { Name = title, Description = desc, About = text, Page = Web.Str(page, "content_urls", "desktop", "page") };
                found.Picture = Picture(Web.Str(page, "thumbnail", "source"));
                break;
            }
            if (found != null || reached)
                lock (sync)
                {
                    if (!artists.ContainsKey(key)) artistOrder.Enqueue(key);
                    artists[key] = found;
                    while (artistOrder.Count > 30) artists.Remove(artistOrder.Dequeue());
                }
            return found;
        }

        // The song's own article, if it has one ("Song (Artist song)", "Song (song)" or just the name), when it's about
        // a song or single by this artist.
        static void Song(SongDetails d, string title, string artist, ref bool reached)
        {
            foreach (var name in new[] { title + " (" + artist + " song)", title + " (song)", title })
            {
                var page = Wiki(name, ref reached);
                if (page == null) continue;
                string desc = Web.Str(page, "description") ?? "", text = Web.Str(page, "extract") ?? "";
                if (!Regex.IsMatch(desc, @"\b(song|single)\b", RegexOptions.IgnoreCase)) continue;
                if ((desc + " " + text).IndexOf(artist, StringComparison.OrdinalIgnoreCase) < 0) continue;
                d.SongDescription = desc;
                d.SongAbout = text;
                d.SongPage = Web.Str(page, "content_urls", "desktop", "page");
                return;
            }
        }

        // A Wikipedia article's summary (null when there's no ordinary article by that name).
        static object Wiki(string title, ref bool reached)
        {
            int status;
            var page = Web.Send("GET", "https://en.wikipedia.org/api/rest_v1/page/summary/" + Q(title.Replace(' ', '_')), null, null, null, out status);
            if (status != 0) reached = true;
            return status == 200 && Web.Str(page, "type") == "standard" ? page : null;
        }

        // The article's picture, kept small.
        static Bitmap Picture(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "DinoVisualizer/1.0 (Windows desktop app)";
                req.Timeout = 15000;
                using (var resp = req.GetResponse())
                using (var s = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    ms.Position = 0;
                    using (var img = Image.FromStream(ms))
                    {
                        float k = Math.Min(1, 220f / Math.Max(img.Width, img.Height));
                        int w = Math.Max(1, (int)(img.Width * k)), h = Math.Max(1, (int)(img.Height * k));
                        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                        using (var g = Graphics.FromImage(b))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.DrawImage(img, new Rectangle(0, 0, w, h));
                        }
                        return b;
                    }
                }
            }
            catch { return null; }
        }

        // For comparing names: lower case, letters and digits only, without anything in brackets.
        static string Plain(string s)
        {
            s = Regex.Replace(s ?? "", @"[\(\[][^\)\]]*[\)\]]", "");
            return new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        static string Q(string s) { return Uri.EscapeDataString(s ?? ""); }
    }

    // ------------------------------------------------------------------ VRChat chatbox

    // Shows what's playing in VRChat's chatbox (the text bubble above your avatar). VRChat listens for OSC messages
    // on this computer (UDP port 9000) once OSC is on in its Action Menu (Options > OSC > Enabled); nothing leaves the
    // computer. VRChat ignores chatbox messages that come too fast, so the text goes at most every 2 seconds, and it's
    // sent again now and then because the bubble fades after a while. Paused or stopped clears the bubble (or leaves
    // just the status). With lyrics the current line of the song's synced lyrics (from LRCLIB, like the Lyrics tab)
    // goes under the song.
    static class VrcChatbox
    {
        public static readonly string[] Names = { "Off", "Song", "Song + Time", "Song + Progress Bar", "Song + Lyrics", "Song + Time + Lyrics", "Song + Bar + Lyrics" };
        // The progress bar's looks: the played part, the knob where the song is (none: \0), the part still to play, and
        // the ends around it (none: \0). New looks go on the end, so a saved choice keeps its place. (Knob, between Moon
        // and Hash, was taken out in 1.0.1; the choice is saved as "vrcbar2" since then, see MainForm.)
        public static readonly string[] BarNames =
        {
            "Line ━━━●───", "Blocks ███░░░", "Squares ■■■□□□", "Dots ●●●○○○", "Hearts ♥♥♥♡♡♡", "Stars ★★★☆☆☆", "Classic [==>--]",
            "Arrows ▶▶▶▷▷▷", "Diamonds ◆◆◆◇◇◇", "Pills ▰▰▰▱▱▱", "Shades ▓▓▓░░░", "Small Squares ▪▪▪▫▫▫", "Notes ♪♪♪···",
            "Moon ●●◐○○○", "Hash [###...]"
        };
        const string BarDone = "━█■●♥★=▶◆▰▓▪♪●#", BarKnob = "●\0\0\0\0\0>\0\0\0\0\0\0◐\0", BarRest = "─░□○♡☆-▷◇▱░▫·○.";
        const string BarLeft = "\0\0\0\0\0\0[\0\0\0\0\0\0\0[", BarRight = "\0\0\0\0\0\0]\0\0\0\0\0\0\0]";
        public static int Bar;
        const int MaxLength = 144;          // VRChat's limit for one message
        const double MinGap = 2, Repeat = 10;
        const int Cells = 10;               // the progress bar's length
        static readonly TimeSpan Lead = TimeSpan.FromSeconds(0.3);      // a lyric line goes a moment early, as it starts
        static readonly TimeSpan Hold = TimeSpan.FromSeconds(1.5);      // how long a clock-only update waits for a line
        public static int Port = 9000;
        static System.Net.Sockets.UdpClient udp;
        static string sent = "";            // the text last sent ("" once cleared)
        static string sentWords = "";       // the same without its time line
        static DateTime sentAt;
        static int mode;

        public static int Mode
        {
            get { return mode; }
            set
            {
                if (value == mode) return;
                mode = value;
                if (mode == 0 && !statusOn) Stop();
            }
        }

        // ---- The status: lines of your own (like "chilling in the club"), one shown at a time above the song, or alone
        // when nothing plays. They can take turns every few seconds or minutes. Kept one per line in
        // %AppData%\Dino Visualizer\statuses.txt; the Status tab is where they're written and picked.
        public static readonly string[] CycleNames = { "Off", "5 s", "10 s", "30 s", "1 min", "5 min" };
        static readonly int[] CycleSeconds = { 0, 5, 10, 30, 60, 300 };
        public const int StatusLength = 100;             // the longest status (it's cut to 70 above a song)
        public static string StatusFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dino Visualizer", "statuses.txt");
        public static readonly List<string> Statuses = LoadStatuses();
        static bool statusOn;
        static int cycle, statusIndex;
        static DateTime statusSince = DateTime.UtcNow;   // when the shown status came up (for taking turns)

        // Called once at startup with the saved choices.
        public static void Restore(bool on, int cycleIndex, int index)
        {
            statusOn = on;
            cycle = Math.Max(0, Math.Min(CycleNames.Length - 1, cycleIndex));
            statusIndex = index;
        }

        public static bool StatusOn
        {
            get { return statusOn; }
            set
            {
                statusOn = value;
                Settings.Set("vrcstatus", value ? 1 : 0);
                if (!statusOn && mode == 0) Stop();
            }
        }

        public static int Cycle
        {
            get { return cycle; }
            set { cycle = value; statusSince = DateTime.UtcNow; Settings.Set("vrcstatuscycle", value); }
        }

        // The status shown now (-1 with none written). Picking one shows it straight away and starts its turn.
        public static int StatusIndex
        {
            get { return Statuses.Count == 0 ? -1 : Math.Max(0, Math.Min(Statuses.Count - 1, statusIndex)); }
            set { statusIndex = value; statusSince = DateTime.UtcNow; Settings.Set("vrcstatusidx", value); }
        }

        // The status showing moved in the list (it's still the one showing, and its turn carries on).
        public static void Moved(int index)
        {
            statusIndex = index;
            Settings.Set("vrcstatusidx", index);
        }

        // How long until the next status takes its turn (zero: they don't take turns).
        public static TimeSpan UntilNext
        {
            get
            {
                if (!statusOn || cycle == 0 || Statuses.Count < 2) return TimeSpan.Zero;
                var left = statusSince.AddSeconds(CycleSeconds[cycle]) - DateTime.UtcNow;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        static List<string> LoadStatuses()
        {
            try
            {
                if (File.Exists(StatusFile))
                    return File.ReadAllLines(StatusFile, System.Text.Encoding.UTF8).Select(s => s.Trim()).Where(s => s != "").ToList();
            }
            catch { }
            return new List<string>();
        }

        public static void SaveStatuses()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatusFile));
                File.WriteAllLines(StatusFile, Statuses, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        // Called twice a second with what's playing.
        public static void Tick(NowPlayingInfo info)
        {
            if (mode == 0 && !statusOn) return;
            if (statusOn && cycle > 0 && Statuses.Count > 1 && (DateTime.UtcNow - statusSince).TotalSeconds >= CycleSeconds[cycle])
            {
                statusIndex = (StatusIndex + 1) % Statuses.Count;
                statusSince = DateTime.UtcNow;
            }
            string words;
            TimeSpan next;
            string text = Compose(info, out words, out next);
            double since = (DateTime.UtcNow - sentAt).TotalSeconds;
            if (since < MinGap) return;
            if (text == sent && (text == "" || since < Repeat)) return;
            // Only the clock moved and the next lyric line is about to start: this update waits for it, so the line
            // isn't held back by the 2-second gap.
            if (words == sentWords && next > TimeSpan.Zero && next < Hold && since < Repeat) return;
            Send(text);
            sentWords = words;
        }

        // Clears the bubble (switched off, or the app is closing).
        public static void Stop()
        {
            if (sent != "") Send("");
            sentWords = "";
            if (udp != null) udp.Close();
            udp = null;
        }

        public static string Text(NowPlayingInfo info, int mode)
        {
            string words;
            TimeSpan next;
            return Text(info, mode, MaxLength, out words, out next);
        }

        // Everything the bubble shows now: the status (if it's on) over the song (while one plays and a VRChat Chatbox
        // choice is picked). "" when there's nothing to show.
        public static string Compose(NowPlayingInfo info)
        {
            string words;
            TimeSpan next;
            return Compose(info, out words, out next);
        }

        static string Compose(NowPlayingInfo info, out string words, out TimeSpan next)
        {
            next = TimeSpan.Zero;
            string status = statusOn && StatusIndex >= 0 ? Statuses[StatusIndex] : "";
            if (mode == 0 || !info.Playing || info.Title == "") return words = Cut(status, MaxLength);
            if (status == "") return Text(info, mode, MaxLength, out words, out next);
            status = Cut(status, 70);
            string body = Text(info, mode, MaxLength - status.Length - 1, out words, out next);
            words = status + "\n" + words;
            return status + "\n" + body;
        }

        // "♫ Song — Artist ft. Guest", then the lyric line, then the time or a progress bar:  1:23 ━━━━●────── 3:45
        // words is the text without the time line; next is how soon the next lyric line starts (zero: none coming).
        static string Text(NowPlayingInfo info, int mode, int max, out string words, out TimeSpan next)
        {
            string t, a, feat, feat2;
            Lyrics.Clean(info.Title, info.Artist, info.App, out t, out a);
            t = Tidy(t, out feat);
            if (t == "") t = Tidy(info.Title, out feat);
            a = Regex.Replace(Tidy(a, out feat2), @"\s+(feat\.?|featuring)\s+", " ft. ", RegexOptions.IgnoreCase);
            if (feat == "") feat = feat2;
            if (feat != "" && a.IndexOf(feat, StringComparison.OrdinalIgnoreCase) < 0) a = a == "" ? "ft. " + feat : a + " ft. " + feat;
            int clock = mode >= 4 ? mode - 3 : mode;   // 1 no time, 2 time, 3 progress bar
            next = TimeSpan.Zero;
            string lyric = mode >= 4 ? LyricAt(info, out next) : "";
            string time = "";
            if (clock >= 2 && info.Duration > TimeSpan.Zero)
            {
                var now = info.Now;
                if (clock == 2) time = Clock(now) + "/" + Clock(info.Duration);
                else time = Clock(now) + " " + BarText(now.TotalSeconds / info.Duration.TotalSeconds, Bar) + " " + Clock(info.Duration);
            }
            // Over the limit, a long song name gives way to the lyric line (down to 40 characters), then the lyric (left
            // out if there's hardly room for it).
            int room = max - (time != "" ? time.Length + 1 : 0);
            string song = SongLine(t, a, Math.Max(Math.Min(40, room), room - (lyric != "" ? lyric.Length + 1 : 0)));
            int left = room - song.Length - 1;
            words = lyric != "" && left >= 8 ? song + "\n" + Cut(lyric, left) : song;
            return time != "" ? words + "\n" + time : words;
        }

        // Shortened to fit, at the end of a word when there's one in the last third (else mid-word), with "…".
        static string Cut(string s, int length)
        {
            if (s.Length <= length) return s;
            if (length <= 1) return "";
            int space = s.LastIndexOf(' ', length - 1);
            int end = space >= length * 2 / 3 ? space : length - 1;
            return s.Substring(0, end).TrimEnd(' ', ',', '-', '—', '(', '[') + "…";
        }

        // The words that matter in a title or an artist, as briefly as they read clearly. Out goes what YouTube
        // titles carry along: hashtags, 【tags】 and anything after " | " (a channel or label). A featured artist,
        // "(feat. Guest)" or "ft. Guest" at the end, is taken out and handed back to go after the artist.
        static string Tidy(string s, out string feat)
        {
            feat = "";
            s = s ?? "";
            var m = Regex.Match(s, @"\s*[\(\[]\s*(?:feat\.?|ft\.?|featuring|with)\s+([^\)\]]+)[\)\]]", RegexOptions.IgnoreCase);
            if (!m.Success) m = Regex.Match(s, @"\s+(?:feat\.?|ft\.?|featuring)\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                feat = m.Groups[1].Value.Trim();
                s = s.Remove(m.Index, m.Length);
            }
            s = Regex.Replace(s, @"【[^】]*】|#\w+", " ");
            int bar = s.IndexOf(" | ");
            if (bar > 0) s = s.Substring(0, bar);
            s = Regex.Replace(s, @"\s{2,}", " ").Trim(' ', '-', '–', '—', '|', ',');
            // A short ending after a dash ("Song - Remix") goes in brackets, so it doesn't read as the artist's dash.
            return Regex.Replace(s, @"^(.+?)\s+[-–]\s+([^-–]{1,24})$", "$1 ($2)");
        }

        // "♫ Song — Artist" in a set length: a long song name is shortened first, keeping the artist (unless that
        // would leave the name less than half the room).
        static string SongLine(string t, string a, int length)
        {
            string full = "♫ " + (a != "" ? t + " — " + a : t);
            if (full.Length <= length || a == "") return Cut(full, length);
            return Cut("♫ " + Cut(t, Math.Max(length / 2, length - 5 - a.Length)) + " — " + a, length);
        }

        // The bar for how far through the song (0..1), in one of the BarNames looks. With a knob it's one cell longer.
        public static string BarText(double through, int style)
        {
            style = Math.Max(0, Math.Min(BarNames.Length - 1, style));
            int done = Math.Max(0, Math.Min(Cells, (int)Math.Round(Cells * through)));
            char knob = BarKnob[style];
            if (knob == '◐' && done == Cells) knob = BarDone[style];   // Moon's half-full cell is full at the end
            if (knob == '\0') done = Math.Max(0, Math.Min(Cells, (int)Math.Ceiling(Cells * through - 0.05)));   // a cell fills as the song gets to it
            string bar = new string(BarDone[style], done) + (knob != '\0' ? knob.ToString() : "") + new string(BarRest[style], Cells - done);
            return BarLeft[style] != '\0' ? BarLeft[style] + bar + BarRight[style] : bar;
        }

        // The synced lyric line playing now ("♪" between verses), or "" before the first line or when the song has no
        // synced lyrics (plain ones can't be followed). Asking starts the lookup if the song hasn't been looked up.
        static string LyricAt(NowPlayingInfo info, out TimeSpan next)
        {
            next = TimeSpan.Zero;
            var r = Lyrics.For(info);
            if (r == null || r.Searching || !r.Synced) return "";
            var now = info.Now + Lead;
            int current = -1;
            for (int i = 0; i < r.Lines.Count && r.Lines[i].Time <= now; i++) current = i;
            if (current + 1 < r.Lines.Count) next = r.Lines[current + 1].Time - now;
            if (current < 0) return "";
            string s = Regex.Replace(Adlib(r.Lines[current].Text.Trim(), Adlibs), @"\s{2,}", " ").TrimEnd(',', ';', ' ');
            return s == "" ? "♪" : s;
        }

        // Background vocals and adlibs, the parts of a lyric line in brackets ("I'm falling (falling!)"), in smaller
        // letters. VRChat's chatbox has no text sizes, so it's done with Unicode's tiny (superscript) letters or small
        // capitals, as chatbox tools do. One running on from the line before (no opening bracket) counts from the start.
        public static readonly string[] AdlibNames = { "Tiny ⁽ʸᵉᵃʰ⁾", "Small Caps (ʏᴇᴀʜ)", "Normal (yeah)", "Hidden" };
        public static int Adlibs;
        const string Plain = "abcdefghijklmnopqrstuvwxyz0123456789+-=!?";
        const string Tiny = "ᵃᵇᶜᵈᵉᶠᵍʰⁱʲᵏˡᵐⁿᵒᵖqʳˢᵗᵘᵛʷˣʸᶻ⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻⁼ᵎˀ";   // no tiny q that fonts have
        const string Caps = "ᴀʙᴄᴅᴇꜰɢʜɪᴊᴋʟᴍɴᴏᴘǫʀꜱᴛᴜᴠᴡxʏᴢ0123456789+-=!?";

        public static string Adlib(string line, int style)
        {
            if (style == 2) return line;
            int open = line.IndexOfAny(new[] { '(', '[' }), close = line.IndexOfAny(new[] { ')', ']' });
            bool inside = close >= 0 && (open < 0 || close < open);
            var s = new System.Text.StringBuilder();
            foreach (char c in line)
            {
                if (c == '(' || c == '[') { inside = true; s.Append(style == 0 ? "⁽" : style == 1 ? "(" : ""); }
                else if (c == ')' || c == ']') { inside = false; s.Append(style == 0 ? "⁾" : style == 1 ? ")" : ""); }
                else if (!inside) s.Append(c);
                else if (style != 3)
                {
                    int k = Plain.IndexOf(char.ToLowerInvariant(c));
                    s.Append(k < 0 ? c : (style == 0 ? Tiny : Caps)[k]);
                }
            }
            return Regex.Replace(s.ToString(), @"\s{2,}", " ").Trim();
        }

        static string Clock(TimeSpan t)
        {
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00")
                                     : ((int)t.TotalMinutes) + ":" + t.Seconds.ToString("00");
        }

        // One OSC message: /chatbox/input with the text, true (show it right away, not in the keyboard) and false
        // (no notification sound). Strings end with a zero and are padded to a multiple of 4 bytes.
        public static byte[] Message(string text)
        {
            var m = new List<byte>();
            Pad(m, System.Text.Encoding.ASCII.GetBytes("/chatbox/input"));
            Pad(m, System.Text.Encoding.ASCII.GetBytes(",sTF"));
            Pad(m, System.Text.Encoding.UTF8.GetBytes(text));
            return m.ToArray();
        }

        static void Pad(List<byte> to, byte[] s)
        {
            to.AddRange(s);
            do to.Add(0); while (to.Count % 4 != 0);
        }

        static void Send(string text)
        {
            sent = text;
            sentAt = DateTime.UtcNow;
            try
            {
                if (udp == null) udp = new System.Net.Sockets.UdpClient();
                var m = Message(text);
                udp.Send(m, m.Length, new IPEndPoint(IPAddress.Loopback, Port));
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ playlists (Spotify and YouTube sign-in)

    // The developer keys and sign-ins (refresh tokens) for Spotify and YouTube, kept encrypted for this Windows
    // account (DPAPI) in %AppData%\Dino Visualizer\accounts.dat, never in plain text.
    static class Vault
    {
        static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dino Visualizer", "accounts.dat");
        static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("Dino Visualizer accounts");
        static readonly Dictionary<string, string> values = Load();
        static readonly object sync = new object();

        static Dictionary<string, string> Load()
        {
            var d = new Dictionary<string, string>();
            try
            {
                var plain = System.Security.Cryptography.ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                foreach (var line in System.Text.Encoding.UTF8.GetString(plain).Split('\n'))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i)] = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(i + 1)));
                }
            }
            catch { }
            return d;
        }

        public static string Get(string key)
        {
            lock (sync)
            {
                string v;
                return values.TryGetValue(key, out v) ? v : null;
            }
        }

        // Sets a value (null removes it) and saves.
        public static void Set(string key, string value)
        {
            lock (sync)
            {
                if (value == null) values.Remove(key); else values[key] = value;
                try
                {
                    string text = string.Join("\n", values.Select(kv => kv.Key + "=" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(kv.Value))).ToArray());
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.WriteAllBytes(FilePath, System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(text), Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser));
                }
                catch { }
            }
        }
    }

    // Web requests and the browser sign-in (OAuth with PKCE) shared by both services. Signing in opens the service's
    // own sign-in page in your browser; when you approve, it sends the browser back to this app on 127.0.0.1 (this
    // PC only) with a one-time code, which is swapped for the tokens. The app never sees your password.
    static class Web
    {
        public const int Port = 43117;
        public static string RedirectUri { get { return "http://127.0.0.1:" + Port + "/callback"; } }
        const string Agent = "DinoVisualizer/1.0 (Windows desktop app)";

        public static object Json(string text)
        {
            try { return string.IsNullOrEmpty(text) ? null : new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 16 << 20 }.DeserializeObject(text); }
            catch { return null; }
        }

        // A value inside parsed JSON by its path of names (or list positions); null when it isn't there.
        public static object At(object o, params object[] path)
        {
            foreach (var p in path)
            {
                if (o == null) return null;
                if (p is int)
                {
                    var list = o as object[];
                    o = list != null && (int)p < list.Length ? list[(int)p] : null;
                }
                else
                {
                    var d = o as Dictionary<string, object>;
                    object v = null;
                    if (d == null || !d.TryGetValue((string)p, out v)) return null;
                    o = v;
                }
            }
            return o;
        }

        public static string Str(object o, params object[] path) { var v = At(o, path); return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture); }

        public static long Num(object o, params object[] path)
        {
            var v = At(o, path);
            try { return v == null ? 0 : Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        // Sends a request; returns the parsed JSON reply (also for error replies) and the HTTP status (0 when the
        // service couldn't be reached).
        public static object Send(string method, string url, string token, string body, string contentType, out int status)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.UserAgent = Agent;
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            if (token != null) req.Headers["Authorization"] = "Bearer " + token;
            try
            {
                if (body != null)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(body);
                    req.ContentType = contentType;
                    req.ContentLength = bytes.Length;
                    using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                }
                else if (method != "GET") req.ContentLength = 0;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8))
                {
                    status = (int)resp.StatusCode;
                    return Json(reader.ReadToEnd());
                }
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                status = resp != null ? (int)resp.StatusCode : 0;
                if (resp == null) return null;
                try { using (var reader = new StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8)) return Json(reader.ReadToEnd()); }
                catch { return null; }
            }
        }

        public static object PostForm(string url, IEnumerable<KeyValuePair<string, string>> form, out int status)
        {
            string body = string.Join("&", form.Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value ?? "")).ToArray());
            return Send("POST", url, null, body, "application/x-www-form-urlencoded", out status);
        }

        public static KeyValuePair<string, string> P(string key, string value) { return new KeyValuePair<string, string>(key, value); }

        static string Base64Url(byte[] b) { return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }

        // PKCE: a random secret kept by the app, and its hash that goes with the sign-in request.
        public static string NewVerifier()
        {
            var b = new byte[48];
            using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider()) rng.GetBytes(b);
            return Base64Url(b);
        }

        public static string Challenge(string verifier)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create()) return Base64Url(sha.ComputeHash(System.Text.Encoding.ASCII.GetBytes(verifier)));
        }

        static string Page(string title, string text)
        {
            return "<!doctype html><html><head><meta charset=\"utf-8\"><title>Dino Visualizer</title></head>" +
                   "<body style=\"background:#06070a;color:#8ea8ff;font-family:Consolas,monospace;text-align:center;padding-top:15vh\">" +
                   "<h1>" + title + "</h1><p>" + text + "</p></body></html>";
        }

        // Opens the sign-in page and waits (up to 5 minutes) for the browser to come back with the code.
        public static string WaitForCode(string authorizeUrl, string state)
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, Port);
            try { listener.Start(); }
            catch { throw new Exception("Another app is using port " + Port + "; close it and try again"); }
            try
            {
                Process.Start(authorizeUrl);
                var deadline = DateTime.UtcNow.AddMinutes(5);
                while (DateTime.UtcNow < deadline)
                {
                    if (!listener.Pending()) { Thread.Sleep(100); continue; }
                    using (var client = listener.AcceptTcpClient())
                    using (var stream = client.GetStream())
                    {
                        stream.ReadTimeout = 5000;
                        var reader = new StreamReader(stream, System.Text.Encoding.ASCII);
                        string first = reader.ReadLine() ?? "";
                        while (!string.IsNullOrEmpty(reader.ReadLine())) { }   // the rest of the request's headers
                        var parts = first.Split(' ');
                        string target = parts.Length > 1 ? parts[1] : "";
                        bool ours = target.StartsWith("/callback");
                        var query = new Dictionary<string, string>();
                        int q = target.IndexOf('?');
                        if (q >= 0)
                            foreach (var kv in target.Substring(q + 1).Split('&'))
                            {
                                int e = kv.IndexOf('=');
                                if (e > 0) query[kv.Substring(0, e)] = Uri.UnescapeDataString(kv.Substring(e + 1).Replace('+', ' '));
                            }
                        string error, code, back;
                        query.TryGetValue("error", out error);
                        query.TryGetValue("code", out code);
                        query.TryGetValue("state", out back);
                        bool good = ours && error == null && code != null && back == state;
                        string html = !ours ? "" : good ? Page("Signed in", "You can close this tab and go back to Dino Visualizer.")
                                                        : Page("Not signed in", "Something went wrong (" + (error ?? "no code") + "). Close this tab and try again from Dino Visualizer.");
                        var bytes = System.Text.Encoding.UTF8.GetBytes(html);
                        var head = System.Text.Encoding.ASCII.GetBytes((ours ? "HTTP/1.1 200 OK" : "HTTP/1.1 404 Not Found") +
                            "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
                        stream.Write(head, 0, head.Length);
                        stream.Write(bytes, 0, bytes.Length);
                        if (!ours) continue;   // the browser asking for a favicon
                        if (error != null) throw new Exception(error == "access_denied" ? "Sign-in was cancelled" : "Sign-in failed: " + error);
                        if (back != state || code == null) throw new Exception("Sign-in failed: the reply didn't match");
                        return code;
                    }
                }
                throw new Exception("Sign-in timed out; try again");
            }
            finally { listener.Stop(); }
        }
    }

    class Playlist
    {
        public MusicAccount Account;
        public string Id, Name, ImageUrl;
        public int Count;
        public List<PlaylistSong> Songs;     // null until loaded
        public string SongsProblem;          // why the songs can't be shown
        public bool Loading;
    }

    class PlaylistSong
    {
        public string Title, Artist, Id;
        public TimeSpan Length;
    }

    // One music service account. Signs in through the browser, keeps its access token fresh with the saved sign-in,
    // and reads the playlists and their songs. All the web requests run on background threads; Changed is raised
    // (on that thread) whenever something new is ready to show.
    abstract class MusicAccount
    {
        public static event Action Changed;
        public abstract string Name { get; }
        public abstract string ShortName { get; }
        public abstract Color BadgeColor { get; }
        public volatile string Status = "";
        public volatile bool Busy;
        public List<Playlist> Playlists = new List<Playlist>();   // replaced as a whole when reloaded
        string access;
        DateTime accessUntil;

        public bool HasKeys { get { return !string.IsNullOrEmpty(Vault.Get(Name + ".client")); } }
        public bool Connected { get { return Vault.Get(Name + ".refresh") != null; } }

        protected abstract string AuthorizeUrl(string challenge, string state);
        protected abstract object Exchange(string code, string verifier, out int status);
        protected abstract object Renew(string refresh, out int status);
        protected abstract List<Playlist> ReadPlaylists();
        protected abstract List<PlaylistSong> ReadSongs(Playlist p, out string problem);
        public abstract void Play(Playlist p, int song);

        internal static void Notify() { var c = Changed; if (c != null) c(); }

        void Run(string status, Action work)
        {
            if (Busy) return;
            Busy = true;
            Status = status;
            Notify();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { work(); }
                catch (Exception ex) { Status = ex.Message; }
                finally { Busy = false; Notify(); }
            });
        }

        public void SignIn()
        {
            Run("Waiting for you to sign in in your browser...", () =>
            {
                string verifier = Web.NewVerifier(), state = Web.NewVerifier().Substring(0, 16);
                string code = Web.WaitForCode(AuthorizeUrl(Web.Challenge(verifier), state), state);
                Status = "Signing in...";
                Notify();
                int st;
                var reply = Exchange(code, verifier, out st);
                string refresh = Web.Str(reply, "refresh_token");
                if (st != 200 || refresh == null) throw new Exception("Sign-in failed: " + (Web.Str(reply, "error_description") ?? Web.Str(reply, "error") ?? "status " + st));
                Vault.Set(Name + ".refresh", refresh);
                access = Web.Str(reply, "access_token");
                accessUntil = DateTime.UtcNow.AddSeconds(Math.Max(60, Web.Num(reply, "expires_in")) - 60);
                LoadNow();
            });
        }

        public void SignOut()
        {
            Vault.Set(Name + ".refresh", null);
            access = null;
            Playlists = new List<Playlist>();
            Status = "Signed out";
            Notify();
        }

        public void Reload()
        {
            if (!Connected) return;
            Run("Loading playlists...", LoadNow);
        }

        void LoadNow()
        {
            Status = "Loading playlists...";
            Notify();
            var list = ReadPlaylists();
            foreach (var p in list) p.Account = this;
            Playlists = list;
            Status = list.Count == 1 ? "1 playlist" : list.Count + " playlists";
        }

        public void LoadSongs(Playlist p)
        {
            if (p.Loading || p.Songs != null) return;
            p.Loading = true;
            Notify();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string problem;
                    var songs = ReadSongs(p, out problem);
                    p.SongsProblem = problem;
                    p.Songs = songs ?? new List<PlaylistSong>();
                }
                catch (Exception ex) { p.SongsProblem = ex.Message; p.Songs = new List<PlaylistSong>(); }
                finally { p.Loading = false; Notify(); }
            });
        }

        // A working access token, renewed with the saved sign-in when it's about to run out.
        protected string Token()
        {
            if (access != null && DateTime.UtcNow < accessUntil) return access;
            string refresh = Vault.Get(Name + ".refresh");
            if (refresh == null) throw new Exception("Not signed in");
            int st;
            var reply = Renew(refresh, out st);
            if (st == 400 || st == 401)
            {
                Vault.Set(Name + ".refresh", null);   // the sign-in was withdrawn or ran out
                throw new Exception(Name + " signed you out; sign in again");
            }
            access = Web.Str(reply, "access_token");
            if (st != 200 || access == null) throw new Exception("Can't reach " + Name + " right now");
            accessUntil = DateTime.UtcNow.AddSeconds(Math.Max(60, Web.Num(reply, "expires_in")) - 60);
            string newer = Web.Str(reply, "refresh_token");
            if (newer != null && newer != refresh) Vault.Set(Name + ".refresh", newer);
            return access;
        }

        protected object Get(string url, out int status) { return Web.Send("GET", url, Token(), null, null, out status); }

        protected static void Open(string target, string fallback)
        {
            try { Process.Start(target); }
            catch { try { if (fallback != null) Process.Start(fallback); } catch { } }
        }
    }

    // Spotify, through its Web API. The app's key (a Client ID) is created by you on Spotify's developer site;
    // Spotify asks that the account owning it has Premium.
    sealed class SpotifyAccount : MusicAccount
    {
        public override string Name { get { return "Spotify"; } }
        public override string ShortName { get { return "SP"; } }
        public override Color BadgeColor { get { return Color.FromArgb(30, 215, 96); } }
        const string Scopes = "playlist-read-private playlist-read-collaborative user-read-playback-state user-modify-playback-state";
        string ClientId { get { return Vault.Get(Name + ".client"); } }

        protected override string AuthorizeUrl(string challenge, string state)
        {
            return "https://accounts.spotify.com/authorize?response_type=code&client_id=" + Uri.EscapeDataString(ClientId) +
                   "&scope=" + Uri.EscapeDataString(Scopes) + "&redirect_uri=" + Uri.EscapeDataString(Web.RedirectUri) +
                   "&code_challenge_method=S256&code_challenge=" + challenge + "&state=" + state;
        }

        protected override object Exchange(string code, string verifier, out int status)
        {
            return Web.PostForm("https://accounts.spotify.com/api/token", new[] { Web.P("grant_type", "authorization_code"), Web.P("code", code),
                Web.P("redirect_uri", Web.RedirectUri), Web.P("client_id", ClientId), Web.P("code_verifier", verifier) }, out status);
        }

        protected override object Renew(string refresh, out int status)
        {
            return Web.PostForm("https://accounts.spotify.com/api/token", new[] { Web.P("grant_type", "refresh_token"), Web.P("refresh_token", refresh), Web.P("client_id", ClientId) }, out status);
        }

        protected override List<Playlist> ReadPlaylists()
        {
            var list = new List<Playlist>();
            string url = "https://api.spotify.com/v1/me/playlists?limit=50";
            while (url != null && list.Count < 1000)
            {
                int st;
                var page = Get(url, out st);
                if (st != 200) throw new Exception(st == 403 ? "Spotify refused (the developer key's owner needs Premium, and you must be on its user list)" : "Spotify didn't answer (" + st + ")");
                var items = Web.At(page, "items") as object[] ?? new object[0];
                foreach (var it in items)
                {
                    if (it == null) continue;
                    long count = Web.Num(it, "items", "total");
                    if (count == 0) count = Web.Num(it, "tracks", "total");
                    list.Add(new Playlist { Id = Web.Str(it, "id"), Name = Web.Str(it, "name") ?? "", ImageUrl = Web.Str(it, "images", 0, "url"), Count = (int)count });
                }
                url = Web.Str(page, "next");
            }
            return list;
        }

        protected override List<PlaylistSong> ReadSongs(Playlist p, out string problem)
        {
            problem = null;
            var songs = new List<PlaylistSong>();
            string url = "https://api.spotify.com/v1/playlists/" + p.Id + "/items?limit=50";
            while (url != null && songs.Count < 1000)
            {
                int st;
                var page = Get(url, out st);
                if (st == 403 || st == 404)
                {
                    problem = "Spotify only shows the songs of playlists you made or collaborate on. Click the playlist name to open it in Spotify.";
                    return songs;
                }
                if (st != 200) throw new Exception("Spotify didn't answer (" + st + ")");
                foreach (var entry in Web.At(page, "items") as object[] ?? new object[0])
                {
                    var t = Web.At(entry, "item") ?? Web.At(entry, "track");
                    if (t == null) continue;
                    var artists = Web.At(t, "artists") as object[];
                    string artist = artists == null ? (Web.Str(t, "show", "name") ?? "") : string.Join(", ", artists.Select(a => Web.Str(a, "name")).Where(a => a != null).ToArray());
                    songs.Add(new PlaylistSong { Title = Web.Str(t, "name") ?? "", Artist = artist, Id = Web.Str(t, "uri"), Length = TimeSpan.FromMilliseconds(Web.Num(t, "duration_ms")) });
                }
                url = Web.Str(page, "next");
            }
            return songs;
        }

        // Plays the playlist (from a song) in Spotify. That needs Spotify open somewhere (and Premium); otherwise the
        // playlist is just opened in the Spotify app, or on the web.
        public override void Play(Playlist p, int song)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                int st = 0;
                try
                {
                    string body = "{\"context_uri\":\"spotify:playlist:" + p.Id + "\"" + (song >= 0 ? ",\"offset\":{\"position\":" + song + "}" : "") + "}";
                    Web.Send("PUT", "https://api.spotify.com/v1/me/player/play", Token(), body, "application/json", out st);
                }
                catch { }
                if (st == 204 || st == 202 || st == 200) { Status = "Playing in Spotify"; Notify(); return; }
                Open("spotify:playlist:" + p.Id, "https://open.spotify.com/playlist/" + p.Id);
                Status = st == 404 ? "Opened in Spotify: press play there" : "Opened in Spotify";
                Notify();
            });
        }
    }

    // YouTube and YouTube Music (one Google account and one list of playlists), through the YouTube Data API. The
    // app's key (a Client ID and its secret) is created by you in Google Cloud.
    sealed class YouTubeAccount : MusicAccount
    {
        public override string Name { get { return "YouTube"; } }
        public override string ShortName { get { return "YT"; } }
        public override Color BadgeColor { get { return Color.FromArgb(255, 48, 48); } }
        public static bool OpenInMusic = true;   // open songs in YouTube Music (else YouTube)
        string ClientId { get { return Vault.Get(Name + ".client"); } }
        string Secret { get { return Vault.Get(Name + ".secret"); } }
        const string Api = "https://www.googleapis.com/youtube/v3/";

        protected override string AuthorizeUrl(string challenge, string state)
        {
            return "https://accounts.google.com/o/oauth2/v2/auth?response_type=code&client_id=" + Uri.EscapeDataString(ClientId) +
                   "&scope=" + Uri.EscapeDataString("https://www.googleapis.com/auth/youtube.readonly") +
                   "&redirect_uri=" + Uri.EscapeDataString(Web.RedirectUri) + "&access_type=offline&prompt=consent" +
                   "&code_challenge_method=S256&code_challenge=" + challenge + "&state=" + state;
        }

        protected override object Exchange(string code, string verifier, out int status)
        {
            return Web.PostForm("https://oauth2.googleapis.com/token", new[] { Web.P("grant_type", "authorization_code"), Web.P("code", code),
                Web.P("redirect_uri", Web.RedirectUri), Web.P("client_id", ClientId), Web.P("client_secret", Secret), Web.P("code_verifier", verifier) }, out status);
        }

        protected override object Renew(string refresh, out int status)
        {
            return Web.PostForm("https://oauth2.googleapis.com/token", new[] { Web.P("grant_type", "refresh_token"), Web.P("refresh_token", refresh),
                Web.P("client_id", ClientId), Web.P("client_secret", Secret) }, out status);
        }

        static string Problem(object reply, int st)
        {
            string reason = Web.Str(reply, "error", "errors", 0, "reason") ?? Web.Str(reply, "error", "message");
            if (reason == "accessNotConfigured" || (reason ?? "").Contains("has not been used")) return "Turn on the YouTube Data API v3 in your Google Cloud project";
            if (reason == "quotaExceeded") return "YouTube's daily limit for your key is used up; try again tomorrow";
            return "YouTube didn't answer (" + (reason ?? st.ToString()) + ")";
        }

        protected override List<Playlist> ReadPlaylists()
        {
            var list = new List<Playlist>();
            int st;
            // Liked videos (YouTube's own list of what you've liked) first, then your playlists.
            var channel = Get(Api + "channels?part=contentDetails&mine=true", out st);
            if (st != 200) throw new Exception(Problem(channel, st));
            string likes = Web.Str(channel, "items", 0, "contentDetails", "relatedPlaylists", "likes");
            if (!string.IsNullOrEmpty(likes)) list.Add(new Playlist { Id = likes, Name = "Liked videos", Count = -1 });
            string token = "";
            while (token != null && list.Count < 1000)
            {
                var page = Get(Api + "playlists?part=snippet,contentDetails&mine=true&maxResults=50" + (token != "" ? "&pageToken=" + token : ""), out st);
                if (st != 200) throw new Exception(Problem(page, st));
                foreach (var it in Web.At(page, "items") as object[] ?? new object[0])
                    list.Add(new Playlist
                    {
                        Id = Web.Str(it, "id"), Name = Web.Str(it, "snippet", "title") ?? "", Count = (int)Web.Num(it, "contentDetails", "itemCount"),
                        ImageUrl = Web.Str(it, "snippet", "thumbnails", "default", "url")
                    });
                token = Web.Str(page, "nextPageToken");
            }
            return list;
        }

        protected override List<PlaylistSong> ReadSongs(Playlist p, out string problem)
        {
            problem = null;
            var songs = new List<PlaylistSong>();
            string token = "";
            while (token != null && songs.Count < 1000)
            {
                int st;
                var page = Get(Api + "playlistItems?part=snippet,contentDetails&playlistId=" + Uri.EscapeDataString(p.Id) + "&maxResults=50" + (token != "" ? "&pageToken=" + token : ""), out st);
                if (st != 200)
                {
                    problem = st == 404 || st == 403 ? "YouTube won't share the videos in this list. Click its name to open it on YouTube." : Problem(page, st);
                    return songs;
                }
                foreach (var it in Web.At(page, "items") as object[] ?? new object[0])
                {
                    string title = Web.Str(it, "snippet", "title") ?? "";
                    if (title == "Deleted video" || title == "Private video") continue;
                    string channel = Regex.Replace(Web.Str(it, "snippet", "videoOwnerChannelTitle") ?? "", @"\s*-\s*Topic$", "");
                    songs.Add(new PlaylistSong { Title = title, Artist = channel, Id = Web.Str(it, "contentDetails", "videoId") });
                }
                token = Web.Str(page, "nextPageToken");
            }
            if (p.Count < 0) p.Count = songs.Count;
            return songs;
        }

        public override void Play(Playlist p, int song)
        {
            string host = OpenInMusic ? "https://music.youtube.com/" : "https://www.youtube.com/";
            var songs = p.Songs;
            string url = song >= 0 && songs != null && song < songs.Count && songs[song].Id != null
                       ? host + "watch?v=" + songs[song].Id + "&list=" + p.Id
                       : host + "playlist?list=" + p.Id;
            Open(url, null);
            Status = "Opened in " + (OpenInMusic ? "YouTube Music" : "YouTube");
            Notify();
        }
    }

    static class Music
    {
        public static readonly SpotifyAccount Spotify = new SpotifyAccount();
        public static readonly YouTubeAccount YouTube = new YouTubeAccount();
        public static MusicAccount[] All { get { return new MusicAccount[] { Spotify, YouTube }; } }

        // Small cover pictures for the playlist list, downloaded once each in the background.
        static readonly Dictionary<string, Bitmap> covers = new Dictionary<string, Bitmap>();
        static readonly HashSet<string> fetching = new HashSet<string>();

        public static Bitmap Cover(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            lock (covers)
            {
                Bitmap b;
                if (covers.TryGetValue(url, out b)) return b;
                if (!fetching.Add(url)) return null;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Bitmap small = null;
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Timeout = 15000;
                    using (var resp = req.GetResponse())
                    using (var s = resp.GetResponseStream())
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        ms.Position = 0;
                        using (var img = Image.FromStream(ms))
                        {
                            int side = Math.Min(img.Width, img.Height);
                            small = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
                            using (var g = Graphics.FromImage(small))
                            {
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.DrawImage(img, new Rectangle(0, 0, 64, 64), new Rectangle((img.Width - side) / 2, (img.Height - side) / 2, side, side), GraphicsUnit.Pixel);
                            }
                        }
                    }
                }
                catch { }
                lock (covers) covers[url] = small;
                MusicAccount.Notify();
            });
            return null;
        }
    }

    // Connecting one service: the steps to make its free developer key (with links to the right pages and the exact
    // sign-in address to register), boxes to paste the key into, and Sign In. The key is saved encrypted (Vault).
    class AccountSetup : Form
    {
        readonly MusicAccount account;
        readonly TextBox id, secret;
        readonly FlowLayoutPanel steps;
        int textWidth;

        public AccountSetup(MusicAccount account)
        {
            this.account = account;
            bool spotify = account is SpotifyAccount;
            Text = "Connect " + account.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Primary;
            Font = Theme.Body;
            ClientSize = new Size(Theme.S(760), Theme.S(spotify ? 600 : 720));
            textWidth = ClientSize.Width - Theme.S(60);

            steps = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Dock = DockStyle.Fill, Padding = new Padding(Theme.S(24), Theme.S(18), Theme.S(12), Theme.S(12)), BackColor = Theme.Bg };
            Controls.Add(steps);

            Heading("Connect " + account.Name);
            if (spotify)
            {
                Note("Spotify only shares your playlists with apps that have a developer key. It's free and takes about 5 minutes, once. " +
                     "Spotify asks that the account making the key has Premium.");
                Step("1.  Open the Spotify developer dashboard and log in with your Spotify account.");
                Link("Open the Spotify dashboard", "https://developer.spotify.com/dashboard");
                Step("2.  Click Create app. Give it any name and description (for example Dino Visualizer). Under Redirect URIs, add exactly this address:");
                Copyable(Web.RedirectUri);
                Step("    Tick Web API, agree to the terms and click Save.");
                Step("3.  On the app's page, copy its Client ID and paste it here:");
                id = Input("Client ID", Vault.Get(account.Name + ".client"));
                Step("4.  Click Sign In and approve in your browser.");
            }
            else
            {
                Note("YouTube (and YouTube Music, which shares your YouTube playlists) only shares them with apps that have a developer key " +
                     "from Google Cloud. It's free and takes about 5 minutes, once.");
                Step("1.  Create a Google Cloud project (any name, for example Dino Visualizer).");
                Link("Create a project", "https://console.cloud.google.com/projectcreate");
                Step("2.  With that project selected, turn on the YouTube Data API v3 (click Enable).");
                Link("Open the YouTube Data API page", "https://console.cloud.google.com/apis/library/youtube.googleapis.com");
                Step("3.  Open Google Auth Platform and click Get started: app name Dino Visualizer, your email, Audience External, then Create. " +
                     "Then under Audience click Publish app, so Google doesn't sign you out every 7 days (or add your Google account under Test users).");
                Link("Open Google Auth Platform", "https://console.cloud.google.com/auth/overview");
                Step("4.  Under Clients, click Create client, choose Desktop app, and Create. Copy its Client ID and Client secret here:");
                Link("Open Clients", "https://console.cloud.google.com/auth/clients");
                id = Input("Client ID", Vault.Get(account.Name + ".client"));
                secret = Input("Client secret", Vault.Get(account.Name + ".secret"));
                Step("5.  Click Sign In. Google may say it hasn't verified the app: it's your own app, so click Advanced, then Go to Dino Visualizer.");
            }

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(0, Theme.S(14), 0, 0), BackColor = Theme.Bg };
            var signIn = MakeButton("Sign In");
            signIn.Click += (s, e) => SignIn();
            var cancel = MakeButton("Cancel");
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
            buttons.Controls.Add(signIn);
            buttons.Controls.Add(cancel);
            steps.Controls.Add(buttons);
            AcceptButton = signIn;
            CancelButton = cancel;
        }

        void SignIn()
        {
            string key = id.Text.Trim(), sec = secret != null ? secret.Text.Trim() : null;
            if (key.Length < 10 || (secret != null && sec.Length < 6))
            {
                MessageBox.Show(this, secret != null ? "Paste both the Client ID and the Client secret first." : "Paste the Client ID first.", Text);
                return;
            }
            Vault.Set(account.Name + ".client", key);
            if (secret != null) Vault.Set(account.Name + ".secret", sec);
            DialogResult = DialogResult.OK;
        }

        void Heading(string text)
        {
            steps.Controls.Add(new Label { Text = text, Font = Theme.OptionTitle, ForeColor = Theme.Bright, AutoSize = true, Margin = new Padding(0, 0, 0, Theme.S(12)) });
        }

        void Note(string text) { AddText(text, Theme.Dim, Theme.S(10)); }

        void Step(string text) { AddText(text, Theme.Primary, Theme.S(6)); }

        void AddText(string text, Color color, int gap)
        {
            steps.Controls.Add(new Label { Text = text, ForeColor = color, AutoSize = true, MaximumSize = new Size(textWidth, 0), Margin = new Padding(0, gap, 0, Theme.S(2)) });
        }

        void Link(string text, string url)
        {
            var l = new LinkLabel { Text = text, AutoSize = true, LinkColor = Theme.Bright, ActiveLinkColor = Color.White, VisitedLinkColor = Theme.Bright, Margin = new Padding(Theme.S(28), Theme.S(2), 0, Theme.S(4)) };
            l.LinkClicked += (s, e) => { try { Process.Start(url); } catch { } };
            steps.Controls.Add(l);
        }

        void Copyable(string text)
        {
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(Theme.S(28), Theme.S(4), 0, Theme.S(4)), BackColor = Theme.Bg };
            var box = new TextBox { Text = text, ReadOnly = true, Width = Theme.S(380), BackColor = Theme.Ink, ForeColor = Theme.Bright, BorderStyle = BorderStyle.FixedSingle };
            var copy = MakeButton("Copy");
            copy.Click += (s, e) => { try { Clipboard.SetText(text); copy.Text = "Copied"; } catch { } };
            row.Controls.Add(box);
            row.Controls.Add(copy);
            steps.Controls.Add(row);
        }

        TextBox Input(string label, string value)
        {
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(Theme.S(28), Theme.S(4), 0, Theme.S(4)), BackColor = Theme.Bg };
            row.Controls.Add(new Label { Text = label + ":", ForeColor = Theme.Primary, AutoSize = false, Size = new Size(Theme.S(130), Theme.S(26)), TextAlign = ContentAlignment.MiddleLeft });
            var box = new TextBox { Text = value ?? "", Width = Theme.S(500), BackColor = Theme.Ink, ForeColor = Theme.Bright, BorderStyle = BorderStyle.FixedSingle };
            row.Controls.Add(box);
            steps.Controls.Add(row);
            return box;
        }

        static Button MakeButton(string text)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, ForeColor = Theme.Bright, BackColor = Theme.Ink, AutoSize = true, Padding = new Padding(Theme.S(10), Theme.S(2), Theme.S(10), Theme.S(2)), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Theme.Primary;
            b.FlatAppearance.BorderSize = Math.Max(1, Theme.S(2));
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, Theme.Primary);
            return b;
        }
    }

    // The Dino Monitor skull as a hologram: a 3D wireframe of the Spinosaurus skull (jaws open, nothing in them)
    // slowly turning above a glowing projector pad. Lines fade with depth, a beam of light rises from the pad, a scan
    // line sweeps up now and then, and the picture flickers and glitches a little like a real projection. In the
    // accent color. (Dino Recorder's camera hologram, without the camera.)
    sealed class SkullHologram
    {
        struct V3
        {
            public float X, Y, Z;
            public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
        }

        // The model, in design units: x right, y up, z toward the viewer, the pad's center at the origin.
        static readonly List<V3[]> Lines = new List<V3[]>();    // polylines; closed ones end on their first point
        static readonly List<V3[]> Panels = new List<V3[]>();   // faint see-through faces that give it volume
        static readonly float Radius;                           // how far anything reaches from the turning axis
        const float Height3D = 158, PadRadius = 92, Distance = 700, Thick = 9;
        static readonly float CosTilt = (float)Math.Cos(14 * Math.PI / 180), SinTilt = (float)Math.Sin(14 * Math.PI / 180);

        // From the skull art's grid (y down) to the model: centered on the turning axis, floating above the pad.
        static V3 V(float gx, float gy, float z) { return new V3(gx - 128, 152 - gy, z); }

        static SkullHologram()
        {
            // The skull as a slab: its outlines on both faces, the jaws' edges joined across, teeth and eye bar on both.
            var outlines = new List<PointF[]>();
            var strokes = new List<PointF[]>();
            SkullArt.Shapes(outlines, strokes);
            foreach (float z in new[] { Thick, -Thick })
            {
                foreach (var o in outlines)
                {
                    var loop = new V3[o.Length + 1];
                    for (int i = 0; i < o.Length; i++) loop[i] = V(o[i].X, o[i].Y, z);
                    loop[o.Length] = loop[0];
                    Lines.Add(loop);
                }
                foreach (var s in strokes) Lines.Add(s.Select(p => V(p.X, p.Y, z)).ToArray());
                Panels.Add(outlines[0].Select(p => V(p.X, p.Y, z)).ToArray());
                Panels.Add(outlines[1].Select(p => V(p.X, p.Y, z)).ToArray());
            }
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < outlines[j].Length; i += 8)
                    Lines.Add(new[] { V(outlines[j][i].X, outlines[j][i].Y, Thick), V(outlines[j][i].X, outlines[j][i].Y, -Thick) });

            // The projector pad on the floor: two rings and ticks round the edge (they show the turning).
            foreach (float r in new[] { PadRadius, PadRadius * 0.8f })
            {
                var ring = new V3[49];
                for (int i = 0; i <= 48; i++) ring[i] = new V3(r * (float)Math.Cos(i * Math.PI / 24), 0, r * (float)Math.Sin(i * Math.PI / 24));
                Lines.Add(ring);
            }
            for (int i = 0; i < 16; i++)
            {
                double a = i * Math.PI / 8;
                Lines.Add(new[] { new V3(PadRadius * (float)Math.Cos(a), 0, PadRadius * (float)Math.Sin(a)),
                                  new V3((PadRadius + 8) * (float)Math.Cos(a), 0, (PadRadius + 8) * (float)Math.Sin(a)) });
            }

            float reach = 0;
            foreach (var line in Lines) foreach (var p in line) reach = Math.Max(reach, (float)Math.Sqrt(p.X * p.X + p.Z * p.Z));
            Radius = reach + 2;
        }

        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Random rnd = new Random();
        Bitmap holo;
        Size laidOut;
        float fit = 1;          // pixels per design unit
        PointF origin;          // where the pad's center lands in the bitmap
        float flicker = 1;
        int glitchFrames;
        double nextGlitch = 5;

        // Turns a point by yaw (around the upright axis), tilts it toward the viewer, applies perspective and places it
        // in the bitmap. depth runs from 0 (far side) to 1 (nearest).
        PointF Project(V3 p, float cos, float sin, out float depth)
        {
            float x = p.X * cos + p.Z * sin, z = -p.X * sin + p.Z * cos;
            float y = p.Y * CosTilt - z * SinTilt, zz = p.Y * SinTilt + z * CosTilt;
            float s = Distance / (Distance - zz);
            depth = Math.Max(0, Math.Min(1, (zz + Radius) / (2 * Radius)));
            return new PointF(origin.X + x * s * fit, origin.Y - y * s * fit);
        }

        // Sizes it to the space, from the cylinder the model turns inside (so it doesn't pulse as it turns).
        void Layout(Size size)
        {
            laidOut = size;
            fit = 1;
            origin = PointF.Empty;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue, d;
            for (int i = 0; i < 36; i++)
                foreach (float y in new[] { 0f, Height3D })
                {
                    double a = i * Math.PI / 18;
                    var p = Project(new V3(Radius * (float)Math.Cos(a), y, Radius * (float)Math.Sin(a)), 1, 0, out d);
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                }
            fit = Math.Min(size.Width / (maxX - minX), size.Height / (maxY - minY));
            origin = new PointF(size.Width / 2f - (minX + maxX) / 2 * fit, size.Height / 2f - (minY + maxY) / 2 * fit);
            if (holo != null) holo.Dispose();
            holo = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppPArgb);
        }

        static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(a.R + (int)((b.R - a.R) * t), a.G + (int)((b.G - a.G) * t), a.B + (int)((b.B - a.B) * t));
        }

        // Lets go of its picture (while the window is hidden); the next frame makes it again.
        public void Release()
        {
            if (holo != null) holo.Dispose();
            holo = null;
            laidOut = Size.Empty;
        }

        // Draws the current frame into area.
        public void Draw(Graphics g, Rectangle area)
        {
            if (area.Width < Theme.S(40) || area.Height < Theme.S(40)) return;
            if (area.Size != laidOut) Layout(area.Size);
            double t = clock.Elapsed.TotalSeconds;
            flicker = 0.9f + 0.1f * (float)rnd.NextDouble();
            if (glitchFrames > 0) glitchFrames--;
            else if (t > nextGlitch) { glitchFrames = 3; nextGlitch = t + 4 + rnd.NextDouble() * 7; }
            Render(t);
            if (glitchFrames > 0)
            {
                // A glitch: the picture tears into strips, a few of them knocked sideways.
                const int strips = 7;
                for (int i = 0; i < strips; i++)
                {
                    int y0 = i * holo.Height / strips, y1 = (i + 1) * holo.Height / strips;
                    int dx = rnd.Next(3) == 0 ? rnd.Next(-Theme.S(6), Theme.S(7)) : 0;
                    g.DrawImage(holo, new Rectangle(area.X + dx, area.Y + y0, holo.Width, y1 - y0), new Rectangle(0, y0, holo.Width, y1 - y0), GraphicsUnit.Pixel);
                }
            }
            else g.DrawImageUnscaled(holo, area.X, area.Y);
        }

        void Render(double t)
        {
            double yaw = t * 20 % 360 * Math.PI / 180;   // a full turn every 18 seconds
            float cos = (float)Math.Cos(yaw), sin = (float)Math.Sin(yaw), d;
            using (var g = Graphics.FromImage(holo))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // The beam of light rising from the pad, and the pad's glow.
                var c = Project(new V3(0, 0, 0), 1, 0, out d);
                var l = Project(new V3(-PadRadius, 0, 0), 1, 0, out d);
                var r = Project(new V3(PadRadius, 0, 0), 1, 0, out d);
                var near = Project(new V3(0, 0, PadRadius), 1, 0, out d);
                var far = Project(new V3(0, 0, -PadRadius), 1, 0, out d);
                var top = Project(new V3(0, Height3D, 0), 1, 0, out d);
                float spread = (r.X - l.X) * 0.12f;
                var beam = new[] { new PointF(l.X, c.Y), new PointF(r.X, c.Y), new PointF(r.X + spread, top.Y), new PointF(l.X - spread, top.Y) };
                using (var b = new LinearGradientBrush(new RectangleF(0, top.Y - 1, 1, c.Y - top.Y + 2), Color.FromArgb(0, Theme.Primary),
                                                       Color.FromArgb((int)(42 * flicker), Theme.Primary), 90f))
                    g.FillPolygon(b, beam);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(l.X, far.Y, r.X - l.X, Math.Max(2, near.Y - far.Y));
                    using (var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(120 * flicker), Theme.Primary), SurroundColors = new[] { Color.FromArgb(0, Theme.Primary) } })
                        g.FillPath(pg, path);
                }

                // See-through faces, then the lines from the far side to the near side, brighter and crisper up front.
                foreach (var face in Panels)
                {
                    var pts = new PointF[face.Length];
                    float depthSum = 0;
                    for (int i = 0; i < face.Length; i++) { pts[i] = Project(face[i], cos, sin, out d); depthSum += d; }
                    using (var b = new SolidBrush(Color.FromArgb((int)(6 + 16 * depthSum / face.Length * flicker), Theme.Primary))) g.FillPolygon(b, pts);
                }
                const int buckets = 5;
                var runs = new List<PointF[]>[buckets];
                for (int b = 0; b < buckets; b++) runs[b] = new List<PointF[]>();
                var run = new List<PointF>();
                foreach (var line in Lines)
                {
                    int current = -1;
                    PointF prev = PointF.Empty;
                    float prevDepth = 0;
                    run.Clear();
                    for (int i = 0; i < line.Length; i++)
                    {
                        var p = Project(line[i], cos, sin, out d);
                        if (i > 0)
                        {
                            int b = Math.Min(buckets - 1, (int)((prevDepth + d) / 2 * buckets));
                            if (b != current)
                            {
                                if (run.Count > 1) runs[current].Add(run.ToArray());
                                run.Clear();
                                run.Add(prev);
                                current = b;
                            }
                            run.Add(p);
                        }
                        prev = p;
                        prevDepth = d;
                    }
                    if (run.Count > 1) runs[current].Add(run.ToArray());
                }
                float width = Math.Max(1f, 2f * fit);
                for (int b = 0; b < buckets; b++)
                {
                    float depth = (b + 0.5f) / buckets;
                    var color = Mix(Theme.Primary, Theme.Bright, depth * depth * 0.45f);
                    using (var glow = new Pen(Color.FromArgb((int)((8 + 34 * depth) * flicker), Theme.Primary), width * 3.5f) { LineJoin = LineJoin.Round })
                    using (var pen = new Pen(Color.FromArgb((int)((40 + 215 * depth) * flicker), color), width) { LineJoin = LineJoin.Round })
                    {
                        foreach (var pts in runs[b]) g.DrawLines(glow, pts);
                        foreach (var pts in runs[b]) g.DrawLines(pen, pts);
                    }
                }

                // Every 5 seconds a scan line sweeps up through the beam.
                double phase = t % 5;
                if (phase < 1.6)
                {
                    float u = (float)(phase / 1.6), y = c.Y + (top.Y - c.Y) * u;
                    float x0 = l.X - spread * u, x1 = r.X + spread * u;
                    int a = (int)(150 * (1 - u) * flicker);
                    using (var p1 = new Pen(Color.FromArgb(a / 4, Theme.Bright), Math.Max(3, 6 * fit)))
                    using (var p2 = new Pen(Color.FromArgb(a, Theme.Bright), Math.Max(1, fit)))
                    {
                        g.DrawLine(p1, x0, y, x1, y);
                        g.DrawLine(p2, x0, y, x1, y);
                    }
                }
            }
        }
    }

    // A little something that drifts around the screen like the old DVD player idle logo while certain bands play: a
    // strawberry for Dance Gavin Dance, an atlas beetle for Saosin. Straight diagonal lines at a steady speed, bouncing
    // off the screen's edges and changing color at every bounce. It floats above everything, never takes focus and
    // lets clicks pass straight through.
    sealed class BouncerWindow : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        struct BlendFunction { public byte Op, Flags, Alpha, Format; }

        [DllImport("user32.dll")]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref Point pos, ref Size size, IntPtr src, ref Point srcPos, int key, ref BlendFunction blend, int flags);

        public const int None = 0, Strawberry = 1, Beetle = 2;

        // Each can be switched off in the Secret tab (both start on).
        static bool strawberryOn = Settings.Num("secretberry", 1) != 0, beetleOn = Settings.Num("secretbeetle", 1) != 0;

        public static bool IsOn(int kind) { return kind == Beetle ? beetleOn : strawberryOn; }

        public static void SetOn(int kind, bool on)
        {
            if (kind == Beetle) beetleOn = on; else strawberryOn = on;
            Settings.Set(kind == Beetle ? "secretbeetle" : "secretberry", on ? 1 : 0);
        }

        // Which one comes out for a song: the band's name as the artist, or in a video's title.
        public static int For(NowPlayingInfo info)
        {
            Func<string, bool> has = band => (info.Artist ?? "").IndexOf(band, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                             (info.Title ?? "").IndexOf(band, StringComparison.OrdinalIgnoreCase) >= 0;
            return has("dance gavin dance") && strawberryOn ? Strawberry : has("saosin") && beetleOn ? Beetle : None;
        }

        // A picture of one, in its first colors, for the Secret tab.
        public static Bitmap Preview(int kind, int size)
        {
            return kind == Beetle ? PaintBeetle(size, ShellColors[0][0], ShellColors[0][1]) : PaintBerry(size, Colors[0][0], Colors[0][1]);
        }

        // The shell colors the beetle takes in turn: light (the shine) and dark.
        static readonly Color[][] ShellColors =
        {
            new[] { Color.FromArgb(84, 86, 150), Color.FromArgb(10, 10, 34) },      // deep navy
            new[] { Color.FromArgb(96, 140, 220), Color.FromArgb(10, 26, 70) },     // midnight blue
            new[] { Color.FromArgb(176, 110, 220), Color.FromArgb(44, 14, 70) },    // violet
            new[] { Color.FromArgb(90, 200, 140), Color.FromArgb(8, 56, 36) },      // emerald
            new[] { Color.FromArgb(214, 150, 80), Color.FromArgb(70, 36, 10) },     // bronze
            new[] { Color.FromArgb(220, 90, 100), Color.FromArgb(70, 10, 20) },     // crimson
            new[] { Color.FromArgb(170, 184, 200), Color.FromArgb(30, 36, 46) },    // steel
        };

        // The colors the strawberry takes in turn: the body's light and dark shades.
        static readonly Color[][] Colors =
        {
            new[] { Color.FromArgb(255, 96, 92), Color.FromArgb(178, 16, 38) },     // strawberry red
            new[] { Color.FromArgb(255, 150, 214), Color.FromArgb(196, 40, 140) },  // pink
            new[] { Color.FromArgb(255, 178, 80), Color.FromArgb(210, 90, 10) },    // orange
            new[] { Color.FromArgb(196, 140, 255), Color.FromArgb(100, 40, 190) },  // purple
            new[] { Color.FromArgb(120, 200, 255), Color.FromArgb(20, 90, 200) },   // blue
            new[] { Color.FromArgb(255, 230, 110), Color.FromArgb(200, 150, 10) },  // gold
            new[] { Color.FromArgb(150, 245, 150), Color.FromArgb(30, 150, 60) },   // green
        };

        public readonly int Kind;
        readonly Color[][] palette;
        readonly Bitmap[] berries;   // one picture per color
        int color;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 15 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        double last;
        float x, y, vx, vy;

        public BouncerWindow(int kind)
        {
            Kind = kind;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            palette = kind == Beetle ? ShellColors : Colors;
            berries = palette.Select(c => kind == Beetle ? PaintBeetle(Theme.S(130), c[0], c[1]) : PaintBerry(Theme.S(110), c[0], c[1])).ToArray();
            Size = berries[0].Size;
            // Somewhere on the main screen, heading off diagonally.
            var rnd = new Random();
            var area = Screen.PrimaryScreen.WorkingArea;
            x = area.X + (float)rnd.NextDouble() * Math.Max(1, area.Width - Width);
            y = area.Y + (float)rnd.NextDouble() * Math.Max(1, area.Height - Height);
            float speed = Theme.S(150);
            vx = rnd.Next(2) == 0 ? speed : -speed;
            vy = rnd.Next(2) == 0 ? speed : -speed;
            Location = new Point((int)x, (int)y);
            timer.Tick += (s, e) => Step();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000 | 0x8;   // layered, clicks pass through, no Alt+Tab, no focus, on top
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            last = clock.Elapsed.TotalSeconds;
            Render();
            timer.Start();
        }

        // ---- The explosion when its song ends: it stops, flashes, and bursts into chunks of fruit, drops of juice,
        // seeds and bits of leaf that fly out, fall and fade, with a ring of juice spreading out, in about a second.

        public string Song;   // the song it came out for (artist|title)

        class Bit
        {
            public float X, Y, VX, VY, Size, Angle, Spin;
            public Color Color;
            public int Kind;   // 0 chunk of fruit, 1 drop of juice, 2 seed, 3 bit of leaf
        }

        const double BoomTime = 1.3;
        bool exploding;
        double boomStart;
        readonly List<Bit> bits = new List<Bit>();
        Bitmap canvas;
        PointF boomAt;       // the burst's center, on the canvas
        float berrySize;     // the strawberry's size when it burst (the window grows for the pieces)

        public void Explode()
        {
            if (exploding) return;
            exploding = true;
            boomStart = clock.Elapsed.TotalSeconds;
            // A bigger window around the strawberry for the pieces to fly in.
            berrySize = Width;
            int side = Width * 5;
            var center = new PointF(x + Width / 2f, y + Height / 2f);
            x = center.X - side / 2f;
            y = center.Y - side / 2f;
            boomAt = new PointF(side / 2f, side / 2f);
            canvas = new Bitmap(side, side, PixelFormat.Format32bppPArgb);
            var rnd = new Random();
            Color light = palette[color][0], dark = palette[color][1];
            float unit = Width / 110f;
            Action<int, int, Color, float, float> spray = (kind, count, c, size, speed) =>
            {
                for (int i = 0; i < count; i++)
                {
                    double a = rnd.NextDouble() * Math.PI * 2;
                    float v = speed * unit * (0.35f + (float)rnd.NextDouble());
                    bits.Add(new Bit
                    {
                        X = boomAt.X + (float)(rnd.NextDouble() - 0.5) * Width * 0.4f, Y = boomAt.Y + (float)(rnd.NextDouble() - 0.5) * Width * 0.4f,
                        VX = (float)Math.Cos(a) * v, VY = (float)Math.Sin(a) * v - 160 * unit,
                        Size = size * unit * (0.6f + (float)rnd.NextDouble() * 0.8f), Angle = (float)(rnd.NextDouble() * 360),
                        Spin = (float)(rnd.NextDouble() - 0.5) * 900, Color = c, Kind = kind
                    });
                }
            };
            if (Kind == Beetle)
            {
                // Shards of shell, crumbs, glints of its shine and broken legs.
                spray(0, 16, Mix(dark, light, 0.35f), 22, 420);
                spray(0, 12, dark, 16, 520);
                spray(1, 22, dark, 7, 650);
                spray(2, 16, Color.FromArgb(230, 236, 255), 4, 600);
                spray(4, 10, Color.FromArgb(dark.R / 2, dark.G / 2, dark.B / 2), 26, 380);
            }
            else
            {
                spray(0, 14, light, 22, 420);
                spray(0, 10, dark, 16, 520);
                spray(1, 26, Color.FromArgb(dark.R * 3 / 4, dark.G * 3 / 4, dark.B * 3 / 4), 9, 650);
                spray(2, 22, Color.FromArgb(255, 222, 96), 5, 560);
                spray(3, 6, Color.FromArgb(80, 170, 60), 18, 380);
            }
        }

        static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(a.R + (int)((b.R - a.R) * t), a.G + (int)((b.G - a.G) * t), a.B + (int)((b.B - a.B) * t));
        }

        void StepBoom(double now)
        {
            float t = (float)(now - boomStart), dt = (float)Math.Min(0.05, now - last);
            last = now;
            if (t > BoomTime)
            {
                timer.Stop();
                Close();
                return;
            }
            float gravity = 1100 * berrySize / 110f;
            foreach (var b in bits)
            {
                b.X += b.VX * dt;
                b.Y += b.VY * dt;
                b.VY += gravity * dt;
                b.VX *= 0.985f;
                b.Angle += b.Spin * dt;
            }
            float fade = 1 - (float)Math.Pow(t / BoomTime, 2);
            Color light = palette[color][0];
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float berry = berrySize;
                // The flash, then a ring of juice spreading out.
                if (t < 0.18f)
                {
                    float r = berry * (0.6f + t * 5);
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(boomAt.X - r, boomAt.Y - r, 2 * r, 2 * r);
                        using (var pg = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(255 * (1 - t / 0.18f)), Color.White), SurroundColors = new[] { Color.FromArgb(0, light) } })
                            g.FillPath(pg, path);
                    }
                }
                float ring = berry * (0.5f + t * 3.2f);
                using (var p = new Pen(Color.FromArgb((int)(200 * fade * Math.Max(0, 1 - t / 0.8f)), light), berry * 0.08f * (1 - t / (float)BoomTime) + 1))
                    g.DrawEllipse(p, boomAt.X - ring, boomAt.Y - ring, 2 * ring, 2 * ring);
                foreach (var b in bits)
                {
                    int alpha = (int)(255 * fade);
                    var state = g.Save();
                    g.TranslateTransform(b.X, b.Y);
                    g.RotateTransform(b.Angle);
                    using (var brush = new SolidBrush(Color.FromArgb(alpha, b.Color)))
                    {
                        if (b.Kind == 0)
                        {
                            // A rough wedge of fruit with a seed on it, or a shard of shell with a glint.
                            g.FillPolygon(brush, new[] { new PointF(-b.Size / 2, -b.Size / 3), new PointF(b.Size / 2, -b.Size / 2), new PointF(b.Size / 3, b.Size / 2), new PointF(-b.Size / 2, b.Size / 3) });
                            var dot = Kind == Beetle ? Color.FromArgb(alpha / 2, 255, 255, 255) : Color.FromArgb(alpha, 255, 222, 96);
                            using (var seed = new SolidBrush(dot)) g.FillEllipse(seed, -b.Size * 0.1f, -b.Size * 0.15f, b.Size * 0.14f, b.Size * 0.22f);
                        }
                        else if (b.Kind == 4)
                        {
                            // A broken leg: two thin segments at an angle.
                            using (var p = new Pen(Color.FromArgb(alpha, b.Color), Math.Max(1.5f, b.Size * 0.1f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                                g.DrawLines(p, new[] { new PointF(-b.Size / 2, 0), new PointF(0, -b.Size * 0.12f), new PointF(b.Size / 2, b.Size * 0.18f) });
                        }
                        else if (b.Kind == 1)
                        {
                            float s = b.Size * (1 - t / (float)BoomTime * 0.6f);   // drops shrink as they fly
                            g.FillEllipse(brush, -s / 2, -s / 2, s, s);
                        }
                        else if (b.Kind == 2) g.FillEllipse(brush, -b.Size / 3, -b.Size / 2, b.Size * 0.66f, b.Size);
                        else g.FillEllipse(brush, -b.Size / 2, -b.Size / 5, b.Size, b.Size * 0.4f);
                    }
                    g.Restore(state);
                }
            }
            Present(canvas);
        }

        void Step()
        {
            double now = clock.Elapsed.TotalSeconds;
            if (exploding) { StepBoom(now); return; }
            float dt = (float)Math.Min(0.05, now - last);
            last = now;
            x += vx * dt;
            y += vy * dt;
            // Bounce off the edges of whichever screen it's on, changing color each time (a corner counts once).
            var area = Screen.FromPoint(new Point((int)x + Width / 2, (int)y + Height / 2)).WorkingArea;
            bool bounced = false;
            if (x < area.Left) { x = area.Left; vx = Math.Abs(vx); bounced = true; }
            if (x > area.Right - Width) { x = area.Right - Width; vx = -Math.Abs(vx); bounced = true; }
            if (y < area.Top) { y = area.Top; vy = Math.Abs(vy); bounced = true; }
            if (y > area.Bottom - Height) { y = area.Bottom - Height; vy = -Math.Abs(vy); bounced = true; }
            if (bounced) color = (color + 1) % berries.Length;
            Render();
        }

        // Shows the strawberry, in its current color, at its place on screen.
        void Render() { Present(berries[color]); }

        // Puts a picture (with its see-through parts) on screen at x, y.
        void Present(Bitmap frame)
        {
            if (!IsHandleCreated) return;
            IntPtr screen = Native.GetDC(IntPtr.Zero), mem = Native.CreateCompatibleDC(screen);
            IntPtr hbmp = frame.GetHbitmap(Color.FromArgb(0)), old = Native.SelectObject(mem, hbmp);
            try
            {
                var pos = new Point((int)x, (int)y);
                var size = frame.Size;
                var src = Point.Empty;
                var blend = new BlendFunction { Op = 0, Flags = 0, Alpha = 255, Format = 1 };   // per-pixel alpha
                UpdateLayeredWindow(Handle, screen, ref pos, ref size, mem, ref src, 0, ref blend, 2);
            }
            finally
            {
                Native.SelectObject(mem, old);
                Native.DeleteObject(hbmp);
                Native.DeleteDC(mem);
                Native.ReleaseDC(IntPtr.Zero, screen);
            }
        }

        // A ripe strawberry: a glossy red heart shape dotted with golden seeds, a crown of green leaves and a stem.
        static Bitmap PaintBerry(int s, Color light, Color dark)
        {
            var b = new Bitmap(s, s, PixelFormat.Format32bppPArgb);
            Func<float, float, PointF> P = (u, v) => new PointF(u * s, v * s);
            using (var g = Graphics.FromImage(b))
            using (var body = new GraphicsPath())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                body.AddBezier(P(0.50f, 0.26f), P(0.72f, 0.15f), P(0.96f, 0.30f), P(0.89f, 0.52f));
                body.AddBezier(P(0.89f, 0.52f), P(0.83f, 0.72f), P(0.62f, 0.92f), P(0.50f, 0.97f));
                body.AddBezier(P(0.50f, 0.97f), P(0.38f, 0.92f), P(0.17f, 0.72f), P(0.11f, 0.52f));
                body.AddBezier(P(0.11f, 0.52f), P(0.04f, 0.30f), P(0.28f, 0.15f), P(0.50f, 0.26f));
                body.CloseFigure();
                using (var pg = new PathGradientBrush(body) { CenterPoint = P(0.40f, 0.42f), CenterColor = light, SurroundColors = new[] { dark } })
                    g.FillPath(pg, body);
                using (var p = new Pen(Color.FromArgb(dark.R * 2 / 3, dark.G * 2 / 3, dark.B * 2 / 3), s * 0.018f)) g.DrawPath(p, body);

                // Seeds in staggered rows, each a little golden oval in a darker dimple.
                for (int row = 0; row < 8; row++)
                {
                    float v = 0.36f + row * 0.075f;
                    for (int col = 0; col < 9; col++)
                    {
                        float u = 0.14f + col * 0.09f + (row % 2) * 0.045f;
                        var at = P(u, v);
                        if (!body.IsVisible(at.X, at.Y) || !body.IsVisible(at.X, at.Y + s * 0.05f)) continue;
                        using (var dimple = new SolidBrush(Color.FromArgb(150, dark.R / 2, dark.G / 2, dark.B / 2))) g.FillEllipse(dimple, at.X - s * 0.022f, at.Y - s * 0.03f, s * 0.044f, s * 0.06f);
                        using (var seed = new SolidBrush(Color.FromArgb(255, 222, 96))) g.FillEllipse(seed, at.X - s * 0.014f, at.Y - s * 0.024f, s * 0.028f, s * 0.044f);
                    }
                }
                // A soft shine on the upper left.
                using (var shine = new SolidBrush(Color.FromArgb(80, 255, 255, 255))) g.FillEllipse(shine, s * 0.2f, s * 0.3f, s * 0.16f, s * 0.11f);

                // The leaves: five pointed ones fanning out over the shoulders, then the stem.
                var top = P(0.5f, 0.25f);
                foreach (float angle in new[] { 160f, 125f, 90f, 55f, 20f })
                {
                    var state = g.Save();
                    g.TranslateTransform(top.X, top.Y);
                    g.RotateTransform(angle);
                    using (var leaf = new GraphicsPath())
                    {
                        leaf.AddBezier(0, 0, s * 0.08f, -s * 0.06f, s * 0.2f, -s * 0.03f, s * 0.27f, 0);
                        leaf.AddBezier(s * 0.27f, 0, s * 0.2f, s * 0.03f, s * 0.08f, s * 0.06f, 0, 0);
                        using (var lg = new LinearGradientBrush(new RectangleF(0, -s * 0.06f, s * 0.27f, s * 0.12f), Color.FromArgb(46, 120, 44), Color.FromArgb(104, 196, 72), 0f))
                            g.FillPath(lg, leaf);
                        using (var p = new Pen(Color.FromArgb(30, 84, 32), s * 0.012f)) g.DrawPath(p, leaf);
                    }
                    g.Restore(state);
                }
                using (var stem = new Pen(Color.FromArgb(76, 128, 52), s * 0.045f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(stem, top.X, top.Y, top.X + s * 0.04f, top.Y - s * 0.17f);
            }
            return b;
        }

        // An atlas beetle seen from above, head up, shaded to look like the real insect: a glossy, near-black shell with
        // a cool sheen (the wing cases split down the middle, a broad shield behind the head), two long slender horns
        // curving up and in from the shield's front corners with a toothed horn rising between them, and six jointed
        // legs: a thick thigh, a spiny shin, a beaded foot and a pair of claws.
        static Bitmap PaintBeetle(int s, Color light, Color dark)
        {
            var b = new Bitmap(s, s, PixelFormat.Format32bppPArgb);
            Func<float, float, PointF> P = (u, v) => new PointF(u * s, v * s);
            Color mid = Mix(dark, light, 0.45f), edge = Color.FromArgb(dark.R / 2, dark.G / 2, dark.B / 2);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // A rounded limb piece from a to b, tapering from width wa to wb, shaded like a cylinder (light along
                // one side, dark at the edges), with a small knob at the joint.
                Action<PointF, PointF, float, float> limb = (a, c, wa, wc) =>
                {
                    float dx = c.X - a.X, dy = c.Y - a.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (len < 0.5f) return;
                    float nx = -dy / len, ny = dx / len;
                    var pts = new[]
                    {
                        new PointF(a.X + nx * wa / 2, a.Y + ny * wa / 2), new PointF(c.X + nx * wc / 2, c.Y + ny * wc / 2),
                        new PointF(c.X - nx * wc / 2, c.Y - ny * wc / 2), new PointF(a.X - nx * wa / 2, a.Y - ny * wa / 2)
                    };
                    using (var lg = new LinearGradientBrush(new PointF(a.X + nx * wa / 2 + 0.01f, a.Y + ny * wa / 2), new PointF(a.X - nx * wa / 2, a.Y - ny * wa / 2), dark, dark))
                    {
                        lg.InterpolationColors = new ColorBlend { Colors = new[] { edge, mid, dark, edge }, Positions = new[] { 0f, 0.3f, 0.6f, 1f } };
                        lg.WrapMode = WrapMode.TileFlipXY;
                        g.FillPolygon(lg, pts);
                    }
                    using (var knob = new SolidBrush(mid)) g.FillEllipse(knob, c.X - wc * 0.6f, c.Y - wc * 0.6f, wc * 1.2f, wc * 1.2f);
                };

                // The legs, each side mirrored: hip, knee, ankle and the foot's tip.
                float[][] legs =
                {
                    new[] { 0.38f, 0.44f, 0.23f, 0.40f, 0.12f, 0.31f, 0.045f, 0.26f },   // front: long, reaching forward
                    new[] { 0.37f, 0.54f, 0.21f, 0.57f, 0.145f, 0.70f, 0.105f, 0.80f },  // middle: out to the side
                    new[] { 0.40f, 0.64f, 0.275f, 0.75f, 0.24f, 0.875f, 0.215f, 0.975f }, // back: trailing
                };
                foreach (int side in new[] { 1, -1 })
                    foreach (var l in legs)
                    {
                        Func<int, PointF> at = k => P(side > 0 ? l[2 * k] : 1 - l[2 * k], l[2 * k + 1]);
                        PointF hip = at(0), knee = at(1), ankle = at(2), tip = at(3);
                        limb(hip, knee, s * 0.036f, s * 0.028f);     // thigh
                        limb(knee, ankle, s * 0.024f, s * 0.016f);   // shin
                        // Spines along the outer edge of the shin.
                        using (var spine = new Pen(edge, s * 0.008f) { EndCap = LineCap.Round })
                            for (int k = 1; k <= 3; k++)
                            {
                                var p = new PointF(knee.X + (ankle.X - knee.X) * k / 4f, knee.Y + (ankle.Y - knee.Y) * k / 4f);
                                g.DrawLine(spine, p, new PointF(p.X + side * -s * 0.022f, p.Y + s * 0.004f));
                            }
                        // The foot: a row of small beads getting smaller, then two hooked claws.
                        for (int k = 0; k < 5; k++)
                        {
                            float t0 = k / 5f, t1 = (k + 0.85f) / 5f;
                            limb(new PointF(ankle.X + (tip.X - ankle.X) * t0, ankle.Y + (tip.Y - ankle.Y) * t0),
                                 new PointF(ankle.X + (tip.X - ankle.X) * t1, ankle.Y + (tip.Y - ankle.Y) * t1), s * (0.016f - k * 0.0015f), s * (0.013f - k * 0.0015f));
                        }
                        float fx = tip.X - ankle.X, fy = tip.Y - ankle.Y, fl = (float)Math.Sqrt(fx * fx + fy * fy);
                        fx /= fl; fy /= fl;
                        using (var claw = new Pen(edge, s * 0.008f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            foreach (float turn in new[] { 0.6f, -0.6f })
                            {
                                float cx = fx * (float)Math.Cos(turn) - fy * (float)Math.Sin(turn), cy = fx * (float)Math.Sin(turn) + fy * (float)Math.Cos(turn);
                                g.DrawBezier(claw, tip, new PointF(tip.X + fx * s * 0.02f, tip.Y + fy * s * 0.02f),
                                             new PointF(tip.X + cx * s * 0.035f, tip.Y + cy * s * 0.035f), new PointF(tip.X + cx * s * 0.028f + fx * s * 0.01f, tip.Y + cy * s * 0.028f + fy * s * 0.01f));
                            }
                    }

                // Glossy shading for a body part: lit from the upper left, dark round the edge, a thin dark outline.
                Action<GraphicsPath, PointF> shell = (path, lit) =>
                {
                    using (var pg = new PathGradientBrush(path) { CenterPoint = lit, CenterColor = mid, SurroundColors = new[] { dark } })
                    {
                        pg.FocusScales = new PointF(0.25f, 0.25f);
                        g.FillPath(pg, path);
                    }
                    using (var p = new Pen(edge, s * 0.008f)) g.DrawPath(p, path);
                };

                // The wing cases, with the seam down the middle and a cool rim of light along the left side.
                var wingBox = new RectangleF(P(0.32f, 0.465f).X, P(0.32f, 0.465f).Y, s * 0.36f, s * 0.45f);
                using (var wings = new GraphicsPath())
                {
                    wings.AddEllipse(wingBox);
                    shell(wings, P(0.42f, 0.58f));
                }
                using (var rim = new Pen(Color.FromArgb(120, light), s * 0.012f)) g.DrawArc(rim, RectangleF.Inflate(wingBox, -s * 0.012f, -s * 0.012f), 120, 110);
                using (var seam = new Pen(edge, s * 0.009f)) g.DrawLine(seam, P(0.5f, 0.505f), P(0.5f, 0.885f));
                using (var seamLight = new Pen(Color.FromArgb(70, light), s * 0.006f)) g.DrawLine(seamLight, P(0.49f, 0.52f), P(0.49f, 0.84f));

                // The two long horns: slender, tapering, curving up and in, with a highlight running along each.
                foreach (int side in new[] { 1, -1 })
                {
                    Func<float, float, PointF> H = (u, v) => P(side > 0 ? u : 1 - u, v);
                    using (var horn = new GraphicsPath())
                    {
                        horn.AddBezier(H(0.33f, 0.40f), H(0.23f, 0.30f), H(0.27f, 0.10f), H(0.36f, 0.005f));
                        horn.AddBezier(H(0.36f, 0.005f), H(0.295f, 0.11f), H(0.27f, 0.29f), H(0.37f, 0.38f));
                        horn.CloseFigure();
                        shell(horn, H(0.28f, 0.22f));
                    }
                    using (var glint = new Pen(Color.FromArgb(110, light), s * 0.007f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawBezier(glint, H(0.30f, 0.36f), H(0.235f, 0.26f), H(0.255f, 0.12f), H(0.33f, 0.05f));
                }

                // The shield behind the head: broad, with rounded shoulders and a bright sheen.
                using (var shield = new GraphicsPath())
                {
                    shield.AddBezier(P(0.29f, 0.475f), P(0.255f, 0.375f), P(0.33f, 0.325f), P(0.50f, 0.325f));
                    shield.AddBezier(P(0.50f, 0.325f), P(0.67f, 0.325f), P(0.745f, 0.375f), P(0.71f, 0.475f));
                    shield.AddBezier(P(0.71f, 0.475f), P(0.62f, 0.525f), P(0.38f, 0.525f), P(0.29f, 0.475f));
                    shield.CloseFigure();
                    shell(shield, P(0.43f, 0.38f));
                }

                // The head with its horn: thick at the base, rising between the others, a tooth partway up, a notched tip.
                using (var head = new GraphicsPath())
                {
                    head.AddBezier(P(0.455f, 0.345f), P(0.445f, 0.25f), P(0.47f, 0.16f), P(0.487f, 0.085f));
                    head.AddLine(P(0.487f, 0.085f), P(0.50f, 0.105f));
                    head.AddLine(P(0.50f, 0.105f), P(0.513f, 0.08f));
                    head.AddBezier(P(0.513f, 0.08f), P(0.522f, 0.13f), P(0.53f, 0.17f), P(0.535f, 0.195f));
                    head.AddLine(P(0.535f, 0.195f), P(0.552f, 0.205f));   // the tooth
                    head.AddBezier(P(0.552f, 0.205f), P(0.545f, 0.24f), P(0.55f, 0.29f), P(0.545f, 0.345f));
                    head.CloseFigure();
                    shell(head, P(0.485f, 0.22f));
                }
                using (var glint = new Pen(Color.FromArgb(110, light), s * 0.006f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(glint, P(0.478f, 0.31f), P(0.49f, 0.13f));

                // Gloss: soft highlights where the light catches the shell.
                using (var gloss = new GraphicsPath())
                {
                    gloss.AddEllipse(P(0.355f, 0.53f).X, P(0.355f, 0.53f).Y, s * 0.075f, s * 0.19f);
                    using (var pg = new PathGradientBrush(gloss) { CenterColor = Color.FromArgb(120, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } })
                        g.FillPath(pg, gloss);
                }
                using (var gloss = new GraphicsPath())
                {
                    gloss.AddEllipse(P(0.37f, 0.35f).X, P(0.37f, 0.35f).Y, s * 0.13f, s * 0.05f);
                    using (var pg = new PathGradientBrush(gloss) { CenterColor = Color.FromArgb(110, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } })
                        g.FillPath(pg, gloss);
                }
            }
            return b;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                foreach (var b in berries) b.Dispose();
                if (canvas != null) canvas.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------ main window

    // The main window's page: what's playing (album art, title, artist, album, the player and a progress bar you can
    // click to jump along the song) and, under it, a box with two tabs: a big visualizer in the app's accent color,
    // and the equalizer's ten band sliders.
    class VisualizerPage : Control
    {
        public readonly VizEngine Engine = new VizEngine();
        public int Style;
        public const int VizTab = 0, EqTab = 1, LyricsTab = 2, PlaylistsTab = 3, SecretTab = 4, InfoTab = 5, StatusTab = 6, TabCount = 7;
        public int Tab;                          // what the lower box shows
        bool ShowEq { get { return Tab == EqTab; } }
        public event Action<TimeSpan> SeekRequested;
        public event Action TabChanged;          // the user picked the other tab
        public event Action EqChanged;           // a band moved (apply it)
        public event Action EqCommitted;         // a band was let go (save it)
        NowPlayingInfo info = new NowPlayingInfo();
        Rectangle progress;
        bool progressHover;

        public VisualizerPage()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            // Where a status is typed (the Status tab): Enter adds it (or saves the one being edited), Escape cancels.
            statusInput = new TextBox
            {
                BorderStyle = BorderStyle.None, Font = Theme.Body, MaxLength = VrcChatbox.StatusLength, Visible = false,
                BackColor = Color.FromArgb(12, 13, 17), ForeColor = Theme.Bright
            };
            statusInput.HandleCreated += (s, e) => SendMessage(statusInput.Handle, 0x1501, (IntPtr)1, "Type a status and press Enter");   // EM_SETCUEBANNER
            statusInput.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitStatus(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CancelStatusEdit(); }
            };
            statusInput.TextChanged += (s, e) => Invalidate(VizBox());   // the character count
            statusInput.GotFocus += (s, e) => Invalidate(VizBox());
            statusInput.LostFocus += (s, e) => Invalidate(VizBox());
            Controls.Add(statusInput);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);

        public void SetInfo(NowPlayingInfo next)
        {
            if (next.Title != info.Title || next.Artist != info.Artist)
            {
                lyricScroll = float.NaN;   // a new song: its lyrics start at the top
                manualUntil = DateTime.MinValue;
            }
            info = next;
            Invalidate();
        }

        Rectangle TrackBox()
        {
            int y = Theme.S(16) + Theme.PageTitle.Height + Theme.S(8) + Theme.Body.Height + Theme.S(16);
            return new Rectangle(Theme.S(20), y, Math.Max(0, Width - Theme.S(34)), Theme.S(232));
        }

        Rectangle VizBox()
        {
            var t = TrackBox();
            int y = t.Bottom + Theme.S(16);
            return new Rectangle(t.X, y, t.Width, Math.Max(0, Height - Theme.S(12) - y));
        }

        Rectangle VizArea
        {
            get
            {
                var b = VizBox();
                return new Rectangle(b.X + Theme.S(18), b.Y + Theme.S(52), Math.Max(0, b.Width - Theme.S(36)), Math.Max(0, b.Height - Theme.S(70)));
            }
        }

        // About 30 times a second: the visualizer moves on (to the player's loudness) and only its area is redrawn.
        public void Animate(float level)
        {
            Engine.Update(info.Playing ? level : 0);
            var a = VizArea;
            if (ShowsViz && a.Width > 0 && a.Height > 0) Invalidate(Rectangle.Inflate(a, Theme.S(8), Theme.S(8)));
            if (!holoArea.IsEmpty) Invalidate(Rectangle.Inflate(holoArea, Theme.S(8), 0));   // the skull keeps turning (glitches shift it sideways)
            if (ShowsLyrics && a.Width > 0 && a.Height > 0 && StepLyrics()) Invalidate(a);
        }

        // Lyrics + Visualizer: the lyrics over the visualizer (dimmed behind them), on its tab and on the Lyrics tab.
        public bool LyricsWithViz;
        bool ShowsViz { get { return Tab == VizTab || (Tab == LyricsTab && LyricsWithViz); } }
        bool ShowsLyrics { get { return Tab == LyricsTab || (Tab == VizTab && LyricsWithViz); } }

        // Lyrics + Visualizer was switched on or off.
        public void LyricsViewChanged()
        {
            lyricScroll = float.NaN;
            Invalidate(VizBox());
        }

        public void ShowTab(int tab) { SetTab(tab); }

        // The window is hidden: what only drawing it needs goes (it's made again when it's shown).
        public void Release()
        {
            Engine.Release();
            skull.Release();
            foreach (var p in secretPictures.Values) p.Dispose();
            secretPictures.Clear();
        }

        // Twice a second: the progress bar and times (and the equalizer, which notices Equalizer APO being installed).
        public void Pulse()
        {
            Invalidate(TrackBox());
            if (Tab != VizTab) Invalidate(VizBox());
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.Paint(e.Graphics, this); }

        static string Time(TimeSpan t)
        {
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                                     : string.Format("{0}:{1:00}", (int)t.TotalMinutes, t.Seconds);
        }

        const TextFormatFlags OneLine = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            bool any = !string.IsNullOrEmpty(info.Title);
            int x = Theme.S(20), y = Theme.S(16);
            Theme.DrawSpaced(g, "Now Playing", Theme.PageTitle, Theme.Bright, x, y, Theme.S(3));
            y += Theme.PageTitle.Height + Theme.S(8);
            string line = !any ? "Nothing playing. Start a song in Spotify, YouTube, YouTube Music or any player and it shows up here."
                        : (info.Playing ? "Playing" : "Paused") + (info.App != "" ? " in " + info.App : "") +
                          (info.Volume >= 0 ? "  ·  volume " + (int)Math.Round(info.Volume * 100) + "%" : "");
            TextRenderer.DrawText(g, line, Theme.Body, new Rectangle(x, y, Width - x - Theme.S(20), Theme.Body.Height + Theme.S(4)), Theme.Dim, OneLine);

            // The track: album art and the song's details.
            var box = TrackBox();
            using (var b = new SolidBrush(Theme.Glass)) g.FillRectangle(b, box);
            UiDraw.Box(g, box, Theme.S(2), Theme.Primary);
            int side = box.Height - Theme.S(36);
            var art = new Rectangle(box.X + Theme.S(18), box.Y + Theme.S(18), side, side);
            if (info.Art != null)
            {
                int s = Math.Min(info.Art.Width, info.Art.Height);
                var mode = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(info.Art, art, new Rectangle((info.Art.Width - s) / 2, (info.Art.Height - s) / 2, s, s), GraphicsUnit.Pixel);
                g.InterpolationMode = mode;
            }
            else DrawFootprint(g, art);
            UiDraw.Box(g, Rectangle.Inflate(art, Theme.S(1), Theme.S(1)), Theme.S(1), Theme.Faint);

            int tx = art.Right + Theme.S(26), tw = Math.Max(0, box.Right - Theme.S(24) - tx);
            string[] labels = { "Previous", info.Playing ? "Pause" : "Play", "Next" };
            int buttonsW = labels.Sum(s => UiDraw.TabButtonWidth(g, s, Theme.ButtonSmall)) + Theme.S(12) * (labels.Length - 1);

            // The skull hologram, turning in the space to the right (above the progress bar); the song's details stop
            // short of it.
            int holoRight = box.Right - Theme.S(18), holoLeft = Math.Max(tx + buttonsW + Theme.S(30), holoRight - Theme.S(340));
            holoArea = holoRight - holoLeft >= Theme.S(120)
                     ? new Rectangle(holoLeft, box.Y + Theme.S(8), holoRight - holoLeft, art.Bottom - Theme.S(40) - box.Y - Theme.S(8))
                     : Rectangle.Empty;
            int textW = holoArea.IsEmpty ? tw : holoArea.X - Theme.S(12) - tx;
            if (!holoArea.IsEmpty && g.ClipBounds.IntersectsWith(holoArea)) skull.Draw(g, holoArea);

            int ty = box.Y + Theme.S(22);
            TextRenderer.DrawText(g, any ? info.Title : "Nothing Playing", Theme.Stat, new Rectangle(tx, ty, textW, Theme.Stat.Height + Theme.S(4)), Theme.Bright, OneLine);
            ty += Theme.Stat.Height + Theme.S(10);
            if (any)
            {
                TextRenderer.DrawText(g, info.Artist, Theme.TileValue, new Rectangle(tx, ty, textW, Theme.TileValue.Height + Theme.S(4)), Theme.Primary, OneLine);
                ty += Theme.TileValue.Height + Theme.S(8);
                TextRenderer.DrawText(g, info.Album, Theme.Body, new Rectangle(tx, ty, textW, Theme.Body.Height + Theme.S(4)), Theme.Dim, OneLine);
            }

            // The player's buttons, in the space above the progress bar (grayed out while nothing is playing).
            int buttonH = Theme.S(40), bx = tx, buttonY = art.Bottom - Theme.S(30) - Theme.S(14) - buttonH;
            for (int k = 0; k < labels.Length; k++)
            {
                int w = UiDraw.TabButtonWidth(g, labels[k], Theme.ButtonSmall);
                playerButtons[k] = new Rectangle(bx, buttonY, w, buttonH);
                UiDraw.TabButton(g, playerButtons[k], labels[k], Theme.ButtonSmall, k == playerHover, !any);
                bx += w + Theme.S(12);
            }

            // The progress bar: click or drag along it to jump.
            progress = Rectangle.Empty;
            if (any && info.Duration > TimeSpan.Zero)
            {
                var now = info.Now;
                int by = art.Bottom - Theme.S(30);
                progress = new Rectangle(tx, by, tw, Theme.S(12));
                using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, progress);
                float f = (float)Math.Max(0, Math.Min(1, now.TotalSeconds / info.Duration.TotalSeconds));
                using (var b = new SolidBrush(progressHover ? Theme.Bright : Theme.Primary)) g.FillRectangle(b, progress.X, progress.Y, (int)(progress.Width * f), progress.Height);
                UiDraw.Box(g, progress, 1, Theme.Dim);
                int ty2 = progress.Bottom + Theme.S(8);
                Theme.DrawSpaced(g, Time(now), Theme.TileTitle, Theme.Primary, tx, ty2, Theme.S(1));
                string total = Time(info.Duration);
                Theme.DrawSpaced(g, total, Theme.TileTitle, Theme.Dim, progress.Right - Theme.SpacedWidth(g, total, Theme.TileTitle, Theme.S(1)), ty2, Theme.S(1));
            }

            // The lower box: the visualizer, the equalizer or the lyrics, picked with the tabs in its corner.
            var vbox = VizBox();
            vizTab = eqTab = lyricsTab = infoTab = playlistsTab = secretTab = statusTab = Rectangle.Empty;
            clicks.Clear();
            bool typing = false;   // the status box is on show
            if (vbox.Height > Theme.S(90))
            {
                using (var b = new SolidBrush(Theme.Glass)) g.FillRectangle(b, vbox);
                UiDraw.Box(g, vbox, Theme.S(2), Theme.Primary);
                int tabX = vbox.X + Theme.S(18), tabY = vbox.Y + Theme.S(18);
                // The tabs move closer together when the window is narrow; narrower still, the long names are
                // shortened, and then the Status tab's switch is left out (it's in the tab too).
                int switchH = Theme.S(26), switchW = ButtonWidth(g, "Off") + Theme.S(4), room = vbox.Width - Theme.S(36);
                Func<string[], int> tabsWidth = list => list.Sum(t => Theme.SpacedWidth(g, t, Theme.TileTitle, Theme.S(1)) + Theme.S(12));
                var names = new[] { "Visualizer", "Equalizer", "Lyrics", "Info", "Playlists", "Secret", "Status" };
                if (tabsWidth(names) + 2 * switchW + 6 * Theme.S(10) > room) names = new[] { "Viz", "EQ", "Lyrics", "Info", "Lists", "Secret", "Status" };
                bool statusSwitchFits = tabsWidth(names) + 2 * switchW + 6 * Theme.S(10) <= room;
                int gap = Math.Max(Theme.S(10), Math.Min(Theme.S(28), (room - tabsWidth(names) - (statusSwitchFits ? 2 : 1) * switchW) / 6));
                vizTab = DrawTab(g, names[0], tabX, tabY, Tab == VizTab, hoverTab == VizTab);
                eqTab = DrawTab(g, names[1], vizTab.Right + gap, tabY, Tab == EqTab, hoverTab == EqTab);
                lyricsTab = DrawTab(g, names[2], eqTab.Right + gap, tabY, Tab == LyricsTab, hoverTab == LyricsTab);
                infoTab = DrawTab(g, names[3], lyricsTab.Right + gap, tabY, Tab == InfoTab, hoverTab == InfoTab);
                playlistsTab = DrawTab(g, names[4], infoTab.Right + gap, tabY, Tab == PlaylistsTab, hoverTab == PlaylistsTab);
                secretTab = DrawTab(g, names[5], playlistsTab.Right + gap, tabY, Tab == SecretTab, hoverTab == SecretTab);
                // Next to it, a switch for all the secrets at once.
                bool anyOn = BouncerWindow.IsOn(BouncerWindow.Strawberry) || BouncerWindow.IsOn(BouncerWindow.Beetle);
                var secretSwitch = Button(g, anyOn ? "On" : "Off", secretTab.Right + Theme.S(4), tabY - (switchH - Theme.TileTitle.Height) / 2, switchH,
                       anyOn ? Theme.Primary : Theme.Faint, () =>
                       {
                           BouncerWindow.SetOn(BouncerWindow.Strawberry, !anyOn);
                           BouncerWindow.SetOn(BouncerWindow.Beetle, !anyOn);
                           SecretChanged();
                       });
                // The VRChat chatbox status, with its own switch.
                statusTab = DrawTab(g, names[6], secretSwitch.Right + gap, tabY, Tab == StatusTab, hoverTab == StatusTab);
                bool statusOn = VrcChatbox.StatusOn;
                var statusSwitch = !statusSwitchFits ? statusTab
                    : Button(g, statusOn ? "On" : "Off", statusTab.Right + Theme.S(4), tabY - (switchH - Theme.TileTitle.Height) / 2, switchH,
                             statusOn ? Theme.Primary : Theme.Faint, () => { VrcChatbox.StatusOn = !statusOn; Invalidate(VizBox()); });
                string name = Tab == EqTab ? (!Equalizer.Installed ? "Not Installed" : !Equalizer.Enabled ? "Off" : PresetLabel())
                            : Tab == LyricsTab ? LyricsLabel()
                            : Tab == PlaylistsTab ? PlaylistsLabel()
                            : Tab == SecretTab ? SecretLabel()
                            : Tab == InfoTab ? InfoLabel()
                            : Tab == StatusTab ? StatusLabel()
                            : VizEngine.Names[Math.Max(0, Math.Min(VizEngine.Names.Length - 1, Style))];
                // On the right, in whatever room the tabs leave (shortened if it's tight).
                int nameX = statusSwitch.Right + Theme.S(16);
                if (vbox.Right - Theme.S(18) - nameX > Theme.S(100))
                    TextRenderer.DrawText(g, name, Theme.TileValue, new Rectangle(nameX, vbox.Y + Theme.S(8), vbox.Right - Theme.S(18) - nameX, Theme.S(40)),
                        Theme.Bright, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                var area = VizArea;
                if (area.Width > 0 && area.Height > 0)
                {
                    if (Tab == EqTab) DrawEq(g, area);
                    else if (Tab == PlaylistsTab) DrawPlaylists(g, area);
                    else if (Tab == SecretTab) DrawSecret(g, area);
                    else if (Tab == InfoTab) DrawInfo(g, area);
                    else if (Tab == StatusTab) typing = DrawStatus(g, area);
                    else
                    {
                        if (ShowsViz)
                        {
                            var palette = ColorMix.Palette();
                            if (palette != null) Engine.DrawMulti(g, area, Style, Math.Max(1.5f, Theme.Scale * 2), palette);
                            else Engine.Draw(g, area, Style, Math.Max(1.5f, Theme.Scale * 2), VizColors.Accent());
                        }
                        if (ShowsLyrics)
                        {
                            // With the visualizer behind, it's dimmed so the words read clearly.
                            if (ShowsViz) using (var b = new SolidBrush(Color.FromArgb(140, 2, 3, 5))) g.FillRectangle(b, area);
                            DrawLyrics(g, area);
                        }
                    }
                }
            }
            if (statusInput.Visible != typing) statusInput.Visible = typing;
            UiDraw.Scanlines(g, this);
        }

        static string PresetLabel()
        {
            int p = Equalizer.PresetIndex;
            return p >= 0 ? Equalizer.PresetNames[p] : "Custom";
        }

        // A tab label: bright and underlined when it's the one showing. Returns where it is (for clicks).
        static Rectangle DrawTab(Graphics g, string text, int x, int y, bool active, bool hover)
        {
            int w = Theme.SpacedWidth(g, text, Theme.TileTitle, Theme.S(1));
            Theme.DrawSpaced(g, text, Theme.TileTitle, active ? Theme.Bright : hover ? Theme.Primary : Theme.Dim, x, y, Theme.S(1));
            if (active) using (var b = new SolidBrush(Theme.Primary)) g.FillRectangle(b, x, y + Theme.TileTitle.Height + Theme.S(4), w, Theme.S(2));
            return new Rectangle(x - Theme.S(6), y - Theme.S(8), w + Theme.S(12), Theme.TileTitle.Height + Theme.S(18));
        }

        // ---- The equalizer's sliders: one per band, -12 to +12 dB, with the response curve drawn through the knobs.

        Rectangle vizTab, eqTab, lyricsTab, infoTab, playlistsTab, secretTab, statusTab, getButton;
        readonly Rectangle[] eqColumns = new Rectangle[10];
        int hoverTab = -1, eqHover = -1, eqDrag = -1, eqTop, eqBottom;
        bool getHover;

        int YFor(float db) { return eqTop + (int)Math.Round((Equalizer.MaxGain - db) / (2 * Equalizer.MaxGain) * (eqBottom - eqTop)); }

        void DrawEq(Graphics g, Rectangle area)
        {
            getButton = Rectangle.Empty;
            for (int i = 0; i < eqColumns.Length; i++) eqColumns[i] = Rectangle.Empty;
            if (!Equalizer.Installed) { DrawEqInstall(g, area); return; }

            bool on = Equalizer.Enabled;
            int n = Equalizer.Bands.Length, scaleW = Theme.S(84), sp = Theme.S(1), small = Theme.TileTitle.Height;
            var cols = new Rectangle(area.X + scaleW, area.Y, area.Width - scaleW, area.Height);

            // From the top: a line explaining what's under the mouse (or how it works), the values, the sliders,
            // then each band's frequency, its plain name, and the Bass / Mids / Treble groups.
            int infoH = Theme.Body.Height * 2 + Theme.S(8);
            int valueY = area.Y + infoH + Theme.S(10);
            int groupY = area.Bottom - small, nameY = groupY - small - Theme.S(10), freqY = nameY - small - Theme.S(6);
            eqTop = valueY + small + Theme.S(16);
            eqBottom = freqY - Theme.S(16);
            if (eqBottom - eqTop < Theme.S(40)) return;

            int focus = eqDrag >= 0 ? eqDrag : eqHover;
            string info;
            if (focus >= 0)
            {
                float gain = Equalizer.Gains[focus];
                string now = Math.Abs(gain) < 0.05f ? "now unchanged (0 dB)"
                           : "now " + Math.Abs(gain).ToString("0.0", CultureInfo.InvariantCulture) + " dB " + (gain > 0 ? "louder" : "quieter");
                info = Equalizer.BandNames[focus] + "  " + Equalizer.BandTitles[focus] + " (" + now + "): " + Equalizer.BandHelp[focus];
            }
            else info = "Each slider makes one range of the sound louder (up) or quieter (down); 0 dB = unchanged. Low notes are on the left, " +
                        "high on the right. Hover a slider to see what it changes; double-click resets it.";
            TextRenderer.DrawText(g, info, Theme.Body, new Rectangle(area.X, area.Y, area.Width, infoH), focus >= 0 ? Theme.Bright : Theme.Dim,
                TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            // The dB scale, with faint guide lines across, and which way is louder.
            foreach (var db in new[] { 12, 6, 0, -6, -12 })
            {
                int y = YFor(db);
                string s = (db > 0 ? "+" : "") + db + " dB";
                Theme.DrawSpaced(g, s, Theme.TileTitle, db == 0 ? Theme.Primary : Theme.Dim, area.X + scaleW - Theme.S(14) - Theme.SpacedWidth(g, s, Theme.TileTitle, sp), y - small / 2, sp);
                using (var b = new SolidBrush(Color.FromArgb(db == 0 ? 90 : 40, Theme.Primary))) g.FillRectangle(b, cols.X, y, cols.Width, Theme.S(1));
            }
            Theme.DrawSpaced(g, "▲ Louder", Theme.TileTitle, Theme.Primary, area.X, valueY, sp);
            Theme.DrawSpaced(g, "▼ Quieter", Theme.TileTitle, Theme.Primary, area.X, freqY, sp);

            float cw = cols.Width / (float)n;
            var knobs = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                int cx = (int)(cols.X + cw * (i + 0.5f));
                knobs[i] = new PointF(cx, YFor(Equalizer.Gains[i]));
                eqColumns[i] = new Rectangle((int)(cols.X + cw * i), eqTop - Theme.S(12), (int)cw, eqBottom - eqTop + Theme.S(24));
            }

            // The response curve behind the sliders.
            var smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var glow = new Pen(Color.FromArgb(on ? 70 : 30, Theme.Primary), Theme.S(6)) { LineJoin = LineJoin.Round }) g.DrawCurve(glow, knobs, 0.5f);
            using (var line = new Pen(on ? Theme.Bright : Theme.Faint, Theme.S(2)) { LineJoin = LineJoin.Round }) g.DrawCurve(line, knobs, 0.5f);
            g.SmoothingMode = smoothing;

            int zero = YFor(0);
            for (int i = 0; i < n; i++)
            {
                int cx = (int)knobs[i].X, ky = (int)knobs[i].Y;
                bool hot = i == eqHover || i == eqDrag;
                var track = new Rectangle(cx - Theme.S(4), eqTop, Theme.S(8), eqBottom - eqTop);
                using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, track);
                using (var b = new SolidBrush(on ? (hot ? Theme.Bright : Theme.Primary) : Theme.Faint))
                    g.FillRectangle(b, track.X, Math.Min(zero, ky), track.Width, Math.Max(1, Math.Abs(ky - zero)));
                UiDraw.Box(g, track, 1, Theme.Faint);
                var knob = new Rectangle(cx - Theme.S(18), ky - Theme.S(8), Theme.S(36), Theme.S(16));
                using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, knob);
                UiDraw.Box(g, knob, Theme.S(2), on ? (hot ? Theme.Bright : Theme.Primary) : Theme.Dim);
                float gain = Equalizer.Gains[i];
                string v = (gain > 0 ? "+" : "") + gain.ToString("0.0", CultureInfo.InvariantCulture);
                Theme.DrawSpaced(g, v, Theme.TileTitle, hot ? Theme.Bright : Math.Abs(gain) < 0.05f ? Theme.Dim : Theme.Primary, cx - Theme.SpacedWidth(g, v, Theme.TileTitle, sp) / 2, valueY, sp);
                string f = Equalizer.BandNames[i];
                Theme.DrawSpaced(g, f, Theme.TileTitle, hot ? Theme.Bright : Theme.Primary, cx - Theme.SpacedWidth(g, f, Theme.TileTitle, sp) / 2, freqY, sp);
                string t = Equalizer.BandTitles[i];
                Theme.DrawSpaced(g, t, Theme.TileTitle, hot ? Theme.Bright : Theme.Dim, cx - Theme.SpacedWidth(g, t, Theme.TileTitle, sp) / 2, nameY, sp);
            }

            // Bass / Mids / Treble: a bracket under each group's sliders with its name in the middle.
            for (int k = 0; k < Equalizer.Groups.Length; k++)
            {
                int x1 = (int)(cols.X + cw * Equalizer.Groups[k][0]) + Theme.S(10), x2 = (int)(cols.X + cw * (Equalizer.Groups[k][1] + 1)) - Theme.S(10);
                string name = Equalizer.GroupNames[k].ToUpperInvariant();
                int nw = Theme.SpacedWidth(g, name, Theme.TileTitle, Theme.S(2)), mid = (x1 + x2) / 2, ly = groupY + small / 2;
                using (var b = new SolidBrush(Theme.Faint))
                {
                    g.FillRectangle(b, x1, ly, Math.Max(0, mid - nw / 2 - Theme.S(10) - x1), Theme.S(1));
                    g.FillRectangle(b, mid + nw / 2 + Theme.S(10), ly, Math.Max(0, x2 - (mid + nw / 2 + Theme.S(10))), Theme.S(1));
                    g.FillRectangle(b, x1, ly - Theme.S(5), Theme.S(1), Theme.S(6));
                    g.FillRectangle(b, x2 - Theme.S(1), ly - Theme.S(5), Theme.S(1), Theme.S(6));
                }
                Theme.DrawSpaced(g, name, Theme.TileTitle, Theme.Primary, mid - nw / 2, groupY, Theme.S(2));
            }

            string note = Equalizer.Problem ?? (!on ? "The equalizer is off. Turn it on in Options." : null);
            if (note != null)
            {
                int w = TextRenderer.MeasureText(g, note, Theme.Body).Width + Theme.S(32), h = Theme.Body.Height + Theme.S(20);
                var r = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
                using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, r);
                UiDraw.Box(g, r, Theme.S(2), Equalizer.Problem != null ? Theme.Rec : Theme.Primary);
                TextRenderer.DrawText(g, note, Theme.Body, r, Equalizer.Problem != null ? Theme.Rec : Theme.Bright,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        // Before Equalizer APO is installed: what it is, the three steps, and a button to its download page.
        void DrawEqInstall(Graphics g, Rectangle area)
        {
            int x = area.X + Theme.S(8), y = area.Y + Theme.S(4), w = area.Width - Theme.S(16);
            TextRenderer.DrawText(g, "Equalizer APO isn't installed yet", Theme.TileValue, new Rectangle(x, y, w, Theme.TileValue.Height + Theme.S(4)), Theme.Bright, OneLine);
            y += Theme.TileValue.Height + Theme.S(14);
            var lines = new[]
            {
                "The equalizer changes everything you hear through Equalizer APO, a free audio add-on for Windows.",
                "1.  Click Get Equalizer APO and download and run its installer.",
                "2.  In the Configurator it shows at the end, tick your speakers and headphones, then restart Windows.",
                "3.  Open Dino Visualizer again: the sliders appear here by themselves."
            };
            foreach (var l in lines)
            {
                TextRenderer.DrawText(g, l, Theme.Body, new Rectangle(x, y, w, Theme.Body.Height + Theme.S(4)), l.StartsWith("The") ? Theme.Dim : Theme.Primary, OneLine);
                y += Theme.Body.Height + Theme.S(10);
            }
            y += Theme.S(8);
            string text = "Get Equalizer APO";
            int tw = Theme.SpacedWidth(g, text, Theme.Option, Theme.S(1));
            getButton = new Rectangle(x, y, tw + Theme.S(36), Theme.S(40));
            if (getButton.Bottom > area.Bottom) getButton.Y = area.Bottom - getButton.Height;
            if (getHover) using (var b = new SolidBrush(Color.FromArgb(55, Theme.Primary))) g.FillRectangle(b, getButton);
            UiDraw.Box(g, getButton, Theme.S(2), getHover ? Theme.Bright : Theme.Primary);
            Theme.DrawSpaced(g, text, Theme.Option, getHover ? Theme.Bright : Theme.Primary, getButton.X + Theme.S(18), getButton.Y + (getButton.Height - Theme.Option.Height) / 2, Theme.S(1));
        }

        int EqColumnAt(Point p)
        {
            if (!ShowEq || !Equalizer.Installed) return -1;
            for (int i = 0; i < eqColumns.Length; i++) if (eqColumns[i].Contains(p)) return i;
            return -1;
        }

        // Sets a band from the mouse height, in half-dB steps.
        void SetBand(int i, int mouseY)
        {
            if (eqBottom <= eqTop) return;
            float db = Equalizer.MaxGain - (mouseY - eqTop) / (float)(eqBottom - eqTop) * 2 * Equalizer.MaxGain;
            db = (float)Math.Round(Math.Max(-Equalizer.MaxGain, Math.Min(Equalizer.MaxGain, db)) * 2) / 2;
            if (Math.Abs(Equalizer.Gains[i] - db) < 0.01f) return;
            Equalizer.Gains[i] = db;
            Invalidate(VizBox());
            if (EqChanged != null) EqChanged();
        }

        void SetTab(int tab)
        {
            if (Tab == tab) return;
            Tab = tab;
            lyricScroll = float.NaN;   // the lyrics jump straight to the right spot when their tab opens
            Invalidate(VizBox());
            if (TabChanged != null) TabChanged();
        }

        // ---- The lyrics: synced ones scroll along with the song, the line being sung lit up in the middle; plain
        // ones scroll through at the song's pace. The mouse wheel scrolls by hand for a few seconds, and clicking a
        // synced line jumps the song there.

        float lyricScroll = float.NaN, lyricTarget;
        int lyricCurrent = -1, lyricHover = -1;
        DateTime manualUntil;
        float manualOffset;
        readonly List<KeyValuePair<Rectangle, int>> lyricHits = new List<KeyValuePair<Rectangle, int>>();

        LyricsResult CurrentLyrics { get { return string.IsNullOrEmpty(info.Title) ? null : Lyrics.For(info); } }

        string LyricsLabel()
        {
            var r = CurrentLyrics;
            if (r == null) return "";
            if (r.Searching) return "Searching";
            if (r.Instrumental && r.Lines.Count == 0) return "Instrumental";
            if (r.Problem == "offline") return "Offline";
            if (r.Problem != null) return "Not Found";
            return r.Synced ? "Synced" : "Not Synced";
        }

        int LineHeight { get { return Theme.TileValue.Height + Theme.S(16); } }

        // Moves the scroll a step toward where it should be. True while something on the tab is moving.
        bool StepLyrics()
        {
            var r = CurrentLyrics;
            if (r == null || r.Lines.Count == 0) return false;
            var area = VizArea;
            var now = info.Now + TimeSpan.FromSeconds(0.25);   // light each line a moment early, as it starts
            int current = -1;
            if (r.Synced) for (int i = 0; i < r.Lines.Count && r.Lines[i].Time <= now; i++) current = i;
            float target;
            if (r.Synced) target = Math.Max(0, current) * LineHeight;
            else
            {
                // Plain lyrics: through the song at an even pace, the middle line roughly where the song is.
                float total = r.Lines.Count * LineHeight;
                double f = info.Duration > TimeSpan.Zero ? Math.Max(0, Math.Min(1, info.Now.TotalSeconds / info.Duration.TotalSeconds)) : 0;
                target = (float)(f * total) - area.Height / 2f + LineHeight;
                target = Math.Max(0, Math.Min(Math.Max(0, total - area.Height + LineHeight), target));
            }
            if (DateTime.UtcNow < manualUntil) target += manualOffset;
            else manualOffset = 0;
            lyricTarget = target;
            bool moved = current != lyricCurrent;
            lyricCurrent = current;
            if (float.IsNaN(lyricScroll)) { lyricScroll = target; return true; }
            float d = target - lyricScroll;
            if (Math.Abs(d) < 0.5f) { lyricScroll = target; return moved; }
            lyricScroll += d * 0.18f;
            return true;
        }

        void DrawLyrics(Graphics g, Rectangle area)
        {
            lyricHits.Clear();
            var r = CurrentLyrics;
            string message = r == null ? "Play a song to see its lyrics here."
                           : r.Searching ? "Looking up the lyrics..."
                           : r.Lines.Count == 0 && r.Instrumental ? "This one's an instrumental: no words to show."
                           : r.Problem == "offline" ? "Can't reach the lyrics service right now. It tries again in a moment."
                           : r.Problem != null ? "No lyrics found for this song."
                           : null;
            if (message != null)
            {
                TextRenderer.DrawText(g, message, Theme.TileValue, area, r != null && r.Searching ? Theme.Primary : Theme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                return;
            }
            if (float.IsNaN(lyricScroll)) StepLyrics();

            var clip = g.Clip;
            g.SetClip(area, CombineMode.Intersect);
            int lh = LineHeight;
            float centerY = r.Synced ? area.Y + area.Height / 2f - lh / 2f : area.Y;
            int first = Math.Max(0, (int)Math.Floor((lyricScroll - (centerY - area.Y)) / lh) - 1);
            for (int i = first; i < r.Lines.Count; i++)
            {
                float y = centerY + i * lh - lyricScroll;
                if (y > area.Bottom) break;
                var line = new Rectangle(area.X + Theme.S(40), (int)y, area.Width - Theme.S(80), lh);
                string text = r.Lines[i].Text.Length == 0 ? "♪" : r.Lines[i].Text;
                Color c;
                if (!r.Synced) c = Theme.Primary;
                else if (i == lyricCurrent) c = Theme.Bright;
                else
                {
                    int away = Math.Abs(i - lyricCurrent);
                    c = i == lyricHover ? Theme.Primary : away <= 2 ? Theme.Dim : Theme.Faint;
                }
                if (ShowsViz && !(r.Synced && i == lyricCurrent))
                {
                    // Over the visualizer each line sits on a dark strip just its width, so it reads over anything.
                    int tw = Math.Min(line.Width, TextRenderer.MeasureText(g, text, Theme.TileValue, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width);
                    using (var b = new SolidBrush(Color.FromArgb(150, 2, 3, 5)))
                        g.FillRectangle(b, line.X + (line.Width - tw) / 2 - Theme.S(12), line.Y + Theme.S(5), tw + Theme.S(24), lh - Theme.S(10));
                }
                if (r.Synced && i == lyricCurrent)
                {
                    if (ShowsViz) using (var b = new SolidBrush(Color.FromArgb(170, 2, 3, 5))) g.FillRectangle(b, area.X, line.Y + Theme.S(2), area.Width, lh - Theme.S(4));
                    using (var b = new SolidBrush(Color.FromArgb(40, Theme.Primary))) g.FillRectangle(b, area.X, line.Y + Theme.S(2), area.Width, lh - Theme.S(4));
                    using (var b = new SolidBrush(Theme.Primary)) g.FillRectangle(b, area.X + Theme.S(12), line.Y + lh / 2 - Theme.S(5), Theme.S(10), Theme.S(10));
                }
                TextRenderer.DrawText(g, text, Theme.TileValue, line, c,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);   // keep to the box (GDI text ignores the clip otherwise)
                if (r.Synced) lyricHits.Add(new KeyValuePair<Rectangle, int>(Rectangle.Intersect(line, area), i));
            }
            // Lines fade out toward the top and bottom edges (more gently over the visualizer, which shows through).
            int fade = Math.Min(area.Height / 4, Theme.S(48)), edge = ShowsViz ? 150 : 235;
            if (fade > 0)
            {
                var top = new Rectangle(area.X, area.Y, area.Width, fade);
                var bottom = new Rectangle(area.X, area.Bottom - fade, area.Width, fade);
                using (var b = new LinearGradientBrush(new Rectangle(top.X, top.Y - 1, top.Width, top.Height + 2), Color.FromArgb(edge, 2, 3, 5), Color.FromArgb(0, 2, 3, 5), 90f)) g.FillRectangle(b, top);
                using (var b = new LinearGradientBrush(new Rectangle(bottom.X, bottom.Y - 1, bottom.Width, bottom.Height + 2), Color.FromArgb(0, 2, 3, 5), Color.FromArgb(edge, 2, 3, 5), 90f)) g.FillRectangle(b, bottom);
            }
            g.Clip = clip;
            string credit = "Lyrics from LRCLIB";
            Theme.DrawSpaced(g, credit, Theme.TileTitle, Theme.Faint, area.Right - Theme.SpacedWidth(g, credit, Theme.TileTitle, Theme.S(1)), area.Bottom + Theme.S(2), Theme.S(1));
        }

        int LyricLineAt(Point p)
        {
            if (!ShowsLyrics) return -1;
            foreach (var hit in lyricHits) if (hit.Key.Contains(p)) return hit.Value;
            return -1;
        }

        // Lyrics were found (or not) for some song: redraw if it's this one.
        public void LyricsArrived()
        {
            if (!ShowsLyrics) return;
            lyricScroll = float.NaN;
            Invalidate(VizBox());
        }

        // ---- Playlists: buttons to connect Spotify and YouTube, then your playlists on the left and the chosen
        // one's songs on the right. Click a song (or the playlist's Play button) to play it there.

        public event Action<MusicAccount, Point> AccountClicked;   // where to show its menu (screen coordinates)
        Playlist chosen;
        int plScroll, songScroll, clickHover = -1;
        Rectangle plList, songList;
        readonly List<KeyValuePair<Rectangle, Action>> clicks = new List<KeyValuePair<Rectangle, Action>>();
        readonly HashSet<MusicAccount> autoLoaded = new HashSet<MusicAccount>();

        int RowHeight { get { return Theme.Body.Height + Theme.S(14); } }

        static string PlaylistsLabel()
        {
            int n = Music.All.Sum(a => a.Playlists.Count);
            return n == 0 ? "" : n == 1 ? "1 Playlist" : n + " Playlists";
        }

        // Something changed in an account (signed in, playlists or songs loaded, a cover arrived).
        public void PlaylistsChanged() { if (Tab == PlaylistsTab) Invalidate(VizBox()); }

        // ---- Info: the playing song on the left (from the iTunes catalog and Wikipedia), its artist on the right.

        SongDetails CurrentInfo { get { return string.IsNullOrEmpty(info.Title) ? null : SongInfo.For(info); } }

        string InfoLabel()
        {
            var d = CurrentInfo;
            if (d == null) return "";
            if (d.Searching) return "Searching";
            if (d.Offline) return "Offline";
            if (!d.Found) return "Not Found";
            return d.Genre != null && d.Released != DateTime.MinValue ? d.Genre + " · " + d.Released.Year : d.Genre ?? "";
        }

        // Something was found (or not) for some song: redraw if the tab is showing.
        public void InfoArrived() { if (Tab == InfoTab) Invalidate(VizBox()); }

        // Paragraphs: wrapped, only whole lines, the last one ending "..." if it doesn't all fit.
        const TextFormatFlags Wrapped = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        void DrawInfo(Graphics g, Rectangle area)
        {
            var d = CurrentInfo;
            string message = d == null ? "Play a song to see what's known about it and its artist here."
                           : d.Searching ? "Looking up the song..."
                           : d.Offline ? "Can't reach the music info services right now. It tries again in a moment."
                           : !d.Found ? "Nothing found about this song or its artist."
                           : null;
            if (message != null)
            {
                TextRenderer.DrawText(g, message, Theme.TileValue, area, d != null && d.Searching ? Theme.Primary : Theme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                return;
            }
            int gap = Theme.S(40), colW = (area.Width - gap) / 2;
            var song = new Rectangle(area.X, area.Y, colW, area.Height);
            var artist = new Rectangle(song.Right + gap, area.Y, area.Right - song.Right - gap, area.Height);
            using (var p = new Pen(Color.FromArgb(70, Theme.Primary), Math.Max(1, Theme.S(1))))
                g.DrawLine(p, song.Right + gap / 2, area.Y, song.Right + gap / 2, area.Bottom);
            DrawSongInfo(g, song, d);
            DrawArtistInfo(g, artist, d.About);
            string credit = "Info from Apple Music and Wikipedia";
            Theme.DrawSpaced(g, credit, Theme.TileTitle, Theme.Faint, area.Right - Theme.SpacedWidth(g, credit, Theme.TileTitle, Theme.S(1)), area.Bottom + Theme.S(2), Theme.S(1));
        }

        void DrawSongInfo(Graphics g, Rectangle r, SongDetails d)
        {
            int y = r.Y, buttonH = Theme.S(30);
            Theme.DrawSpaced(g, "THE SONG", Theme.TileTitle, Theme.Dim, r.X, y, Theme.S(2));
            y += Theme.TileTitle.Height + Theme.S(12);
            TextRenderer.DrawText(g, d.Track ?? info.Title, Theme.Stat, new Rectangle(r.X, y, r.Width, Theme.Stat.Height + Theme.S(4)), Theme.Bright, OneLine);
            y += Theme.Stat.Height + Theme.S(6);
            TextRenderer.DrawText(g, d.Artist ?? info.Artist, Theme.TileValue, new Rectangle(r.X, y, r.Width, Theme.TileValue.Height + Theme.S(4)), Theme.Primary, OneLine);
            y += Theme.TileValue.Height + Theme.S(16);

            // The facts from the catalog, a row each (the album and length from the player when it isn't listed).
            var facts = new List<KeyValuePair<string, string>>();
            string album = d.Album ?? info.Album;
            var length = d.Length > TimeSpan.Zero ? d.Length : info.Duration;
            if (!string.IsNullOrEmpty(album)) facts.Add(new KeyValuePair<string, string>("Album", album));
            if (d.Released != DateTime.MinValue) facts.Add(new KeyValuePair<string, string>("Released", d.Released.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)));
            if (!string.IsNullOrEmpty(d.Genre)) facts.Add(new KeyValuePair<string, string>("Genre", d.Genre));
            if (d.TrackNumber > 0) facts.Add(new KeyValuePair<string, string>("Track", d.TrackCount > 0 ? d.TrackNumber + " of " + d.TrackCount : d.TrackNumber.ToString()));
            if (length > TimeSpan.Zero) facts.Add(new KeyValuePair<string, string>("Length", Time(length)));
            int labelW = Theme.S(110), rowH = Theme.Body.Height + Theme.S(9);
            foreach (var f in facts)
            {
                if (y + rowH > r.Bottom) break;
                Theme.DrawSpaced(g, f.Key, Theme.TileTitle, Theme.Dim, r.X, y + (Theme.Body.Height - Theme.TileTitle.Height) / 2, Theme.S(1));
                TextRenderer.DrawText(g, f.Value, Theme.Body, new Rectangle(r.X + labelW, y, r.Width - labelW, Theme.Body.Height + Theme.S(4)), Theme.Bright, OneLine);
                y += rowH;
            }

            // About the song, when it has its own article.
            if (d.SongAbout == null) return;
            y += Theme.S(10);
            int bottom = r.Bottom - (d.SongPage != null ? buttonH + Theme.S(12) : 0);
            if (!string.IsNullOrEmpty(d.SongDescription) && y + Theme.Body.Height < bottom)
            {
                TextRenderer.DrawText(g, Capital(d.SongDescription), Theme.Body, new Rectangle(r.X, y, r.Width, Theme.Body.Height + Theme.S(4)), Theme.Primary, OneLine);
                y += Theme.Body.Height + Theme.S(8);
            }
            if (bottom - y > Theme.Body.Height)
                TextRenderer.DrawText(g, d.SongAbout, Theme.Body, new Rectangle(r.X, y, r.Width, bottom - y), Theme.Dim, Wrapped);
            if (d.SongPage != null)
            {
                string page = d.SongPage;
                Button(g, "Song On Wikipedia", r.X, r.Bottom - buttonH, buttonH, Theme.Primary, () => Open(page));
            }
        }

        void DrawArtistInfo(Graphics g, Rectangle r, ArtistDetails a)
        {
            int y = r.Y, buttonH = Theme.S(30);
            Theme.DrawSpaced(g, "THE ARTIST", Theme.TileTitle, Theme.Dim, r.X, y, Theme.S(2));
            y += Theme.TileTitle.Height + Theme.S(12);
            if (a == null)
            {
                TextRenderer.DrawText(g, "No Wikipedia article was found for this artist.", Theme.Body, new Rectangle(r.X, y, r.Width, Theme.Body.Height * 3), Theme.Dim, Wrapped);
                return;
            }
            // Their picture, with the name and what they are beside it.
            int side = Math.Min(Theme.S(130), r.Height / 3), tx = r.X, picH = 0;
            if (a.Picture != null)
            {
                float k = Math.Min(side / (float)a.Picture.Width, side / (float)a.Picture.Height);
                var pic = new Rectangle(r.X, y, (int)(a.Picture.Width * k), (int)(a.Picture.Height * k));
                picH = pic.Height;
                var mode = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(a.Picture, pic);
                g.InterpolationMode = mode;
                UiDraw.Box(g, Rectangle.Inflate(pic, Theme.S(1), Theme.S(1)), Theme.S(1), Theme.Faint);
                tx = pic.Right + Theme.S(18);
            }
            int tw = r.Right - tx;
            TextRenderer.DrawText(g, a.Name, Theme.TileValue, new Rectangle(tx, y, tw, Theme.TileValue.Height + Theme.S(4)), Theme.Bright, OneLine);
            if (!string.IsNullOrEmpty(a.Description))
                TextRenderer.DrawText(g, Capital(a.Description), Theme.Body, new Rectangle(tx, y + Theme.TileValue.Height + Theme.S(8), tw, Theme.Body.Height * 3 + Theme.S(4)), Theme.Primary, Wrapped);
            y += Math.Max(picH, Theme.TileValue.Height + Theme.Body.Height * 2 + Theme.S(8)) + Theme.S(16);

            int bottom = r.Bottom - (a.Page != null ? buttonH + Theme.S(12) : 0);
            if (!string.IsNullOrEmpty(a.About) && bottom - y > Theme.Body.Height)
                TextRenderer.DrawText(g, a.About, Theme.Body, new Rectangle(r.X, y, r.Width, bottom - y), Theme.Dim, Wrapped);
            if (a.Page != null)
            {
                string page = a.Page;
                Button(g, "Artist On Wikipedia", r.X, r.Bottom - buttonH, buttonH, Theme.Primary, () => Open(page));
            }
        }

        static string Capital(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1); }

        // Opens a Wikipedia page in the browser.
        static void Open(string url)
        {
            if (url == null || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            try { Process.Start(url); } catch { }
        }

        // ---- The secrets: the things that float around the screen while certain bands play, each with its own
        // switch (and the one next to the tab switches them all).

        public event Action SecretsChanged;   // one was switched on or off
        static readonly int[] SecretKinds = { BouncerWindow.Strawberry, BouncerWindow.Beetle };
        readonly Dictionary<int, Bitmap> secretPictures = new Dictionary<int, Bitmap>();

        static string SecretLabel()
        {
            int on = SecretKinds.Count(BouncerWindow.IsOn);
            return on == SecretKinds.Length ? "All On" : on == 0 ? "All Off" : on + " of " + SecretKinds.Length + " On";
        }

        void SecretChanged()
        {
            Invalidate(VizBox());
            if (SecretsChanged != null) SecretsChanged();
        }

        void DrawSecret(Graphics g, Rectangle area)
        {
            TextRenderer.DrawText(g, "Little surprises that float around your screen while certain bands play.", Theme.Body,
                new Rectangle(area.X, area.Y, area.Width, Theme.Body.Height + Theme.S(4)), Theme.Dim, OneLine);
            int top = area.Y + Theme.Body.Height + Theme.S(14), gap = Theme.S(16);
            int cardW = (area.Width - gap) / 2, cardH = Math.Min(area.Bottom - top, Theme.S(230));
            if (cardW < Theme.S(200) || cardH < Theme.S(90)) return;
            for (int i = 0; i < SecretKinds.Length; i++)
            {
                int kind = SecretKinds[i];
                bool on = BouncerWindow.IsOn(kind);
                var card = new Rectangle(area.X + i * (cardW + gap), top, cardW, cardH);
                using (var b = new SolidBrush(Color.FromArgb(on ? 40 : 14, Theme.Primary))) g.FillRectangle(b, card);
                UiDraw.Box(g, card, Theme.S(2), on ? Theme.Primary : Theme.Faint);

                // The picture, faded while it's switched off.
                int side = Math.Min(cardH - Theme.S(28), Theme.S(140));
                Bitmap pic;
                if (!secretPictures.TryGetValue(kind, out pic) || pic.Width != side)
                {
                    if (pic != null) pic.Dispose();
                    secretPictures[kind] = pic = BouncerWindow.Preview(kind, side);
                }
                var picRect = new Rectangle(card.X + Theme.S(14), card.Y + (cardH - side) / 2, side, side);
                if (on) g.DrawImage(pic, picRect);
                else
                    using (var faded = new ImageAttributes())
                    {
                        faded.SetColorMatrix(new ColorMatrix { Matrix00 = 0.5f, Matrix11 = 0.5f, Matrix22 = 0.5f, Matrix33 = 0.35f });
                        g.DrawImage(pic, picRect, 0, 0, pic.Width, pic.Height, GraphicsUnit.Pixel, faded);
                    }

                // What it is, which band brings it out, and its switch.
                int tx = picRect.Right + Theme.S(16), tw = card.Right - Theme.S(14) - tx, ty = card.Y + Theme.S(14);
                bool beetle = kind == BouncerWindow.Beetle;
                TextRenderer.DrawText(g, beetle ? "Beetle" : "Strawberry", Theme.TileValue, new Rectangle(tx, ty, tw, Theme.TileValue.Height + Theme.S(4)), on ? Theme.Bright : Theme.Dim, OneLine);
                ty += Theme.TileValue.Height + Theme.S(6);
                Theme.DrawSpaced(g, beetle ? "Saosin" : "Dance Gavin Dance", Theme.TileTitle, on ? Theme.Primary : Theme.Faint, tx, ty, Theme.S(1));
                ty += Theme.TileTitle.Height + Theme.S(8);
                int switchH = Theme.S(30), switchY = card.Bottom - Theme.S(14) - switchH;
                string about = beetle ? "An atlas beetle bounces around the screen like an old DVD logo, changing color at every bounce. It bursts when the song ends."
                                      : "A strawberry bounces around the screen like an old DVD logo, changing color at every bounce. It explodes when the song ends.";
                var aboutRect = new Rectangle(tx, ty, tw, Math.Max(0, switchY - Theme.S(6) - ty));
                var clip = g.Clip;
                g.SetClip(aboutRect, CombineMode.Intersect);   // no half-cut line under the last one that fits
                TextRenderer.DrawText(g, about, Theme.Body, aboutRect, Theme.Dim,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
                g.Clip = clip;
                Button(g, on ? "On" : "Off", tx, switchY, switchH, on ? Theme.Primary : Theme.Faint, () =>
                {
                    BouncerWindow.SetOn(kind, !on);
                    SecretChanged();
                });
            }
        }

        Rectangle Button(Graphics g, string text, int x, int y, int h, Color dot, Action click)
        {
            int tw = Theme.SpacedWidth(g, text, Theme.TileTitle, Theme.S(1));
            var r = new Rectangle(x, y, tw + Theme.S(42), h);
            bool hot = clicks.Count == clickHover;
            if (hot) using (var b = new SolidBrush(Color.FromArgb(55, Theme.Primary))) g.FillRectangle(b, r);
            UiDraw.Box(g, r, Theme.S(2), hot ? Theme.Bright : Theme.Primary);
            var smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, r.X + Theme.S(10), r.Y + h / 2 - Theme.S(5), Theme.S(10), Theme.S(10));
            g.SmoothingMode = smoothing;
            Theme.DrawSpaced(g, text, Theme.TileTitle, hot ? Theme.Bright : Theme.Primary, r.X + Theme.S(28), y + (h - Theme.TileTitle.Height) / 2, Theme.S(1));
            clicks.Add(new KeyValuePair<Rectangle, Action>(r, click));
            return r;
        }

        static int ButtonWidth(Graphics g, string text) { return Theme.SpacedWidth(g, text, Theme.TileTitle, Theme.S(1)) + Theme.S(42); }

        // ---- The VRChat chatbox status: write a few lines, pick the one to show (or let them take turns), and see
        // what the bubble above your head says right now.

        readonly TextBox statusInput;
        int statusEditing = -1, statusScroll;   // the status being edited in the box (-1: typing a new one)
        Rectangle statusList;

        static string StatusLabel()
        {
            int n = VrcChatbox.Statuses.Count;
            if (n == 0) return "No Statuses";
            string s = (VrcChatbox.StatusOn ? "On" : "Off") + " · " + n + (n == 1 ? " Status" : " Statuses");
            return VrcChatbox.Cycle > 0 && n > 1 ? s + " · Every " + VrcChatbox.CycleNames[VrcChatbox.Cycle] : s;
        }

        void CommitStatus()
        {
            string s = Regex.Replace(statusInput.Text, @"\s+", " ").Trim();   // one line
            var list = VrcChatbox.Statuses;
            if (s != "")
            {
                if (statusEditing >= 0 && statusEditing < list.Count) list[statusEditing] = s;
                else
                {
                    list.Add(s);
                    if (list.Count == 1) VrcChatbox.StatusIndex = 0;
                    statusScroll = int.MaxValue;   // down to the new one (kept in range when drawn)
                }
                VrcChatbox.SaveStatuses();
            }
            CancelStatusEdit();
        }

        void CancelStatusEdit()
        {
            statusEditing = -1;
            statusInput.Text = "";
            Invalidate(VizBox());
        }

        void EditStatus(int i)
        {
            statusEditing = i;
            statusInput.Text = VrcChatbox.Statuses[i];
            statusInput.Focus();
            statusInput.SelectAll();
            Invalidate(VizBox());
        }

        // The one showing stays showing when another goes; if it's the one deleted, the next one takes over.
        void DeleteStatus(int i)
        {
            var list = VrcChatbox.Statuses;
            int shown = VrcChatbox.StatusIndex;
            list.RemoveAt(i);
            if (i < shown) VrcChatbox.Moved(shown - 1);
            else if (i == shown) VrcChatbox.StatusIndex = list.Count == 0 ? 0 : Math.Min(shown, list.Count - 1);
            if (statusEditing == i) CancelStatusEdit();
            else if (statusEditing > i) statusEditing--;
            VrcChatbox.SaveStatuses();
            Invalidate(VizBox());
        }

        // Up or down the list (the order they take turns in).
        void MoveStatus(int i, int to)
        {
            var list = VrcChatbox.Statuses;
            if (to < 0 || to >= list.Count) return;
            int shown = VrcChatbox.StatusIndex;
            string s = list[i];
            list[i] = list[to];
            list[to] = s;
            if (shown == i) VrcChatbox.Moved(to);
            else if (shown == to) VrcChatbox.Moved(i);
            if (statusEditing == i) statusEditing = to;
            else if (statusEditing == to) statusEditing = i;
            VrcChatbox.SaveStatuses();
            Invalidate(VizBox());
        }

        // A small text button at the right of a status's row, ending at right; returns where it starts.
        int Link(Graphics g, string text, int right, Rectangle row, Action click)
        {
            int w = TextRenderer.MeasureText(g, text, Theme.Body, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            bool hot = clicks.Count == clickHover;
            TextRenderer.DrawText(g, text, Theme.Body, new Rectangle(right - w, row.Y, w, row.Height), hot ? Theme.Bright : Theme.Dim, Clipped);
            clicks.Add(new KeyValuePair<Rectangle, Action>(Rectangle.Intersect(new Rectangle(right - w - Theme.S(6), row.Y, w + Theme.S(12), row.Height), statusList), click));
            return right - w;
        }

        // Returns whether the box for typing is on show.
        bool DrawStatus(Graphics g, Rectangle area)
        {
            int sp = Theme.S(1), h = Theme.S(30);
            int y = area.Y;
            TextRenderer.DrawText(g, "Your own lines for VRChat's chatbox. The one lit up shows above the song, or alone when nothing plays.", Theme.Body,
                new Rectangle(area.X, y, area.Width, Theme.Body.Height + Theme.S(4)), Theme.Dim, OneLine);
            y += Theme.Body.Height + Theme.S(14);
            int top = y;
            // With room, what VRChat shows right now goes down the right side, in what the Cycle buttons leave.
            int labelW = Theme.SpacedWidth(g, "Cycle:", Theme.TileTitle, sp);
            int cyclesW = labelW + Theme.S(10) + VrcChatbox.CycleNames.Sum(c => ButtonWidth(g, c) + Theme.S(6));
            int spare = area.Width - cyclesW - Theme.S(24);
            int previewW = spare >= Theme.S(240) ? Math.Min(Theme.S(330), spare) : 0;
            int left = area.Width - (previewW > 0 ? previewW + Theme.S(24) : 0);

            // The switch, and how often they take turns (on a line of its own if it doesn't fit beside it).
            bool on = VrcChatbox.StatusOn;
            var onButton = Button(g, on ? "Status On" : "Status Off", area.X, y, h, on ? Theme.Primary : Theme.Faint, () => { VrcChatbox.StatusOn = !on; Invalidate(VizBox()); });
            int cx = onButton.Right + Theme.S(28);
            if (cx + cyclesW > area.X + left) { cx = area.X; y += h + Theme.S(10); }
            Theme.DrawSpaced(g, "Cycle:", Theme.TileTitle, Theme.Primary, cx, y + (h - Theme.TileTitle.Height) / 2, sp);
            cx += labelW + Theme.S(10);
            for (int i = 0; i < VrcChatbox.CycleNames.Length; i++)
            {
                int pick = i;
                cx = Button(g, VrcChatbox.CycleNames[i], cx, y, h, i == VrcChatbox.Cycle ? Theme.Primary : Theme.Faint, () => { VrcChatbox.Cycle = pick; Invalidate(VizBox()); }).Right + Theme.S(6);
            }
            y += h + Theme.S(14);

            // The box to type in, with Add (or Save and Cancel while editing one).
            int ih = Theme.S(34);
            bool editing = statusEditing >= 0;
            int actW = ButtonWidth(g, editing ? "Save" : "Add") + (editing ? ButtonWidth(g, "Cancel") + Theme.S(8) : 0);
            var field = new Rectangle(area.X, y, Math.Max(Theme.S(120), left - actW - Theme.S(10)), ih);
            using (var b = new SolidBrush(statusInput.BackColor)) g.FillRectangle(b, field);
            UiDraw.Box(g, field, Theme.S(2), statusInput.Focused ? Theme.Bright : Theme.Dim);
            clicks.Add(new KeyValuePair<Rectangle, Action>(field, () => statusInput.Focus()));
            string count = statusInput.TextLength + "/" + VrcChatbox.StatusLength;
            int countW = TextRenderer.MeasureText(g, count, Theme.TileTitle, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, count, Theme.TileTitle, new Rectangle(field.Right - Theme.S(10) - countW, field.Y, countW, ih), Theme.Faint, Clipped);
            int th = statusInput.PreferredHeight;
            var inner = new Rectangle(field.X + Theme.S(10), field.Y + (ih - th) / 2, Math.Max(Theme.S(20), field.Width - Theme.S(30) - countW), th);
            if (statusInput.Bounds != inner) statusInput.Bounds = inner;
            if (statusInput.ForeColor != Theme.Bright) statusInput.ForeColor = Theme.Bright;
            var act = Button(g, editing ? "Save" : "Add", field.Right + Theme.S(10), y, ih, Theme.Primary, CommitStatus);
            if (editing) Button(g, "Cancel", act.Right + Theme.S(8), y, ih, Theme.Faint, CancelStatusEdit);
            y += ih + Theme.S(14);

            // The statuses: click one to show it now. The one showing is lit up, with how long until the next one's turn.
            statusList = new Rectangle(area.X, y, left, Math.Max(0, area.Bottom - y));
            var list = VrcChatbox.Statuses;
            if (list.Count == 0)
                TextRenderer.DrawText(g, "No statuses yet. Type one above and press Enter. Add as many as you like, and set Cycle to have them take turns.",
                    Theme.Body, statusList, Theme.Faint, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            else
            {
                int rh = RowHeight + Theme.S(6);
                statusScroll = Math.Max(0, Math.Min(statusScroll, list.Count * rh - statusList.Height));
                var clip = g.Clip;
                g.SetClip(statusList, CombineMode.Intersect);
                int shown = VrcChatbox.StatusIndex;
                var until = VrcChatbox.UntilNext;
                for (int i = 0; i < list.Count; i++)
                {
                    var row = new Rectangle(statusList.X, statusList.Y + i * rh - statusScroll, statusList.Width, rh - Theme.S(4));
                    if (row.Bottom < statusList.Y || row.Y > statusList.Bottom) continue;
                    bool current = i == shown, hot = clicks.Count == clickHover;
                    if (current || hot) using (var b = new SolidBrush(Color.FromArgb(current ? (on ? 60 : 30) : 25, Theme.Primary))) g.FillRectangle(b, row);
                    if (current) using (var b = new SolidBrush(on ? Theme.Primary : Theme.Dim)) g.FillRectangle(b, row.X, row.Y, Theme.S(4), row.Height);
                    if (i == statusEditing) UiDraw.Box(g, row, Theme.S(1), Theme.Dim);
                    int index = i;
                    clicks.Add(new KeyValuePair<Rectangle, Action>(Rectangle.Intersect(row, statusList), () => { VrcChatbox.StatusIndex = index; Invalidate(VizBox()); }));

                    int rx = Link(g, "Delete", row.Right - Theme.S(10), row, () => DeleteStatus(index)) - Theme.S(16);
                    rx = Link(g, "Edit", rx, row, () => EditStatus(index)) - Theme.S(16);
                    int arrowW = TextRenderer.MeasureText(g, "▼", Theme.Body, Size.Empty, TextFormatFlags.NoPadding).Width;
                    if (i < list.Count - 1) Link(g, "▼", rx, row, () => MoveStatus(index, index + 1));
                    rx -= arrowW + Theme.S(12);
                    if (i > 0) Link(g, "▲", rx, row, () => MoveStatus(index, index - 1));
                    rx -= arrowW + Theme.S(20);

                    string tag = !current ? "" : !on ? "picked" : until > TimeSpan.Zero ? "showing · next in " + (int)Math.Ceiling(until.TotalSeconds) + " s" : "showing";
                    if (tag != "")
                    {
                        int tw = TextRenderer.MeasureText(g, tag, Theme.TileTitle, Size.Empty, TextFormatFlags.NoPadding).Width;
                        TextRenderer.DrawText(g, tag, Theme.TileTitle, new Rectangle(rx - tw, row.Y, tw, row.Height), on ? Theme.Primary : Theme.Faint, Clipped);
                        rx -= tw + Theme.S(16);
                    }
                    int tx = row.X + Theme.S(16);
                    TextRenderer.DrawText(g, list[i], Theme.Body, new Rectangle(tx, row.Y, Math.Max(0, rx - tx), row.Height), current || hot ? Theme.Bright : Theme.Primary, Clipped);
                }
                g.Clip = clip;
            }

            if (previewW > 0) DrawChatboxPreview(g, new Rectangle(area.Right - previewW, top, previewW, area.Bottom - top));
            return true;
        }

        // What the bubble above your head says right now, drawn like VRChat's (white on dark gray), and whether it's
        // being sent at all.
        void DrawChatboxPreview(Graphics g, Rectangle r)
        {
            int h = Theme.S(30);
            Theme.DrawSpaced(g, "In VRChat Now", Theme.TileTitle, Theme.Primary, r.X, r.Y + (h - Theme.TileTitle.Height) / 2, Theme.S(1));
            int y = r.Y + h + Theme.S(14);
            string text = VrcChatbox.Compose(info);
            bool sending = VrcChatbox.Mode != 0 || VrcChatbox.StatusOn;
            const TextFormatFlags wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter;
            int tw = r.Width - Theme.S(32);
            string shown = text != "" ? text : "(nothing)";
            int textH = TextRenderer.MeasureText(g, shown, Theme.Body, new Size(tw, int.MaxValue), wrap).Height;
            var bubble = new Rectangle(r.X, y, r.Width, Math.Max(0, Math.Min(r.Bottom - y, textH + Theme.S(28))));   // before the note under it
            if (bubble.Height > Theme.S(20))
            {
                using (var b = new SolidBrush(Color.FromArgb(34, 36, 42))) g.FillRectangle(b, bubble);
                UiDraw.Box(g, bubble, Theme.S(1), Color.FromArgb(70, 72, 80));
                TextRenderer.DrawText(g, shown, Theme.Body, Rectangle.Inflate(bubble, -Theme.S(16), -Theme.S(14)),
                    text != "" && sending ? Color.FromArgb(240, 240, 245) : Color.FromArgb(120, 122, 130), wrap);
                y = bubble.Bottom + Theme.S(10);
            }
            string note = !sending ? "Not sending. Switch the status on, or pick a VRChat Chatbox option in Options for the song."
                        : VrcChatbox.Mode == 0 ? "Status only: pick a VRChat Chatbox option in Options to add the song."
                        : "Sent to VRChat on this computer (OSC), with OSC on in VRChat's Action Menu.";
            const TextFormatFlags noteFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            int noteH = TextRenderer.MeasureText(g, note, Theme.Body, new Size(r.Width, int.MaxValue), noteFlags).Height;
            if (y + noteH <= r.Bottom) TextRenderer.DrawText(g, note, Theme.Body, new Rectangle(r.X, y, r.Width, noteH), Theme.Dim, noteFlags);   // only if it all fits
        }

        const TextFormatFlags Clipped = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                                        TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsClipping;

        void DrawPlaylists(Graphics g, Rectangle area)
        {
            int h = Theme.S(34), x = area.X, sp = Theme.S(1);
            // The account buttons: connected ones get their service's color.
            foreach (var a in Music.All)
            {
                if (a.Connected && !a.Busy && a.Playlists.Count == 0 && autoLoaded.Add(a)) a.Reload();   // the first look at the tab loads them
                var acct = a;
                string state = a.Busy ? "Working..." : a.Connected ? "Connected" : a.HasKeys ? "Sign In" : "Connect";
                Rectangle r = Rectangle.Empty;
                r = Button(g, a.Name + ": " + state, x, area.Y, h, a.Connected ? a.BadgeColor : Theme.Faint, () =>
                {
                    if (AccountClicked != null) AccountClicked(acct, PointToScreen(new Point(r.X, r.Bottom)));
                });
                x = r.Right + Theme.S(12);
            }
            // On the right: where YouTube songs open, and Refresh.
            string where = "Open YouTube in: " + (YouTubeAccount.OpenInMusic ? "YT Music" : "YouTube");
            int ww = Theme.SpacedWidth(g, where, Theme.TileTitle, sp) + Theme.S(42);
            var toggle = Button(g, where, area.Right - ww, area.Y, h, YouTubeAccount.OpenInMusic ? Music.YouTube.BadgeColor : Theme.Faint, () =>
            {
                YouTubeAccount.OpenInMusic = !YouTubeAccount.OpenInMusic;
                Settings.Set("ytmusic", YouTubeAccount.OpenInMusic ? 1 : 0);
                Invalidate(VizBox());
            });
            if (Music.All.Any(a => a.Connected))
            {
                int rw = Theme.SpacedWidth(g, "Refresh", Theme.TileTitle, sp) + Theme.S(42);
                if (toggle.X - rw - Theme.S(12) > x) Button(g, "Refresh", toggle.X - rw - Theme.S(12), area.Y, h, Theme.Primary, () => { foreach (var a in Music.All) a.Reload(); });
            }

            // What each account is up to.
            int y = area.Y + h + Theme.S(8);
            string status = string.Join("   ·   ", Music.All.Where(a => a.Connected || a.Busy || a.Status != "").Select(a => a.Name + ": " + (a.Status == "" ? "connected" : a.Status)).ToArray());
            if (status != "") TextRenderer.DrawText(g, status, Theme.Body, new Rectangle(area.X, y, area.Width, Theme.Body.Height + Theme.S(4)), Theme.Dim, OneLine);
            int top = y + Theme.Body.Height + Theme.S(12);

            var all = Music.All.SelectMany(a => a.Playlists).ToList();
            plList = songList = Rectangle.Empty;
            if (all.Count == 0)
            {
                string message = Music.All.Any(a => a.Busy) ? "Loading..."
                               : Music.All.Any(a => a.Connected) ? "No playlists found yet."
                               : "Click Spotify or YouTube above to connect your account. The first time, a window walks you through making the free " +
                                 "developer key each one needs (about 5 minutes); after that you just sign in. YouTube Music playlists come with YouTube.";
                TextRenderer.DrawText(g, message, Theme.TileValue, new Rectangle(area.X + Theme.S(40), top, area.Width - Theme.S(80), area.Bottom - top),
                    Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }
            if (chosen == null || !all.Contains(chosen)) { chosen = all[0]; songScroll = 0; }
            chosen.Account.LoadSongs(chosen);

            int rh = RowHeight;
            plList = new Rectangle(area.X, top, (int)(area.Width * 0.38f), area.Bottom - top);
            songList = new Rectangle(plList.Right + Theme.S(16), top, area.Right - plList.Right - Theme.S(16), area.Bottom - top);
            UiDraw.Box(g, plList, 1, Theme.Faint);
            UiDraw.Box(g, songList, 1, Theme.Faint);

            // The playlists, each with its service's badge and cover.
            plScroll = Math.Max(0, Math.Min(plScroll, all.Count * rh + Theme.S(4) - plList.Height));
            var clip = g.Clip;
            g.SetClip(Rectangle.Inflate(plList, -1, -1), CombineMode.Intersect);
            for (int i = 0; i < all.Count; i++)
            {
                int ry = plList.Y + Theme.S(2) + i * rh - plScroll;
                if (ry + rh < plList.Y) continue;
                if (ry > plList.Bottom) break;
                var p = all[i];
                var row = new Rectangle(plList.X + Theme.S(2), ry, plList.Width - Theme.S(4), rh);
                bool hot = clicks.Count == clickHover;
                if (p == chosen) using (var b = new SolidBrush(Color.FromArgb(70, Theme.Primary))) g.FillRectangle(b, row);
                else if (hot) using (var b = new SolidBrush(Color.FromArgb(30, Theme.Primary))) g.FillRectangle(b, row);
                var badge = new Rectangle(row.X + Theme.S(6), row.Y + (rh - Theme.S(16)) / 2, Theme.S(26), Theme.S(16));
                using (var b = new SolidBrush(p.Account.BadgeColor)) g.FillRectangle(b, badge);
                TextRenderer.DrawText(g, p.Account.ShortName, Theme.TileTitle, badge, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
                int cx = badge.Right + Theme.S(8), cs = rh - Theme.S(8);
                var cover = Music.Cover(p.ImageUrl);
                if (cover != null) g.DrawImage(cover, cx, row.Y + Theme.S(4), cs, cs);
                else using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, cx, row.Y + Theme.S(4), cs, cs);
                string count = p.Count >= 0 ? p.Count.ToString() : "";
                int countW = count == "" ? 0 : TextRenderer.MeasureText(g, count, Theme.Body).Width;
                int nx = cx + cs + Theme.S(8);
                TextRenderer.DrawText(g, p.Name, Theme.Body, new Rectangle(nx, row.Y, row.Right - nx - countW - Theme.S(10), rh), p == chosen ? Theme.Bright : Theme.Primary, Clipped);
                if (count != "") TextRenderer.DrawText(g, count, Theme.Body, new Rectangle(row.Right - countW - Theme.S(6), row.Y, countW, rh), Theme.Dim, Clipped);
                var pick = p;
                clicks.Add(new KeyValuePair<Rectangle, Action>(Rectangle.Intersect(row, plList), () =>
                {
                    chosen = pick;
                    songScroll = 0;
                    pick.Account.LoadSongs(pick);
                    Invalidate(VizBox());
                }));
            }
            g.Clip = clip;

            // The chosen playlist: its name, a Play button, then its songs.
            int hy = songList.Y + Theme.S(8);
            var playAll = chosen;
            var play = Button(g, "Play", songList.Right - Theme.SpacedWidth(g, "Play", Theme.TileTitle, sp) - Theme.S(50), hy, Theme.S(30), playAll.Account.BadgeColor,
                              () => playAll.Account.Play(playAll, -1));
            TextRenderer.DrawText(g, chosen.Name, Theme.TileValue, new Rectangle(songList.X + Theme.S(12), hy, play.X - songList.X - Theme.S(24), Theme.S(30)), Theme.Bright, OneLine | TextFormatFlags.VerticalCenter);
            var songsArea = new Rectangle(songList.X, hy + Theme.S(38), songList.Width, songList.Bottom - hy - Theme.S(38));
            var songs = chosen.Songs;
            string note = chosen.Loading || songs == null ? "Loading songs..." : chosen.SongsProblem ?? (songs.Count == 0 ? "This playlist is empty." : null);
            if (note != null)
            {
                TextRenderer.DrawText(g, note, Theme.Body, Rectangle.Inflate(songsArea, -Theme.S(16), -Theme.S(8)), Theme.Dim, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }
            songScroll = Math.Max(0, Math.Min(songScroll, songs.Count * rh + Theme.S(4) - songsArea.Height));
            g.SetClip(Rectangle.Inflate(songsArea, -1, -1), CombineMode.Intersect);
            int numW = TextRenderer.MeasureText(g, songs.Count.ToString(), Theme.Body).Width + Theme.S(16);
            for (int i = 0; i < songs.Count; i++)
            {
                int ry = songsArea.Y + i * rh - songScroll;
                if (ry + rh < songsArea.Y) continue;
                if (ry > songsArea.Bottom) break;
                var s = songs[i];
                var row = new Rectangle(songsArea.X + Theme.S(2), ry, songsArea.Width - Theme.S(4), rh);
                bool hot = clicks.Count == clickHover;
                if (hot) using (var b = new SolidBrush(Color.FromArgb(40, Theme.Primary))) g.FillRectangle(b, row);
                TextRenderer.DrawText(g, (i + 1).ToString(), Theme.Body, new Rectangle(row.X + Theme.S(8), row.Y, numW, rh), Theme.Faint, Clipped);
                string length = s.Length > TimeSpan.Zero ? Time(s.Length) : "";
                int lw = length == "" ? 0 : TextRenderer.MeasureText(g, length, Theme.Body).Width;
                int tx = row.X + Theme.S(8) + numW;
                string text = s.Title + (s.Artist != "" ? "  ·  " + s.Artist : "");
                TextRenderer.DrawText(g, text, Theme.Body, new Rectangle(tx, row.Y, row.Right - tx - lw - Theme.S(12), rh), hot ? Theme.Bright : Theme.Primary, Clipped);
                if (length != "") TextRenderer.DrawText(g, length, Theme.Body, new Rectangle(row.Right - lw - Theme.S(8), row.Y, lw, rh), Theme.Dim, Clipped);
                int index = i;
                var from = chosen;
                clicks.Add(new KeyValuePair<Rectangle, Action>(Rectangle.Intersect(row, songsArea), () => from.Account.Play(from, index)));
            }
            g.Clip = clip;
        }

        int ClickAt(Point p)
        {
            for (int i = clicks.Count - 1; i >= 0; i--) if (clicks[i].Key.Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int i = EqColumnAt(e.Location);
            if (i < 0 || !Equalizer.Enabled) return;
            // A double-click puts the band back to 0 dB.
            Equalizer.Gains[i] = 0;
            Invalidate(VizBox());
            if (EqChanged != null) EqChanged();
            if (EqCommitted != null) EqCommitted();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Tab == StatusTab && statusList.Contains(e.Location))
            {
                statusScroll = Math.Max(0, statusScroll - e.Delta / 120 * RowHeight * 2);
                clickHover = -1;
                Invalidate(VizBox());
                return;
            }
            if (Tab == PlaylistsTab && (plList.Contains(e.Location) || songList.Contains(e.Location)))
            {
                int step = e.Delta / 120 * RowHeight * 3;
                if (plList.Contains(e.Location)) plScroll = Math.Max(0, plScroll - step);
                else songScroll = Math.Max(0, songScroll - step);
                clickHover = -1;
                Invalidate(VizBox());
                return;
            }
            if (ShowsLyrics && VizBox().Contains(e.Location))
            {
                // Scroll the lyrics by hand; they drift back to the song 4 seconds after the last turn of the wheel.
                if (DateTime.UtcNow >= manualUntil) manualOffset = 0;
                manualOffset -= e.Delta / 120f * LineHeight * 2;
                manualUntil = DateTime.UtcNow.AddSeconds(4);
                Invalidate(VizBox());
                return;
            }
            int i = EqColumnAt(e.Location);
            if (i < 0 || !Equalizer.Enabled) return;
            Equalizer.Gains[i] = Math.Max(-Equalizer.MaxGain, Math.Min(Equalizer.MaxGain, Equalizer.Gains[i] + (e.Delta > 0 ? 0.5f : -0.5f)));
            Invalidate(VizBox());
            if (EqChanged != null) EqChanged();
            if (EqCommitted != null) EqCommitted();
        }

        // Shown when the player gives no album art: the Dino footprint in a dark square.
        static void DrawFootprint(Graphics g, Rectangle r)
        {
            using (var b = new SolidBrush(Color.FromArgb(10, 12, 16))) g.FillRectangle(b, r);
            var pts = new PointF[SkullArt.Foot.Length / 2];
            float u = r.Height * 0.3f;
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(r.X + r.Width / 2f + SkullArt.Foot[2 * i] * u, r.Y + r.Height / 2f + SkullArt.Foot[2 * i + 1] * u);
            var smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            {
                path.AddClosedCurve(pts, 0.35f);
                using (var fill = new SolidBrush(Color.FromArgb(60, Theme.Primary))) g.FillPath(fill, path);
                using (var line = new Pen(Theme.Dim, Theme.S(2))) g.DrawPath(line, path);
            }
            g.SmoothingMode = smoothing;
        }

        Rectangle ProgressHit { get { return progress.IsEmpty ? Rectangle.Empty : Rectangle.Inflate(progress, Theme.S(4), Theme.S(10)); } }
        bool seeking;

        readonly SkullHologram skull = new SkullHologram();
        Rectangle holoArea;

        // Previous, Play/Pause and Next in the track box.
        public event Action<int> PlayerCommand;   // a MediaWatcher command
        readonly Rectangle[] playerButtons = new Rectangle[3];
        int playerHover = -1;

        int PlayerButtonAt(Point p)
        {
            if (string.IsNullOrEmpty(info.Title)) return -1;
            for (int k = 0; k < playerButtons.Length; k++) if (playerButtons[k].Contains(p)) return k;
            return -1;
        }

        void SeekTo(int mouseX)
        {
            if (progress.Width <= 0 || info.Duration <= TimeSpan.Zero) return;
            double f = Math.Max(0, Math.Min(1, (mouseX - progress.X) / (double)progress.Width));
            SeekToTime(TimeSpan.FromSeconds(info.Duration.TotalSeconds * f));
        }

        void SeekToTime(TimeSpan target)
        {
            // Show the new spot right away; the player confirms it a moment later.
            info.Position = target;
            info.UpdatedUtc = DateTime.UtcNow;
            Invalidate(TrackBox());
            if (SeekRequested != null) SeekRequested(target);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int player = PlayerButtonAt(e.Location);
            if (player >= 0)
            {
                if (PlayerCommand != null) PlayerCommand(player == 0 ? MediaWatcher.Previous : player == 1 ? MediaWatcher.PlayPause : MediaWatcher.Next);
                return;
            }
            if (vizTab.Contains(e.Location)) { SetTab(VizTab); return; }
            if (eqTab.Contains(e.Location)) { SetTab(EqTab); return; }
            if (lyricsTab.Contains(e.Location)) { SetTab(LyricsTab); return; }
            if (playlistsTab.Contains(e.Location)) { SetTab(PlaylistsTab); return; }
            if (secretTab.Contains(e.Location)) { SetTab(SecretTab); return; }
            if (infoTab.Contains(e.Location)) { SetTab(InfoTab); return; }
            if (statusTab.Contains(e.Location)) { SetTab(StatusTab); return; }
            int click = ClickAt(e.Location);
            if (click >= 0) { clicks[click].Value(); return; }
            int lyric = LyricLineAt(e.Location);
            if (lyric >= 0)
            {
                var lyrics = CurrentLyrics;
                if (lyrics != null && lyric < lyrics.Lines.Count)
                {
                    manualUntil = DateTime.MinValue;
                    SeekToTime(lyrics.Lines[lyric].Time);
                }
                return;
            }
            if (getButton.Contains(e.Location))
            {
                try { Process.Start(Equalizer.Download); } catch { }
                return;
            }
            int band = EqColumnAt(e.Location);
            if (band >= 0 && Equalizer.Enabled)
            {
                eqDrag = band;
                Capture = true;
                SetBand(band, e.Y);
                return;
            }
            if (!ProgressHit.Contains(e.Location)) return;
            seeking = true;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (eqDrag >= 0) { SetBand(eqDrag, e.Y); return; }
            bool on = seeking || ProgressHit.Contains(e.Location);
            int tab = vizTab.Contains(e.Location) ? VizTab : eqTab.Contains(e.Location) ? EqTab : lyricsTab.Contains(e.Location) ? LyricsTab
                    : playlistsTab.Contains(e.Location) ? PlaylistsTab : secretTab.Contains(e.Location) ? SecretTab : infoTab.Contains(e.Location) ? InfoTab
                    : statusTab.Contains(e.Location) ? StatusTab : -1;
            int band = Equalizer.Enabled ? EqColumnAt(e.Location) : -1;
            int lyric = LyricLineAt(e.Location);
            int click = ClickAt(e.Location);
            bool get = getButton.Contains(e.Location);
            int player = PlayerButtonAt(e.Location);
            Cursor = on || tab >= 0 || get || lyric >= 0 || click >= 0 || player >= 0 ? Cursors.Hand : band >= 0 ? Cursors.SizeNS : Cursors.Default;
            if (on != progressHover) { progressHover = on; Invalidate(TrackBox()); }
            if (player != playerHover) { playerHover = player; Invalidate(TrackBox()); }
            if (tab != hoverTab || band != eqHover || get != getHover || lyric != lyricHover || click != clickHover)
            {
                hoverTab = tab;
                eqHover = band;
                getHover = get;
                lyricHover = lyric;
                clickHover = click;
                Invalidate(VizBox());
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (eqDrag >= 0)
            {
                eqDrag = -1;
                Capture = false;
                Invalidate(VizBox());
                if (EqCommitted != null) EqCommitted();
                return;
            }
            if (!seeking) return;
            seeking = false;
            Capture = false;
            SeekTo(e.X);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (progressHover && !seeking) { progressHover = false; Invalidate(TrackBox()); }
            if (playerHover >= 0) { playerHover = -1; Invalidate(TrackBox()); }
            if (eqDrag < 0 && (hoverTab >= 0 || eqHover >= 0 || getHover || lyricHover >= 0 || clickHover >= 0))
            {
                hoverTab = eqHover = lyricHover = clickHover = -1;
                getHover = false;
                Invalidate(VizBox());
            }
        }
    }

    class MainForm : Form
    {
        static readonly string[] StyleNames = { "Off", "Dino Style", "Windows Style", "Aero Style", "XP Style", "Orb Style" };
        static readonly string[] PresetNames = { "Blue", "Cyan", "Green", "Amber", "Red", "Magenta", "Violet" };
        static readonly float[] PresetHues = { 225, 190, 130, 40, 0, 305, 265 };
        static readonly string[] ScanlineNames = { "Off", "Subtle", "Strong" };
        static readonly int[] ScanlineAlphas = { 0, 70, 125 };
        const int HotWidget = 1, WmHotkey = 0x0312, ModCtrl = 2, NoRepeat = 0x4000;

        readonly TopBar top;
        readonly BottomBar bottom;
        readonly OptionsPanel options;
        readonly VisualizerPage page;
        readonly BarButton widgetButton;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer frames = new System.Windows.Forms.Timer { Interval = 33 };
        readonly System.Windows.Forms.Timer ui = new System.Windows.Forms.Timer { Interval = 500 };
        MediaWatcher watcher;
        NowPlayingInfo current = new NowPlayingInfo();
        NowPlayingWidget widget;
        float widgetOpacity = (float)Math.Max(NowPlayingWidget.MinOpacity, Math.Min(1, Settings.Num("widgetopacity", 1)));
        int vizStyle, scanIdx, uiTicks;
        bool closeToTray, exiting, trayHintShown, minimized, startInTray;
        readonly VrWrist wrist = new VrWrist();

        public MainForm()
        {
            scanIdx = Clamp(Settings.Num("scanlines", 1), ScanlineNames.Length);
            vizStyle = Clamp(Settings.Num("viz", 0), VizEngine.Names.Length);
            closeToTray = Settings.Num("tray", 1) != 0;
            UiDraw.ScanlineAlpha = ScanlineAlphas[scanIdx];
            Theme.Saturation = (float)Math.Max(0, Math.Min(Theme.MaxSaturation, Settings.Num("saturation", 1)));
            Theme.SetHue((float)Settings.Num("hue", Theme.DefaultHue));
            hueNow = hueGoal = Theme.Hue;
            cycleIdx = Clamp(Settings.Num("huecycle", 0), CycleNames.Length);
            randomIdx = Clamp(Settings.Num("randomcolors", 0), RandomNames.Length);
            ColorMix.Index = Clamp(Settings.Num("colors", 0), ColorMix.Names.Length);
            ColorMix.Random = randomIdx > 0;
            ColorMix.Reroll();

            Text = "Dino Visualizer";
            BackColor = Theme.Bg;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Theme.S(1400), wa.Width), Math.Min(Theme.S(960), wa.Height));
            MinimumSize = new Size(Math.Min(Theme.S(1000), wa.Width), Math.Min(Theme.S(920), wa.Height));
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            top = new TopBar { Dock = DockStyle.Top, Title = "Dino Visualizer Status: ", Right1 = "Dino Visualizer v " + Updater.Shown(Updater.Current), Right2 = "Saosin LLC 2026" };
            bottom = new BottomBar { Dock = DockStyle.Bottom };
            var frame = new FramePanel { Dock = DockStyle.Fill };
            options = new OptionsPanel { Dock = DockStyle.Right };
            page = new VisualizerPage { Dock = DockStyle.Fill, Style = vizStyle, Tab = Clamp(Settings.Num("tab", 0), VisualizerPage.TabCount), LyricsWithViz = Settings.Num("lyricsviz", 0) != 0 };
            YouTubeAccount.OpenInMusic = Settings.Num("ytmusic", 1) != 0;
            VrcChatbox.Port = (int)Settings.Num("vrcport", 9000);
            VrcChatbox.Mode = Clamp(Settings.Num("vrchat", 0), VrcChatbox.Names.Length);
            // The bar's look; from before Knob was taken out (it was 14), the ones after it move down one.
            int oldBar = (int)Settings.Num("vrcbar", 0);
            VrcChatbox.Bar = Clamp(Settings.Num("vrcbar2", oldBar == 14 ? 0 : oldBar > 14 ? oldBar - 1 : oldBar), VrcChatbox.BarNames.Length);
            VrcChatbox.Adlibs = Clamp(Settings.Num("vrcadlibs", 0), VrcChatbox.AdlibNames.Length);
            VrcChatbox.Restore(Settings.Num("vrcstatus", 0) != 0, (int)Settings.Num("vrcstatuscycle", 0), (int)Settings.Num("vrcstatusidx", 0));
            Updater.Mode = Clamp(Settings.Num("updates", 0), Updater.Names.Length);
            // The VR wrist (it only connects while SteamVR is running).
            wrist.Width = (float)Math.Max(0.06, Math.Min(0.4, Settings.Num("vrwristsize", 0.17)));
            wrist.OffsetX = (float)Settings.Num("vrwristx", 0);
            wrist.OffsetY = (float)Settings.Num("vrwristy", 0);
            wrist.OffsetZ = (float)Settings.Num("vrwristz", 0);
            wrist.LevelSource = () => watcher != null ? watcher.Level : 0;
            wrist.App = this;
            wrist.Live = page;
            wrist.WindowWidth = (float)Math.Max(0.4, Math.Min(3, Settings.Num("vrwindowsize", 1)));
            wrist.Roll = (float)Settings.Num("vrwristroll", 0);
            wrist.Adjusted += () =>
            {
                Settings.Set("vrwristsize", Math.Round(wrist.Width, 3));
                Settings.Set("vrwristroll", wrist.Roll);
                options.Invalidate();   // the VR Wrist Size row
            };
            wrist.WindowResized += size => Settings.Set("vrwindowsize", Math.Round(size, 2));
            wrist.Visualizer = vizStyle;
            wrist.Command += slot => { if (watcher != null) watcher.Send(slot); };
            wrist.Changed += () => { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)UpdateStatus); };
            wrist.Look = Clamp(Settings.Num("vrwristlook", 0), VrWrist.Looks.Length);
            wrist.Place = Clamp(Settings.Num("vrwrist", 0), VrWrist.Names.Length);
            AutoStart.Follow();
            startInTray = AutoStart.InTray;
            if (startInTray) Opacity = 0;
            MusicAccount.Changed += () => { try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)page.PlaylistsChanged); } catch { } };
            page.AccountClicked += AccountMenu;
            page.SeekRequested += position => { if (watcher != null) watcher.Seek(position); };
            page.TabChanged += () => Settings.Set("tab", page.Tab);
            Lyrics.Changed += () => { try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)page.LyricsArrived); } catch { } };
            SongInfo.Changed += () => { try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)page.InfoArrived); } catch { } };
            // While a band is dragged the sound follows along (a few times a second); letting go saves it.
            page.EqChanged += () => { eqDirty = true; options.Invalidate(); };
            page.EqCommitted += () => { Equalizer.Save(); ApplyEq(); };
            eqTimer.Tick += (s, e) => { if (eqDirty) ApplyEq(); };
            eqTimer.Start();
            Equalizer.Load();
            eqInstalled = Equalizer.Installed;
            if (eqInstalled) Equalizer.Apply();
            frame.Controls.Add(page);
            frame.Controls.Add(options);
            Controls.Add(frame);
            Controls.Add(top);
            Controls.Add(bottom);

            BuildOptions();
            // Quit closes the whole app (the widget and the tray icon too); Minimize just puts the window away. The
            // player's buttons are in the track box above.
            page.PlayerCommand += Send;
            page.SecretsChanged += UpdateStrawberry;
            bottom.Buttons.Add(new BarButton { Text = "Quit", Click = () => BeginInvoke((Action)ExitApp) });
            // Checks for a new version; once one's found, installs it.
            updateButton = new BarButton { Click = () => { if (Updater.Now == Updater.Step.Available) Updater.Install(); else Updater.Check(); } };
            bottom.Buttons.Add(updateButton);
            Updater.Changed += () => { try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)UpdaterChanged); } catch { } };
            UpdaterChanged();
            widgetButton = new BarButton { Right = true, Click = ToggleWidget };
            bottom.Buttons.Add(widgetButton);
            bottom.Buttons.Add(new BarButton { Text = "Minimize", Right = true, Click = () => WindowState = FormWindowState.Minimized });

            // It keeps running in the notification area, so the widget stays up with the window closed.
            tray = new NotifyIcon { Text = "Dino Visualizer", Visible = true };
            try { tray.Icon = Icon != null ? new Icon(Icon, SystemInformation.SmallIconSize) : SystemIcons.Application; } catch { tray.Icon = SystemIcons.Application; }
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Dino Visualizer", null, (s, e) => ShowWindow());
            var widgetItem = new ToolStripMenuItem("Show Widget", null, (s, e) => ToggleWidget()) { ShortcutKeyDisplayString = "Ctrl+F11" };
            menu.Items.Add(widgetItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitApp());
            menu.Opening += (s, e) => widgetItem.Text = widget == null ? "Show Widget" : "Hide Widget";
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };

            Theme.Changed += OnThemeChanged;
            Deactivate += (s, e) => options.CloseList();
            frames.Tick += (s, e) =>
            {
                StepColors();   // also while the window is hidden: the widget shows the colors too
                // (Also while the window floats in VR, even with it put away here.)
                if ((Visible && WindowState != FormWindowState.Minimized) || wrist.WindowOpen) page.Animate(watcher != null ? watcher.Level : 0);
            };
            ui.Tick += (s, e) =>
            {
                if ((Visible && WindowState != FormWindowState.Minimized) || wrist.WindowOpen) page.Pulse();
                VrcChatbox.Tick(current);
                // Memory that's no longer needed goes back to Windows: once starting up has settled, then every ten
                // minutes.
                uiTicks++;
                if (uiTicks == 40 || uiTicks % 1200 == 0) Ram.Trim();
                StepSleep();
                // Updates: looked for 30 seconds after starting, then every 6 hours.
                if (Updater.Mode != 2 && (uiTicks == 60 || uiTicks % 43200 == 0)) Updater.Check();
                // The moment Equalizer APO turns up (just installed), the saved bands are handed to it.
                bool installed = Equalizer.Installed;
                if (installed && !eqInstalled) ApplyEq();
                eqInstalled = installed;
            };
            UpdateStatus();
        }

        static int Clamp(double v, int count) { return Math.Max(0, Math.Min(count - 1, (int)v)); }

        // ---- Moving colors: Hue Cycle turns the accent hue round the color wheel; Random Colors jumps it (and the
        // Colors palette) somewhere new on the beat or every few seconds, gliding there rather than snapping.

        static readonly string[] CycleNames = { "Off", "Slow", "Medium", "Fast" };
        static readonly float[] CycleSpeeds = { 0, 3, 9, 30 };            // degrees a second: a full turn in 2 min, 40 s, 12 s
        static readonly string[] RandomNames = { "Off", "On Beats", "Every 5 s", "Every 15 s" };
        static readonly double[] RandomEvery = { 0, 0.5, 5, 15 };         // seconds (on beats: at most this often)
        const double BeatFallback = 3;                                    // on beats: change anyway after this long without one
        int cycleIdx, randomIdx;
        float hueNow, hueGoal;                                            // hues unwrapped (they can run past 360)
        readonly BeatDetector beats = new BeatDetector();
        bool settingHue;
        readonly Stopwatch colorClock = Stopwatch.StartNew();
        readonly Random colorRandom = new Random();
        DateTime lastJump, lastHueSet;

        static float WrapHue(float h) { return ((h % 360) + 360) % 360; }

        void StepColors()
        {
            float dt = Math.Min(0.2f, (float)colorClock.Elapsed.TotalSeconds);
            colorClock.Restart();
            if (cycleIdx == 0 && randomIdx == 0 && Math.Abs(hueGoal - hueNow) < 0.1f) return;
            var now = DateTime.UtcNow;
            float spin = CycleSpeeds[cycleIdx] * dt;
            hueNow += spin;
            hueGoal += spin;
            if (randomIdx > 0)
            {
                // On beats: on each beat the detector finds (at most twice a second), and every few seconds anyway
                // while music plays, in case a quiet or very even song gives it no clear beats.
                bool playing = watcher != null && current.Playing;
                bool beat = beats.Next(playing ? watcher.Level : 0);
                double since = (now - lastJump).TotalSeconds;
                bool due = randomIdx == 1 ? playing && ((beat && since >= RandomEvery[1]) || since >= BeatFallback)
                                          : since >= RandomEvery[randomIdx];
                if (due)
                {
                    lastJump = now;
                    hueGoal = hueNow + 60 + (float)colorRandom.NextDouble() * 240;   // somewhere clearly different
                    ColorMix.Reroll();
                }
            }
            hueNow += (hueGoal - hueNow) * 0.12f;
            // Recoloring repaints the whole window, so the hue is set at most 15 times a second.
            float shown = WrapHue(hueNow), diff = Math.Abs(shown - Theme.Hue);
            if ((now - lastHueSet).TotalMilliseconds >= 66 && Math.Min(diff, 360 - diff) > 0.15f)
            {
                lastHueSet = now;
                settingHue = true;
                try { Theme.SetHue(shown); }
                finally { settingHue = false; }
            }
        }

        // Turning the moving colors off keeps the color they were on.
        void ColorsStopped()
        {
            if (cycleIdx == 0 && randomIdx == 0) Settings.Set("hue", Math.Round(Theme.Hue, 1));
        }

        readonly System.Windows.Forms.Timer eqTimer = new System.Windows.Forms.Timer { Interval = 120 };
        bool eqDirty, eqInstalled;

        void ApplyEq()
        {
            eqDirty = false;
            Equalizer.Apply();
            page.Invalidate();
            options.Invalidate();
        }

        void BuildOptions()
        {
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Widget:", Choices = StyleNames,
                Current = () => widget == null ? 0 : widget.Style + 1,
                Choose = i =>
                {
                    if (i == 0) { if (widget != null) ToggleWidget(); return; }
                    Settings.Set("widgetstyle", i - 1);
                    if (widget == null) ToggleWidget();
                    else
                    {
                        widget.Style = i - 1;
                        KeepOnScreen();
                        UpdateStatus();
                    }
                }
            });
            // How see-through the widget is, 20% to 100%; it changes live while dragging.
            float minOp = NowPlayingWidget.MinOpacity;
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Widget Opacity:", Slider = true, Value = () => ((int)Math.Round(widgetOpacity * 100)) + "%",
                Position = () => (widgetOpacity - minOp) / (1 - minOp),
                Slide = p =>
                {
                    widgetOpacity = minOp + p * (1 - minOp);
                    if (widget != null) widget.CardOpacity = widgetOpacity;
                },
                Commit = () => Settings.Set("widgetopacity", Math.Round(widgetOpacity, 2)),
                TrackColors = () => new[] { Theme.Hsv(Theme.Hue, 0.8f, 0.15f), Theme.Dim, Theme.Primary }
            });
            // One visualizer choice for this window and the XP widget's screen (its own drop-down changes it too).
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Visualizer:", Choices = VizEngine.Names, Current = () => vizStyle,
                Choose = SetViz
            });
            // The lyrics over the visualizer, on both their tabs. Switching it on from another tab shows the visualizer.
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Lyrics + Visualizer:", Checked = () => page.LyricsWithViz,
                Toggle = () =>
                {
                    page.LyricsWithViz = !page.LyricsWithViz;
                    Settings.Set("lyricsviz", page.LyricsWithViz ? 1 : 0);
                    if (page.LyricsWithViz && page.Tab != VisualizerPage.VizTab && page.Tab != VisualizerPage.LyricsTab) page.ShowTab(VisualizerPage.VizTab);
                    page.LyricsViewChanged();
                }
            });
            // The equalizer (it works through Equalizer APO; see the Equalizer tab). Picking a preset shows the tab.
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Equalizer:", Checked = () => Equalizer.Enabled,
                Toggle = () =>
                {
                    Equalizer.Enabled = !Equalizer.Enabled;
                    Equalizer.Save();
                    ApplyEq();
                }
            });
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "EQ Preset:", Choices = Equalizer.PresetNames, Current = () => Equalizer.PresetIndex,
                Choose = i =>
                {
                    Equalizer.UsePreset(i);
                    Equalizer.Enabled = true;
                    Equalizer.Save();
                    ApplyEq();
                    if (page.Tab != VisualizerPage.EqTab) { page.Tab = VisualizerPage.EqTab; Settings.Set("tab", page.Tab); }
                }
            });
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Close To Tray:", Checked = () => closeToTray,
                Toggle = () => { closeToTray = !closeToTray; Settings.Set("tray", closeToTray ? 1 : 0); }
            });
            // New versions from GitHub: shown in the bottom bar (and the tray), installed by themselves, or not looked for.
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Updates:", Choices = Updater.Names, Current = () => Updater.Mode,
                Choose = i =>
                {
                    Updater.Mode = i;
                    Settings.Set("updates", i);
                    if (i == 1 && Updater.Now == Updater.Step.Available) Updater.Install();
                }
            });
            // Pauses the music after a while (fading it out first); the top bar counts down.
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Sleep Timer:", Choices = SleepNames, Current = () => sleepIdx,
                Choose = SetSleep
            });
            // Starts when you sign in to Windows, with the window open or just in the tray.
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Auto Start:", Choices = AutoStart.Names, Current = () => AutoStart.Mode,
                Choose = i => AutoStart.Mode = i
            });
            // What's playing above your head in VRChat (through OSC; see VrcChatbox).
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "VRChat Chatbox:", Choices = VrcChatbox.Names, Current = () => VrcChatbox.Mode,
                Choose = i => { VrcChatbox.Mode = i; Settings.Set("vrchat", i); VrcChatbox.Tick(current); }
            });
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Chatbox Bar:", Choices = VrcChatbox.BarNames, Current = () => VrcChatbox.Bar,
                Choose = i => { VrcChatbox.Bar = i; Settings.Set("vrcbar2", i); VrcChatbox.Tick(current); }
            });
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Chatbox Adlibs:", Choices = VrcChatbox.AdlibNames, Current = () => VrcChatbox.Adlibs,
                Choose = i => { VrcChatbox.Adlibs = i; Settings.Set("vrcadlibs", i); VrcChatbox.Tick(current); }
            });
            // The hologram card on a wrist in SteamVR, and which of the widget's looks it has.
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "VR Wrist:", Choices = VrWrist.Names, Current = () => wrist.Place,
                Choose = i => { wrist.Place = i; Settings.Set("vrwrist", i); UpdateStatus(); }
            });
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "VR Wrist Look:", Choices = VrWrist.Looks, Current = () => wrist.Look,
                Choose = i => { wrist.Look = i; Settings.Set("vrwristlook", i); }
            });
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "VR Wrist Size:", Choices = VrWrist.Sizes, Current = () => wrist.SizeIndex,
                Choose = i => { wrist.SizeIndex = i; Settings.Set("vrwristsize", wrist.Width); }
            });
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Scanlines:", Choices = ScanlineNames, Current = () => scanIdx,
                Choose = i =>
                {
                    scanIdx = i;
                    UiDraw.ScanlineAlpha = ScanlineAlphas[i];
                    Settings.Set("scanlines", i);
                    Invalidate(true);
                }
            });
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Color Preset:", Choices = PresetNames,
                Current = () => Array.FindIndex(PresetHues, h => Math.Abs(h - Theme.Hue) < 1),
                Choose = i =>
                {
                    Theme.SetHue(PresetHues[i]);
                    Settings.Set("hue", Theme.Hue);
                }
            });
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Hue Cycle:", Choices = CycleNames, Current = () => cycleIdx,
                Choose = i => { cycleIdx = i; Settings.Set("huecycle", i); ColorsStopped(); }
            });
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Random Colors:", Choices = RandomNames, Current = () => randomIdx,
                Choose = i =>
                {
                    randomIdx = i;
                    ColorMix.Random = i > 0;
                    if (i > 0) ColorMix.Reroll();
                    Settings.Set("randomcolors", i);
                    ColorsStopped();
                    page.Invalidate();
                }
            });
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Colors:", Choices = ColorMix.Names, Current = () => ColorMix.Index,
                Choose = i => { ColorMix.Index = i; Settings.Set("colors", i); page.Invalidate(); }
            });
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Accent Hue:", Slider = true, Value = () => ((int)Math.Round(Theme.Hue)) + "°",
                Position = () => Theme.Hue / 360f,
                Slide = p => Theme.SetHue(Math.Min(359.9f, p * 360f)),
                Commit = () => Settings.Set("hue", Math.Round(Theme.Hue, 1)),
                TrackColors = () => Enumerable.Range(0, 7).Select(k => Theme.Hsv(k * 60, 0.82f, 1f)).ToArray()
            });
            // Saturation: gray at the left, the normal look at 100%, extra vivid toward the right.
            options.Rows.Add(new OptionsPanel.Row
            {
                Label = "Accent Saturation:", Slider = true, Value = () => ((int)Math.Round(Theme.Saturation * 100)) + "%",
                Position = () => Theme.Saturation / Theme.MaxSaturation,
                Slide = p => Theme.SetSaturation(p * Theme.MaxSaturation),
                Commit = () => Settings.Set("saturation", Math.Round(Theme.Saturation, 2)),
                TrackColors = () => new[] { Theme.Hsv(Theme.Hue, 0f, 0.8f), Theme.Hsv(Theme.Hue, 0.5f, 0.9f), Theme.Hsv(Theme.Hue, 1f, 1f) }
            });
        }

        // An account button on the Playlists tab: not connected yet opens the setup window (then signs in);
        // connected shows a small menu.
        void AccountMenu(MusicAccount account, Point at)
        {
            if (account.Busy) return;
            if (!account.Connected)
            {
                using (var setup = new AccountSetup(account))
                    if (setup.ShowDialog(this) == DialogResult.OK) account.SignIn();
                return;
            }
            var menu = new ContextMenuStrip();
            menu.Items.Add("Reload Playlists", null, (s, e) => account.Reload());
            menu.Items.Add("Change Developer Key...", null, (s, e) =>
            {
                using (var setup = new AccountSetup(account))
                    if (setup.ShowDialog(this) == DialogResult.OK) { account.SignOut(); account.SignIn(); }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sign Out of " + account.Name, null, (s, e) => account.SignOut());
            menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
            menu.Show(at);
        }

        void SetViz(int v)
        {
            vizStyle = v;
            Settings.Set("viz", v);
            page.Style = v;
            if (widget != null) widget.Visualizer = v;
            wrist.Visualizer = v;
            page.Invalidate();
            options.Invalidate();
        }

        // A new report from the media controls (on this window's thread). The page and the widget share it; album art
        // is let go once neither shows it any more.
        void OnInfo(NowPlayingInfo info)
        {
            var old = current;
            current = info;
            page.SetInfo(info);
            if (widget != null) widget.SetInfo(info);
            wrist.SetInfo(info);
            if (old.Art != null && old.Art != info.Art) old.Art.Dispose();
            UpdateStatus();
        }

        // The bouncing strawberry (Dance Gavin Dance) or beetle (Saosin): out while that band is playing. When its song
        // ends (the player moves on to another track, or stops at the end) it explodes; paused partway through, it just
        // goes. If the next song is one of theirs too, a fresh one comes out.
        BouncerWindow strawberry;

        void UpdateStrawberry()
        {
            int kind = current.Playing ? BouncerWindow.For(current) : BouncerWindow.None;
            string song = current.Artist + "|" + current.Title;
            if (strawberry != null)
            {
                bool atEnd = current.Duration > TimeSpan.Zero && current.Now >= current.Duration - TimeSpan.FromSeconds(2);
                bool ended = song != strawberry.Song || (!current.Playing && atEnd);
                if (ended) strawberry.Explode();
                else if (kind != strawberry.Kind) strawberry.Close();
                if (ended || kind != strawberry.Kind) strawberry = null;
            }
            if (kind != BouncerWindow.None && strawberry == null)
            {
                strawberry = new BouncerWindow(kind) { Song = song };
                strawberry.Show();
            }
        }

        void UpdateStatus()
        {
            UpdateStrawberry();
            bool any = !string.IsNullOrEmpty(current.Title);
            top.State = !any ? "Idle" : current.Playing ? "Playing" : "Paused";
            top.Line2 = "Widget: " + (widget == null ? "Off" : StyleNames[widget.Style + 1]) + "   (Ctrl+F11 shows or hides it)" + SleepNote()
                      + (wrist.Place == 0 ? "" : wrist.Connected ? "   ·   VR Wrist: On" : "   ·   VR Wrist: Waiting For SteamVR");
            top.Invalidate();
            widgetButton.Text = widget == null ? "Show Widget" : "Hide Widget";
            bottom.Invalidate();
            string tip = any ? "Dino Visualizer: " + current.Title + (current.Artist != "" ? " - " + current.Artist : "") : "Dino Visualizer";
            tray.Text = tip.Length > 63 ? tip.Substring(0, 62) + "…" : tip;   // the tray tooltip is limited to 63 characters
        }

        void Send(int slot)
        {
            if (watcher == null || string.IsNullOrEmpty(current.Title)) return;
            if (slot == MediaWatcher.PlayPause)
            {
                // Show the new state right away; the player confirms it a moment later.
                current.Position = current.Now;
                current.UpdatedUtc = DateTime.UtcNow;
                current.Playing = !current.Playing;
                page.Invalidate();
                if (widget != null) widget.Invalidate();
                UpdateStatus();
            }
            watcher.Send(slot);
        }

        // Keeps the widget on a screen after a style change (the styles have different sizes).
        void KeepOnScreen()
        {
            if (widget == null) return;
            var area = Screen.FromRectangle(widget.Bounds).WorkingArea;
            widget.Location = new Point(Math.Max(area.Left, Math.Min(widget.Left, area.Right - widget.Width)),
                                        Math.Max(area.Top, Math.Min(widget.Top, area.Bottom - widget.Height)));
            Settings.Set("widgetx", widget.Left);
            Settings.Set("widgety", widget.Top);
        }

        // Shows or hides the widget (Options, the button, the tray menu or Ctrl+F11), in the last style picked.
        void ToggleWidget()
        {
            if (widget != null)
            {
                widget.Close();
                widget = null;
                Settings.Set("widget", 0);
            }
            else
            {
                widget = new NowPlayingWidget((float)Settings.Num("widgetscale", 1), Math.Max(0, Math.Min(NowPlayingWidget.OrbStyle, (int)Settings.Num("widgetstyle", 0))));
                widget.CardOpacity = widgetOpacity;
                // Where it was left, if that's still on a screen; else the top-right corner of the main display.
                var wa = Screen.PrimaryScreen.WorkingArea;
                var at = new Point((int)Settings.Num("widgetx", wa.Right - widget.Width - Theme.S(24)), (int)Settings.Num("widgety", wa.Top + Theme.S(24)));
                if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(at, widget.Size)))) at = new Point(wa.Right - widget.Width - Theme.S(24), wa.Top + Theme.S(24));
                widget.Location = at;
                KeepOnScreen();
                var w = widget;
                widget.Moved += () => { Settings.Set("widgetx", w.Left); Settings.Set("widgety", w.Top); };
                widget.Resized += () =>
                {
                    Settings.Set("widgetscale", Math.Round(w.CardScale, 3));
                    Settings.Set("widgetx", w.Left);
                    Settings.Set("widgety", w.Top);
                };
                widget.Command += slot => { if (watcher != null) watcher.Send(slot); };
                widget.VolumeChanged += level => { if (watcher != null) watcher.SetVolume(level); };
                widget.SeekRequested += position => { if (watcher != null) watcher.Seek(position); };
                widget.LevelSource = () => watcher != null ? watcher.Level : 0;
                widget.Visualizer = vizStyle;
                widget.VisualizerChanged += SetViz;
                widget.CloseRequested += () => { if (widget == w) ToggleWidget(); };   // the XP widget's red button
                widget.Show();
                widget.SetInfo(current);
                Settings.Set("widget", 1);
            }
            options.Invalidate();
            UpdateStatus();
        }

        void ShowWindow()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        // ---- The sleep timer: when it runs out the music pauses. Over its last 30 seconds the player's own volume fades
        // down, and a moment after the pause it goes back to where it was, ready for next time. It isn't remembered
        // between launches.

        static readonly string[] SleepNames = { "Off", "15 min", "30 min", "45 min", "1 hour", "1.5 hours", "2 hours" };
        static readonly int[] SleepMinutes = { 0, 15, 30, 45, 60, 90, 120 };
        static readonly TimeSpan SleepFade = TimeSpan.FromSeconds(30);
        int sleepIdx;
        DateTime sleepAt, restoreAt;   // when the music pauses; when its volume goes back (MinValue: not waiting)
        float fadeFrom = -1;           // the player's volume when the fade began (-1: not fading)

        void SetSleep(int i)
        {
            sleepIdx = i;
            sleepAt = i == 0 ? DateTime.MinValue : DateTime.UtcNow.AddMinutes(SleepMinutes[i]);
            if (fadeFrom >= 0 && watcher != null) watcher.SetVolume(fadeFrom);   // changed partway through a fade
            fadeFrom = -1;
            restoreAt = DateTime.MinValue;
            UpdateStatus();
        }

        string SleepNote()
        {
            if (sleepAt == DateTime.MinValue) return "";
            var left = sleepAt - DateTime.UtcNow;
            return "   ·   Sleep in " + Util.Clock(left > TimeSpan.Zero ? left : TimeSpan.Zero);
        }

        // Twice a second.
        void StepSleep()
        {
            var now = DateTime.UtcNow;
            if (restoreAt != DateTime.MinValue && now >= restoreAt)
            {
                if (watcher != null && fadeFrom >= 0) watcher.SetVolume(fadeFrom);
                fadeFrom = -1;
                restoreAt = DateTime.MinValue;
            }
            if (sleepAt == DateTime.MinValue) return;
            var left = sleepAt - now;
            if (left > TimeSpan.Zero)
            {
                if (left <= SleepFade && watcher != null && current.Playing)
                {
                    if (fadeFrom < 0 && current.Volume > 0) fadeFrom = current.Volume;
                    if (fadeFrom >= 0) watcher.SetVolume(fadeFrom * (float)(left.TotalSeconds / SleepFade.TotalSeconds));
                }
            }
            else
            {
                // Time's up: pause, then put the volume back once the player has stopped.
                if (current.Playing) Send(MediaWatcher.PlayPause);
                if (fadeFrom >= 0) restoreAt = now.AddSeconds(2);
                sleepIdx = 0;
                sleepAt = DateTime.MinValue;
                options.Invalidate();
            }
            UpdateStatus();
        }

        // ---- Updates (see Updater): the button in the bottom bar says where it's got to.
        BarButton updateButton;
        Version announced;   // the new version the tray balloon has already mentioned

        void UpdaterChanged()
        {
            var u = Updater.Now;
            updateButton.Text = u == Updater.Step.Checking ? "Checking..."
                              : u == Updater.Step.UpToDate ? "Up To Date"
                              : u == Updater.Step.Available ? "Update To v" + Updater.Shown(Updater.Latest)
                              : u == Updater.Step.Downloading ? "Updating " + Updater.Percent + "%"
                              : u == Updater.Step.Ready ? "Restarting..."
                              : u == Updater.Step.Failed ? Updater.Problem
                              : "Check For Updates";
            updateButton.Disabled = u == Updater.Step.Checking || u == Updater.Step.Downloading || u == Updater.Step.Ready;
            bottom.Invalidate();
            if (u == Updater.Step.Available)
            {
                if (Updater.Mode == 1) Updater.Install();
                else if (announced != Updater.Latest)
                {
                    announced = Updater.Latest;
                    tray.ShowBalloonTip(6000, "Dino Visualizer " + Updater.Shown(Updater.Latest) + " is out",
                        "Click Update To v" + Updater.Shown(Updater.Latest) + " at the bottom of the window to install it.", ToolTipIcon.Info);
                }
            }
            else if (u == Updater.Step.Ready)
            {
                Updater.Restart(!Visible);
                ExitApp();
            }
        }

        void ExitApp()
        {
            exiting = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && closeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                options.CloseList();
                Hide();
                PutAway();
                if (!trayHintShown)
                {
                    trayHintShown = true;
                    tray.ShowBalloonTip(4000, "Dino Visualizer", "Still running here in the tray. Click the icon to open it, or right-click it to exit.", ToolTipIcon.Info);
                }
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Backdrop.Ready -= BackdropReady;
            frames.Stop();
            ui.Stop();
            if (widget != null) widget.Close();
            widget = null;
            if (watcher != null) watcher.Dispose();
            watcher = null;
            if (strawberry != null) strawberry.Close();
            strawberry = null;
            VrcChatbox.Stop();
            wrist.Dispose();
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosed(e);
        }

        void OnThemeChanged()
        {
            // The hue slider or a preset moved the color by hand: the moving colors carry on from there.
            if (!settingHue) hueNow = hueGoal = Theme.Hue;
            if (IsHandleCreated) ColorTitleBar();
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int on = 1; Native.DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch { }   // dark title bar
            ColorTitleBar();
            if (!Native.RegisterHotKey(Handle, HotWidget, ModCtrl | NoRepeat, (int)Keys.F11))
                ShowMessage("Ctrl+F11 is taken by another app (Dino Recorder?); use the Widget option");
            if (watcher == null)
                watcher = new MediaWatcher(info =>
                {
                    try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => OnInfo(info))); } catch { }
                });
            if (Settings.Num("widget", 1) != 0) BeginInvoke((Action)ToggleWidget);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Native.UnregisterHotKey(Handle, HotWidget);
            base.OnHandleDestroyed(e);
        }

        // Ctrl+F11 works from any app; a second copy of the app being started asks this one to show its window.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotWidget) { ToggleWidget(); return; }
            if (m.Msg == Native.ShowMessage && Native.ShowMessage != 0) { ShowWindow(); return; }
            base.WndProc(ref m);
        }

        // Windows 11: black caption with the title in the accent color (older Windows just ignores this).
        void ColorTitleBar()
        {
            try
            {
                int caption = 0x00050303, border = ToColorRef(Theme.Primary), text = ToColorRef(Theme.Primary);
                Native.DwmSetWindowAttribute(Handle, 35, ref caption, 4);
                Native.DwmSetWindowAttribute(Handle, 34, ref border, 4);
                Native.DwmSetWindowAttribute(Handle, 36, ref text, 4);
            }
            catch { }
        }

        static int ToColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        void ShowMessage(string msg)
        {
            top.Note = msg;
            top.Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.Paint(e.Graphics, this); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                if (!minimized) PutAway();
                minimized = true;
            }
            else minimized = false;
            Invalidate(true);
        }

        // The window is hidden in the tray or minimized: what only drawing it needs goes (the backdrop, the page's
        // pictures and its Earth), and the memory goes back to Windows. It's all made again when the window shows.
        void PutAway()
        {
            Backdrop.Release();
            page.Release();
            Ram.Trim();
        }

        void BackdropReady()
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => Invalidate(true))); } catch { }
        }

        protected override void OnResizeBegin(EventArgs e) { base.OnResizeBegin(e); Backdrop.Fast = true; }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            Backdrop.Fast = false;
            Invalidate(true);
        }

        // Paint the whole window as one double-buffered image, so the panels don't flicker while resizing.
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;   // WS_EX_COMPOSITED
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Backdrop.Ready += BackdropReady;
            Backdrop.Start(Screen.PrimaryScreen.Bounds.Size, ClientSize);
            frames.Start();
            ui.Start();
            // Started with Windows in the tray: it was shown see-through so everything starts up, and now goes away.
            if (startInTray)
                BeginInvoke((Action)(() =>
                {
                    Hide();
                    Opacity = 1;
                    PutAway();
                }));
        }
    }
}
