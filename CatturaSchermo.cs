// CatturaSchermo - cattura lo schermo (o una parte) con una combinazione di tasti,
// permette di evidenziare/annotare l'immagine e poi copiarla negli appunti o salvarla.
//   Ctrl+Alt+S  -> seleziona un'area (clic singolo = schermo intero sotto il mouse)
//   Ctrl+Alt+F  -> cattura subito lo schermo intero sotto il mouse
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CatturaSchermo
{
    static class Program
    {
        public static string AppName { get { return Lang.T("Cattura Schermo", "Screen Capture"); } }
        public static Action<bool, int> RequestCapture;
        public static Action<string, string> Notify;   // fumetto vicino all'orologio

        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            bool created;
            Lang.Load();
            using (new Mutex(true, "CatturaSchermo_SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show(Lang.T("Cattura Schermo è già in esecuzione (icona vicino all'orologio).",
                                           "Screen Capture is already running (icon next to the clock)."), AppName);
                    return;
                }
                // Coordinate in pixel reali su tutti i monitor (anche con ridimensionamento 125%, 150%...).
                try { if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) SetProcessDPIAware(); }
                catch { try { SetProcessDPIAware(); } catch { } }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }
    }

    // ------------------------------------------------------------------ lingua (italiano / inglese)

    public static class Lang
    {
        const string Key = @"Software\CatturaSchermo";
        static bool en;

        public static bool En { get { return en; } }
        public static event Action Changed;   // le finestre aperte si aggiornano subito

        // La scelta salvata; la prima volta segue la lingua di Windows.
        public static void Load()
        {
            string v = null;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key))
                    if (k != null) v = k.GetValue("Language") as string;
            }
            catch { }
            if (v == "en") en = true;
            else if (v == "it") en = false;
            else en = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "it";
        }

        public static void Set(bool english)
        {
            en = english;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                    k.SetValue("Language", english ? "en" : "it");
            }
            catch { }
            if (Changed != null) Changed();
        }

        public static string T(string it, string english) { return en ? english : it; }
    }

    // ------------------------------------------------------------------ icona dell'app

    public static class IconArt
    {
        public static Bitmap Draw(int s)
        {
            var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                var rc = new RectangleF(s * 0.03f, s * 0.03f, s * 0.94f, s * 0.94f);
                using (var path = RoundRect(rc, s * 0.2f))
                using (var br = new LinearGradientBrush(rc, Color.FromArgb(52, 120, 230), Color.FromArgb(24, 60, 150), 90f))
                    g.FillPath(br, path);
                using (var hb = new SolidBrush(Color.FromArgb(255, 228, 40)))
                    g.FillRectangle(hb, s * 0.24f, s * 0.42f, s * 0.52f, s * 0.17f);
                float a = s * 0.19f, z = s * 0.81f, l = s * 0.17f;
                using (var p = new Pen(Color.White, Math.Max(1.5f, s * 0.075f)))
                {
                    p.LineJoin = LineJoin.Miter;
                    g.DrawLines(p, new[] { new PointF(a, a + l), new PointF(a, a), new PointF(a + l, a) });
                    g.DrawLines(p, new[] { new PointF(z - l, a), new PointF(z, a), new PointF(z, a + l) });
                    g.DrawLines(p, new[] { new PointF(z, z - l), new PointF(z, z), new PointF(z - l, z) });
                    g.DrawLines(p, new[] { new PointF(a + l, z), new PointF(a, z), new PointF(a, z - l) });
                }
            }
            return b;
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Scrive un file .ico con immagini PNG (usato dallo script di compilazione).
        public static void SaveIco(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 256 };
            var images = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = Draw(s))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
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

    // ------------------------------------------------------------------ icona nell'area di notifica

    class TrayContext : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const int IdArea = 1, IdFull = 2;

        readonly NotifyIcon tray;
        readonly HotkeyWindow hotkeys;
        bool busy;

        public TrayContext()
        {
            Program.RequestCapture = StartCapture;

            int isz = SystemInformation.SmallIconSize.Width;
            tray = new NotifyIcon();
            using (var bmp = IconArt.Draw(isz)) tray.Icon = Icon.FromHandle(bmp.GetHicon());
            Program.Notify = (title, text) => tray.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

            var menu = new ContextMenuStrip();
            var area = new ToolStripMenuItem("", null, delegate { StartCapture(false, 250); });
            var full = new ToolStripMenuItem("", null, delegate { StartCapture(true, 250); });
            var auto = new ToolStripMenuItem();
            auto.Checked = IsAutostart();
            auto.Click += delegate { SetAutostart(!auto.Checked); auto.Checked = IsAutostart(); };
            var language = new ToolStripMenuItem("Lingua / Language");
            var italian = new ToolStripMenuItem("Italiano", null, delegate { Lang.Set(false); });
            var english = new ToolStripMenuItem("English", null, delegate { Lang.Set(true); });
            language.DropDownItems.Add(italian);
            language.DropDownItems.Add(english);
            var exit = new ToolStripMenuItem("", null, delegate { ExitThread(); });
            menu.Items.Add(area);
            menu.Items.Add(full);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(auto);
            menu.Items.Add(language);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);
            tray.ContextMenuStrip = menu;

            relabel = delegate
            {
                tray.Text = Program.AppName + "  (Ctrl+Alt+S)";
                area.Text = Lang.T("Cattura un'area", "Capture an area") + "\tCtrl+Alt+S";
                full.Text = Lang.T("Cattura schermo intero", "Capture full screen") + "\tCtrl+Alt+F";
                auto.Text = Lang.T("Avvia con Windows", "Start with Windows");
                exit.Text = Lang.T("Esci", "Exit");
                italian.Checked = !Lang.En;
                english.Checked = Lang.En;
            };
            relabel();
            Lang.Changed += relabel;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) StartCapture(false, 250); };
            tray.Visible = true;

            hotkeys = new HotkeyWindow();
            hotkeys.Pressed += id => StartCapture(id == IdFull, 0);
            var failed = new List<string>();
            if (!hotkeys.Register(IdArea, HotkeyWindow.MOD_CONTROL | HotkeyWindow.MOD_ALT, Keys.S)) failed.Add("Ctrl+Alt+S");
            if (!hotkeys.Register(IdFull, HotkeyWindow.MOD_CONTROL | HotkeyWindow.MOD_ALT, Keys.F)) failed.Add("Ctrl+Alt+F");

            if (failed.Count > 0)
                tray.ShowBalloonTip(5000, Program.AppName,
                    Lang.T("Combinazione già usata da un altro programma: ", "Shortcut already used by another program: ") +
                    string.Join(", ", failed.ToArray()) +
                    Lang.T(".\nPuoi comunque catturare cliccando sull'icona.", ".\nYou can still capture by clicking the icon."), ToolTipIcon.Warning);
            else
                tray.ShowBalloonTip(4000, Lang.T("Cattura Schermo è attivo", "Screen Capture is running"),
                    Lang.T("Ctrl+Alt+S  cattura un'area\nCtrl+Alt+F  cattura lo schermo intero",
                           "Ctrl+Alt+S  capture an area\nCtrl+Alt+F  capture the full screen"), ToolTipIcon.Info);
        }

        readonly Action relabel;

        void StartCapture(bool fullScreen, int delayMs)
        {
            if (busy) return;
            busy = true;
            if (delayMs <= 0) { DoCapture(fullScreen); return; }
            var t = new System.Windows.Forms.Timer();
            t.Interval = delayMs;
            t.Tick += delegate { t.Stop(); t.Dispose(); DoCapture(fullScreen); };
            t.Start();
        }

        void DoCapture(bool fullScreen)
        {
            try
            {
                DateTime when = DateTime.Now;
                IntPtr foreground = WindowInfo.Foreground();
                Rectangle vs = SystemInformation.VirtualScreen;
                Bitmap shot = ScreenGrab.Capture(vs);

                Rectangle area;
                if (fullScreen)
                {
                    area = Screen.FromPoint(Cursor.Position).Bounds;
                    area.Offset(-vs.Left, -vs.Top);
                }
                else
                {
                    using (var ov = new SelectionOverlay(shot, vs))
                    {
                        if (ov.ShowDialog() != DialogResult.OK) { shot.Dispose(); return; }
                        area = ov.Selection;
                    }
                }
                area.Intersect(new Rectangle(0, 0, shot.Width, shot.Height));
                if (area.Width < 1 || area.Height < 1) { shot.Dispose(); return; }

                // Programma catturato: la finestra attiva (schermo intero) o quella al centro dell'area scelta.
                string app = fullScreen
                    ? WindowInfo.AppName(foreground)
                    : WindowInfo.AppNameAt(new Point(vs.Left + area.X + area.Width / 2, vs.Top + area.Y + area.Height / 2));
                string stamp = when.ToString("dd/MM/yyyy HH:mm") + (string.IsNullOrEmpty(app) ? "" : "  ·  " + app);

                Bitmap crop = shot.Clone(area, PixelFormat.Format24bppRgb);
                shot.Dispose();
                var editor = new EditorForm(crop, stamp);
                editor.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lang.T("Errore durante la cattura:\n", "Error while capturing:\n") + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                busy = false;
            }
        }

        static bool IsAutostart()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("CatturaSchermo") != null;
        }

        static void SetAutostart(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("CatturaSchermo", "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue("CatturaSchermo", false);
            }
        }

        protected override void ExitThreadCore()
        {
            Lang.Changed -= relabel;
            hotkeys.Dispose();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }

    static class ScreenGrab
    {
        const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, int rop);

        // Copia una parte dello schermo (coordinate del desktop virtuale), compresi menu e finestre a comparsa.
        public static Bitmap Capture(Rectangle area)
        {
            var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr dest = g.GetHdc();
                IntPtr screen = GetDC(IntPtr.Zero);
                try
                {
                    if (!BitBlt(dest, 0, 0, area.Width, area.Height, screen, area.Left, area.Top, SRCCOPY | CAPTUREBLT))
                        throw new System.ComponentModel.Win32Exception();
                }
                finally
                {
                    ReleaseDC(IntPtr.Zero, screen);
                    g.ReleaseHdc(dest);
                }
            }
            return bmp;
        }
    }

    // Trova il nome del programma a cui appartiene una finestra (es. "Google Chrome", "Microsoft Word").
    static class WindowInfo
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder s, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder s, int max);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, System.Text.StringBuilder s, ref int size);

        public static IntPtr Foreground() { return GetForegroundWindow(); }

        public static string AppNameAt(Point screenPoint)
        {
            return AppName(WindowFromPoint(screenPoint));
        }

        public static string AppName(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return "";
                IntPtr root = GetAncestor(hwnd, 2);   // GA_ROOT: la finestra principale
                if (root != IntPtr.Zero) hwnd = root;

                string cls = ClassName(hwnd);
                if (cls == "Progman" || cls == "WorkerW") return "Desktop";
                if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return Lang.T("Barra delle applicazioni", "Taskbar");

                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return Program.AppName;

                string path = ProcessPath(pid);
                string exe = path == null ? "" : Path.GetFileNameWithoutExtension(path);
                // Le app dello Store girano dentro ApplicationFrameHost: meglio il titolo della finestra.
                if (exe.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)) return Title(hwnd);
                if (path != null)
                {
                    string desc = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription;
                    if (!string.IsNullOrEmpty(desc) && desc.Trim().Length > 0) return desc.Trim();
                    return exe;
                }
                return Title(hwnd);
            }
            catch
            {
                return "";
            }
        }

        static string ProcessPath(uint pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        static string Title(IntPtr hwnd)
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            string t = sb.ToString().Trim();
            return t.Length > 60 ? t.Substring(0, 57) + "…" : t;
        }

        static string ClassName(IntPtr hwnd)
        {
            var sb = new System.Text.StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }

    class HotkeyWindow : NativeWindow, IDisposable
    {
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event Action<int> Pressed;
        readonly List<int> ids = new List<int>();

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        public bool Register(int id, uint mods, Keys key)
        {
            if (!RegisterHotKey(Handle, id, mods | MOD_NOREPEAT, (uint)key)) return false;
            ids.Add(id);
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && Pressed != null) Pressed(m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (int id in ids) UnregisterHotKey(Handle, id);
            ids.Clear();
            DestroyHandle();
        }
    }

    // ------------------------------------------------------------------ selezione dell'area

    class SelectionOverlay : Form
    {
        readonly Bitmap bright, dim;
        readonly Rectangle virtualScreen;
        Point start;
        bool dragging;
        Rectangle sel;
        public Rectangle Selection { get { return sel; } }

        public SelectionOverlay(Bitmap shot, Rectangle vs)
        {
            virtualScreen = vs;
            bright = shot.Clone(new Rectangle(0, 0, shot.Width, shot.Height), PixelFormat.Format32bppPArgb);
            dim = shot.Clone(new Rectangle(0, 0, shot.Width, shot.Height), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(dim))
            using (var br = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                g.FillRectangle(br, 0, 0, dim.Width, dim.Height);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            Text = Program.AppName;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            Bounds = vs;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = virtualScreen;
            Activate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Rectangle clip = e.ClipRectangle;
            g.DrawImage(dim, clip, clip, GraphicsUnit.Pixel);

            float scale = DeviceDpi / 96f;
            using (var font = new Font("Segoe UI", 10.5f))
            {
                if (sel.Width > 0 && sel.Height > 0)
                {
                    g.DrawImage(bright, sel, sel, GraphicsUnit.Pixel);
                    using (var p = new Pen(Color.FromArgb(0, 150, 255), 1))
                        g.DrawRectangle(p, sel.X, sel.Y, sel.Width - 1, sel.Height - 1);
                    string txt = sel.Width + " × " + sel.Height;
                    SizeF ts = g.MeasureString(txt, font);
                    float x = sel.X, y = sel.Y - ts.Height - 6;
                    if (y < 0) y = sel.Y + 6;
                    DrawLabel(g, txt, font, new PointF(x, y));
                }
                else if (!dragging)
                {
                    Rectangle mon = Screen.FromPoint(Cursor.Position).Bounds;
                    mon.Offset(-virtualScreen.Left, -virtualScreen.Top);
                    string txt = Lang.T("Trascina per selezionare un'area   ·   Clic = schermo intero   ·   Esc = annulla",
                                        "Drag to select an area   ·   Click = full screen   ·   Esc = cancel");
                    SizeF ts = g.MeasureString(txt, font);
                    DrawLabel(g, txt, font, new PointF(mon.X + (mon.Width - ts.Width) / 2, mon.Y + 40 * scale));
                }
            }
        }

        static void DrawLabel(Graphics g, string txt, Font font, PointF at)
        {
            SizeF ts = g.MeasureString(txt, font);
            var r = new RectangleF(at.X, at.Y, ts.Width + 12, ts.Height + 6);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = IconArt.RoundRect(r, 5))
            using (var br = new SolidBrush(Color.FromArgb(215, 25, 25, 25)))
                g.FillPath(br, path);
            g.SmoothingMode = SmoothingMode.None;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.DrawString(txt, font, Brushes.White, at.X + 6, at.Y + 3);
        }

        Rectangle PaintArea(Rectangle r)
        {
            if (r.Width == 0 && r.Height == 0) return Rectangle.Empty;
            int labelH = (int)(40 * DeviceDpi / 96f);
            return Rectangle.FromLTRB(r.Left - 4, r.Top - labelH, Math.Max(r.Right, r.Left + 220) + 4, r.Bottom + labelH);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; return; }
            if (e.Button != MouseButtons.Left) return;
            start = e.Location;
            dragging = true;
            sel = Rectangle.Empty;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            Rectangle old = sel;
            sel = Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X) + 1, Math.Max(start.Y, e.Y) + 1);
            Rectangle a = PaintArea(old), b = PaintArea(sel);
            Invalidate(a.IsEmpty ? b : Rectangle.Union(a, b));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false;
            if (sel.Width < 4 || sel.Height < 4)
            {
                // Clic semplice: tutto il monitor sotto il mouse.
                Rectangle mon = Screen.FromPoint(PointToScreen(e.Location)).Bounds;
                mon.Offset(-virtualScreen.Left, -virtualScreen.Top);
                sel = mon;
            }
            DialogResult = DialogResult.OK;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            if (keyData == Keys.Enter)
            {
                sel = new Rectangle(0, 0, bright.Width, bright.Height);
                DialogResult = DialogResult.OK;
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { bright.Dispose(); dim.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------ annotazioni

    enum Tool { Evidenziatore, Marcatore, Penna, Rettangolo, Freccia, Testo, Oscura, Numero, Lente, Linea, Ovale }

    class Shape
    {
        public Tool Tool;
        public Color Color;
        public float Width;          // testo: carattere in px · oscura: lato dei quadretti · numero: diametro · lente: ingrandimento
        public Point A, B;
        public string Text;          // testo scritto, oppure il numero del cerchietto
        public Bitmap Source;        // immagine originale (serve per pixelare)
        public bool HasFocus;        // lente spostata: continua a ingrandire il punto Focus
        public PointF Focus;
        public float Angle;          // inclinazione del testo in gradi
        public readonly List<Point> Points = new List<Point>();

        Bitmap pixelCache;
        Rectangle pixelCacheRect;

        public const string TextFont = "Segoe UI";

        public bool IsFreehand { get { return Tool == Tool.Marcatore || Tool == Tool.Penna; } }

        public Shape Clone()
        {
            var s = (Shape)MemberwiseClone();
            s.pixelCache = null;
            return s;
        }

        public bool IsValid
        {
            get
            {
                if (Tool == Tool.Testo) return Text != null && Text.Trim().Length > 0;
                if (Tool == Tool.Numero) return true;
                if (IsFreehand) return Points.Count > 0;
                return Math.Abs(A.X - B.X) > 2 || Math.Abs(A.Y - B.Y) > 2;
            }
        }

        static Rectangle Norm(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        // Colore del bordo intorno alle lettere: scuro per i colori chiari, bianco per quelli scuri.
        public static Color HaloFor(Color c)
        {
            double luma = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            return luma > 0.6 ? Color.FromArgb(40, 40, 40) : Color.White;
        }

        GraphicsPath RawTextPath()
        {
            var path = new GraphicsPath();
            using (var ff = new FontFamily(TextFont))
                path.AddString(Text ?? "", ff, (int)FontStyle.Bold, Width, new PointF(A.X, A.Y), StringFormat.GenericDefault);
            return path;
        }

        // Testo con l'inclinazione applicata (ruotato attorno al proprio centro).
        GraphicsPath TextPath()
        {
            var path = RawTextPath();
            if (Angle != 0)
            {
                RectangleF b = path.GetBounds();
                using (var m = new Matrix())
                {
                    m.RotateAt(Angle, new PointF(b.X + b.Width / 2, b.Y + b.Height / 2));
                    path.Transform(m);
                }
            }
            return path;
        }

        // Rettangolo del testo prima dell'inclinazione.
        public RectangleF TextRect()
        {
            using (var p = RawTextPath()) return p.GetBounds();
        }

        public PointF TextCenter()
        {
            RectangleF b = TextRect();
            return new PointF(b.X + b.Width / 2, b.Y + b.Height / 2);
        }

        // Riquadro / ovale: centro e angoli (ruotati) del rettangolo che li contiene.
        public PointF BoxCenter
        {
            get { return new PointF((A.X + B.X) / 2f, (A.Y + B.Y) / 2f); }
        }

        public PointF[] BoxCorners()
        {
            Rectangle r = Norm(A, B);
            PointF c = BoxCenter;
            return new[]
            {
                RotatePoint(new PointF(r.Left, r.Top), c, Angle), RotatePoint(new PointF(r.Right, r.Top), c, Angle),
                RotatePoint(new PointF(r.Right, r.Bottom), c, Angle), RotatePoint(new PointF(r.Left, r.Bottom), c, Angle)
            };
        }

        public static PointF RotatePoint(PointF p, PointF c, float degrees)
        {
            double a = degrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            float dx = p.X - c.X, dy = p.Y - c.Y;
            return new PointF((float)(c.X + dx * cos - dy * sin), (float)(c.Y + dx * sin + dy * cos));
        }

        public bool HitText(PointF p, float tolerance)
        {
            RectangleF b = TextRect();
            b.Inflate(tolerance, tolerance);
            return b.Contains(RotatePoint(p, TextCenter(), -Angle));
        }

        // Il pallino per inclinare: sopra il centro del testo, ruotato insieme al testo.
        public PointF HandlePoint(float distance, bool below)
        {
            RectangleF b = TextRect();
            PointF c = TextCenter();
            return RotatePoint(new PointF(c.X, below ? b.Bottom + distance : b.Top - distance), c, Angle);
        }

        public PointF[] TextCorners(float pad)
        {
            RectangleF b = TextRect();
            b.Inflate(pad, pad);
            PointF c = TextCenter();
            return new[]
            {
                RotatePoint(new PointF(b.Left, b.Top), c, Angle), RotatePoint(new PointF(b.Right, b.Top), c, Angle),
                RotatePoint(new PointF(b.Right, b.Bottom), c, Angle), RotatePoint(new PointF(b.Left, b.Bottom), c, Angle)
            };
        }

        public Rectangle Bounds()
        {
            Rectangle r;
            if (Tool == Tool.Numero)
            {
                int rad = (int)Math.Ceiling(Width / 2) + 6;
                return new Rectangle(A.X - rad, A.Y - rad, 2 * rad + 4, 2 * rad + 4);
            }
            if (Tool == Tool.Testo)
            {
                using (var path = TextPath())
                {
                    RectangleF b = path.GetBounds();
                    r = Rectangle.FromLTRB((int)b.Left, (int)b.Top, (int)Math.Ceiling(b.Right), (int)Math.Ceiling(b.Bottom));
                }
                r.Inflate((int)(Width / 5) + 4, (int)(Width / 5) + 4);
                return r;
            }
            if (IsFreehand)
            {
                if (Points.Count == 0) return Rectangle.Empty;
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
                foreach (var p in Points)
                {
                    x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y);
                    x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y);
                }
                r = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
            }
            else r = Norm(A, B);
            if ((Tool == Tool.Rettangolo || Tool == Tool.Ovale) && Angle != 0)
            {
                // Forma ruotata: ingombro degli angoli ruotati.
                PointF[] c = BoxCorners();
                float x0 = c[0].X, y0 = c[0].Y, x1 = c[0].X, y1 = c[0].Y;
                foreach (var q in c)
                {
                    x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y);
                    x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y);
                }
                r = Rectangle.FromLTRB((int)Math.Floor(x0), (int)Math.Floor(y0), (int)Math.Ceiling(x1), (int)Math.Ceiling(y1));
            }
            int pad = (int)Math.Max(18, Width * 5) + 4;
            if (Tool == Tool.Lente) pad = Math.Max(pad, Math.Min(r.Width, r.Height) / 40 * 3 + 8);   // bordo e ombra
            if (Tool == Tool.Lente && HasFocus)
            {
                RectangleF f = FocusRect();
                r = Rectangle.Union(r, Rectangle.FromLTRB((int)f.Left, (int)f.Top, (int)Math.Ceiling(f.Right), (int)Math.Ceiling(f.Bottom)));
            }
            r.Inflate(pad, pad);
            return r;
        }

        public void Draw(Graphics g)
        {
            switch (Tool)
            {
                case Tool.Evidenziatore:
                    using (var br = new SolidBrush(Color.FromArgb(100, Color)))
                        g.FillRectangle(br, Norm(A, B));
                    break;
                case Tool.Marcatore:
                    DrawStroke(g, Color.FromArgb(110, Color));
                    break;
                case Tool.Penna:
                    DrawStroke(g, Color);
                    break;
                case Tool.Rettangolo:
                case Tool.Ovale:
                {
                    var state = g.Save();
                    if (Angle != 0)
                    {
                        // Ruota attorno al centro della forma.
                        PointF c = BoxCenter;
                        g.TranslateTransform(c.X, c.Y);
                        g.RotateTransform(Angle);
                        g.TranslateTransform(-c.X, -c.Y);
                    }
                    using (var p = new Pen(Color, Width))
                    {
                        if (Tool == Tool.Rettangolo) g.DrawRectangle(p, Norm(A, B));
                        else g.DrawEllipse(p, Norm(A, B));
                    }
                    g.Restore(state);
                    break;
                }
                case Tool.Freccia:
                    DrawArrow(g);
                    break;
                case Tool.Linea:
                    using (var p = new Pen(Color, Width))
                    {
                        p.StartCap = p.EndCap = LineCap.Round;
                        g.DrawLine(p, A, B);
                    }
                    break;
                case Tool.Testo:
                    using (var path = TextPath())
                    using (var halo = new Pen(Color.FromArgb(220, HaloFor(Color)), Math.Max(2f, Width / 6f)))
                    using (var br = new SolidBrush(Color))
                    {
                        halo.LineJoin = LineJoin.Round;
                        g.DrawPath(halo, path);   // bordo per leggerlo su qualsiasi sfondo
                        g.FillPath(br, path);
                    }
                    break;
                case Tool.Oscura:
                    DrawPixelated(g);
                    break;
                case Tool.Numero:
                    DrawNumber(g);
                    break;
                case Tool.Lente:
                    DrawLens(g);
                    break;
            }
        }

        public PointF LensCenter
        {
            get { return new PointF((A.X + B.X) / 2f, (A.Y + B.Y) / 2f); }
        }

        // La zona dell'immagine originale che la lente sta ingrandendo.
        // Lente spostata: raggio del cerchietto sul punto ingrandito, e se si vede (è fuori dalla lente).
        public float FocusRadius
        {
            get { RectangleF f = FocusRect(); return Math.Max(f.Width, f.Height) / 2f; }
        }

        public bool FocusVisible
        {
            get
            {
                if (Tool != Tool.Lente || !HasFocus) return false;
                PointF c = LensCenter;
                double dist = Math.Sqrt((c.X - Focus.X) * (c.X - Focus.X) + (c.Y - Focus.Y) * (c.Y - Focus.Y));
                return dist > Math.Abs(B.X - A.X) / 2f + FocusRadius;
            }
        }

        public RectangleF FocusRect()
        {
            Rectangle r = Norm(A, B);
            float factor = Math.Max(1.1f, Width);
            float sw = r.Width / factor, sh = r.Height / factor;
            PointF c = HasFocus ? Focus : LensCenter;
            return new RectangleF(c.X - sw / 2, c.Y - sh / 2, sw, sh);
        }

        // Cerchio che mostra ingrandito un punto dell'immagine (il suo centro, o il punto d'origine se è stata spostata).
        void DrawLens(Graphics g)
        {
            if (Source == null) return;
            Rectangle r = Norm(A, B);
            if (r.Width < 3 || r.Height < 3) return;
            RectangleF src = FocusRect();
            float sw = src.Width, sh = src.Height;
            float sx = Math.Max(0, Math.Min(Source.Width - sw, src.X));    // resta dentro l'immagine
            float sy = Math.Max(0, Math.Min(Source.Height - sh, src.Y));
            float ring = Math.Max(3f, Math.Min(r.Width, r.Height) / 40f);

            if (HasFocus)
            {
                // Lente spostata: cerchietto sul punto d'origine e linea fino alla lente.
                PointF c = LensCenter, f = Focus;
                double dist = Math.Sqrt((c.X - f.X) * (c.X - f.X) + (c.Y - f.Y) * (c.Y - f.Y));
                float fr = Math.Max(sw, sh) / 2f;
                if (dist > r.Width / 2f + fr)
                {
                    float ux = (float)((c.X - f.X) / dist), uy = (float)((c.Y - f.Y) / dist);
                    var from = new PointF(f.X + ux * fr, f.Y + uy * fr);
                    var to = new PointF(c.X - ux * r.Width / 2f, c.Y - uy * r.Height / 2f);
                    using (var white = new Pen(Color.White, ring * 2.2f))
                    using (var p = new Pen(Color, ring))
                    {
                        white.StartCap = white.EndCap = LineCap.Round;
                        g.DrawEllipse(white, f.X - fr, f.Y - fr, 2 * fr, 2 * fr);
                        g.DrawLine(white, from, to);
                        g.DrawEllipse(p, f.X - fr, f.Y - fr, 2 * fr, 2 * fr);
                        g.DrawLine(p, from, to);
                    }
                }
            }

            using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                g.FillEllipse(shadow, r.X + ring, r.Y + ring * 1.5f, r.Width, r.Height);
            var state = g.Save();
            using (var clip = new GraphicsPath())
            {
                clip.AddEllipse(r);
                g.SetClip(clip, CombineMode.Intersect);
                g.FillEllipse(Brushes.White, r);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(Source, new RectangleF(r.X, r.Y, r.Width, r.Height), new RectangleF(sx, sy, sw, sh), GraphicsUnit.Pixel);
            }
            g.Restore(state);
            using (var white = new Pen(Color.White, ring * 2))
                g.DrawEllipse(white, r);
            using (var p = new Pen(Color, ring))
                g.DrawEllipse(p, r.X - ring / 2, r.Y - ring / 2, r.Width + ring, r.Height + ring);
        }

        // Quadretti grandi = media dei colori dell'area: il contenuto non si legge più.
        void DrawPixelated(Graphics g)
        {
            if (Source == null) return;
            Rectangle r = Norm(A, B);
            r.Intersect(new Rectangle(0, 0, Source.Width, Source.Height));
            if (r.Width < 1 || r.Height < 1) return;
            if (pixelCache == null || pixelCacheRect != r)
            {
                if (pixelCache != null) pixelCache.Dispose();
                pixelCache = Pixelate(Source, r, Math.Max(2, (int)Width));
                pixelCacheRect = r;
            }
            var oldInterp = g.InterpolationMode;
            var oldOffset = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(pixelCache, r, new Rectangle(0, 0, r.Width, r.Height), GraphicsUnit.Pixel);
            g.InterpolationMode = oldInterp;
            g.PixelOffsetMode = oldOffset;
        }

        static Bitmap Pixelate(Bitmap src, Rectangle r, int block)
        {
            int sw = Math.Max(1, (r.Width + block - 1) / block), sh = Math.Max(1, (r.Height + block - 1) / block);
            var result = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppPArgb);
            using (var small = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(small))
                using (var attr = new ImageAttributes())
                {
                    attr.SetWrapMode(WrapMode.TileFlipXY);   // niente bordi scuri ai lati
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(src, new Rectangle(0, 0, sw, sh), r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel, attr);
                }
                using (var g = Graphics.FromImage(result))
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(small, new Rectangle(0, 0, sw * block, sh * block), 0, 0, sw, sh, GraphicsUnit.Pixel);
                }
            }
            return result;
        }

        void DrawNumber(Graphics g)
        {
            float d = Width, ring = Math.Max(2f, d / 12f);
            var rc = new RectangleF(A.X - d / 2, A.Y - d / 2, d, d);
            using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                g.FillEllipse(shadow, rc.X + ring / 2, rc.Y + ring, d, d);
            using (var br = new SolidBrush(Color))
                g.FillEllipse(br, rc);
            using (var p = new Pen(Color.White, ring))
                g.DrawEllipse(p, rc);

            string n = Text ?? "?";
            Color textColor = HaloFor(Color) == Color.White ? Color.White : Color.FromArgb(30, 30, 30);
            float fs = d * (n.Length > 1 ? 0.42f : 0.55f);
            var oldHint = g.TextRenderingHint;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using (var f = new Font(TextFont, fs, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var tb = new SolidBrush(textColor))
                g.DrawString(n, f, tb, new RectangleF(rc.X - d, rc.Y + d * 0.03f, d * 3, d), sf);
            g.TextRenderingHint = oldHint;
        }

        void DrawStroke(Graphics g, Color c)
        {
            if (Points.Count == 1)
            {
                using (var br = new SolidBrush(c))
                    g.FillEllipse(br, Points[0].X - Width / 2, Points[0].Y - Width / 2, Width, Width);
                return;
            }
            using (var p = new Pen(c, Width))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                g.DrawLines(p, Points.ToArray());   // un'unica passata: niente sovrapposizioni più scure
            }
        }

        void DrawArrow(Graphics g)
        {
            double ang = Math.Atan2(B.Y - A.Y, B.X - A.X);
            float len = Math.Max(16f, Width * 4.5f);
            var tip = new PointF(B.X, B.Y);
            var p1 = new PointF((float)(B.X - len * Math.Cos(ang - 0.42)), (float)(B.Y - len * Math.Sin(ang - 0.42)));
            var p2 = new PointF((float)(B.X - len * Math.Cos(ang + 0.42)), (float)(B.Y - len * Math.Sin(ang + 0.42)));
            var baseMid = new PointF((float)(B.X - len * 0.8 * Math.Cos(ang)), (float)(B.Y - len * 0.8 * Math.Sin(ang)));
            using (var p = new Pen(Color, Width))
            {
                p.StartCap = LineCap.Round;
                g.DrawLine(p, new PointF(A.X, A.Y), baseMid);
            }
            using (var br = new SolidBrush(Color))
                g.FillPolygon(br, new[] { tip, p1, p2 });
        }
    }

    class Canvas : Control
    {
        public Canvas()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            Cursor = Cursors.Cross;
        }
    }

    // ------------------------------------------------------------------ copia e salva (editor e immagini appese)

    static class ImageExport
    {
        static string lastFolder;

        public static bool Copy(IWin32Window owner, Bitmap bmp)
        {
            try
            {
                var data = new DataObject();
                data.SetImage(bmp);
                var png = new MemoryStream();
                bmp.Save(png, ImageFormat.Png);
                data.SetData("PNG", false, png);   // per le app che supportano la qualità PNG
                Clipboard.SetDataObject(data, true, 5, 100);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, Lang.T("Impossibile copiare negli appunti:\n", "Could not copy to the clipboard:\n") + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        // Chiede dove salvare e salva; restituisce il percorso, oppure null se annullato o fallito.
        public static string Save(IWin32Window owner, Bitmap bmp)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = Lang.T("Salva cattura", "Save capture");
                dlg.Filter = Lang.T("Immagine PNG (*.png)|*.png|Immagine JPEG (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp",
                                   "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp");
                dlg.FileName = Lang.T("Cattura_", "Capture_") + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png";
                dlg.InitialDirectory = lastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                dlg.AddExtension = true;
                dlg.DefaultExt = "png";
                if (dlg.ShowDialog(owner) != DialogResult.OK) return null;

                string path = dlg.FileName;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp")
                {
                    ext = dlg.FilterIndex == 2 ? ".jpg" : dlg.FilterIndex == 3 ? ".bmp" : ".png";
                    path += ext;
                }
                try
                {
                    if (ext == ".jpg" || ext == ".jpeg")
                    {
                        ImageCodecInfo jpeg = null;
                        foreach (var c in ImageCodecInfo.GetImageEncoders())
                            if (c.FormatID == ImageFormat.Jpeg.Guid) jpeg = c;
                        var ps = new EncoderParameters(1);
                        ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                        bmp.Save(path, jpeg, ps);
                    }
                    else if (ext == ".bmp") bmp.Save(path, ImageFormat.Bmp);
                    else bmp.Save(path, ImageFormat.Png);
                    lastFolder = Path.GetDirectoryName(path);
                    return path;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(owner, Lang.T("Impossibile salvare il file:\n", "Could not save the file:\n") + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
            }
        }
    }

    // ------------------------------------------------------------------ immagine appesa sullo schermo

    class PinForm : Form
    {
        static bool hintShown;

        readonly Bitmap image;
        readonly Color borderColor;
        readonly int border;      // spessore del bordo in pixel
        float scale = 1f;
        bool dragging;
        Point dragStart;
        readonly ToolStripMenuItem[] opacityItems;

        public PinForm(Bitmap img, Point screenLocation, Color borderColor)
        {
            image = img;
            this.borderColor = borderColor;
            using (var g = CreateGraphics()) border = Math.Max(4, (int)Math.Round(5 * g.DpiX / 96f));
            screenLocation.Offset(-border, -border);   // l'immagine resta dov'era, il bordo le sta attorno
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Cursor = Cursors.SizeAll;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            using (var bmp = IconArt.Draw(32)) Icon = Icon.FromHandle(bmp.GetHicon());

            // Le immagini molto grandi partono rimpicciolite.
            Rectangle wa = Screen.FromPoint(screenLocation).WorkingArea;
            scale = Math.Min(1f, Math.Min(wa.Width * 0.8f / img.Width, wa.Height * 0.8f / img.Height));
            ClientSize = ScaledSize();
            Location = new Point(
                Math.Max(wa.Left, Math.Min(wa.Right - Width, screenLocation.X)),
                Math.Max(wa.Top, Math.Min(wa.Bottom - Height, screenLocation.Y)));

            var menu = new ContextMenuStrip();
            var copyItem = new ToolStripMenuItem("", null, delegate { ImageExport.Copy(this, image); });
            var saveItem = new ToolStripMenuItem("", null, delegate { ImageExport.Save(this, image); });
            var realSize = new ToolStripMenuItem("", null, delegate { SetScale(1f, null); });
            var opacity = new ToolStripMenuItem();
            int[] levels = { 100, 80, 60, 40 };
            opacityItems = new ToolStripMenuItem[levels.Length];
            for (int i = 0; i < levels.Length; i++)
            {
                int level = levels[i];
                var item = new ToolStripMenuItem(level + "%");
                item.Click += delegate { SetOpacity(level / 100.0); };
                opacityItems[i] = item;
                opacity.DropDownItems.Add(item);
            }
            var closeItem = new ToolStripMenuItem("", null, delegate { Close(); });
            menu.Items.Add(copyItem);
            menu.Items.Add(saveItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(realSize);
            menu.Items.Add(opacity);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(closeItem);
            menu.Opening += delegate
            {
                for (int i = 0; i < levels.Length; i++)
                    opacityItems[i].Checked = Math.Abs(Opacity * 100 - levels[i]) < 1;
            };
            ContextMenuStrip = menu;

            var tip = new ToolTip();
            relabel = delegate
            {
                Text = Program.AppName + Lang.T(" — immagine appesa", " — pinned image");
                copyItem.Text = Lang.T("Copia", "Copy") + "\tCtrl+C";
                saveItem.Text = Lang.T("Salva…", "Save…") + "\tCtrl+S";
                realSize.Text = Lang.T("Dimensione reale (100%)", "Actual size (100%)") + "\tCtrl+0";
                opacity.Text = Lang.T("Trasparenza", "Transparency");
                opacityItems[0].Text = Lang.T("Nessuna (100%)", "None (100%)");
                closeItem.Text = Lang.T("Chiudi", "Close") + "\t" + Lang.T("Esc o doppio clic", "Esc or double-click");
                tip.SetToolTip(this, Lang.T(
                    "Trascina per spostare · Rotellina = grandezza · Ctrl+rotellina = trasparenza\nDoppio clic o Esc = chiudi · Tasto destro = menu",
                    "Drag to move · Wheel = size · Ctrl+wheel = transparency\nDouble-click or Esc = close · Right-click = menu"));
            };
            relabel();
            Lang.Changed += relabel;
        }

        readonly Action relabel;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;   // CS_DROPSHADOW: ombra attorno alla finestra
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            if (!hintShown && Program.Notify != null)
            {
                hintShown = true;
                Program.Notify(Lang.T("Immagine appesa allo schermo", "Image pinned to the screen"),
                    Lang.T("Trascinala per spostarla, rotellina per la grandezza, Ctrl+rotellina per la trasparenza. Doppio clic o Esc per chiuderla.",
                           "Drag it to move it, wheel to resize, Ctrl+wheel for transparency. Double-click or Esc to close it."));
            }
        }

        Size ScaledSize()
        {
            return new Size(Math.Max(16, (int)Math.Round(image.Width * scale)) + 2 * border,
                            Math.Max(16, (int)Math.Round(image.Height * scale)) + 2 * border);
        }

        // Cambia la grandezza tenendo fermo il punto sotto il mouse.
        void SetScale(float s, Point? anchorClient)
        {
            s = Math.Max(0.1f, Math.Min(4f, s));
            if (Math.Abs(s - 1f) < 0.04f) s = 1f;
            Point anchor = anchorClient ?? Point.Empty;
            float ratio = s / scale;
            scale = s;
            var screenAnchor = PointToScreen(anchor);
            ClientSize = ScaledSize();
            Location = new Point(screenAnchor.X - (int)(anchor.X * ratio), screenAnchor.Y - (int)(anchor.Y * ratio));
            Invalidate();
        }

        void SetOpacity(double o)
        {
            Opacity = Math.Max(0.2, Math.Min(1.0, o));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = scale == 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var br = new SolidBrush(borderColor))
                g.FillRectangle(br, new Rectangle(Point.Empty, ClientSize));
            var inner = new Rectangle(border, border, ClientSize.Width - 2 * border, ClientSize.Height - 2 * border);
            g.DrawImage(image, inner, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { dragging = true; dragStart = e.Location; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left) Close();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Control) != 0)
                SetOpacity(Opacity + (e.Delta > 0 ? 0.1 : -0.1));
            else
                SetScale(e.Delta > 0 ? scale * 1.1f : scale / 1.1f, e.Location);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: Close(); return true;
                case Keys.Control | Keys.C: ImageExport.Copy(this, image); return true;
                case Keys.Control | Keys.S: ImageExport.Save(this, image); return true;
                case Keys.Control | Keys.D0:
                case Keys.Control | Keys.NumPad0: SetScale(1f, null); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Lang.Changed -= relabel;
                image.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------ finestra di modifica

    class EditorForm : Form
    {
        static Tool lastTool = Tool.Evidenziatore;
        static Color lastColor = Color.FromArgb(255, 230, 0);
        static int lastWidthIndex = 1;
        static bool stampOn = true;

        static readonly Color[] Palette =
        {
            Color.FromArgb(255, 230, 0), Color.FromArgb(0, 230, 70), Color.FromArgb(255, 60, 200),
            Color.FromArgb(0, 175, 255), Color.FromArgb(230, 30, 30), Color.FromArgb(20, 20, 20)
        };
        static readonly string[] PaletteNames = { "Giallo", "Verde", "Rosa", "Azzurro", "Rosso", "Nero" };
        static readonly string[] PaletteNamesEn = { "Yellow", "Green", "Pink", "Light blue", "Red", "Black" };

        readonly Bitmap image;
        readonly string stamp;    // scritta in basso a destra: data, ora e programma
        Bitmap view;
        float zoom = 1f;
        readonly List<Shape> shapes = new List<Shape>();
        readonly Stack<EditOp> undoOps = new Stack<EditOp>();
        readonly Stack<EditOp> redoOps = new Stack<EditOp>();
        Shape current;
        Rectangle lastBounds;
        Shape moving, movingOriginal;   // lente o testo che si sta spostando/inclinando
        int movingIndex;
        Point moveStart;
        bool rotating;                  // true = si sta inclinando un testo
        int activeText = -1;            // testo che mostra cornice e pallino
        int activeBox = -1;             // riquadro/ovale che mostra cornice e quadratini
        int boxHandle = -1;             // quadratino trascinato (0-7), -1 = si sposta tutta la forma
        bool focusDrag;                 // si sta spostando il cerchietto (punto ingrandito) di una lente
        TextBox textBox;          // casella aperta mentre si scrive un testo
        Point textAt;             // dove andrà il testo (coordinate dell'immagine)
        float textBoxSize;        // dimensione del carattere della casella aperta
        bool modified;

        Tool tool;
        Color color;
        readonly Panel host;
        readonly Canvas canvas;
        readonly ToolStrip strip;
        readonly Dictionary<Tool, ToolStripButton> toolButtons = new Dictionary<Tool, ToolStripButton>();
        readonly List<ToolStripButton> colorButtons = new List<ToolStripButton>();
        readonly ToolStripComboBox widthBox;
        bool syncingSize;   // si sta aggiornando lo spessore per mostrare quello del testo selezionato
        readonly ToolStripLabel zoomLabel;
        readonly ToolStripStatusLabel status, info;
        readonly System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer copiedTimer = new System.Windows.Forms.Timer();
        ToolStripButton copyButton;
        bool copiedShown;   // il pulsante mostra "✔ Copiato"

        public EditorForm(Bitmap img, string stampText)
        {
            image = img;
            stamp = stampText;
            tool = lastTool;
            color = lastColor;

            Font = SystemFonts.MessageBoxFont;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            using (var bmp = IconArt.Draw(32)) Icon = Icon.FromHandle(bmp.GetHicon());
            Loc(delegate { Text = Program.AppName + " — " + img.Width + " × " + img.Height; });

            float scale;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            int isz = (int)Math.Round(20 * scale);

            // --- barra strumenti
            strip = new ToolStrip();
            strip.GripStyle = ToolStripGripStyle.Hidden;
            strip.ImageScalingSize = new Size(isz, isz);
            strip.Padding = new Padding((int)(4 * scale), (int)(3 * scale), 0, (int)(3 * scale));

            AddToolButton(Tool.Evidenziatore, isz, "Evidenzia", "Highlight",
                "Evidenzia un'area rettangolare (1)", "Highlight a rectangular area (1)");
            AddToolButton(Tool.Marcatore, isz, "Marcatore", "Marker",
                "Evidenziatore a mano libera (2) — tieni premuto Maiusc per una linea dritta",
                "Freehand highlighter (2) — hold Shift for a straight line");
            AddToolButton(Tool.Penna, isz, "Penna", "Pen",
                "Disegno a mano libera (3)", "Freehand drawing (3)");
            AddToolButton(Tool.Rettangolo, isz, "Riquadro", "Box",
                "Rettangolo vuoto (4) — con Maiusc un quadrato", "Empty rectangle (4) — hold Shift for a square");
            AddToolButton(Tool.Ovale, isz, "Ovale", "Oval",
                "Cerchio o ovale (O) — con Maiusc un cerchio perfetto", "Circle or oval (O) — hold Shift for a perfect circle");
            AddToolButton(Tool.Freccia, isz, "Freccia", "Arrow", "Freccia (5)", "Arrow (5)");
            AddToolButton(Tool.Linea, isz, "Linea", "Line",
                "Linea dritta (0) — con Maiusc resta orizzontale, verticale o a 45°",
                "Straight line (0) — hold Shift for horizontal, vertical or 45°");
            AddToolButton(Tool.Testo, isz, "Testo", "Text",
                "Scrivi un testo (6) — clicca dove vuoi scrivere; trascina un testo per spostarlo, il pallino per inclinarlo",
                "Write text (6) — click where you want to write; drag a text to move it, the dot to tilt it");
            AddToolButton(Tool.Numero, isz, "Numeri", "Numbers",
                "Cerchietti numerati 1, 2, 3… con descrizione accanto (7) — clicca dove vuoi il numero",
                "Numbered circles 1, 2, 3… with a description beside them (7) — click where you want the number");
            AddToolButton(Tool.Oscura, isz, "Oscura", "Blur",
                "Nascondi dati sensibili con i quadretti (8) — trascina sopra l'area",
                "Hide sensitive data with pixelation (8) — drag over the area");
            AddToolButton(Tool.Lente, isz, "Lente", "Magnifier",
                "Lente d'ingrandimento (9) — trascina sopra il dettaglio; trascina la lente per spostarla, il suo cerchietto per cambiare il punto ingrandito",
                "Magnifier (9) — drag over the detail; drag the magnifier to move it, its small circle to change the magnified spot");
            strip.Items.Add(new ToolStripSeparator());

            for (int i = 0; i < Palette.Length; i++)
            {
                Color c = Palette[i];
                var b = new ToolStripButton(ColorIcon(c, isz));
                b.DisplayStyle = ToolStripItemDisplayStyle.Image;
                b.Tag = c;
                b.Click += delegate { SetColor(c); };
                string it = PaletteNames[i], en = PaletteNamesEn[i];
                Loc(delegate { b.ToolTipText = Lang.T(it, en); });
                colorButtons.Add(b);
                strip.Items.Add(b);
            }
            strip.Items.Add(new ToolStripSeparator());

            widthBox = new ToolStripComboBox();
            widthBox.DropDownStyle = ComboBoxStyle.DropDownList;
            widthBox.AutoSize = false;
            widthBox.Width = (int)(80 * scale);
            Loc(delegate
            {
                // Rimette le voci nella lingua scelta senza cambiare lo spessore selezionato.
                syncingSize = true;
                int sel = widthBox.SelectedIndex < 0 ? lastWidthIndex : widthBox.SelectedIndex;
                widthBox.Items.Clear();
                widthBox.Items.AddRange(Lang.En ? new object[] { "Thin", "Medium", "Thick" } : new object[] { "Sottile", "Medio", "Spesso" });
                widthBox.SelectedIndex = sel;
                syncingSize = false;
                widthBox.ToolTipText = Lang.T(
                    "Spessore del tratto — per il testo è la dimensione, e cambia anche il testo selezionato (quello con la cornice)",
                    "Stroke thickness — for text it is the size, and it also resizes the selected text (the one with the frame)");
            });
            widthBox.SelectedIndexChanged += delegate
            {
                if (syncingSize) return;
                lastWidthIndex = widthBox.SelectedIndex;
                if (tool == Tool.Testo) ResizeActiveText();
                canvas.Focus();
            };
            strip.Items.Add(widthBox);
            strip.Items.Add(new ToolStripSeparator());

            var stampButton = new ToolStripButton("", ClockIcon(isz));
            stampButton.CheckOnClick = true;
            stampButton.Checked = stampOn && !string.IsNullOrEmpty(stamp);
            stampButton.Enabled = !string.IsNullOrEmpty(stamp);
            Loc(delegate
            {
                stampButton.Text = Lang.T("Data e ora", "Date/time");
                stampButton.ToolTipText = Lang.T("Scritta in basso a destra: ", "Label at the bottom right: ") + stamp +
                    Lang.T("  (clic per mostrarla/nasconderla)", "  (click to show/hide it)");
            });
            stampButton.CheckedChanged += delegate { stampOn = stampButton.Checked; modified = true; canvas.Invalidate(); };
            strip.Items.Add(stampButton);
            strip.Items.Add(new ToolStripSeparator());

            AddAction("Annulla", "Undo", "Annulla l'ultima modifica (Ctrl+Z)", "Undo the last change (Ctrl+Z)", Undo);
            AddAction("Ripeti", "Redo", "Ripeti (Ctrl+Y)", "Redo (Ctrl+Y)", Redo);
            strip.Items.Add(new ToolStripSeparator());
            AddAction("−", "−", "Riduci (Ctrl+meno o Ctrl+rotellina)", "Zoom out (Ctrl+minus or Ctrl+wheel)", delegate { SetZoom(zoom / 1.25f); });
            zoomLabel = new ToolStripLabel("100%");
            Loc(delegate { zoomLabel.ToolTipText = Lang.T("Clic: dimensione reale / adatta alla finestra (Ctrl+0)", "Click: actual size / fit to window (Ctrl+0)"); });
            zoomLabel.Click += delegate { ToggleFit(); };
            strip.Items.Add(zoomLabel);
            AddAction("+", "+", "Ingrandisci (Ctrl+più o Ctrl+rotellina)", "Zoom in (Ctrl+plus or Ctrl+wheel)", delegate { SetZoom(zoom * 1.25f); });

            // A destra (il primo aggiunto sta all'estremità): lingua, Salva, Copia, Appendi, Nuova cattura.
            var langButton = new ToolStripButton("", GlobeIcon(isz));
            langButton.Alignment = ToolStripItemAlignment.Right;
            langButton.Font = new Font(Font, FontStyle.Bold);
            langButton.Click += delegate { Lang.Set(!Lang.En); };
            Loc(delegate
            {
                langButton.Text = Lang.En ? "EN" : "IT";
                langButton.ToolTipText = Lang.T("Lingua: italiano — clic per passare all'inglese", "Language: English — click to switch to Italian");
            });
            var save = new ToolStripButton();
            save.Alignment = ToolStripItemAlignment.Right;
            save.Click += delegate { Save(); };
            Loc(delegate { save.Text = Lang.T("Salva…", "Save…"); save.ToolTipText = Lang.T("Salva come PNG o JPG (Ctrl+S)", "Save as PNG or JPG (Ctrl+S)"); });
            // Copia: pulsante verde in grassetto, ben visibile; dopo il clic mostra per un attimo "✔ Copiato".
            var copy = new ToolStripButton();
            copyButton = copy;
            copy.Alignment = ToolStripItemAlignment.Right;
            copy.Font = new Font(Font, FontStyle.Bold);
            copy.AutoSize = false;
            copy.Margin = new Padding((int)(4 * scale), 1, (int)(4 * scale), 2);
            copy.Click += delegate { Copy(); };
            copy.Paint += (s, e) => PaintCopyButton(e.Graphics, scale);
            Loc(delegate
            {
                copy.Text = copiedShown ? Lang.T("✔ Copiato", "✔ Copied") : Lang.T("Copia", "Copy");
                copy.ToolTipText = Lang.T("Copia negli appunti, poi incolla con Ctrl+V dove vuoi (Ctrl+C)", "Copy to the clipboard, then paste it anywhere with Ctrl+V (Ctrl+C)");
                // Larghezza fissa (quella della scritta più lunga), così gli altri pulsanti non si spostano.
                int copyWidth = 0;
                foreach (string t in new[] { "Copia", "Copy", "✔ Copiato", "✔ Copied" })
                    copyWidth = Math.Max(copyWidth, TextRenderer.MeasureText(t, copy.Font).Width);
                copy.Size = new Size(copyWidth + (int)(22 * scale), (int)(26 * scale));
            });
            copiedTimer.Interval = 1600;
            copiedTimer.Tick += delegate
            {
                copiedTimer.Stop();
                copiedShown = false;
                copy.Text = Lang.T("Copia", "Copy");
                copy.Invalidate();
            };
            var pin = new ToolStripButton();
            pin.Alignment = ToolStripItemAlignment.Right;
            pin.Click += delegate { Pin(); };
            Loc(delegate
            {
                pin.Text = Lang.T("Appendi", "Pin");
                pin.ToolTipText = Lang.T("Appendi l'immagine sullo schermo, sempre in primo piano, mentre lavori in un'altra finestra (Ctrl+P)",
                                         "Pin the image to the screen, always on top, while you work in another window (Ctrl+P)");
            });
            var again = new ToolStripButton();
            again.Alignment = ToolStripItemAlignment.Right;
            again.Click += delegate { NewCapture(); };
            Loc(delegate { again.Text = Lang.T("Nuova cattura", "New capture"); again.ToolTipText = Lang.T("Nuova cattura (Ctrl+N)", "New capture (Ctrl+N)"); });
            strip.Items.Add(langButton);
            strip.Items.Add(save);
            strip.Items.Add(copy);
            strip.Items.Add(pin);
            strip.Items.Add(again);

            // --- area di disegno
            host = new Panel();
            host.Dock = DockStyle.Fill;
            host.AutoScroll = true;
            host.BackColor = Color.FromArgb(64, 64, 68);
            canvas = new Canvas();
            host.Controls.Add(canvas);
            host.Resize += delegate { CenterCanvas(); };

            canvas.Paint += CanvasPaint;
            canvas.MouseDown += CanvasMouseDown;
            canvas.MouseMove += CanvasMouseMove;
            canvas.MouseUp += CanvasMouseUp;
            canvas.MouseWheel += CanvasMouseWheel;
            Lang.Changed += OnLanguageChanged;

            // --- barra di stato
            var bar = new StatusStrip();
            status = new ToolStripStatusLabel();
            status.Spring = true;
            status.TextAlign = ContentAlignment.MiddleLeft;
            info = new ToolStripStatusLabel(img.Width + " × " + img.Height + " px");
            bar.Items.Add(status);
            bar.Items.Add(info);
            statusTimer.Interval = 4000;
            statusTimer.Tick += delegate { statusTimer.Stop(); ShowHint(); };

            Controls.Add(host);
            Controls.Add(strip);
            Controls.Add(bar);

            SelectTool(tool);
            SetColor(color);
            ShowHint();

            // --- dimensione iniziale: l'immagine intera, se ci sta
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int chromeW = 40, chromeH = strip.PreferredSize.Height + bar.PreferredSize.Height + 60;
            int minW = Math.Max(strip.PreferredSize.Width + 30, (int)(560 * scale));
            int maxW = (int)(wa.Width * 0.9), maxH = (int)(wa.Height * 0.9);
            float fit = Math.Min(1f, Math.Min((maxW - chromeW) / (float)img.Width, (maxH - chromeH) / (float)img.Height));
            SetZoom(fit);
            int w = Math.Min(maxW, Math.Max(minW, (int)(img.Width * zoom) + chromeW));
            int h = Math.Min(maxH, Math.Max((int)(300 * scale), (int)(img.Height * zoom) + chromeH));
            Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
            MinimumSize = new Size((int)(400 * scale), (int)(250 * scale));
        }

        // --- lingua: ogni testo dell'interfaccia viene impostato da una funzione, rieseguita quando si cambia lingua

        readonly List<Action> relabels = new List<Action>();

        void Loc(Action setTexts)
        {
            setTexts();
            relabels.Add(setTexts);
        }

        void OnLanguageChanged()
        {
            foreach (var a in relabels) a();
            if (ActiveTextValid) SetActiveText(activeText);
            ShowHint();
        }

        void AddToolButton(Tool t, int isz, string it, string en, string tipIt, string tipEn)
        {
            var b = new ToolStripButton("", ToolIcon(t, isz));
            b.Click += delegate { SelectTool(t); };
            Loc(delegate { b.Text = Lang.T(it, en); b.ToolTipText = Lang.T(tipIt, tipEn); });
            toolButtons[t] = b;
            strip.Items.Add(b);
        }

        void AddAction(string it, string en, string tipIt, string tipEn, Action action)
        {
            var b = new ToolStripButton();
            b.Click += delegate { action(); };
            Loc(delegate { b.Text = Lang.T(it, en); b.ToolTipText = Lang.T(tipIt, tipEn); });
            strip.Items.Add(b);
        }

        static Bitmap GlobeIcon(int s)
        {
            var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            using (var p = new Pen(Color.FromArgb(40, 110, 200), Math.Max(1.2f, s * 0.08f)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float m = s * 0.12f, d = s - 2 * m;
                g.DrawEllipse(p, m, m, d, d);
                g.DrawEllipse(p, s * 0.34f, m, s * 0.32f, d);
                g.DrawLine(p, m, s / 2f, s - m, s / 2f);
                g.DrawLine(p, s / 2f, m, s / 2f, s - m);
            }
            return b;
        }

        void ShowHint()
        {
            status.Text = Lang.T(
                "Disegna sull'immagine · Ctrl+Z annulla, Ctrl+Y ripeti · Ctrl+C copia per incollarla altrove · Ctrl+S salva · Ctrl+P appendi",
                "Draw on the image · Ctrl+Z undo, Ctrl+Y redo · Ctrl+C copy to paste it elsewhere · Ctrl+S save · Ctrl+P pin");
        }

        void Flash(string msg)
        {
            status.Text = msg;
            statusTimer.Stop();
            statusTimer.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Porta la finestra in primo piano anche se un'altra app aveva il focus.
            TopMost = true;
            Activate();
            TopMost = false;
            canvas.Focus();
        }

        // --- strumenti e colori

        void SelectTool(Tool t)
        {
            CommitText();
            tool = t;
            lastTool = t;
            foreach (var kv in toolButtons) kv.Value.Checked = kv.Key == t;
            if (status == null) return;
            activeText = -1;
            activeBox = -1;
            if (canvas != null) canvas.Invalidate();
            if (t == Tool.Testo)
                Flash(Lang.T("Clicca dove vuoi scrivere (Invio conferma) · trascina un testo per spostarlo, il pallino per inclinarlo · Sottile/Medio/Spesso cambia la dimensione del testo selezionato",
                    "Click where you want to write (Enter confirms) · drag a text to move it, the dot to tilt it · Thin/Medium/Thick resizes the selected text"));
            else if (t == Tool.Numero)
                Flash(Lang.T("Clicca dove vuoi il numero ", "Click where you want number ") + NextNumber() + Lang.T(", poi scrivi la descrizione accanto · Invio conferma · Esc = solo il numero · clic altrove = numero successivo",
                    ", then write the description beside it · Enter confirms · Esc = number only · click elsewhere = next number"));
            else if (t == Tool.Rettangolo || t == Tool.Ovale)
                Flash(Lang.T("Trascina per disegnare (Maiusc = " + (t == Tool.Ovale ? "cerchio" : "quadrato") + ") · trascina il bordo di una forma per spostarla, i quadratini per ridimensionarla, il pallino sopra per ruotarla",
                             "Drag to draw (Shift = " + (t == Tool.Ovale ? "circle" : "square") + ") · drag a shape's edge to move it, the small squares to resize it, the dot above to rotate it"));
            else if (t == Tool.Linea)
                Flash(Lang.T("Trascina per tracciare una linea dritta · tieni premuto Maiusc per orizzontale, verticale o 45°",
                    "Drag to draw a straight line · hold Shift for horizontal, vertical or 45°"));
            else if (t == Tool.Oscura)
                Flash(Lang.T("Trascina sopra nomi, email, numeri… da nascondere · lo spessore cambia la grandezza dei quadretti",
                    "Drag over names, emails, numbers… to hide · the thickness sets the size of the squares"));
            else if (t == Tool.Lente)
                Flash(Lang.T("Trascina sopra il dettaglio · trascina una lente per spostarla, il suo cerchietto per cambiare il punto ingrandito · Sottile ×1,5 · Medio ×2 · Spesso ×3",
                    "Drag over the detail · drag a magnifier to move it, its small circle to change the magnified spot · Thin ×1.5 · Medium ×2 · Thick ×3"));
        }

        void SetColor(Color c)
        {
            color = c;
            lastColor = c;
            foreach (var b in colorButtons) b.Checked = ((Color)b.Tag).ToArgb() == c.ToArgb();
            if (textBox != null)
            {
                StyleTextBox();
                textBox.Focus();
            }
        }

        // Dimensioni del testo per Sottile / Medio / Spesso (pixel dell'immagine).
        static readonly float[] TextSizes = { 18f, 28f, 44f };

        // Anche la descrizione accanto ai numeri segue lo spessore.
        float NumberTextSize()
        {
            return StrokeWidth(Tool.Testo);
        }

        void SetActiveText(int index)
        {
            activeText = index;
            if (!ActiveTextValid) return;
            // Lo spessore mostra la dimensione del testo selezionato.
            float w = shapes[index].Width;
            int best = 0;
            for (int i = 1; i < TextSizes.Length; i++)
                if (Math.Abs(TextSizes[i] - w) < Math.Abs(TextSizes[best] - w)) best = i;
            syncingSize = true;
            widthBox.SelectedIndex = best;
            syncingSize = false;
        }

        // Cambia la dimensione del testo selezionato tenendolo centrato dov'era.
        void ResizeActiveText()
        {
            if (!ActiveTextValid) return;
            var old = shapes[activeText];
            float nw = StrokeWidth(Tool.Testo);
            if (Math.Abs(old.Width - nw) < 0.01f) return;
            var ns = old.Clone();
            ns.Width = nw;
            PointF c0 = old.TextCenter(), c1 = ns.TextCenter();
            ns.A = ns.B = new Point(old.A.X + (int)Math.Round(c0.X - c1.X), old.A.Y + (int)Math.Round(c0.Y - c1.Y));
            Record(new EditOp { Index = activeText, Before = old, After = ns });
            canvas.Invalidate();
        }

        float StrokeWidth(Tool t)
        {
            int i = widthBox.SelectedIndex < 0 ? 1 : widthBox.SelectedIndex;
            if (t == Tool.Testo) return TextSizes[i];
            if (t == Tool.Numero) return new[] { 24f, 32f, 46f }[i];
            if (t == Tool.Oscura) return new[] { 8f, 12f, 20f }[i];
            if (t == Tool.Lente) return new[] { 1.5f, 2f, 3f }[i];
            if (t == Tool.Marcatore) return new[] { 10f, 18f, 28f }[i];
            return new[] { 2f, 4f, 7f }[i];
        }

        static Bitmap ToolIcon(Tool t, int s)
        {
            var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float m = s * 0.12f;
                switch (t)
                {
                    case Tool.Evidenziatore:
                        using (var p = new Pen(Color.FromArgb(120, 120, 120), Math.Max(1f, s * 0.07f)))
                            for (int i = 0; i < 3; i++)
                            {
                                float y = s * (0.28f + i * 0.22f);
                                g.DrawLine(p, m, y, s - m, y);
                            }
                        using (var br = new SolidBrush(Color.FromArgb(170, 255, 225, 0)))
                            g.FillRectangle(br, m * 0.5f, s * 0.36f, s - m, s * 0.28f);
                        break;
                    case Tool.Marcatore:
                        using (var p = new Pen(Color.FromArgb(200, 255, 215, 0), s * 0.3f))
                        {
                            p.StartCap = p.EndCap = LineCap.Round;
                            g.DrawLine(p, m * 1.6f, s - m * 2.2f, s - m * 1.6f, m * 2.2f);
                        }
                        break;
                    case Tool.Penna:
                        using (var p = new Pen(Color.FromArgb(50, 50, 50), Math.Max(1.5f, s * 0.09f)))
                        {
                            p.StartCap = p.EndCap = LineCap.Round;
                            g.DrawBezier(p, m, s * 0.7f, s * 0.35f, -s * 0.1f, s * 0.55f, s * 1.1f, s - m, s * 0.3f);
                        }
                        break;
                    case Tool.Rettangolo:
                        using (var p = new Pen(Color.FromArgb(220, 40, 40), Math.Max(1.5f, s * 0.1f)))
                            g.DrawRectangle(p, m, s * 0.22f, s - 2 * m, s * 0.56f);
                        break;
                    case Tool.Ovale:
                        using (var p = new Pen(Color.FromArgb(220, 40, 40), Math.Max(1.5f, s * 0.1f)))
                            g.DrawEllipse(p, m * 0.7f, s * 0.2f, s - 1.4f * m, s * 0.6f);
                        break;
                    case Tool.Freccia:
                        var sh = new Shape { Tool = Tool.Freccia, Color = Color.FromArgb(220, 40, 40), Width = Math.Max(1.5f, s * 0.1f) };
                        sh.A = new Point((int)m, (int)(s - m));
                        sh.B = new Point((int)(s - m), (int)m);
                        using (var p = new Pen(sh.Color, sh.Width)) g.DrawLine(p, sh.A.X, sh.A.Y, s * 0.62f, s * 0.38f);
                        using (var br = new SolidBrush(sh.Color))
                            g.FillPolygon(br, new[] { new PointF(s - m, m), new PointF(s * 0.42f, s * 0.3f), new PointF(s * 0.7f, s * 0.58f) });
                        break;
                    case Tool.Linea:
                        using (var p = new Pen(Color.FromArgb(50, 50, 50), Math.Max(1.5f, s * 0.1f)))
                        {
                            p.StartCap = p.EndCap = LineCap.Round;
                            g.DrawLine(p, m, s - m, s - m, m);
                        }
                        break;
                    case Tool.Testo:
                        using (var f = new Font("Georgia", s * 0.8f, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        using (var br = new SolidBrush(Color.FromArgb(40, 40, 40)))
                        {
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                            g.DrawString("A", f, br, new RectangleF(0, s * 0.04f, s, s), sf);
                        }
                        break;
                    case Tool.Numero:
                        using (var br = new SolidBrush(Color.FromArgb(220, 40, 40)))
                            g.FillEllipse(br, m * 0.6f, m * 0.6f, s - 1.2f * m, s - 1.2f * m);
                        using (var f = new Font("Segoe UI", s * 0.55f, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        {
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                            g.DrawString("1", f, Brushes.White, new RectangleF(0, s * 0.03f, s, s), sf);
                        }
                        break;
                    case Tool.Lente:
                        using (var p = new Pen(Color.FromArgb(50, 50, 50), Math.Max(1.5f, s * 0.1f)))
                        using (var glass = new SolidBrush(Color.FromArgb(120, 0, 175, 255)))
                        {
                            float d = s * 0.58f;
                            g.FillEllipse(glass, m, m, d, d);
                            g.DrawEllipse(p, m, m, d, d);
                            p.Width = Math.Max(2f, s * 0.16f);
                            p.StartCap = p.EndCap = LineCap.Round;
                            g.DrawLine(p, m + d * 0.88f, m + d * 0.88f, s - m, s - m);
                        }
                        break;
                    case Tool.Oscura:
                        int cells = 4;
                        float cs = (s - 2 * m * 0.5f) / cells;
                        for (int y = 0; y < cells; y++)
                            for (int x = 0; x < cells; x++)
                            {
                                int v = 90 + ((x * 7 + y * 13) % 5) * 30;
                                using (var br = new SolidBrush(Color.FromArgb(v, v, v + 10)))
                                    g.FillRectangle(br, m * 0.5f + x * cs, m * 0.5f + y * cs, cs + 0.5f, cs + 0.5f);
                            }
                        break;
                }
            }
            return b;
        }

        static Bitmap ClockIcon(int s)
        {
            var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            using (var p = new Pen(Color.FromArgb(50, 50, 50), Math.Max(1.2f, s * 0.08f)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float m = s * 0.12f, c = s / 2f;
                g.DrawEllipse(p, m, m, s - 2 * m, s - 2 * m);
                p.StartCap = p.EndCap = LineCap.Round;
                g.DrawLine(p, c, c, c, s * 0.28f);
                g.DrawLine(p, c, c, s * 0.68f, s * 0.6f);
            }
            return b;
        }

        // Etichetta scura in basso a destra con data, ora e programma (coordinate dell'immagine).
        void DrawStamp(Graphics g)
        {
            if (!stampOn || string.IsNullOrEmpty(stamp)) return;
            float fs = Math.Max(11f, Math.Min(18f, image.Height / 40f));
            using (var font = new Font("Segoe UI", fs, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var oldHint = g.TextRenderingHint;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                SizeF ts = g.MeasureString(stamp, font);
                float padX = fs * 0.55f, padY = fs * 0.25f, margin = fs * 0.5f;
                float w = ts.Width + 2 * padX, h = ts.Height + 2 * padY;
                var box = new RectangleF(Math.Max(0, image.Width - w - margin), Math.Max(0, image.Height - h - margin), w, h);
                using (var path = IconArt.RoundRect(box, Math.Min(h / 2, fs * 0.4f)))
                using (var br = new SolidBrush(Color.FromArgb(175, 20, 20, 20)))
                    g.FillPath(br, path);
                g.DrawString(stamp, font, Brushes.White, box.X + padX, box.Y + padY);
                g.TextRenderingHint = oldHint;
            }
        }

        static Bitmap ColorIcon(Color c, int s)
        {
            var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new RectangleF(s * 0.12f, s * 0.12f, s * 0.76f, s * 0.76f);
                using (var path = IconArt.RoundRect(r, s * 0.15f))
                using (var br = new SolidBrush(c))
                using (var p = new Pen(Color.FromArgb(90, 0, 0, 0), 1))
                {
                    g.FillPath(br, path);
                    g.DrawPath(p, path);
                }
            }
            return b;
        }

        // --- zoom

        void SetZoom(float z)
        {
            CommitText();
            z = Math.Max(0.1f, Math.Min(4f, z));
            // Evita immagini ingrandite enormi in memoria.
            double maxPixels = 40e6;
            if ((double)image.Width * image.Height * z * z > maxPixels)
                z = (float)Math.Sqrt(maxPixels / ((double)image.Width * image.Height));
            if (Math.Abs(z - 1f) < 0.02f) z = 1f;
            zoom = z;

            if (view != null) view.Dispose();
            int w = Math.Max(1, (int)Math.Round(image.Width * zoom)), h = Math.Max(1, (int)Math.Round(image.Height * zoom));
            view = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(view))
            {
                g.InterpolationMode = zoom == 1f ? InterpolationMode.NearestNeighbor
                    : zoom < 1f ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(image, new Rectangle(0, 0, w, h), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
            }
            canvas.Size = new Size(w, h);
            zoomLabel.Text = (int)Math.Round(zoom * 100) + "%";
            CenterCanvas();
            canvas.Invalidate();
        }

        void ToggleFit()
        {
            if (zoom != 1f) { SetZoom(1f); return; }
            Size avail = host.ClientSize;
            float fit = Math.Min((avail.Width - 20) / (float)image.Width, (avail.Height - 20) / (float)image.Height);
            SetZoom(Math.Min(1f, fit));
        }

        void CenterCanvas()
        {
            Size avail = host.ClientSize;
            int x = Math.Max(0, (avail.Width - canvas.Width) / 2);
            int y = Math.Max(0, (avail.Height - canvas.Height) / 2);
            canvas.Location = new Point(x + host.AutoScrollPosition.X, y + host.AutoScrollPosition.Y);
        }

        void CanvasMouseWheel(object sender, MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == 0) return;
            SetZoom(e.Delta > 0 ? zoom * 1.25f : zoom / 1.25f);
            var he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
        }

        // --- disegno

        Point ToImage(Point p)
        {
            int x = (int)Math.Round(p.X / zoom), y = (int)Math.Round(p.Y / zoom);
            return new Point(Math.Max(0, Math.Min(image.Width, x)), Math.Max(0, Math.Min(image.Height, y)));
        }

        Rectangle ToView(Rectangle r)
        {
            return Rectangle.FromLTRB((int)Math.Floor(r.Left * zoom) - 2, (int)Math.Floor(r.Top * zoom) - 2,
                (int)Math.Ceiling(r.Right * zoom) + 2, (int)Math.Ceiling(r.Bottom * zoom) + 2);
        }

        void CanvasPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Rectangle clip = Rectangle.Intersect(e.ClipRectangle, new Rectangle(0, 0, view.Width, view.Height));
            g.DrawImage(view, clip, clip, GraphicsUnit.Pixel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(zoom, zoom);
            DrawStamp(g);
            foreach (var s in shapes) s.Draw(g);
            if (current != null) current.Draw(g);
            DrawTextHandle(g);
            DrawBoxHandles(g);
        }

        // --- testi già messi: spostare e inclinare

        float HandleDistance { get { return 24f / zoom; } }
        float HandleRadius { get { return 7f / zoom; } }

        bool ActiveTextValid
        {
            get { return tool == Tool.Testo && textBox == null && activeText >= 0 && activeText < shapes.Count && shapes[activeText].Tool == Tool.Testo; }
        }

        // Cornice tratteggiata e pallino per inclinare (solo a video, non finiscono nell'immagine).
        void DrawTextHandle(Graphics g)
        {
            if (!ActiveTextValid) return;
            var s = shapes[activeText];
            PointF[] c = s.TextCorners(5f / zoom);
            bool below;
            PointF h = HandlePos(s, out below);
            PointF topMid = below ? new PointF((c[2].X + c[3].X) / 2, (c[2].Y + c[3].Y) / 2)
                                  : new PointF((c[0].X + c[1].X) / 2, (c[0].Y + c[1].Y) / 2);
            using (var p = new Pen(Color.FromArgb(0, 120, 215), 1.2f / zoom))
            {
                p.DashStyle = DashStyle.Dash;
                g.DrawPolygon(p, c);
                p.DashStyle = DashStyle.Solid;
                g.DrawLine(p, topMid, h);
                float r = HandleRadius;
                g.FillEllipse(Brushes.White, h.X - r, h.Y - r, 2 * r, 2 * r);
                p.Width = 2f / zoom;
                g.DrawEllipse(p, h.X - r, h.Y - r, 2 * r, 2 * r);
            }
        }

        // Il pallino sta sopra il testo; se lì uscirebbe dall'immagine, sta sotto.
        PointF HandlePos(Shape s, out bool below)
        {
            PointF h = s.HandlePoint(HandleDistance, false);
            below = h.X < 0 || h.Y < 0 || h.X > image.Width || h.Y > image.Height;
            return below ? s.HandlePoint(HandleDistance, true) : h;
        }

        bool OnTextHandle(Point p)
        {
            if (!ActiveTextValid) return false;
            bool below;
            PointF h = HandlePos(shapes[activeText], out below);
            float dx = p.X - h.X, dy = p.Y - h.Y;
            float r = HandleRadius * 1.8f;
            return dx * dx + dy * dy <= r * r;
        }

        int HitText(Point p)
        {
            for (int i = shapes.Count - 1; i >= 0; i--)
                if (shapes[i].Tool == Tool.Testo && shapes[i].HitText(p, 4f / zoom)) return i;
            return -1;
        }

        // Inizia a trascinare una forma già messa (lente o testo); a fine trascinamento diventa un passo annullabile.
        void BeginDrag(int index, Point p, bool rotate)
        {
            movingIndex = index;
            movingOriginal = shapes[index];
            moving = movingOriginal.Clone();
            shapes[index] = moving;
            moveStart = p;
            rotating = rotate;
            lastBounds = moving.Bounds();
            canvas.Invalidate();
        }

        // --- riquadri e ovali già messi: spostare (trascinando il bordo) e ridimensionare (quadratini)

        static bool IsBoxTool(Tool t) { return t == Tool.Rettangolo || t == Tool.Ovale; }

        bool ActiveBoxValid
        {
            get { return IsBoxTool(tool) && activeBox >= 0 && activeBox < shapes.Count && shapes[activeBox].Tool == tool; }
        }

        static Rectangle BoxRect(Shape s)
        {
            return Rectangle.FromLTRB(Math.Min(s.A.X, s.B.X), Math.Min(s.A.Y, s.B.Y), Math.Max(s.A.X, s.B.X), Math.Max(s.A.Y, s.B.Y));
        }

        // Quadratini: 0 alto-sx, 1 alto, 2 alto-dx, 3 destra, 4 basso-dx, 5 basso, 6 basso-sx, 7 sinistra.
        static PointF[] BoxHandles(Rectangle r)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            return new[]
            {
                new PointF(r.Left, r.Top), new PointF(cx, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, cy),
                new PointF(r.Right, r.Bottom), new PointF(cx, r.Bottom), new PointF(r.Left, r.Bottom), new PointF(r.Left, cy)
            };
        }

        // Quadratini nella posizione reale (ruotati insieme alla forma).
        static PointF[] BoxHandlesOf(Shape s)
        {
            PointF[] hs = BoxHandles(BoxRect(s));
            if (s.Angle != 0)
                for (int i = 0; i < hs.Length; i++) hs[i] = Shape.RotatePoint(hs[i], s.BoxCenter, s.Angle);
            return hs;
        }

        // Pallino per ruotare: sopra la forma (sotto, se sopra uscirebbe dall'immagine), ruotato con lei.
        PointF BoxRotateHandle(Shape s, out PointF anchor)
        {
            Rectangle r = BoxRect(s);
            PointF c = s.BoxCenter;
            bool below = r.Top - HandleDistance < 0;
            var local = new PointF(c.X, below ? r.Bottom + HandleDistance : r.Top - HandleDistance);
            anchor = Shape.RotatePoint(new PointF(c.X, below ? r.Bottom : r.Top), c, s.Angle);
            return Shape.RotatePoint(local, c, s.Angle);
        }

        bool OnBoxRotateHandle(Point p)
        {
            if (!ActiveBoxValid) return false;
            PointF anchor;
            PointF h = BoxRotateHandle(shapes[activeBox], out anchor);
            float dx = p.X - h.X, dy = p.Y - h.Y, r = HandleRadius * 1.8f;
            return dx * dx + dy * dy <= r * r;
        }

        int BoxHandleAt(Point p)
        {
            if (!ActiveBoxValid) return -1;
            PointF[] hs = BoxHandlesOf(shapes[activeBox]);
            float r = HandleRadius * 1.6f;
            for (int i = 0; i < hs.Length; i++)
            {
                float dx = p.X - hs[i].X, dy = p.Y - hs[i].Y;
                if (dx * dx + dy * dy <= r * r) return i;
            }
            return -1;
        }

        // Forma dello strumento attivo il cui bordo è sotto il mouse (dentro una forma vuota si disegna ancora).
        int HitBox(Point p)
        {
            for (int i = shapes.Count - 1; i >= 0; i--)
            {
                var s = shapes[i];
                if (s.Tool != tool) continue;
                Rectangle r = BoxRect(s);
                PointF q = s.Angle != 0 ? Shape.RotatePoint(p, s.BoxCenter, -s.Angle) : new PointF(p.X, p.Y);
                float tol = Math.Max(6f / zoom, s.Width / 2 + 3f / zoom);
                if (s.Tool == Tool.Rettangolo)
                {
                    RectangleF outer = r, inner = r;
                    outer.Inflate(tol, tol);
                    inner.Inflate(-tol, -tol);
                    if (outer.Contains(q) && !(inner.Width > 0 && inner.Height > 0 && inner.Contains(q))) return i;
                }
                else
                {
                    float rx = r.Width / 2f, ry = r.Height / 2f;
                    if (rx < 1 || ry < 1) continue;
                    float nx = (q.X - (r.X + rx)) / rx, ny = (q.Y - (r.Y + ry)) / ry;
                    double d = Math.Sqrt(nx * nx + ny * ny);
                    if (Math.Abs(d - 1) * Math.Min(rx, ry) <= tol) return i;
                }
            }
            return -1;
        }

        static Cursor HandleCursor(int h, float angle)
        {
            // Direzione del quadratino tenendo conto della rotazione (a passi di 45°).
            int step = ((h + (int)Math.Round(angle / 45.0)) % 8 + 8) % 8;
            switch (step)
            {
                case 0: case 4: return Cursors.SizeNWSE;
                case 2: case 6: return Cursors.SizeNESW;
                case 1: case 5: return Cursors.SizeNS;
                default: return Cursors.SizeWE;
            }
        }

        // Nuova forma trascinando il quadratino: si lavora "dritti" (senza rotazione) e il lato opposto resta fermo.
        void ResizeBox(Point p)
        {
            Rectangle r0 = BoxRect(movingOriginal);
            PointF c0 = movingOriginal.BoxCenter;
            float ang = movingOriginal.Angle;
            PointF lp = Shape.RotatePoint(p, c0, -ang), ls = Shape.RotatePoint(moveStart, c0, -ang);
            int dx = (int)Math.Round(lp.X - ls.X), dy = (int)Math.Round(lp.Y - ls.Y);
            int l = r0.Left, t = r0.Top, r = r0.Right, b = r0.Bottom;
            int h = boxHandle;
            if (h == 0 || h == 6 || h == 7) l += dx;
            if (h == 2 || h == 3 || h == 4) r += dx;
            if (h == 0 || h == 1 || h == 2) t += dy;
            if (h == 4 || h == 5 || h == 6) b += dy;
            if ((ModifierKeys & Keys.Shift) != 0 && h % 2 == 0)
            {
                // Maiusc sugli angoli: quadrato / cerchio.
                int side = Math.Max(Math.Abs(r - l), Math.Abs(b - t));
                if (h == 0 || h == 6) l = r - Math.Sign(r - l == 0 ? 1 : r - l) * side; else r = l + Math.Sign(r - l == 0 ? 1 : r - l) * side;
                if (h == 0 || h == 2) t = b - Math.Sign(b - t == 0 ? 1 : b - t) * side; else b = t + Math.Sign(b - t == 0 ? 1 : b - t) * side;
            }
            Rectangle nl = Rectangle.FromLTRB(Math.Min(l, r), Math.Min(t, b), Math.Max(l, r), Math.Max(t, b));
            if (ang != 0)
            {
                // Il centro nuovo, riportato nella posizione ruotata.
                var cl = new PointF(nl.X + nl.Width / 2f, nl.Y + nl.Height / 2f);
                PointF cw = Shape.RotatePoint(cl, c0, ang);
                nl.Offset((int)Math.Round(cw.X - cl.X), (int)Math.Round(cw.Y - cl.Y));
            }
            moving.A = new Point(nl.Left, nl.Top);
            moving.B = new Point(nl.Right, nl.Bottom);
        }

        void RotateBox(Point p)
        {
            PointF c = movingOriginal.BoxCenter;
            double a0 = Math.Atan2(moveStart.Y - c.Y, moveStart.X - c.X);
            double a1 = Math.Atan2(p.Y - c.Y, p.X - c.X);
            double angle = movingOriginal.Angle + (a1 - a0) * 180 / Math.PI;
            if ((ModifierKeys & Keys.Shift) != 0) angle = Math.Round(angle / 15) * 15;   // scatti di 15°
            angle = ((angle % 360) + 540) % 360 - 180;                                    // tra -180 e 180
            if (Math.Abs(angle) < 2) angle = 0;                                           // torna dritto facilmente
            moving.Angle = (float)angle;
            Flash(Lang.T("Rotazione: ", "Rotation: ") + (int)Math.Round(angle) + Lang.T("°   (Maiusc = scatti di 15°)", "°   (Shift = 15° steps)"));
        }

        void DrawBoxHandles(Graphics g)
        {
            if (!ActiveBoxValid) return;
            var s = shapes[activeBox];
            float hr = HandleRadius * 0.8f;
            using (var p = new Pen(Color.FromArgb(0, 120, 215), 1.2f / zoom))
            {
                p.DashStyle = DashStyle.Dash;
                g.DrawPolygon(p, s.BoxCorners());
                p.DashStyle = DashStyle.Solid;

                PointF anchor;
                PointF rh = BoxRotateHandle(s, out anchor);
                g.DrawLine(p, anchor, rh);
                float rr = HandleRadius;
                g.FillEllipse(Brushes.White, rh.X - rr, rh.Y - rr, 2 * rr, 2 * rr);

                p.Width = 1.5f / zoom;
                g.DrawEllipse(p, rh.X - rr, rh.Y - rr, 2 * rr, 2 * rr);
                foreach (var h in BoxHandlesOf(s))
                {
                    g.FillRectangle(Brushes.White, h.X - hr, h.Y - hr, 2 * hr, 2 * hr);
                    g.DrawRectangle(p, h.X - hr, h.Y - hr, 2 * hr, 2 * hr);
                }
            }
        }

        void CanvasMouseDown(object sender, MouseEventArgs e)
        {
            if (textBox != null)
            {
                // Clic fuori dalla casella: conferma il testo che si stava scrivendo.
                // Con i numeri si prosegue subito con il numero successivo.
                CommitText();
                canvas.Focus();
                if (tool != Tool.Numero) return;
            }
            canvas.Focus();
            if (e.Button != MouseButtons.Left) return;
            Point p = ToImage(e.Location);
            if (tool == Tool.Testo)
            {
                // Pallino = inclina, clic sul testo = sposta, altrove = nuovo testo.
                if (OnTextHandle(p)) { BeginDrag(activeText, p, true); return; }
                int hitText = HitText(p);
                if (hitText >= 0) { SetActiveText(hitText); BeginDrag(hitText, p, false); return; }
                activeText = -1;
                canvas.Invalidate();
                StartText(p, StrokeWidth(Tool.Testo));
                return;
            }
            if (tool == Tool.Lente)
            {
                // Cerchietto di una lente spostata: si sposta il punto ingrandito, la lente resta ferma.
                int hitFocus = HitLensFocus(p);
                if (hitFocus >= 0) { focusDrag = true; BeginDrag(hitFocus, p, false); return; }
                int hit = HitLens(p);
                if (hit >= 0)
                {
                    // Clic dentro una lente già messa: si sposta.
                    BeginDrag(hit, p, false);
                    return;
                }
            }
            if (IsBoxTool(tool))
            {
                // Pallino = ruota, quadratino = ridimensiona, bordo = sposta, altrove = nuova forma.
                if (OnBoxRotateHandle(p)) { boxHandle = -1; BeginDrag(activeBox, p, true); return; }
                int h = BoxHandleAt(p);
                if (h >= 0) { boxHandle = h; BeginDrag(activeBox, p, false); return; }
                int hitBox = HitBox(p);
                if (hitBox >= 0) { activeBox = hitBox; boxHandle = -1; BeginDrag(hitBox, p, false); return; }
                activeBox = -1;
                canvas.Invalidate();
            }
            current = new Shape { Tool = tool, Color = color, Width = StrokeWidth(tool), A = p, B = p, Source = image };
            if (tool == Tool.Numero) current.Text = NextNumber().ToString();
            current.Points.Add(p);
            lastBounds = current.Bounds();
            canvas.Invalidate(ToView(lastBounds));
        }

        void CanvasMouseMove(object sender, MouseEventArgs e)
        {
            Point p = ToImage(e.Location);
            if (moving != null && moving.Tool == Tool.Testo)
            {
                if (rotating)
                {
                    PointF c = movingOriginal.TextCenter();
                    double a0 = Math.Atan2(moveStart.Y - c.Y, moveStart.X - c.X);
                    double a1 = Math.Atan2(p.Y - c.Y, p.X - c.X);
                    double angle = movingOriginal.Angle + (a1 - a0) * 180 / Math.PI;
                    if ((ModifierKeys & Keys.Shift) != 0) angle = Math.Round(angle / 15) * 15;   // scatti di 15°
                    angle = ((angle % 360) + 540) % 360 - 180;                                    // tra -180 e 180
                    if (Math.Abs(angle) < 2) angle = 0;                                           // torna dritto facilmente
                    moving.Angle = (float)angle;
                    Flash(Lang.T("Inclinazione: ", "Tilt: ") + (int)Math.Round(angle) + Lang.T("°   (Maiusc = scatti di 15°)", "°   (Shift = 15° steps)"));
                }
                else
                {
                    int tx = p.X - moveStart.X, ty = p.Y - moveStart.Y;
                    moving.A = moving.B = new Point(movingOriginal.A.X + tx, movingOriginal.A.Y + ty);
                }
                canvas.Invalidate();
                return;
            }
            if (moving != null && IsBoxTool(moving.Tool))
            {
                if (rotating) RotateBox(p);
                else if (boxHandle >= 0) ResizeBox(p);
                else
                {
                    int bx = p.X - moveStart.X, by = p.Y - moveStart.Y;
                    moving.A = new Point(movingOriginal.A.X + bx, movingOriginal.A.Y + by);
                    moving.B = new Point(movingOriginal.B.X + bx, movingOriginal.B.Y + by);
                }
                canvas.Invalidate();
                return;
            }
            if (moving != null && focusDrag)
            {
                float fx = movingOriginal.Focus.X + p.X - moveStart.X, fy = movingOriginal.Focus.Y + p.Y - moveStart.Y;
                moving.Focus = new PointF(Math.Max(0, Math.Min(image.Width, fx)), Math.Max(0, Math.Min(image.Height, fy)));
                canvas.Invalidate();
                return;
            }
            if (moving != null)
            {
                int dx = p.X - moveStart.X, dy = p.Y - moveStart.Y;
                moving.A = new Point(movingOriginal.A.X + dx, movingOriginal.A.Y + dy);
                moving.B = new Point(movingOriginal.B.X + dx, movingOriginal.B.Y + dy);
                if ((ModifierKeys & Keys.Shift) != 0)
                {
                    // Maiusc: la lente si sposta e ingrandisce il nuovo punto.
                    moving.HasFocus = movingOriginal.HasFocus;
                    moving.Focus = new PointF(movingOriginal.Focus.X + dx, movingOriginal.Focus.Y + dy);
                }
                else
                {
                    // Normale: la lente continua a mostrare il dettaglio di prima, collegata con una linea.
                    moving.HasFocus = true;
                    moving.Focus = movingOriginal.HasFocus ? movingOriginal.Focus : movingOriginal.LensCenter;
                }
                Rectangle mb = moving.Bounds();
                canvas.Invalidate(ToView(Rectangle.Union(lastBounds, mb)));
                lastBounds = mb;
                return;
            }
            if (current == null)
            {
                if (tool == Tool.Testo && textBox == null)
                {
                    // Passando sopra un testo compaiono la cornice e il pallino per inclinarlo.
                    bool onHandle = OnTextHandle(p);
                    int hit = onHandle ? activeText : HitText(p);
                    if (hit >= 0 && hit != activeText) { SetActiveText(hit); canvas.Invalidate(); }
                    canvas.Cursor = onHandle ? Cursors.Hand : hit >= 0 ? Cursors.SizeAll : Cursors.IBeam;
                    return;
                }
                if (IsBoxTool(tool))
                {
                    // Sul bordo di un riquadro/ovale compaiono cornice, quadratini e pallino per ruotare.
                    bool onRotate = OnBoxRotateHandle(p);
                    int h = onRotate ? -1 : BoxHandleAt(p);
                    int hit = onRotate || h >= 0 ? activeBox : HitBox(p);
                    if (hit >= 0 && hit != activeBox) { activeBox = hit; canvas.Invalidate(); }
                    canvas.Cursor = onRotate ? Cursors.Hand
                        : h >= 0 ? HandleCursor(h, shapes[activeBox].Angle)
                        : hit >= 0 ? Cursors.SizeAll : Cursors.Cross;
                    return;
                }
                // Sopra una lente già messa il cursore diventa "sposta".
                canvas.Cursor = tool == Tool.Lente && (HitLensFocus(p) >= 0 || HitLens(p) >= 0) ? Cursors.SizeAll : Cursors.Cross;
                return;
            }
            if (current.IsFreehand)
            {
                if ((ModifierKeys & Keys.Shift) != 0)
                {
                    // Maiusc: linea orizzontale dritta (comodo per evidenziare una riga di testo).
                    Point first = current.Points[0];
                    current.Points.Clear();
                    current.Points.Add(first);
                    current.Points.Add(new Point(p.X, first.Y));
                }
                else
                {
                    Point last = current.Points[current.Points.Count - 1];
                    if (last != p) current.Points.Add(p);
                }
            }
            else if (current.Tool == Tool.Numero) current.A = current.B = p;   // il cerchietto segue il mouse
            else if (current.Tool == Tool.Lente ||
                     ((current.Tool == Tool.Ovale || current.Tool == Tool.Rettangolo) && (ModifierKeys & Keys.Shift) != 0))
            {
                // Lente sempre rotonda; ovale e riquadro con Maiusc: cerchio e quadrato.
                int side = Math.Max(Math.Abs(p.X - current.A.X), Math.Abs(p.Y - current.A.Y));
                current.B = new Point(current.A.X + (p.X >= current.A.X ? side : -side), current.A.Y + (p.Y >= current.A.Y ? side : -side));
            }
            else if (current.Tool == Tool.Linea && (ModifierKeys & Keys.Shift) != 0)
            {
                // Maiusc: linea orizzontale, verticale o a 45°.
                double ang = Math.Atan2(p.Y - current.A.Y, p.X - current.A.X);
                double snapped = Math.Round(ang / (Math.PI / 4)) * (Math.PI / 4);
                double len = Math.Sqrt(Math.Pow(p.X - current.A.X, 2) + Math.Pow(p.Y - current.A.Y, 2));
                current.B = new Point(current.A.X + (int)Math.Round(len * Math.Cos(snapped)), current.A.Y + (int)Math.Round(len * Math.Sin(snapped)));
            }
            else current.B = p;

            Rectangle nb = current.Bounds();
            canvas.Invalidate(ToView(Rectangle.Union(lastBounds, nb)));
            lastBounds = nb;
        }

        void CanvasMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (moving != null)
            {
                var moved = moving;
                moving = null;
                shapes[movingIndex] = movingOriginal;
                rotating = false;
                boxHandle = -1;
                focusDrag = false;
                if (moved.A != movingOriginal.A || moved.B != movingOriginal.B || moved.Angle != movingOriginal.Angle ||
                    moved.Focus != movingOriginal.Focus)
                    Record(new EditOp { Index = movingIndex, Before = movingOriginal, After = moved });
                canvas.Invalidate();
                return;
            }
            if (current == null) return;
            var done = current;
            current = null;
            if (done.IsValid)
            {
                Record(new EditOp { Index = shapes.Count, After = done });
                if (IsBoxTool(done.Tool)) activeBox = shapes.Count - 1;   // quadratini subito visibili
            }
            canvas.Invalidate(ToView(lastBounds));

            if (done.Tool == Tool.Numero)
            {
                // Subito a destra del numero si apre la casella per scrivere la descrizione.
                float fs = NumberTextSize();
                StartText(new Point(done.A.X + (int)(done.Width / 2 + done.Width * 0.3f), done.A.Y - (int)(fs * 0.73f)), fs);
            }
        }

        // Indice della lente sotto il punto (la più in alto), oppure -1.
        int HitLensFocus(Point p)
        {
            for (int i = shapes.Count - 1; i >= 0; i--)
            {
                var s = shapes[i];
                if (s.Tool != Tool.Lente || !s.FocusVisible) continue;
                float r = s.FocusRadius + 6f / zoom;
                float dx = p.X - s.Focus.X, dy = p.Y - s.Focus.Y;
                if (dx * dx + dy * dy <= r * r) return i;
            }
            return -1;
        }

        int HitLens(Point p)
        {
            for (int i = shapes.Count - 1; i >= 0; i--)
            {
                var s = shapes[i];
                if (s.Tool != Tool.Lente) continue;
                float rx = Math.Abs(s.B.X - s.A.X) / 2f, ry = Math.Abs(s.B.Y - s.A.Y) / 2f;
                if (rx < 1 || ry < 1) continue;
                float cx = (s.A.X + s.B.X) / 2f, cy = (s.A.Y + s.B.Y) / 2f;
                float nx = (p.X - cx) / rx, ny = (p.Y - cy) / ry;
                if (nx * nx + ny * ny <= 1f) return i;
            }
            return -1;
        }

        // --- annulla / ripeti: ogni modifica è un'aggiunta o una sostituzione (spostamento)

        class EditOp
        {
            public int Index;
            public Shape Before;   // null = forma aggiunta
            public Shape After;
        }

        void Apply(EditOp op)
        {
            if (op.Before == null) shapes.Insert(op.Index, op.After);
            else shapes[op.Index] = op.After;
        }

        void Revert(EditOp op)
        {
            if (op.Before == null) shapes.RemoveAt(op.Index);
            else shapes[op.Index] = op.Before;
        }

        void Record(EditOp op)
        {
            Apply(op);
            undoOps.Push(op);
            redoOps.Clear();
            modified = true;
        }

        // Il numero che avrà il prossimo cerchietto (dopo un Annulla riprende da dove si era).
        int NextNumber()
        {
            int max = 0, n;
            foreach (var s in shapes)
                if (s.Tool == Tool.Numero && int.TryParse(s.Text, out n)) max = Math.Max(max, n);
            return max + 1;
        }

        // --- testo

        void StartText(Point at, float size)
        {
            textAt = at;
            textBoxSize = size;
            textBox = new TextBox();
            textBox.Multiline = true;
            textBox.AcceptsReturn = true;
            textBox.WordWrap = false;
            textBox.BorderStyle = BorderStyle.FixedSingle;
            StyleTextBox();
            textBox.TextChanged += delegate { ResizeTextBox(); };
            var thisBox = textBox;
            textBox.LostFocus += delegate
            {
                // Conferma solo se è ancora aperta questa casella (non una nuova aperta nel frattempo).
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(new Action(delegate { if (textBox == thisBox) CommitText(); }));
            };
            canvas.Controls.Add(textBox);
            ResizeTextBox();
            textBox.Focus();
        }

        void StyleTextBox()
        {
            if (textBox == null) return;
            textBox.Font = new Font(Shape.TextFont, Math.Max(6f, textBoxSize * zoom), FontStyle.Bold, GraphicsUnit.Pixel);
            textBox.ForeColor = color;
            Color halo = Shape.HaloFor(color);
            textBox.BackColor = halo == Color.White ? Color.White : Color.FromArgb(60, 60, 60);
            ResizeTextBox();
        }

        void ResizeTextBox()
        {
            if (textBox == null) return;
            Size sz = TextRenderer.MeasureText(textBox.Text + "W", textBox.Font);
            textBox.Size = new Size(sz.Width + 12, sz.Height + 8);
            textBox.Location = new Point((int)(textAt.X * zoom) - 3, (int)(textAt.Y * zoom) - 3);
        }

        void CommitText()
        {
            if (textBox == null) return;
            var box = textBox;
            textBox = null;           // evita di confermare due volte
            var s = new Shape { Tool = Tool.Testo, Color = box.ForeColor, Width = textBoxSize, A = textAt, B = textAt };
            s.Text = box.Text.Replace("\r\n", "\n").TrimEnd();
            canvas.Controls.Remove(box);
            box.Dispose();
            if (s.IsValid)
            {
                Record(new EditOp { Index = shapes.Count, After = s });
                SetActiveText(shapes.Count - 1);   // mostra subito il pallino per inclinarlo
            }
            canvas.Invalidate();
            canvas.Focus();
        }

        void CancelText()
        {
            if (textBox == null) return;
            var box = textBox;
            textBox = null;
            canvas.Controls.Remove(box);
            box.Dispose();
            canvas.Focus();
        }

        void Undo()
        {
            if (undoOps.Count == 0) return;
            var op = undoOps.Pop();
            Revert(op);
            redoOps.Push(op);
            modified = true;
            canvas.Invalidate();
        }

        void Redo()
        {
            if (redoOps.Count == 0) return;
            var op = redoOps.Pop();
            Apply(op);
            undoOps.Push(op);
            modified = true;
            canvas.Invalidate();
        }

        // --- esportazione

        Bitmap Render()
        {
            var bmp = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                var r = new Rectangle(0, 0, image.Width, image.Height);
                g.DrawImage(image, r, r, GraphicsUnit.Pixel);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawStamp(g);
                foreach (var s in shapes) s.Draw(g);
            }
            return bmp;
        }

        void Copy()
        {
            CommitText();
            using (var bmp = Render())
                if (!ImageExport.Copy(this, bmp)) return;
            modified = false;
            Flash(Lang.T("✔ Copiata negli appunti — incollala con Ctrl+V dove vuoi.", "✔ Copied to the clipboard — paste it anywhere with Ctrl+V."));
            copiedShown = true;
            copyButton.Text = Lang.T("✔ Copiato", "✔ Copied");
            copyButton.Invalidate();
            copiedTimer.Stop();
            copiedTimer.Start();
        }

        // Disegna il pulsante Copia: verde (più scuro sotto il mouse e quando è premuto; verde intenso dopo la copia).
        void PaintCopyButton(Graphics g, float scale)
        {
            var b = copyButton;
            var r = new Rectangle(0, 0, b.Width - 1, b.Height - 1);
            Color bg = copiedShown ? Color.FromArgb(22, 128, 61)
                     : b.Pressed ? Color.FromArgb(21, 115, 55)
                     : b.Selected ? Color.FromArgb(30, 145, 70)
                     : Color.FromArgb(40, 167, 69);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = IconArt.RoundRect(r, 4 * scale))
            using (var br = new SolidBrush(bg))
                g.FillPath(br, path);
            TextRenderer.DrawText(g, b.Text, b.Font, r, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        void Save()
        {
            CommitText();
            string path;
            using (var bmp = Render())
                path = ImageExport.Save(this, bmp);
            if (path == null) return;
            modified = false;
            Flash(Lang.T("✔ Salvata: ", "✔ Saved: ") + path);
        }

        // Appende l'immagine (con i segni) sullo schermo, sempre in primo piano, e chiude l'editor.
        void Pin()
        {
            CommitText();
            Point at = canvas.PointToScreen(Point.Empty);
            var pin = new PinForm(Render(), at, color);
            pin.Show();
            modified = false;
            Close();
        }

        void NewCapture()
        {
            if (Program.RequestCapture == null) return;
            WindowState = FormWindowState.Minimized;
            Program.RequestCapture(false, 350);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (textBox != null && textBox.Focused)
            {
                // Mentre si scrive, i tasti vanno alla casella di testo.
                if (keyData == Keys.Escape) { CancelText(); return true; }
                if (keyData == Keys.Enter) { CommitText(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            switch (keyData)
            {
                case Keys.Control | Keys.C: Copy(); return true;
                case Keys.Control | Keys.S: Save(); return true;
                case Keys.Control | Keys.Z: Undo(); return true;
                case Keys.Control | Keys.Y: Redo(); return true;
                case Keys.Control | Keys.N: NewCapture(); return true;
                case Keys.Control | Keys.P: Pin(); return true;
                case Keys.Control | Keys.Oemplus:
                case Keys.Control | Keys.Add: SetZoom(zoom * 1.25f); return true;
                case Keys.Control | Keys.OemMinus:
                case Keys.Control | Keys.Subtract: SetZoom(zoom / 1.25f); return true;
                case Keys.Control | Keys.D0:
                case Keys.Control | Keys.NumPad0: ToggleFit(); return true;
                case Keys.Escape: Close(); return true;
            }
            if (!widthBox.Focused)
            {
                switch (keyData)
                {
                    case Keys.D1: case Keys.NumPad1: SelectTool(Tool.Evidenziatore); return true;
                    case Keys.D2: case Keys.NumPad2: SelectTool(Tool.Marcatore); return true;
                    case Keys.D3: case Keys.NumPad3: SelectTool(Tool.Penna); return true;
                    case Keys.D4: case Keys.NumPad4: SelectTool(Tool.Rettangolo); return true;
                    case Keys.D5: case Keys.NumPad5: SelectTool(Tool.Freccia); return true;
                    case Keys.D6: case Keys.NumPad6: SelectTool(Tool.Testo); return true;
                    case Keys.D7: case Keys.NumPad7: SelectTool(Tool.Numero); return true;
                    case Keys.D8: case Keys.NumPad8: SelectTool(Tool.Oscura); return true;
                    case Keys.D9: case Keys.NumPad9: SelectTool(Tool.Lente); return true;
                    case Keys.D0: case Keys.NumPad0: SelectTool(Tool.Linea); return true;
                    case Keys.O: SelectTool(Tool.Ovale); return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            CommitText();
            if (modified && shapes.Count > 0 && e.CloseReason == CloseReason.UserClosing)
            {
                var r = MessageBox.Show(this, Lang.T("Hai fatto delle modifiche che non hai né copiato né salvato.\nChiudere comunque?",
                                                     "You made changes that you have neither copied nor saved.\nClose anyway?"),
                    Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) e.Cancel = true;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Lang.Changed -= OnLanguageChanged;
                image.Dispose();
                if (view != null) view.Dispose();
                statusTimer.Dispose();
                copiedTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
