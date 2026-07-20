using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Torify
{
    // ── helper de animação: valor que desliza suavemente até um alvo ──
    class Tween
    {
        public float Value;
        public float Target;
        public float Speed = 0.12f;
        public void Step()
        {
            Value += (Target - Value) * Speed;
            if (Math.Abs(Target - Value) < 0.01f) Value = Target;
        }
        public bool Done { get { return Value == Target; } }
    }

    // ── dot de status / decorativo (pulsa) ──
    class DotControl : Control
    {
        public Color DotColor = Color.White;
        public bool Pulse = true;
        float phase = 0;
        public DotControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
            Width = 12; Height = 12;
            var t = new System.Windows.Forms.Timer(); t.Interval = 40;
            t.Tick += (s, e) => { phase += 0.12f; if (phase > 6.28f) phase = 0; Invalidate(); };
            t.Start();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            float a = Pulse ? (0.45f + 0.55f * (float)Math.Abs(Math.Sin(phase))) : 1f;
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * a), DotColor)))
            using (var g = e.Graphics)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(b, 1, 1, Width - 2, Height - 2);
            }
        }
    }

    // ── toggle animado (slide) ──
    class ToggleControl : Control
    {
        public bool On = false;
        Tween pos = new Tween();
        public event EventHandler Toggled;
        public ToggleControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
            Width = 38; Height = 22; pos.Value = 0; pos.Target = 0;
            var t = new System.Windows.Forms.Timer(); t.Interval = 16;
            t.Tick += (s, e) => { pos.Step(); if (!pos.Done) Invalidate(); };
            t.Start();
            this.Click += (s, e) => { On = !On; pos.Target = On ? 1 : 0; Invalidate(); if (Toggled != null) Toggled(this, e); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var g = e.Graphics)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int x = 1, y = 1, w = Width - 2, h = Height - 2;
                using (var path = Ui.RoundRect(x, y, w, h, h / 2))
                {
                    Color bg = On ? Color.FromArgb(0, 224, 143) : (Theme.Dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(200, 200, 200));
                    using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                    int dotD = h - 8;
                    int dotX = x + 4 + (int)(pos.Value * (w - dotD - 8));
                    using (var db = new SolidBrush(On ? Color.Black : Color.White))
                        g.FillEllipse(db, dotX, y + 4, dotD, dotD);
                }
            }
        }
    }

    static class Theme
    {
        public static bool Dark = true;
        public static Color WinBg { get { return Dark ? Color.FromArgb(10, 10, 10) : Color.White; } }
        public static Color Border { get { return Dark ? Color.FromArgb(51, 51, 51) : Color.Black; } }
        public static Color BorderSoft { get { return Dark ? Color.FromArgb(34, 34, 34) : Color.FromArgb(229, 229, 229); } }
        public static Color Text { get { return Dark ? Color.White : Color.Black; } }
        public static Color Muted { get { return Dark ? Color.FromArgb(102, 102, 102) : Color.FromArgb(153, 153, 153); } }
        public static Color Accent { get { return Color.FromArgb(0, 224, 143); } }
        public static Color CardBorderHi { get { return Dark ? Color.White : Color.Black; } }
    }

    class NavItem : Panel
    {
        public string Icon;
        public string Label;
        public bool Active = false;
        public NavItem(string icon, string label)
        {
            Icon = icon; Label = label;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
            Height = 42;
            this.MouseEnter += (s, e) => { if (!Active) Invalidate(); };
            this.MouseLeave += (s, e) => { if (!Active) Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var g = e.Graphics)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color bar = Active ? Theme.Text : Color.Transparent;
                using (var b = new SolidBrush(bar))
                    g.FillRectangle(b, 0, 0, 3, Height);
                Color txt = Active ? Theme.Text : (this.ClientRectangle.Contains(this.PointToClient(Cursor.Position)) ? Theme.Text : Theme.Muted);
                using (var sf = new StringFormat() { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Near })
                {
                    var fIcon = new Font("Segoe UI Symbol", 13, FontStyle.Regular);
                    var fLabel = new Font("JetBrains Mono", 12, Active ? FontStyle.Bold : FontStyle.Regular);
                    g.DrawString(Icon, fIcon, new SolidBrush(txt), new RectangleF(16, 0, 22, Height), sf);
                    g.DrawString(Label, fLabel, new SolidBrush(txt), new RectangleF(44, 0, Width - 50, Height), sf);
                }
            }
        }
    }

    class Card : Panel
    {
        Tween fade = new Tween();
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
            fade.Value = 0; fade.Target = 1; fade.Speed = 0.10f;
            var t = new System.Windows.Forms.Timer(); t.Interval = 16;
            t.Tick += (s, e) => { fade.Step(); if (!fade.Done) Invalidate(); else t.Stop(); };
            t.Start();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var g = e.Graphics)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int a = (int)(255 * fade.Value);
                using (var path = Ui.RoundRect(0, 0, Width - 1, Height - 1, 6))
                {
                    Color bc = Theme.Dark ? Color.FromArgb(a, 51, 51, 51) : Color.FromArgb(a, 204, 204, 204);
                    using (var pen = new Pen(bc, Hi ? 1.5f : 1f))
                        g.DrawPath(pen, path);
                }
            }
        }
        public bool Hi = false;
    }

    static class Ui
    {
        public static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
        {
            var p = new GraphicsPath();
            p.AddArc(x, y, r * 2, r * 2, 180, 90);
            p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            p.CloseFigure();
            return p;
        }
        public static void StyleButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.Font = new Font("JetBrains Mono", 11, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
        }
    }

    public class MainForm : Form
    {
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int nLeft, int nTop, int nRight, int nBottom, int nWidthEllipse, int nHeightEllipse);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern int SendMessage(IntPtr h, int m, int w, int l);

        // paths
        static string BaseDir;
        static string TorDir;
        static string TorExe;
        static string Torrc;
        static string PcDir;
        static string PcExe;
        static string PcConf;
        static string AppsFile;
        static DateTime _lastNewnym = DateTime.MinValue;

        // ui
        Panel sidebar, content, titlebar;
        Label titleLabel, versionLabel, statusLabel;
        DotControl statusDot;
        Dictionary<string, NavItem> navs = new Dictionary<string, NavItem>();
        string current = "start";
        System.Windows.Forms.Timer clock;
        Label torIpVal, realIpVal;
        Card torCard, realCard;
        ListBox appsBox;
        TextBox pathBox;
        ToggleControl rotateToggle;
        Label rotateSub;
        System.Windows.Forms.Timer rotateTimer;
        int rotateInterval = 60;
        int rotateCountdown = 0;
        bool rotating = false;
        List<AppEntry> apps = new List<AppEntry>();

        public MainForm()
        {
            InitPaths();
            this.Text = "torify";
            this.Size = new Size(660, 460);
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Theme.WinBg;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Load += (s, e) =>
            {
                this.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 10, 10));
                new Thread(SetupThread).Start();
            };
            BuildUi();
            clock = new System.Windows.Forms.Timer(); clock.Interval = 1000; clock.Tick += (s, e) => RefreshStatus(); clock.Start();
        }

        void SetupThread()
        {
            try
            {
                string done = Path.Combine(BaseDir, ".setup-complete");
                if (!File.Exists(done))
                {
                    if (!(Directory.Exists(TorDir) && File.Exists(TorExe) && Directory.Exists(PcDir) && FindPcExe() != null))
                        RunSetup();
                }
            }
            catch { }
            this.BeginInvoke((Action)(() => { EnableUi(true); RefreshStatus(); }));
        }

        void EnableUi(bool on)
        {
            foreach (NavItem n in sidebar.Controls) n.Enabled = on;
            statusLabel.Text = on ? statusLabel.Text : "setting up...";
        }

        // ── build UI ──
        void BuildUi()
        {
            titlebar = new Panel() { Height = 46, Dock = DockStyle.Top, BackColor = Theme.WinBg };
            titlebar.Paint += (s, e) =>
            {
                using (var g = e.Graphics)
                using (var pen = new Pen(Theme.Border, 1))
                    g.DrawLine(pen, 0, titlebar.Height - 1, titlebar.Width, titlebar.Height - 1);
            };
            var tleft = new Panel() { Dock = DockStyle.Left, Width = 200, BackColor = Color.Transparent };
            var dot = new DotControl() { DotColor = Color.White, Location = new Point(16, 19) };
            titleLabel = new Label() { Text = "torify", ForeColor = Theme.Text, Font = new Font("JetBrains Mono", 14, FontStyle.Regular), Location = new Point(32, 14), AutoSize = true };
            versionLabel = new Label() { Text = "v1.4", ForeColor = Theme.Muted, Font = new Font("JetBrains Mono", 11), Location = new Point(96, 17), AutoSize = true };
            tleft.Controls.Add(dot); tleft.Controls.Add(titleLabel); tleft.Controls.Add(versionLabel);

            var tright = new Panel() { Dock = DockStyle.Right, Width = 130, BackColor = Color.Transparent };
            statusDot = new DotControl() { DotColor = Theme.Accent, Pulse = true, Location = new Point(18, 18) };
            statusLabel = new Label() { Text = "offline", ForeColor = Theme.Text, Font = new Font("JetBrains Mono", 11), Location = new Point(34, 15), AutoSize = true };
            tright.Controls.Add(statusDot); tright.Controls.Add(statusLabel);

            // theme toggle (mini button)
            var themeBtn = new Label() { Text = "◐", ForeColor = Theme.Muted, Font = new Font("Segoe UI Symbol", 14), Location = new Point(95, 13), AutoSize = true, Cursor = Cursors.Hand };
            themeBtn.Click += (s, e) => ToggleTheme();
            tright.Controls.Add(themeBtn);

            titlebar.Controls.Add(tleft); titlebar.Controls.Add(tright);
            titlebar.MouseDown += (s, e) => { ReleaseCapture(); SendMessage(this.Handle, 0xA1, 0x2, 0); };

            sidebar = new Panel() { Width = 170, Dock = DockStyle.Left, BackColor = Theme.WinBg };
            sidebar.Paint += (s, e) =>
            {
                using (var g = e.Graphics)
                using (var pen = new Pen(Theme.Border, 1))
                    g.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
            };
            string[,] items = {
                {"start","▶","start tor"},
                {"check","◎","check ip"},
                {"config","⚙","configure"},
                {"apps","▤","apps"},
                {"rotate","↻","auto-rotate"},
                {"stop","⏻","stop tor"}
            };
            int yy = 16;
            for (int i = 0; i < items.GetLength(0); i++)
            {
                var n = new NavItem(items[i, 1], items[i, 2]) { Width = 170, Location = new Point(0, yy) };
                string key = items[i, 0];
                n.Click += (s, e) => SelectNav(key);
                navs[key] = n;
                sidebar.Controls.Add(n);
                yy += 46;
            }
            navs["start"].Active = true;

            content = new Panel() { Dock = DockStyle.Fill, BackColor = Theme.WinBg, Padding = new Padding(22, 18, 22, 18) };

            this.Controls.Add(content);
            this.Controls.Add(sidebar);
            this.Controls.Add(titlebar);

            BuildStartView();
            BuildCheckView();
            BuildConfigView();
            BuildAppsView();
            BuildRotateView();
            BuildStopView();
            ShowView("start");
        }

        Dictionary<string, Panel> views = new Dictionary<string, Panel>();
        void ShowView(string key)
        {
            current = key;
            foreach (var kv in views) kv.Value.Visible = (kv.Key == key);
            foreach (var kv in navs) kv.Value.Active = (kv.Key == key);
            sidebar.Refresh();
        }
        void SelectNav(string key) { ShowView(key); }

        // ── START view ──
        Label startStatus;
        void BuildStartView()
        {
            var p = new Panel() { Dock = DockStyle.Fill };
            var head = SectionLabel("start tor");
            var btn = new Button() { Text = "start", Size = new Size(120, 34), Location = new Point(0, 32) };
            Ui.StyleButton(btn); btn.BackColor = Theme.Accent; btn.ForeColor = Color.Black; btn.FlatAppearance.BorderColor = Theme.Accent;
            btn.Click += (s, e) => { btn.Text = "starting..."; btn.Enabled = false; new Thread(() => { StartTor(); this.BeginInvoke((Action)(() => { btn.Text = "running"; btn.Enabled = true; ShowCheck(); })); }).Start(); };
            startStatus = new Label() { Text = "boot tor, rotate ip, verify", ForeColor = Theme.Muted, Font = new Font("JetBrains Mono", 11), Location = new Point(0, 74), AutoSize = true };
            p.Controls.Add(head); p.Controls.Add(btn); p.Controls.Add(startStatus);
            views["start"] = p; content.Controls.Add(p);
        }

        // ── CHECK view ──
        void BuildCheckView()
        {
            var p = new Panel() { Dock = DockStyle.Fill };
            var head = SectionLabel("ip comparison");
            realCard = new Card() { Size = new Size(250, 70), Location = new Point(0, 30) };
            torCard = new Card() { Size = new Size(250, 70), Location = new Point(270, 30) }; torCard.Hi = true;
            var rlab = CardLabel("real ip", new Point(14, 12)); var rval = new Label() { Name = "rv", Text = "—", ForeColor = Theme.Text, Font = new Font("JetBrains Mono", 13, FontStyle.Bold), Location = new Point(14, 34), AutoSize = true };
            var tlab = CardLabel("tor ip", new Point(14, 12)); var tval = new Label() { Name = "tv", Text = "—", ForeColor = Theme.Text, Font = new Font("JetBrains Mono", 13, FontStyle.Bold), Location = new Point(14, 34), AutoSize = true };
            realCard.Controls.Add(rlab); realCard.Controls.Add(rval);
            torCard.Controls.Add(tlab); torCard.Controls.Add(tval);
            realIpVal = rval; torIpVal = tval;
            var btn = new Button() { Text = "refresh", Size = new Size(110, 32), Location = new Point(0, 116) };
            Ui.StyleButton(btn); btn.BackColor = Theme.WinBg; btn.ForeColor = Theme.Text; btn.FlatAppearance.BorderColor = Theme.Border;
            btn.Click += (s, e) => ShowCheck();
            p.Controls.Add(head); p.Controls.Add(realCard); p.Controls.Add(torCard); p.Controls.Add(btn);
            views["check"] = p; content.Controls.Add(p);
        }
        void ShowCheck()
        {
            var t = new Thread(() =>
            {
                string real = GetRealIP();
                string tor = CheckTorIP();
                this.BeginInvoke((Action)(() =>
                {
                    realIpVal.Text = string.IsNullOrEmpty(real) ? "falhou" : real;
                    torIpVal.Text = string.IsNullOrEmpty(tor) ? "offline" : tor;
                }));
            });
            t.Start();
        }

        // ── CONFIG view ──
        void BuildConfigView()
        {
            var p = new Panel() { Dock = DockStyle.Fill };
            var head = SectionLabel("configure");
            pathBox = new TextBox() { Location = new Point(0, 32), Width = 380, Font = new Font("JetBrains Mono", 11), BackColor = Theme.WinBg, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            var cfgFile = Path.Combine(BaseDir, "target-app.txt");
            if (File.Exists(cfgFile)) pathBox.Text = File.ReadAllText(cfgFile).Trim();
            var btn = new Button() { Text = "save", Size = new Size(90, 30), Location = new Point(390, 31) };
            Ui.StyleButton(btn); btn.BackColor = Theme.Accent; btn.ForeColor = Color.Black; btn.FlatAppearance.BorderColor = Theme.Accent;
            btn.Click += (s, e) =>
            {
                if (File.Exists(pathBox.Text.Trim())) { File.WriteAllText(cfgFile, pathBox.Text.Trim()); Flash(btn, "saved"); }
                else Flash(btn, "not found");
            };
            var hint = new Label() { Text = "set the target executable path", ForeColor = Theme.Muted, Font = new Font("JetBrains Mono", 11), Location = new Point(0, 70), AutoSize = true };
            var btnAuto = new Button() { Text = "auto-detect", Size = new Size(110, 30), Location = new Point(0, 100) };
            Ui.StyleButton(btnAuto); btnAuto.BackColor = Theme.WinBg; btnAuto.ForeColor = Theme.Text; btnAuto.FlatAppearance.BorderColor = Theme.Border;
            btnAuto.Click += (s, e) => { string f = FindTargetApp(); if (f != null) { pathBox.Text = f; File.WriteAllText(cfgFile, f); Flash(btnAuto, "set"); } else Flash(btnAuto, "none"); };
            p.Controls.Add(head); p.Controls.Add(pathBox); p.Controls.Add(btn); p.Controls.Add(hint); p.Controls.Add(btnAuto);
            views["config"] = p; content.Controls.Add(p);
        }

        // ── APPS view ──
        void BuildAppsView()
        {
            var p = new Panel() { Dock = DockStyle.Fill };
            var head = SectionLabel("apps");
            appsBox = new ListBox() { Location = new Point(0, 30), Size = new Size(400, 150), Font = new Font("JetBrains Mono", 11), BackColor = Theme.WinBg, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            var btnAdd = new Button() { Text = "add", Size = new Size(90, 30), Location = new Point(410, 30) };
            Ui.StyleButton(btnAdd); btnAdd.BackColor = Theme.WinBg; btnAdd.ForeColor = Theme.Text; btnAdd.FlatAppearance.BorderColor = Theme.Border;
            btnAdd.Click += (s, e) =>
            {
                var d = new OpenFileDialog() { Filter = "Executables (*.exe)|*.exe", Title = "select executable to route via tor" };
                if (d.ShowDialog() == DialogResult.OK) { AddAppToList(d.FileName); LaunchAppViaProxy(d.FileName, false); RefreshApps(); }
            };
            var btnOpen = new Button() { Text = "open via tor", Size = new Size(110, 30), Location = new Point(410, 66) };
            Ui.StyleButton(btnOpen); btnOpen.BackColor = Theme.Accent; btnOpen.ForeColor = Color.Black; btnOpen.FlatAppearance.BorderColor = Theme.Accent;
            btnOpen.Click += (s, e) =>
            {
                if (appsBox.SelectedIndex >= 0) { StartTor(); if (IsTorRunning()) { SendNEWNYM(); Thread.Sleep(1500); LaunchAppViaProxy(apps[appsBox.SelectedIndex].Path, false); } }
            };
            p.Controls.Add(head); p.Controls.Add(appsBox); p.Controls.Add(btnAdd); p.Controls.Add(btnOpen);
            views["apps"] = p; content.Controls.Add(p);
            RefreshApps();
        }
        void RefreshApps()
        {
            apps = LoadApps();
            appsBox.Items.Clear();
            foreach (var a in apps) appsBox.Items.Add(a.Name + "  —  " + (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(a.Path)).Length > 0 ? "active" : "idle"));
        }

        // ── ROTATE view ──
        void BuildRotateView()
        {
            var p = new Panel() { Dock = DockStyle.Fill };
            var head = SectionLabel("auto-rotate");
            rotateToggle = new ToggleControl() { Location = new Point(300, 30) };
            rotateToggle.Toggled += (s, e) =>
            {
                if (rotateToggle.On)
                {
                    string app = FindTargetApp();
                    if (app == null) { FlashLbl(statusLabel, "no app set"); rotateToggle.On = false; return; }
                    StartTor();
                    if (!IsTorRunning()) { rotateToggle.On = false; return; }
                    rotating = true; rotateCountdown = rotateInterval;
                    LaunchAppViaProxy(app, false);
                    rotateTimer = new System.Windows.Forms.Timer(); rotateTimer.Interval = 1000;
                    rotateTimer.Tick += RotateTick; rotateTimer.Start();
                }
                else
                {
                    rotating = false;
                    if (rotateTimer != null) rotateTimer.Stop();
                }
            };
            rotateSub = new Label() { Text = "next in — · interval 60s", ForeColor = Theme.Muted, Font = new Font("JetBrains Mono", 10), Location = new Point(0, 64), AutoSize = true };
            var rtitle = new Label() { Text = "auto-rotate", ForeColor = Theme.Text, Font = new Font("JetBrains Mono", 13, FontStyle.Bold), Location = new Point(0, 32), AutoSize = true };
            p.Controls.Add(head); p.Controls.Add(rtitle); p.Controls.Add(rotateToggle); p.Controls.Add(rotateSub);
            views["rotate"] = p; content.Controls.Add(p);
        }
        void RotateTick(object s, EventArgs e)
        {
            rotateCountdown--;
            if (rotateCountdown <= 0)
            {
                SendNEWNYM();
                Thread.Sleep(1500);
                ShowCheck();
                rotateCountdown = rotateInterval;
            }
            rotateSub.Text = string.Format("next in {0}s · interval {1}s", rotateCountdown, rotateInterval);
        }

        // ── STOP view ──
        void BuildStopView()
        {
            var p = new Panel() { Dock = DockStyle.Fill };
            var head = SectionLabel("stop tor");
            var btn = new Button() { Text = "stop tor", Size = new Size(120, 34), Location = new Point(0, 32) };
            Ui.StyleButton(btn); btn.BackColor = Theme.WinBg; btn.ForeColor = Theme.Text; btn.FlatAppearance.BorderColor = Theme.Border;
            btn.Click += (s, e) => { KillTor(); Flash(btn, "stopped"); };
            var hint = new Label() { Text = "kills the tor process", ForeColor = Theme.Muted, Font = new Font("JetBrains Mono", 11), Location = new Point(0, 76), AutoSize = true };
            p.Controls.Add(head); p.Controls.Add(btn); p.Controls.Add(hint);
            views["stop"] = p; content.Controls.Add(p);
        }

        Label SectionLabel(string t)
        {
            return new Label() { Text = t, ForeColor = Theme.Muted, Font = new Font("JetBrains Mono", 11), Location = new Point(0, 0), AutoSize = true };
        }
        Label CardLabel(string t, Point loc)
        {
            return new Label() { Text = t, ForeColor = Theme.Muted, Font = new Font("JetBrains Mono", 10), Location = loc, AutoSize = true };
        }
        void Flash(Button b, string txt) { string o = b.Text; b.Text = txt; var tm = new System.Windows.Forms.Timer(); tm.Interval = 900; tm.Tick += (s, e) => { b.Text = o; tm.Stop(); }; tm.Start(); }
        void FlashLbl(Label l, string txt) { string o = l.Text; l.Text = txt; var tm = new System.Windows.Forms.Timer(); tm.Interval = 1200; tm.Tick += (s, e) => { l.Text = o; tm.Stop(); }; tm.Start(); }

        void ToggleTheme()
        {
            Theme.Dark = !Theme.Dark;
            this.BackColor = Theme.WinBg;
            titlebar.BackColor = Theme.WinBg;
            sidebar.BackColor = Theme.WinBg;
            content.BackColor = Theme.WinBg;
            titleLabel.ForeColor = Theme.Text; versionLabel.ForeColor = Theme.Muted;
            statusLabel.ForeColor = Theme.Text;
            statusDot.DotColor = rotating ? Theme.Accent : (IsTorRunning() ? Theme.Accent : Theme.Muted);
            foreach (NavItem n in sidebar.Controls) n.Invalidate();
            foreach (Control c in content.Controls) c.Refresh();
            realCard.Invalidate(); torCard.Invalidate();
        }

        void RefreshStatus()
        {
            bool running = IsTorRunning();
            string tor = CheckTorIP();
            statusLabel.Text = running && !string.IsNullOrEmpty(tor) ? "connected" : (running ? "booting" : "offline");
            statusDot.DotColor = (running && !string.IsNullOrEmpty(tor)) ? Theme.Accent : (running ? Theme.Muted : Theme.Muted);
            statusDot.Pulse = (running && !string.IsNullOrEmpty(tor));
            if (current == "check") { if (!string.IsNullOrEmpty(realIpVal.Text) && realIpVal.Text == "—") ShowCheck(); }
        }

        // ════════════════════ LÓGICA (mantida da v1.4) ════════════════════
        class AppEntry { public string Name; public string Path; }

        void InitPaths()
        {
            BaseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Torify");
            Directory.CreateDirectory(BaseDir);
            TorDir = Path.Combine(BaseDir, "tor");
            TorExe = Path.Combine(TorDir, "tor.exe");
            Torrc = Path.Combine(TorDir, "Data", "Tor", "torrc");
            PcDir = Path.Combine(BaseDir, "proxychains");
            PcExe = FindPcExe();
            PcConf = Path.Combine(PcDir, "proxychains.conf");
            AppsFile = Path.Combine(BaseDir, "apps.txt");
        }

        void RunSetup()
        {
            string setupDone = Path.Combine(BaseDir, ".setup-complete");
            if (File.Exists(setupDone)) return;
            using (var wc = new WebClient())
            {
                wc.DownloadProgressChanged += (s, e) => { this.BeginInvoke((Action)(() => { statusLabel.Text = "setup " + e.ProgressPercentage + "%"; })); };
                string torVer = "15.0.18";
                string torUrl = "https://www.torproject.org/dist/torbrowser/" + torVer + "/tor-expert-bundle-windows-x86_64-" + torVer + ".tar.gz";
                string torFile = Path.Combine(BaseDir, "tor-expert.tar.gz");
                try { wc.DownloadFile(torUrl, torFile); }
                catch { wc.DownloadFile("https://archive.torproject.org/tor-package-archive/torbrowser/" + torVer + "/tor-expert-bundle-windows-x86_64-" + torVer + ".tar.gz", torFile); }
                var tar = new Process(); tar.StartInfo.FileName = "tar.exe"; tar.StartInfo.Arguments = "-xzf \"" + torFile + "\" -C \"" + BaseDir + "\""; tar.StartInfo.UseShellExecute = false; tar.StartInfo.CreateNoWindow = true; tar.Start(); tar.WaitForExit();
                if (!File.Exists(TorExe))
                {
                    string ext = null;
                    if (Directory.Exists(TorDir)) ext = TorDir;
                    else foreach (string d in Directory.GetDirectories(BaseDir, "*", SearchOption.TopDirectoryOnly)) if (File.Exists(Path.Combine(d, "tor.exe"))) { ext = d; break; }
                    if (ext != null && !ext.Equals(TorDir, StringComparison.OrdinalIgnoreCase)) MoveDirectorySafe(ext, TorDir);
                }
                File.Delete(torFile);
                Directory.CreateDirectory(Path.Combine(TorDir, "Data", "Tor"));
                File.WriteAllText(Torrc, "SOCKSPort 127.0.0.1:9050\nControlPort 127.0.0.1:9051\nCookieAuthentication 0\nDataDirectory " + TorDir + "\\Data\\Tor\nLog notice stdout\n");
                string pcVer = "0.6.8";
                string pcUrl = "https://github.com/shunf4/proxychains-windows/releases/download/" + pcVer + "/proxychains_" + pcVer + "_win32_x64.zip";
                string pcFile = Path.Combine(BaseDir, "proxychains.zip");
                wc.DownloadFile(pcUrl, pcFile);
                if (!Directory.Exists(PcDir)) Directory.CreateDirectory(PcDir);
                var un = new Process(); un.StartInfo.FileName = "powershell.exe"; un.StartInfo.Arguments = "-NoProfile -Command \"Expand-Archive -Path '" + pcFile + "' -DestinationPath '" + PcDir + "' -Force\""; un.StartInfo.UseShellExecute = false; un.StartInfo.CreateNoWindow = true; un.Start(); un.WaitForExit();
                File.Delete(pcFile);
                File.WriteAllText(PcConf, "strict_chain\nproxy_dns\ntcp_read_time_out 15000\ntcp_connect_time_out 8000\n[ProxyList]\nsocks5 127.0.0.1 9050\n");
                PcExe = FindPcExe();
                File.WriteAllText(setupDone, "ok");
            }
        }

        void MoveDirectorySafe(string source, string dest)
        {
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            try { Directory.Move(source, dest); return; } catch { }
            foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(dir.Replace(source, dest));
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, file.Replace(source, dest), true);
            Directory.Delete(source, true);
        }

        string FindPcExe()
        {
            if (!Directory.Exists(PcDir)) return null;
            string x64 = Path.Combine(PcDir, "proxychains_win32_x64.exe");
            if (File.Exists(x64)) return x64;
            string x86 = Path.Combine(PcDir, "proxychains_win32.exe");
            if (File.Exists(x86)) return x86;
            try { var f = Directory.GetFiles(PcDir, "proxychains*.exe"); if (f.Length > 0) return f[0]; } catch { }
            return null;
        }

        bool IsTorRunning() { try { return Process.GetProcessesByName("tor").Length > 0; } catch { return false; } }
        void KillTor() { foreach (var p in Process.GetProcessesByName("tor")) { try { p.Kill(); p.WaitForExit(5000); } catch { } } }

        void StartTor()
        {
            if (IsTorRunning()) return;
            if (!File.Exists(TorExe)) return;
            var tor = new Process(); tor.StartInfo.FileName = TorExe; tor.StartInfo.Arguments = "-f \"" + Torrc + "\""; tor.StartInfo.UseShellExecute = true; tor.StartInfo.WindowStyle = ProcessWindowStyle.Hidden; tor.Start();
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 60000)
            {
                try { using (var c = new TcpClient()) { c.Connect("127.0.0.1", 9050); c.Close(); return; } } catch { }
                Thread.Sleep(500);
            }
        }

        static int ReadExact(Stream ns, byte[] b, int off, int cnt)
        {
            int t = 0; while (t < cnt) { int r = ns.Read(b, off + t, cnt - t); if (r <= 0) break; t += r; } return t;
        }
        string HttpGetViaSocks5(string host, int port, string path)
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    tcp.Connect("127.0.0.1", 9050); tcp.ReceiveTimeout = 15000; tcp.SendTimeout = 10000;
                    var ns = tcp.GetStream();
                    byte[] hs = { 5, 1, 0 }; ns.Write(hs, 0, 3);
                    byte[] resp = new byte[2]; if (ReadExact(ns, resp, 0, 2) != 2 || resp[0] != 5 || resp[1] != 0) return null;
                    byte[] hb = Encoding.ASCII.GetBytes(host);
                    byte[] req = new byte[6 + hb.Length];
                    req[0] = 5; req[1] = 1; req[2] = 0; req[3] = 3; req[4] = (byte)hb.Length;
                    Array.Copy(hb, 0, req, 5, hb.Length);
                    req[req.Length - 2] = (byte)(port >> 8); req[req.Length - 1] = (byte)(port & 0xFF);
                    ns.Write(req, 0, req.Length);
                    byte[] hdr = new byte[4]; if (ReadExact(ns, hdr, 0, 4) != 4) return null;
                    if (hdr[0] != 5 || hdr[1] != 0) return null;
                    int extra = 0; byte atyp = hdr[3];
                    if (atyp == 1) extra = 4; else if (atyp == 4) extra = 16; else if (atyp == 3) { byte[] lb = new byte[1]; if (ReadExact(ns, lb, 0, 1) != 1) return null; extra = lb[0] + 2; }
                    if (extra > 0) { byte[] sk = new byte[extra]; ReadExact(ns, sk, 0, extra); }
                    string http = "GET " + path + " HTTP/1.1\r\nHost: " + host + "\r\nUser-Agent: Torify/1.4\r\nAccept: */*\r\nConnection: close\r\n\r\n";
                    byte[] hb2 = Encoding.ASCII.GetBytes(http); ns.Write(hb2, 0, hb2.Length);
                    using (var ms = new MemoryStream())
                    {
                        byte[] buf = new byte[8192]; int rd;
                        while ((rd = ns.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, rd);
                        string txt = Encoding.ASCII.GetString(ms.ToArray());
                        int he = txt.IndexOf("\r\n\r\n"); if (he < 0) return null;
                        string headers = txt.Substring(0, he); string body = txt.Substring(he + 4);
                        if (headers.ToLower().Contains("transfer-encoding: chunked"))
                        {
                            var sb = new StringBuilder(); int pos = 0;
                            while (pos < body.Length) { int nl = body.IndexOf("\r\n", pos); if (nl < 0) break; string sz = body.Substring(pos, nl - pos).Trim(); int cs; if (!int.TryParse(sz, System.Globalization.NumberStyles.HexNumber, null, out cs)) break; if (cs == 0) break; sb.Append(body.Substring(nl + 2, cs)); pos = nl + 2 + cs + 2; }
                            body = sb.ToString();
                        }
                        body = body.Trim();
                        if (!string.IsNullOrEmpty(body)) return body;
                    }
                }
            }
            catch { }
            return null;
        }
        string GetRealIP()
        {
            try { return new WebClient().DownloadString("https://api.ipify.org").Trim(); } catch { return null; }
        }
        string CheckTorIP()
        {
            string ip = HttpGetViaSocks5("api.ipify.org", 80, "/");
            if (!string.IsNullOrEmpty(ip) && ip.Length >= 7 && ip.Length <= 45) return ip;
            return null;
        }
        void SendNEWNYM()
        {
            double el = (DateTime.Now - _lastNewnym).TotalSeconds;
            if (el < 10 && _lastNewnym != DateTime.MinValue) { Thread.Sleep((int)((10 - el) * 1000)); }
            try
            {
                string script = "$s=New-Object Net.Sockets.TcpClient('127.0.0.1',9051);" +
                    "$w=New-Object IO.StreamWriter($s.GetStream());" +
                    "$r=New-Object IO.StreamReader($s.GetStream());" +
                    "$w.WriteLine('AUTHENTICATE \"\"');$w.Flush();" +
                    "if($r.ReadLine()-match'250'){$w.WriteLine('SIGNAL NEWNYM');$w.Flush();$r.ReadLine()};$w.Close();$r.Close();$s.Close()";
                var p = new Process(); p.StartInfo.FileName = "powershell.exe"; p.StartInfo.Arguments = "-NoProfile -Command \"" + script.Replace("\"", "\\\"") + "\""; p.StartInfo.UseShellExecute = false; p.StartInfo.RedirectStandardOutput = true; p.StartInfo.CreateNoWindow = true; p.Start(); p.WaitForExit(15000);
                _lastNewnym = DateTime.Now;
            }
            catch { }
        }
        string FindTargetApp()
        {
            string cf = Path.Combine(BaseDir, "target-app.txt");
            if (File.Exists(cf)) { string s = File.ReadAllText(cf).Trim(); if (!string.IsNullOrEmpty(s) && File.Exists(s)) return s; }
            return null;
        }
        void LaunchAppViaProxy(string path, bool doNewnym)
        {
            if (string.IsNullOrEmpty(PcExe) || !File.Exists(PcExe)) { FlashLbl(statusLabel, "no proxychains"); return; }
            if (doNewnym) { SendNEWNYM(); Thread.Sleep(1500); }
            try
            {
                var proc = new Process(); proc.StartInfo.FileName = PcExe; proc.StartInfo.Arguments = "-q -f \"" + PcConf + "\" \"" + path + "\""; proc.StartInfo.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); proc.StartInfo.UseShellExecute = true; proc.Start();
            }
            catch (Exception ex) { FlashLbl(statusLabel, "err: " + ex.Message); }
        }
        void AddAppToList(string path)
        {
            var a = LoadApps(); string name = Path.GetFileNameWithoutExtension(path);
            for (int i = 0; i < a.Count; i++) if (a[i].Path.Equals(path, StringComparison.OrdinalIgnoreCase)) { a.RemoveAt(i); break; }
            a.Insert(0, new AppEntry { Name = name, Path = path });
            using (var w = new StreamWriter(AppsFile, false)) foreach (var x in a) w.WriteLine(x.Name + "|" + x.Path);
        }
        List<AppEntry> LoadApps()
        {
            var l = new List<AppEntry>();
            if (!File.Exists(AppsFile)) return l;
            foreach (string line in File.ReadAllLines(AppsFile))
            {
                string t = line.Trim(); if (string.IsNullOrEmpty(t)) continue;
                int sep = t.IndexOf('|');
                if (sep > 0 && sep < t.Length - 1) l.Add(new AppEntry { Name = t.Substring(0, sep), Path = t.Substring(sep + 1) });
            }
            return l;
        }
    }

    class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "torify", MessageBoxButtons.OK); }
        }
    }
}
