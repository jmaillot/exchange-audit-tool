using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Exchange Audit Tool")]
[assembly: System.Reflection.AssemblyProduct("Exchange Audit Tool")]
[assembly: System.Reflection.AssemblyDescription("Modern GUI to run Exchange Online and on-premises audit exports.")]
[assembly: System.Reflection.AssemblyCompany("Prodware")]
[assembly: System.Reflection.AssemblyVersion("1.5.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.5.0.0")]

namespace ExchangeAuditTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e)
            {
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ExchangeAudit");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "ExchangeAuditTool.crash.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + e.Exception + Environment.NewLine + "----" + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ExchangeAudit");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "ExchangeAuditTool.crash.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + e.ExceptionObject + Environment.NewLine + "----" + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            };
            try
            {
                AuditRegistry.BuildAll();
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExchangeAuditTool.startup.log");
                try { File.AppendAllText(path, DateTime.Now + Environment.NewLine + ex + Environment.NewLine, Encoding.UTF8); } catch { }
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ExchangeAudit");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "ExchangeAuditTool.activity.log"),
                        "[" + DateTime.Now.ToString("HH:mm:ss") + "] [startup] " + ex + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
                MessageBox.Show("Exchange Audit Tool could not start." + Environment.NewLine + Environment.NewLine + ex.Message,
                    "Startup error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public sealed partial class MainForm : Form
    {
        private Panel titleBar;
        private readonly Panel modernPageHost = new Panel();
        private readonly Label pageTitle = new Label();
        private readonly Label pageSubtitle = new Label();
        private readonly Panel progressBanner = new Panel();
        private readonly Label footerStatus = new Label();
        private readonly TextBox logBox = new TextBox();
        private readonly ProgressBar progress = new ProgressBar();
        private ModernButton clearLogButton;

        private readonly List<Button> navButtons = new List<Button>();
        private Control[] pages;
        private Rectangle restoreBounds;
        private bool maximized;

        // Collapsible navigation categories.
        private sealed class NavCategory
        {
            public string Name;                 // display text (already upper-cased)
            public Control Header;
            public readonly List<Button> Buttons = new List<Button>();
            public bool Expanded;
            public bool Collapsible;
        }
        private readonly List<NavCategory> navCategories = new List<NavCategory>();
        private Panel navHost;

        // Activity log file. Overwritten (truncated) on every application start,
        // then appended to for the whole session.
        private string logFilePath;

        // ---- Startup window control -------------------------------------------------
        // StartMaximized = true  -> the app opens filling the whole working area
        //                           (recommended; always "large" on any screen).
        // StartMaximized = false -> the app opens as a fixed large window of
        //                           StartupSize, centered and clamped to the screen.
        private static readonly bool StartMaximized = true;
        private static readonly Size StartupSize = new Size(1320, 880);

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public MainForm() { InitializeShell(); }

        private void InitializeShell()
        {
            InitLogFile();
            SuspendLayout();
            Text = "Exchange Audit Tool";
            StartPosition = FormStartPosition.Manual;
            // Kept modest so the window always fits, even on 1366x768 laptops
            // (the real startup size is applied in ApplyStartupWindow()).
            MinimumSize = new Size(960, 640);
            Size = StartupSize;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            BackColor = UiTheme.Window;
            ForeColor = UiTheme.Text;
            ShowInTaskbar = true;
            Icon = BrandAssets.AppIcon(32);
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(1);
            Resize += delegate { ApplyRoundedRegion(); };
            FormClosing += delegate { CloseSession(); };

            titleBar = BuildTitleBar();
            var sidebar = BuildSidebar();
            var workspace = BuildWorkspace();

            Controls.Add(workspace);
            Controls.Add(sidebar);
            Controls.Add(titleBar);

            Shown += delegate
            {
                ApplyStartupWindow();
                AppendLog("Ready. Configure your Exchange connection, then pick an audit section.");
                footerStatus.Text = "Ready";
                SwitchPage(0);
                CheckExoModuleAsync();
            };

            ResumeLayout(true);
        }

        private Panel BuildTitleBar()
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.FromArgb(8, 18, 30) };
            bar.MouseDown += DragWindow;

            var icon = new PictureBox
            {
                Image = UiAssets.Render("mailbox", 22),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(10, 5),
                Size = new Size(24, 24),
                BackColor = bar.BackColor
            };
            icon.MouseDown += DragWindow;

            var title = new Label
            {
                Text = "Exchange Audit Tool",
                Location = new Point(40, 0),
                Size = new Size(320, 34),
                ForeColor = UiTheme.Text,
                Font = new Font("Segoe UI Semibold", 9.5F),
                TextAlign = ContentAlignment.MiddleLeft
            };
            title.MouseDown += DragWindow;

            var close = NewWindowButton("close");
            close.Dock = DockStyle.Right;
            close.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28);
            close.Click += delegate { Close(); };

            var max = NewWindowButton("max");
            max.Dock = DockStyle.Right;
            max.Click += delegate { ToggleMaximize(); };

            var min = NewWindowButton("min");
            min.Dock = DockStyle.Right;
            min.Click += delegate { WindowState = FormWindowState.Minimized; };

            bar.Controls.Add(min);
            bar.Controls.Add(max);
            bar.Controls.Add(close);
            bar.Controls.Add(title);
            bar.Controls.Add(icon);
            return bar;
        }

        private static Button NewWindowButton(string glyph)
        {
            string name = glyph == "close" ? "Close" : glyph == "max" ? "Maximize or restore" : "Minimize";
            var b = new Button
            {
                Width = 46,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(8, 18, 30),
                Image = UiAssets.Render(glyph, 14),
                ImageAlign = ContentAlignment.MiddleCenter,
                TabStop = false,
                AccessibleName = name,
                AccessibleDescription = name + " the Exchange Audit Tool window"
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(27, 41, 58);
            return b;
        }

        // Applies the startup window size reliably AFTER the handle exists, so it is
        // never silently clamped. On small screens a fixed 1320x880 would be cropped
        // (hence the "reduced" look); here we always adapt to the real working area.
        private void ApplyStartupWindow()
        {
            Rectangle wa = Screen.FromControl(this).WorkingArea;

            // Windowed "restore" size = StartupSize, but never larger than the screen.
            int rw = Math.Min(StartupSize.Width, wa.Width - 40);
            int rh = Math.Min(StartupSize.Height, wa.Height - 40);
            rw = Math.Max(MinimumSize.Width, rw);
            rh = Math.Max(MinimumSize.Height, rh);
            restoreBounds = new Rectangle(
                wa.X + (wa.Width - rw) / 2,
                wa.Y + (wa.Height - rh) / 2,
                rw, rh);

            if (StartMaximized)
            {
                // Fill the whole working area (respects the taskbar) - guaranteed "large".
                Bounds = new Rectangle(
                    wa.X + 1, wa.Y + 1,
                    Math.Max(MinimumSize.Width, wa.Width - 2),
                    Math.Max(MinimumSize.Height, wa.Height - 2));
                maximized = true;
            }
            else
            {
                Bounds = restoreBounds;
                maximized = false;
            }
            ApplyRoundedRegion();
        }

        private void ToggleMaximize()
        {
            if (!maximized)
            {
                restoreBounds = Bounds;
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                Bounds = new Rectangle(wa.X + 1, wa.Y + 1, Math.Max(MinimumSize.Width, wa.Width - 2), Math.Max(MinimumSize.Height, wa.Height - 2));
                maximized = true;
            }
            else
            {
                if (restoreBounds.Width > 0) Bounds = restoreBounds;
                maximized = false;
            }
            ApplyRoundedRegion();
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        }

        private Panel BuildSidebar()
        {
            var sidebar = new Panel { Dock = DockStyle.Left, Width = 260, BackColor = UiTheme.Sidebar, Padding = new Padding(12) };

            Image logo = BrandAssets.Logo();
            var brand = new Panel { Dock = DockStyle.Top, Height = logo != null ? 138 : 64, BackColor = UiTheme.Sidebar };
            if (logo != null)
            {
                var brandLogo = new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Padding = new Padding(2, 6, 2, 6), BackColor = UiTheme.Sidebar };
                brand.Controls.Add(brandLogo);
            }
            else
            {
                var brandIcon = new PictureBox { Image = UiAssets.Render("shield", 40), SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(6, 12), Size = new Size(40, 40), BackColor = UiTheme.Sidebar };
                var brandText = new Label { Text = "EXCHANGE\nAUDIT", Location = new Point(54, 12), Size = new Size(170, 44), ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 12F) };
                brand.Controls.Add(brandText);
                brand.Controls.Add(brandIcon);
            }

            navHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Sidebar, AutoScroll = true, Padding = new Padding(0, 4, 0, 4) };

            // CONNECTION: single essential entry, always visible (not collapsible).
            var connCat = new NavCategory { Name = "CONNECTION", Expanded = true, Collapsible = false };
            connCat.Header = NewCategoryHeader(connCat);
            Button connBtn = NewNavButton("Connection", "connect", 0);
            connBtn.Click += delegate { SwitchPage(0); };
            navButtons.Add(connBtn);
            connCat.Buttons.Add(connBtn);
            navHost.Controls.Add(connCat.Header);
            navHost.Controls.Add(connBtn);
            navCategories.Add(connCat);

            var catOrder = new List<string>();
            var byCat = new Dictionary<string, List<int>>();
            for (int i = 0; i < AuditRegistry.Sections.Count; i++)
            {
                string cat = AuditRegistry.Sections[i].Category;
                if (string.IsNullOrEmpty(cat)) cat = "Other";
                if (!byCat.ContainsKey(cat)) { byCat[cat] = new List<int>(); catOrder.Add(cat); }
                byCat[cat].Add(i);
            }

            // Expanded by default: Mailboxes, Groups, Contacts. Others start collapsed.
            var defaultExpanded = new List<string>(new string[] { "Mailboxes", "Groups", "Contacts" });

            foreach (string cat in catOrder)
            {
                var navCat = new NavCategory
                {
                    Name = cat.ToUpperInvariant(),
                    Collapsible = true,
                    Expanded = defaultExpanded.Contains(cat)
                };
                navCat.Header = NewCategoryHeader(navCat);
                navHost.Controls.Add(navCat.Header);

                foreach (int idx in byCat[cat])
                {
                    AuditSection s = AuditRegistry.Sections[idx];
                    Button b = NewNavButton(s.NavTitle, s.IconKey, 0);
                    int pageIndex = idx + 1;
                    b.Click += delegate { SwitchPage(pageIndex); };
                    while (navButtons.Count <= pageIndex) navButtons.Add(null);
                    navButtons[pageIndex] = b;
                    navHost.Controls.Add(b);
                    navCat.Buttons.Add(b);
                }
                navCategories.Add(navCat);
            }

            RelayoutNav();

            footerStatus.Text = "Ready";
            footerStatus.Dock = DockStyle.Bottom;
            footerStatus.Height = 26;
            footerStatus.ForeColor = UiTheme.Green;
            footerStatus.Font = new Font("Segoe UI", 8.5F);
            footerStatus.TextAlign = ContentAlignment.MiddleLeft;

            var version = new Label { Text = "v" + Application.ProductVersion, Dock = DockStyle.Bottom, Height = 20, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8.5F), TextAlign = ContentAlignment.MiddleLeft };
            _optionTip.SetToolTip(version, "Credits: Jérémy MAILLOT - jmaillot@prodware.fr");

            sidebar.Controls.Add(navHost);
            sidebar.Controls.Add(brand);
            sidebar.Controls.Add(version);
            sidebar.Controls.Add(footerStatus);
            return sidebar;
        }

        private Button NewCategoryHeader(NavCategory cat)
        {
            var btn = new Button
            {
                Size = new Size(230, 32),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(66, 138, 247),
                BackColor = UiTheme.Sidebar,
                Font = new Font("Segoe UI Semibold", 8.5F),
                Cursor = cat.Collapsible ? Cursors.Hand : Cursors.Default,
                TabStop = cat.Collapsible,
                UseMnemonic = false
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(17, 34, 53);
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(23, 44, 68);

            if (cat.Collapsible)
                btn.Click += delegate { cat.Expanded = !cat.Expanded; RelayoutNav(); };
            btn.Text = CategoryHeaderText(cat);
            return btn;
        }

        // Chevron triangles (always available in Segoe UI) read cleaner than
        // ASCII [-]/[+] blocks. The sub-section count stays as " (N)".
        private static string CategoryHeaderText(NavCategory cat)
        {
            if (!cat.Collapsible) return cat.Name;
            return (cat.Expanded ? "\u25BE " : "\u25B8 ") + cat.Name + " (" + cat.Buttons.Count + ")";
        }

        private void RelayoutNav()
        {
            if (navHost == null) return;
            navHost.SuspendLayout();
            int y = 0;
            foreach (NavCategory cat in navCategories)
            {
                y += 6;
                if (cat.Header != null)
                {
                    cat.Header.Location = new Point(4, y);
                    cat.Header.Text = CategoryHeaderText(cat);
                }
                y += 34;

                foreach (Button b in cat.Buttons)
                {
                    if (b == null) continue;
                    b.Visible = cat.Expanded;
                    if (cat.Expanded)
                    {
                        b.Location = new Point(8, y);
                        y += 38;
                    }
                }
            }
            navHost.ResumeLayout(true);
        }

        private static Button NewNavButton(string text, string iconKey, int top)
        {
            var b = new Button
            {
                Text = "  " + text,
                Location = new Point(8, top),
                Size = new Size(212, 36),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                ImageAlign = ContentAlignment.MiddleLeft,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = UiAssets.Render(iconKey, 18),
                Padding = new Padding(12, 0, 0, 0),
                Font = new Font("Segoe UI Semibold", 9.5F),
                Cursor = Cursors.Hand,
                BackColor = UiTheme.Sidebar,
                ForeColor = UiTheme.NavText,
                TabStop = true,
                Tag = iconKey
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private Panel BuildWorkspace()
        {
            var workspace = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Window, Padding = new Padding(16, 12, 16, 12) };

            var header = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = UiTheme.Window };
            pageTitle.Dock = DockStyle.Top; pageTitle.Height = 34; pageTitle.Font = new Font("Segoe UI Semibold", 17F); pageTitle.ForeColor = UiTheme.Text;
            pageSubtitle.Dock = DockStyle.Fill; pageSubtitle.Font = new Font("Segoe UI", 8.8F); pageSubtitle.ForeColor = UiTheme.Muted; pageSubtitle.TextAlign = ContentAlignment.TopLeft;
            progressBanner.Dock = DockStyle.Bottom; progressBanner.Height = 3; progressBanner.BackColor = UiTheme.Window;
            header.Controls.Add(pageSubtitle);
            header.Controls.Add(pageTitle);
            header.Controls.Add(progressBanner);

            RoundedPanel logCard = BuildLogCard();

            modernPageHost.Dock = DockStyle.Fill;
            modernPageHost.BackColor = UiTheme.Window;

            var pageList = new List<Control>();
            pageList.Add(BuildConnectionPage());
            foreach (AuditSection s in AuditRegistry.Sections)
                pageList.Add(BuildSectionPage(s));
            pages = pageList.ToArray();

            foreach (Control page in pages)
            {
                page.Dock = DockStyle.Fill;
                page.Visible = false;
                modernPageHost.Controls.Add(page);
            }

            workspace.Controls.Add(modernPageHost);
            workspace.Controls.Add(logCard);
            workspace.Controls.Add(header);
            return workspace;
        }

        private RoundedPanel BuildLogCard()
        {
            var card = new RoundedPanel { Dock = DockStyle.Bottom, Height = 150, BackColor = UiTheme.Surface, CornerRadius = 7, Padding = new Padding(12, 8, 12, 10) };
            var head = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = UiTheme.Surface };
            var title = new Label { Text = "Activity Log", Dock = DockStyle.Left, Width = 180, ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 9.5F), TextAlign = ContentAlignment.MiddleLeft };
            clearLogButton = new ModernButton { Text = "Clear", Dock = DockStyle.Right, Width = 70, Height = 32, Padding = new Padding(0) };
            clearLogButton.Click += delegate { logBox.Clear(); };
            head.Controls.Add(clearLogButton);
            head.Controls.Add(title);

            logBox.Dock = DockStyle.Fill; logBox.Multiline = true; logBox.ScrollBars = ScrollBars.Both; logBox.ReadOnly = true;
            logBox.BorderStyle = BorderStyle.None; logBox.WordWrap = false; logBox.BackColor = Color.FromArgb(6, 15, 26);
            logBox.ForeColor = Color.FromArgb(187, 198, 211); logBox.Font = new Font("Consolas", 8.5F);

            progress.Dock = DockStyle.Bottom; progress.Height = 4; progress.Style = ProgressBarStyle.Marquee; progress.MarqueeAnimationSpeed = 30; progress.Visible = false;

            card.Controls.Add(logBox);
            card.Controls.Add(head);
            card.Controls.Add(progress);
            return card;
        }

        private void SwitchPage(int index)
        {
            if (pages == null || index < 0 || index >= pages.Length) return;
            for (int i = 0; i < pages.Length; i++) pages[i].Visible = i == index;
            pages[index].BringToFront();

            for (int i = 0; i < navButtons.Count; i++) if (navButtons[i] != null) StyleNav(navButtons[i], i == index);

            if (index == 0)
            {
                pageTitle.Text = "Connection";
                pageSubtitle.Text = "Choose how the tool connects to Exchange before running any audit.";
                AcceptButton = null;
                CancelButton = null;
            }
            else
            {
                AuditSection s = AuditRegistry.Sections[index - 1];
                pageTitle.Text = s.Title;
                pageSubtitle.Text = s.Subtitle;
                SectionUi sui;
                if (_sectionUi.TryGetValue(s.Id, out sui))
                {
                    AcceptButton = sui.RunButton;
                    CancelButton = sui.CancelButton;
                }
            }
            progressBanner.BackColor = UiTheme.Window;
        }

        private static void StyleNav(Button b, bool active)
        {
            b.BackColor = active ? UiTheme.Blue : UiTheme.Sidebar;
            b.ForeColor = active ? Color.White : UiTheme.NavText;
            b.FlatAppearance.MouseOverBackColor = active ? UiTheme.BlueHover : Color.FromArgb(17, 34, 53);
            string key = b.Tag as string;
            if (!string.IsNullOrEmpty(key))
            {
                Image old = b.Image;
                b.Image = UiAssets.Render(key, 18, active);
                if (old != null) old.Dispose();
            }
        }

        private void ApplyRoundedRegion()
        {
            if (Width < 8 || Height < 8) return;
            using (var path = RoundedPanel.BuildRoundRect(new Rectangle(0, 0, Width - 1, Height - 1), 9))
                Region = new Region(path);
        }

        // Creates (or truncates) the activity log file for this session. The file is
        // overwritten every time the application starts and appended to afterwards.
        private void InitLogFile()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ExchangeAudit");
                Directory.CreateDirectory(dir);
                logFilePath = Path.Combine(dir, "ExchangeAuditTool.activity.log");
                string header = "==== Exchange Audit Tool - Activity Log ====" + Environment.NewLine +
                                "Session started: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                                "============================================" + Environment.NewLine;
                File.WriteAllText(logFilePath, header, new UTF8Encoding(false));
            }
            catch
            {
                logFilePath = null;
            }
        }

        internal void AppendLog(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(AppendLog), text); return; }
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text;
            logBox.AppendText(line + Environment.NewLine);
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();

            if (logFilePath != null)
            {
                try { File.AppendAllText(logFilePath, line + Environment.NewLine, new UTF8Encoding(false)); }
                catch { }
            }
        }

        internal void SetFooter(string text, Color color)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string, Color>(SetFooter), text, color); return; }
            footerStatus.Text = text;
            footerStatus.ForeColor = color;
        }

        internal void SetBusy(bool busy)
        {
            if (InvokeRequired) { BeginInvoke(new Action<bool>(SetBusy), busy); return; }
            progress.Visible = busy;
            progress.Style = ProgressBarStyle.Marquee;
            UseWaitCursor = busy;
        }
    }
}
