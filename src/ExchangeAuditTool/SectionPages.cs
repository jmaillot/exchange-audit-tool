using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExchangeAuditTool
{
    public sealed partial class MainForm
    {
        private readonly PowerShellSession _ps = new PowerShellSession();

        // Shared tooltip used to reveal the full text of options whose label is
        // truncated when the column is too narrow.
        private readonly ToolTip _optionTip = new ToolTip
        {
            AutoPopDelay = 15000,
            InitialDelay = 400,
            ReshowDelay = 150,
            ShowAlways = true
        };

        private Label _exoModuleStatus;
        private Label _connStatus;
        private Label _connectedAs;
        private bool _logCommands = true;

        private void LogCommand(string title, string script)
        {
            if (!_logCommands || string.IsNullOrEmpty(script)) return;
            script = RedactSecrets(script);
            AppendLog("---- " + title + " ----");
            foreach (string raw in script.Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Trim().Length == 0) continue;
                AppendLog("PS> " + line);
            }
            AppendLog("--------");
        }

        private static string RedactSecrets(string script)
        {
            if (string.IsNullOrEmpty(script)) return script;
            string redacted = script;
            redacted = Regex.Replace(redacted, "(-UserPrincipalName\\s+)(('[^']*')|(\"[^\"]*\")|(\\S+))", "$1***", RegexOptions.IgnoreCase);
            redacted = Regex.Replace(redacted, "(-AppId\\s+)(('[^']*')|(\"[^\"]*\")|(\\S+))", "$1***", RegexOptions.IgnoreCase);
            redacted = Regex.Replace(redacted, "(-Organization\\s+)(('[^']*')|(\"[^\"]*\")|(\\S+))", "$1***", RegexOptions.IgnoreCase);
            redacted = Regex.Replace(redacted, "(-CertificateThumbprint\\s+)(('[^']*')|(\"[^\"]*\")|(\\S+))", "$1***", RegexOptions.IgnoreCase);
            redacted = Regex.Replace(redacted, "(\\$uri\\s*=\\s*)(('[^']*')|(\"[^\"]*\")|(\\S+))", "$1'***'", RegexOptions.IgnoreCase);
            return redacted;
        }

        internal static bool IsValidUpn(string upn)
        {
            if (string.IsNullOrEmpty(upn) || upn.IndexOf(' ') >= 0) return false;
            int at = upn.IndexOf('@');
            if (at <= 0 || at != upn.LastIndexOf('@') || at == upn.Length - 1) return false;
            return upn.IndexOf('.', at) > at + 1 && upn.Length - upn.LastIndexOf('.') > 2;
        }

        internal static bool IsValidThumbprint(string thumb)
        {
            if (string.IsNullOrEmpty(thumb) || thumb.Length != 40) return false;
            foreach (char c in thumb)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        internal static bool IsValidHostname(string host)
        {
            if (string.IsNullOrEmpty(host) || host.Length < 3 || host.Length > 253) return false;
            if (host.IndexOf(' ') >= 0 || host.IndexOf('/') >= 0 || host.IndexOf('\\') >= 0) return false;
            foreach (char c in host)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') || c == '-' || c == '.';
                if (!ok) return false;
            }
            return host.IndexOf('.') > 0;
        }

        internal static bool IsValidCsvPath(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return false;
            if (csv.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            string file;
            try { file = Path.GetFileName(csv); }
            catch { return false; }
            if (string.IsNullOrEmpty(file) || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            return csv.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class SectionUi
        {
            public AuditSection Section;
            public Dictionary<string, List<CheckBox>> Checks = new Dictionary<string, List<CheckBox>>();
            public Dictionary<string, List<RadioButton>> Radios = new Dictionary<string, List<RadioButton>>();
            public TextBox OutputPath;
            public ModernButton RunButton;
            public ModernButton CancelButton;
            public ModernButton OpenButton;
            public DataGridView Grid;
            public Label ResultInfo;
            public Label EmptyState;
            public string LastCsv;
            public string Filter;
            public PowerShellSession Ps;
            public CancellationTokenSource RunCts;
            public PowerShellSession ActiveSession;
            public CheckBox XlsxBox;
            public ModernButton OpenXlsxButton;
            public string LastXlsx;
        }

        private readonly Dictionary<string, SectionUi> _sectionUi = new Dictionary<string, SectionUi>();

        // Parallel audits: each section runs on its own powershell.exe process
        // (PowerShellSession is process-isolated), gated by a global slot count
        // so Exchange throttling stays out of the picture. The shared _ps
        // remains the control session (connect/disconnect/module checks).
        private const int MaxParallelAudits = 3;
        private readonly SemaphoreSlim _runSlots = new SemaphoreSlim(MaxParallelAudits, MaxParallelAudits);
        private int _runningAudits;

        // Prompt modes (interactive sign-in, Basic/credential dialog) run audits
        // on the connected control session, serialized: exactly one sign-in,
        // then every audit reuses it. A separate process could never see the
        // control session's connection, so it would prompt again.
        private readonly SemaphoreSlim _serialAuditGate = new SemaphoreSlim(1, 1);

        // Worker sign-in is serialized: the first worker prompts (browser /
        // credential dialog) and follow-ups reuse the cached token, so parallel
        // audits cost a single sign-in instead of one per session.
        private readonly SemaphoreSlim _connectGate = new SemaphoreSlim(1, 1);

        private Control BuildConnectionPage()
        {
            var page = new Panel { BackColor = UiTheme.Window, Padding = new Padding(0, 0, 0, 10), AutoScroll = true };
            var card = new RoundedPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = UiTheme.Surface, CornerRadius = 8, Padding = new Padding(18, 14, 18, 14) };

            var head = NewSectionHeader("connect", "Exchange connection", "Connect once - the session stays open until you close the app.");

            var modeGroup = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = UiTheme.Surface, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(0, 4, 0, 4) };
            var rInteractive = NewRadio("Exchange Online (interactive)", true);
            var rApp = NewRadio("Exchange Online (app-only cert)", false);
            var rLocal = NewRadio("On-premises (run on Exchange server)", false);
            var rRemote = NewRadio("On-premises (remote PowerShell)", false);
            rInteractive.Margin = new Padding(0, 4, 16, 4);
            rApp.Margin = new Padding(0, 4, 16, 4);
            rLocal.Margin = new Padding(0, 4, 16, 4);
            rRemote.Margin = new Padding(0, 4, 0, 4);
            modeGroup.Controls.Add(rInteractive); modeGroup.Controls.Add(rApp);
            modeGroup.Controls.Add(rLocal); modeGroup.Controls.Add(rRemote);

            var moduleRow = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = UiTheme.Surface };
            var moduleLabel = new Label { Text = "EXO module:", Dock = DockStyle.Left, Width = 90, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 8.8F) };
            _exoModuleStatus = new Label { Text = "checking...", Dock = DockStyle.Left, Width = 300, ForeColor = UiTheme.Orange, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 8.8F) };
            var checkBtn = new ModernButton { Text = "Re-check", Dock = DockStyle.Right, Width = 96, Height = 32, Padding = new Padding(0) };
            checkBtn.Click += delegate { CheckExoModuleAsync(); };
            moduleRow.Controls.Add(_exoModuleStatus);
            moduleRow.Controls.Add(moduleLabel);
            moduleRow.Controls.Add(checkBtn);

            var connectedRow = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = UiTheme.Surface };
            var connectedLabel = new Label { Text = "Connected as:", Dock = DockStyle.Left, Width = 90, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 8.8F) };
            _connectedAs = new Label { Text = "✗ not connected", Dock = DockStyle.Left, Width = 420, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 8.8F) };
            connectedRow.Controls.Add(_connectedAs);
            connectedRow.Controls.Add(connectedLabel);

            var logCmdRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = UiTheme.Surface, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(0, 4, 0, 4) };
            var cbLogCmd = new CheckBox { Text = "Log executed PowerShell commands in the Activity Log", Checked = _logCommands, AutoSize = true, ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 8.8F), Margin = new Padding(0, 0, 0, 0) };
            cbLogCmd.CheckedChanged += delegate { _logCommands = cbLogCmd.Checked; };
            logCmdRow.Controls.Add(cbLogCmd);

            var tbUpn = NewField();
            var tbAppId = NewField();
            var tbOrg = NewField();
            var tbThumb = NewField();

            var rowUpn = NewLabeledRow("User principal name", tbUpn);

            var rowDevice = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = UiTheme.Surface, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(150, 4, 0, 4) };
            var cbDevice = new CheckBox { Text = "Open external browser for sign-in (disable WAM) - recommended", Checked = ConnectionSettings.DisableWam, AutoSize = true, ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 8.8F), Margin = new Padding(0, 0, 0, 0) };
            rowDevice.Controls.Add(cbDevice);

            var rowAppId = NewLabeledRow("Application (client) ID", tbAppId);
            var rowOrg = NewLabeledRow("Tenant (organization)", tbOrg);
            var rowThumb = NewLabeledRow("Certificate thumbprint", tbThumb);
            var rowLocalInfo = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = UiTheme.Surface };
            var localInfo = new Label { Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8.5F), TextAlign = ContentAlignment.MiddleLeft,
                Text = "Run the tool on the Exchange server. It loads RemoteExchange.ps1 (2013/2016/2019) or the E2010 snap-in automatically - no fields needed." };
            rowLocalInfo.Controls.Add(localInfo);

            // ---- Remote on-premises (implicit remoting) fields --------------------------
            var tbRemoteServer = NewField();
            var tbRemoteUser = NewField();
            var rowRemoteServer = NewLabeledRow("Exchange server (FQDN)", tbRemoteServer);
            var rowRemoteUser = NewLabeledRow("Username (optional)", tbRemoteUser);

            var rowRemoteAuth = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = UiTheme.Surface, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(0, 4, 0, 4) };
            var authLabel = new Label { Text = "Authentication", Width = 150, Height = 24, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 8.8F), Margin = new Padding(0, 4, 0, 4) };
            var rKerb = new RadioButton { Text = "Kerberos (domain-joined)", Checked = true, AutoSize = true, ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 8.8F), Margin = new Padding(4, 6, 16, 4) };
            var rBasic = new RadioButton { Text = "Basic (off-domain)", Checked = false, AutoSize = true, ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 8.8F), Margin = new Padding(0, 6, 16, 4) };
            var cbHttps = new CheckBox { Text = "Use HTTPS", Checked = false, AutoSize = true, ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 8.8F), Margin = new Padding(0, 6, 0, 4) };
            rowRemoteAuth.Controls.Add(cbHttps);
            rowRemoteAuth.Controls.Add(rBasic);
            rowRemoteAuth.Controls.Add(rKerb);
            rowRemoteAuth.Controls.Add(authLabel);

            var rowRemoteInfo = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = UiTheme.Surface };
            var remoteInfo = new Label { Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8.5F), TextAlign = ContentAlignment.MiddleLeft,
                Text = "Opens a remote session to http(s)://<server>/PowerShell/ and imports the cmdlets. Kerberos uses your identity; Basic prompts securely (use HTTPS)." };
            rowRemoteInfo.Controls.Add(remoteInfo);

            // Keep Basic and HTTPS in sync (Basic should be used over HTTPS).
            rBasic.CheckedChanged += delegate { if (rBasic.Checked) cbHttps.Checked = true; };

            var fields = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = UiTheme.Surface, Padding = new Padding(0, 8, 0, 0) };
            fields.Controls.Add(rowLocalInfo);
            fields.Controls.Add(rowRemoteInfo);
            fields.Controls.Add(rowRemoteAuth);
            fields.Controls.Add(rowRemoteUser);
            fields.Controls.Add(rowRemoteServer);
            fields.Controls.Add(rowThumb);
            fields.Controls.Add(rowOrg);
            fields.Controls.Add(rowAppId);
            fields.Controls.Add(rowDevice);
            fields.Controls.Add(rowUpn);

            Action applyMode = delegate
            {
                bool interactive = rInteractive.Checked;
                bool app = rApp.Checked;
                bool local = rLocal.Checked;
                bool remote = rRemote.Checked;
                rowUpn.Visible = interactive;
                rowDevice.Visible = interactive;
                rowAppId.Visible = app; rowOrg.Visible = app; rowThumb.Visible = app;
                rowLocalInfo.Visible = local;
                rowRemoteServer.Visible = remote;
                rowRemoteUser.Visible = remote;
                rowRemoteAuth.Visible = remote;
                rowRemoteInfo.Visible = remote;
            };
            rInteractive.CheckedChanged += delegate { applyMode(); };
            rApp.CheckedChanged += delegate { applyMode(); };
            rLocal.CheckedChanged += delegate { applyMode(); };
            rRemote.CheckedChanged += delegate { applyMode(); };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = UiTheme.Surface, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(0, 8, 0, 0) };
            var save = new ModernButton { Text = "Connect", Width = 160, Height = 36, Margin = new Padding(0, 0, 8, 8) };
            save.NormalColor = UiTheme.Blue; save.BackColor = UiTheme.Blue; save.ForeColor = Color.White;
            var installMod = new ModernButton { Text = "Install EXO module", Width = 170, Height = 36, Margin = new Padding(0, 0, 8, 8) };
            var disconnectBtn = new ModernButton { Text = "Disconnect", Width = 120, Height = 36, Margin = new Padding(0, 0, 8, 8) };
            _connStatus = new Label { Text = ConnectionSettings.Summary(), AutoSize = true, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8.5F), Margin = new Padding(8, 10, 0, 8) };
            buttons.Controls.Add(save); buttons.Controls.Add(installMod); buttons.Controls.Add(disconnectBtn); buttons.Controls.Add(_connStatus);

            save.Click += async delegate
            {
                if (_ps.IsAlive)
                {
                    AppendLog("Closing previous session before applying new settings...");
                    await RunPowerShellCaptureAsync(ConnectionSettings.BuildDisconnect());
                }

                string upn = tbUpn.Text.Trim();
                string appId = tbAppId.Text.Trim();
                string org = tbOrg.Text.Trim();
                string thumb = tbThumb.Text.Trim().Replace(" ", "").Replace(":", "");
                string remoteServer = tbRemoteServer.Text.Trim();
                string remoteUser = tbRemoteUser.Text.Trim();

                if (rInteractive.Checked && upn.Length > 0 && !IsValidUpn(upn))
                {
                    Warn("That UPN does not look like an email address (expected user@domain).");
                    tbUpn.Focus();
                    tbUpn.SelectAll();
                    return;
                }
                if (rApp.Checked)
                {
                    Guid parsedAppId;
                    if (string.IsNullOrEmpty(appId) || !Guid.TryParse(appId, out parsedAppId))
                    {
                        Warn("Enter a valid Application (client) ID (GUID).");
                        tbAppId.Focus();
                        tbAppId.SelectAll();
                        return;
                    }
                    if (string.IsNullOrEmpty(org) || org.IndexOf('.') < 0 || org.IndexOf(' ') >= 0)
                    {
                        Warn("Enter the tenant organization (e.g. contoso.onmicrosoft.com).");
                        tbOrg.Focus();
                        tbOrg.SelectAll();
                        return;
                    }
                    if (!IsValidThumbprint(thumb))
                    {
                        Warn("Enter a valid 40-character certificate thumbprint (hex).");
                        tbThumb.Focus();
                        tbThumb.SelectAll();
                        return;
                    }
                }
                if (rRemote.Checked)
                {
                    if (string.IsNullOrEmpty(remoteServer) || !IsValidHostname(remoteServer))
                    {
                        Warn("Enter the Exchange server FQDN for the remote PowerShell connection.");
                        tbRemoteServer.Focus();
                        tbRemoteServer.SelectAll();
                        return;
                    }
                }

                ConnectionSettings.Mode = rInteractive.Checked ? ConnectionMode.ExchangeOnlineInteractive
                                        : rApp.Checked ? ConnectionMode.ExchangeOnlineApp
                                        : rLocal.Checked ? ConnectionMode.OnPremisesLocal
                                        : ConnectionMode.OnPremisesRemote;
                ConnectionSettings.Upn = upn;
                ConnectionSettings.DisableWam = cbDevice.Checked;
                ConnectionSettings.AppId = appId;
                ConnectionSettings.Organization = org;
                ConnectionSettings.CertThumbprint = thumb;
                ConnectionSettings.RemoteServer = remoteServer;
                ConnectionSettings.RemoteUser = remoteUser;
                ConnectionSettings.RemoteAuth = rBasic.Checked ? RemoteAuthMode.Basic : RemoteAuthMode.Kerberos;
                ConnectionSettings.RemoteUseHttps = cbHttps.Checked;

                _connStatus.Text = ConnectionSettings.Summary();

                SetFooter("Connecting...", UiTheme.Orange);
                SetBusy(true);
                AppendLog("Establishing connection: " + ConnectionSettings.Summary());
                string connectScript = ConnectionSettings.BuildPrelude() + "Write-Host 'Connection ready.'";
                LogCommand("Connect - PowerShell", connectScript);
                var r = await RunPowerShellStreamingAsync(connectScript, 300000);
                SetBusy(false);
                bool ok = r.ExitCode == 0;
                SetFooter(ok ? "Connected" : "Connection failed", ok ? UiTheme.Green : UiTheme.Red);
                if (ok)
                {
                    ApplyScopeDefaults(ConnectionSettings.IsOnline);
                    await RefreshConnectedAsAsync();
                }
                else SetConnectedAs(null);
            };

            installMod.Click += async delegate
            {
                AppendLog("Installing ExchangeOnlineManagement module for current user...");
                SetBusy(true);
                string installScript = "Install-Module ExchangeOnlineManagement -Scope CurrentUser -Force -AllowClobber; " +
                    "Import-Module ExchangeOnlineManagement; Write-Host ('Installed ' + (Get-Module ExchangeOnlineManagement).Version.ToString())";
                LogCommand("Install EXO module - PowerShell", installScript);
                var r = await RunPowerShellCaptureAsync(installScript);
                SetBusy(false);
                AppendLog(r.Output);
                SetFooter(r.ExitCode == 0 ? "EXO module ready" : "Module install failed", r.ExitCode == 0 ? UiTheme.Green : UiTheme.Red);
                CheckExoModuleAsync();
            };

            disconnectBtn.Click += async delegate
            {
                SetBusy(true);
                AppendLog("Disconnecting...");
                string disc = ConnectionSettings.BuildDisconnect();
                LogCommand("Disconnect - PowerShell", disc);
                var r = await RunPowerShellCaptureAsync(disc);
                SetBusy(false);
                AppendLog(r.Output);
                SetFooter("Disconnected", UiTheme.Muted);
                SetConnectedAs(null);
            };

            applyMode();
            card.Controls.Add(buttons);
            card.Controls.Add(fields);
            card.Controls.Add(logCmdRow);
            card.Controls.Add(connectedRow);
            card.Controls.Add(moduleRow);
            card.Controls.Add(modeGroup);
            card.Controls.Add(head);
            page.Controls.Add(card);
            return page;
        }

        private async void CheckExoModuleAsync()
        {
            if (_exoModuleStatus == null) return;
            _exoModuleStatus.Text = "checking...";
            _exoModuleStatus.ForeColor = UiTheme.Orange;
            try
            {
                var r = await RunPowerShellCaptureAsync(ConnectionSettings.BuildModuleCheck());
                string outp = (r.Output ?? "").Trim();
            if (outp.IndexOf("INSTALLED", StringComparison.OrdinalIgnoreCase) >= 0 && outp.IndexOf("NOTINSTALLED", StringComparison.OrdinalIgnoreCase) < 0)
            {
                string ver = outp.Replace("INSTALLED", "").Trim();
                _exoModuleStatus.Text = "installed" + (ver.Length > 0 ? " (v" + ver + ")" : "");
                _exoModuleStatus.ForeColor = UiTheme.Green;
            }
            else
            {
                _exoModuleStatus.Text = "not installed - use \"Install EXO module\"";
                _exoModuleStatus.ForeColor = UiTheme.Red;
            }
            }
            catch (Exception ex)
            {
                _exoModuleStatus.Text = "check failed - retry";
                _exoModuleStatus.ForeColor = UiTheme.Red;
                AppendLog("[exo-module-check] " + ex.Message);
            }
        }

        private void ApplyScopeDefaults(bool online)
        {
            foreach (var kv in _sectionUi)
            {
                SectionUi ui = kv.Value;
                if (ui.Section == null || !ui.Section.ScopeAwareDefaults) continue;
                foreach (var g in ui.Checks)
                    foreach (CheckBox cb in g.Value)
                    {
                        var opt = cb.Tag as AuditOption;
                        if (opt != null) cb.Checked = online ? opt.DefOnline : opt.DefOnPrem;
                    }
            }
        }

        private async Task RefreshConnectedAsAsync()
        {
            SetConnectedAs("checking...");
            var r = await RunPowerShellCaptureAsync(ConnectionSettings.BuildConnectedAsCheck());
            string outp = (r.Output ?? "").Trim();
            int idx = outp.IndexOf("CONNECTEDAS", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string who = outp.Substring(idx + "CONNECTEDAS".Length).Trim();
                int nl = who.IndexOfAny(new char[] { '\r', '\n' });
                if (nl >= 0) who = who.Substring(0, nl).Trim();
                SetConnectedAs(who.Length > 0 ? who : "connected");
            }
            else SetConnectedAs(null);
        }

        private bool _isConnected;

        private void SetConnectedAs(string who)
        {
            if (_connectedAs == null) return;
            if (string.IsNullOrEmpty(who)) { _connectedAs.Text = "✗ not connected"; _connectedAs.ForeColor = UiTheme.Muted; _isConnected = false; }
            else if (who == "checking...") { _connectedAs.Text = "… checking..."; _connectedAs.ForeColor = UiTheme.Orange; }
            else { _connectedAs.Text = "✓ " + who; _connectedAs.ForeColor = UiTheme.Green; _isConnected = true; }
            SetCanRun(_isConnected);
        }

        private void SetCanRun(bool connected)
        {
            foreach (var kv in _sectionUi)
            {
                if (kv.Value.RunButton == null) continue;
                kv.Value.RunButton.Enabled = connected;
                _optionTip.SetToolTip(kv.Value.RunButton, connected ? "Run the audit and export CSV" : "Connect to Exchange first");
            }
        }

        private Control BuildSectionPage(AuditSection section)
        {
            var ui = new SectionUi { Section = section };
            _sectionUi[section.Id] = ui;

            var page = new Panel { BackColor = UiTheme.Window, Padding = new Padding(0, 0, 0, 10) };

            // NOTE: do NOT set Panel1MinSize/Panel2MinSize here. At construction the
            // splitter is still its 200px default, so large min sizes make the
            // first layout pass throw (SplitterDistance range check) and kill
            // startup. Minimums are enforced by clamping in the handler below.
            var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = UiTheme.Window, SplitterWidth = 6 };
            var leftColumn = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Window, Padding = new Padding(0, 0, 8, 0) };
            var rightColumn = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Window, Padding = new Padding(8, 0, 0, 0) };
            split.Panel1.Controls.Add(leftColumn);
            split.Panel2.Controls.Add(rightColumn);

            var optionsCard = new RoundedPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, CornerRadius = 8, Padding = new Padding(16, 14, 16, 14), AutoScroll = true };

            var headerRow = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = UiTheme.Surface };
            var scopeBadge = new Label { Text = ScopeText(section.Scope), Dock = DockStyle.Left, Width = 240, ForeColor = UiTheme.Orange, Font = new Font("Segoe UI Semibold", 8F), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            _optionTip.SetToolTip(scopeBadge, scopeBadge.Text);
            var selectAllBtn = new ModernButton { Text = "Select all", Dock = DockStyle.Right, Width = 130, Height = 32, Padding = new Padding(0) };
            Func<CheckBox, bool> inScope = delegate (CheckBox cb)
            {
                if (string.IsNullOrEmpty(ui.Filter)) return true;
                var opt = cb.Tag as AuditOption;
                string hay = ((opt != null ? opt.Label : cb.Text) ?? "").ToLowerInvariant();
                return hay.Contains(ui.Filter);
            };
            Action updateSelectAll = delegate
            {
                int total = 0;
                int on = 0;
                foreach (var kv in ui.Checks)
                    foreach (CheckBox cb in kv.Value)
                    {
                        if (!inScope(cb)) continue;
                        total++;
                        if (cb.Checked) on++;
                    }
                if (total > 0 && on == total) selectAllBtn.Text = "Deselect all";
                else if (on == 0) selectAllBtn.Text = "Select all";
                else selectAllBtn.Text = "Select all (" + on + "/" + total + ")";
            };
            selectAllBtn.Click += delegate
            {
                bool anyUnchecked = false;
                foreach (var kv in ui.Checks)
                    foreach (CheckBox cb in kv.Value)
                        if (inScope(cb) && !cb.Checked) { anyUnchecked = true; break; }
                bool target = anyUnchecked;
                foreach (var kv in ui.Checks)
                    foreach (CheckBox cb in kv.Value)
                        if (inScope(cb)) cb.Checked = target;
                updateSelectAll();
            };
            headerRow.Controls.Add(selectAllBtn);
            headerRow.Controls.Add(scopeBadge);

            var groupsHost = new Panel { Dock = DockStyle.Top, BackColor = UiTheme.Surface, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            var built = new List<Control>();
            foreach (AuditOptionGroup grp in section.Groups)
                built.Add(BuildOptionGroup(ui, grp));
            for (int i = built.Count - 1; i >= 0; i--) groupsHost.Controls.Add(built[i]);

            var filterRow = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = UiTheme.Surface };
            var filterLabel = new Label { Text = "Filter", Dock = DockStyle.Left, Width = 60, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 8.8F) };
            var filterHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 7, 0, 7), BackColor = filterRow.BackColor };
            var tbFilter = NewField();
            tbFilter.Dock = DockStyle.Fill;
            filterHost.Controls.Add(tbFilter);
            filterRow.Controls.Add(filterHost);
            filterRow.Controls.Add(filterLabel);
            Action applyFilter = delegate
            {
                string f = (tbFilter.Text ?? "").Trim().ToLowerInvariant();
                if (f.Length == 0)
                {
                    foreach (var kv in ui.Checks)
                        foreach (CheckBox cb in kv.Value) cb.ForeColor = UiTheme.Text;
                    foreach (var kv in ui.Radios)
                        foreach (RadioButton rb in kv.Value) rb.ForeColor = UiTheme.Text;
                    foreach (Control card in built) card.Visible = true;
                    return;
                }
                foreach (var kv in ui.Checks)
                    foreach (CheckBox cb in kv.Value)
                    {
                        var opt = cb.Tag as AuditOption;
                        string hay = ((opt != null ? opt.Label : cb.Text) ?? "").ToLowerInvariant();
                        cb.ForeColor = hay.Contains(f) ? UiTheme.Text : UiTheme.Muted;
                    }
                foreach (var kv in ui.Radios)
                    foreach (RadioButton rb in kv.Value)
                    {
                        var opt = rb.Tag as AuditOption;
                        string hay = ((opt != null ? opt.Label : rb.Text) ?? "").ToLowerInvariant();
                        rb.ForeColor = hay.Contains(f) ? UiTheme.Text : UiTheme.Muted;
                    }
                foreach (Control card in built)
                {
                    bool anyMatch = false;
                    foreach (Control inner in card.Controls)
                    {
                        var table = inner as TableLayoutPanel;
                        if (table == null) continue;
                        foreach (Control c in table.Controls)
                        {
                            var cb = c as CheckBox;
                            if (cb != null)
                            {
                                var opt = cb.Tag as AuditOption;
                                string hay = ((opt != null ? opt.Label : cb.Text) ?? "").ToLowerInvariant();
                                if (hay.Contains(f)) { anyMatch = true; break; }
                                continue;
                            }
                            var rb = c as RadioButton;
                            if (rb != null)
                            {
                                var opt = rb.Tag as AuditOption;
                                string hay = ((opt != null ? opt.Label : rb.Text) ?? "").ToLowerInvariant();
                                if (hay.Contains(f)) { anyMatch = true; break; }
                            }
                        }
                        if (anyMatch) break;
                    }
                    card.Visible = anyMatch;
                }
            };
            tbFilter.TextChanged += delegate
            {
                ui.Filter = (tbFilter.Text ?? "").Trim().ToLowerInvariant();
                applyFilter();
                updateSelectAll();
            };
            _optionTip.SetToolTip(tbFilter, "Type to highlight matching options; groups without matches are hidden");
            _optionTip.SetToolTip(selectAllBtn, "Toggles options matching the current filter, or every option when no filter is typed");

            optionsCard.Controls.Add(groupsHost);
            optionsCard.Controls.Add(filterRow);
            optionsCard.Controls.Add(headerRow);

            var outputRow = NewLabeledRow("Output CSV", null);
            ui.OutputPath = (TextBox)outputRow.Tag;
            ui.OutputPath.Text = Path.Combine(DefaultExportDir(), section.DefaultFileName);
            var browse = new ModernButton { Text = "Browse", Dock = DockStyle.Right, Width = 88, Height = 34, Padding = new Padding(0) };
            browse.Click += delegate
            {
                using (var dlg = new SaveFileDialog { Filter = "CSV file (*.csv)|*.csv", DefaultExt = "csv", AddExtension = true, FileName = section.DefaultFileName })
                    if (dlg.ShowDialog(this) == DialogResult.OK) ui.OutputPath.Text = dlg.FileName;
            };
            ui.XlsxBox = new CheckBox { Text = "XLSX", Checked = true, Dock = DockStyle.Right, Width = 62, ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 8.8F) };
            _optionTip.SetToolTip(ui.XlsxBox, "Also save an .xlsx workbook (bold header, filter, frozen top row) next to the CSV");
            outputRow.Controls.Add(ui.XlsxBox);
            outputRow.Controls.Add(browse);
            outputRow.Dock = DockStyle.Bottom;

            ui.RunButton = new ModernButton { Text = "RUN AUDIT", Dock = DockStyle.Fill, Height = 42, Enabled = false };
            ui.RunButton.NormalColor = UiTheme.Blue; ui.RunButton.BackColor = UiTheme.Blue; ui.RunButton.ForeColor = Color.White;
            ui.RunButton.Click += async delegate { await RunSectionAsync(ui); };
            _optionTip.SetToolTip(ui.RunButton, "Connect to Exchange first");
            ui.CancelButton = new ModernButton { Text = "Cancel", Dock = DockStyle.Right, Width = 110, Height = 42, Enabled = false };
            ui.CancelButton.Click += delegate
            {
                AppendLog("Cancellation requested for " + ui.Section.NavTitle + "...");
                if (ui.RunCts != null) { try { ui.RunCts.Cancel(); } catch { } }
                PowerShellSession active = ui.ActiveSession;
                if (active != null && active.IsBusy) active.Cancel();
            };
            var buttonsRow = new Panel { Dock = DockStyle.Bottom, Height = 42, BackColor = UiTheme.Window };
            buttonsRow.Controls.Add(ui.RunButton);
            buttonsRow.Controls.Add(ui.CancelButton);

            var slowHint = new Label { Text = "Slow options selected - this run may take much longer.", Dock = DockStyle.Bottom, Height = 24, ForeColor = UiTheme.Orange, Font = new Font("Segoe UI", 8.5F), TextAlign = ContentAlignment.MiddleLeft, Visible = false };

            leftColumn.Controls.Add(optionsCard);
            leftColumn.Controls.Add(outputRow);
            leftColumn.Controls.Add(slowHint);
            leftColumn.Controls.Add(buttonsRow);

            foreach (var kv in ui.Checks)
                foreach (CheckBox cb in kv.Value)
                    cb.CheckedChanged += delegate { slowHint.Visible = HasSlowOptions(ui); updateSelectAll(); };
            slowHint.Visible = HasSlowOptions(ui);
            updateSelectAll();

            var resultsCard = new RoundedPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, CornerRadius = 8, Padding = new Padding(12, 10, 12, 12) };
            var rHead = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = UiTheme.Surface };
            var rTitle = new Label { Text = "Results preview", Dock = DockStyle.Left, Width = 160, ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 9.5F), TextAlign = ContentAlignment.MiddleLeft };
            ui.OpenButton = new ModernButton { Text = "Open CSV", Dock = DockStyle.Right, Width = 96, Height = 32, Padding = new Padding(0), Enabled = false };
            ui.OpenButton.Click += delegate { if (!string.IsNullOrEmpty(ui.LastCsv) && File.Exists(ui.LastCsv)) OpenPath(ui.LastCsv); };
            ui.OpenXlsxButton = new ModernButton { Text = "XLSX", Dock = DockStyle.Right, Width = 64, Height = 32, Padding = new Padding(0), Enabled = false };
            ui.OpenXlsxButton.Click += delegate { if (!string.IsNullOrEmpty(ui.LastXlsx) && File.Exists(ui.LastXlsx)) OpenPath(ui.LastXlsx); };
            _optionTip.SetToolTip(ui.OpenXlsxButton, "Open the .xlsx workbook");
            var folderBtn = new ModernButton { Text = "Folder", Dock = DockStyle.Right, Width = 70, Height = 32, Padding = new Padding(0) };
            folderBtn.Click += delegate
            {
                if (string.IsNullOrEmpty(ui.LastCsv)) return;
                try
                {
                    string dir = Path.GetDirectoryName(ui.LastCsv);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) OpenPath(dir);
                }
                catch { }
            };
            rHead.Controls.Add(ui.OpenXlsxButton);
            rHead.Controls.Add(ui.OpenButton);
            rHead.Controls.Add(folderBtn);
            rHead.Controls.Add(rTitle);

            ui.ResultInfo = new Label { Text = "No results yet.", Dock = DockStyle.Top, Height = 22, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8.5F), AutoEllipsis = true };

            ui.Grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.FromArgb(6, 15, 26),
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false,
                GridColor = UiTheme.Border,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                AllowUserToOrderColumns = true,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
            };
            ui.Grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.Surface2;
            ui.Grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.Text;
            ui.Grid.DefaultCellStyle.BackColor = Color.FromArgb(8, 18, 30);
            ui.Grid.DefaultCellStyle.ForeColor = UiTheme.Text;
            ui.Grid.DefaultCellStyle.SelectionBackColor = UiTheme.Blue;
            ui.Grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            // Reveal full cell content (long SMTP addresses, permission lists, etc.) on hover.
            ui.Grid.ShowCellToolTips = true;

            resultsCard.Controls.Add(ui.Grid);
            resultsCard.Controls.Add(ui.ResultInfo);
            resultsCard.Controls.Add(rHead);
            ui.EmptyState = new Label { Text = "No results yet." + Environment.NewLine + "Pick options and press RUN AUDIT.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 10F), BackColor = Color.FromArgb(6, 15, 26), Cursor = Cursors.Hand };
            ui.EmptyState.Click += delegate { if (ui.RunButton.Enabled) ui.RunButton.Focus(); };
            _optionTip.SetToolTip(ui.EmptyState, "Pick options on the left, then press RUN AUDIT" + Environment.NewLine + "Click to focus the Run button.");
            resultsCard.Controls.Add(ui.EmptyState);
            ui.EmptyState.BringToFront();
            rightColumn.Controls.Add(resultsCard);

            page.Controls.Add(split);
            // Place the splitter once the page has its real width. VisibleChanged
            // fires before Dock layout runs (stale 200px default), so use Layout
            // and wait until the width is sane. Minimums are clamped, never set
            // pre-layout (that crashed startup on narrow widths).
            bool splitPlaced = false;
            page.Layout += delegate
            {
                if (splitPlaced) return;
                const int minLeft = 300;
                const int minRight = 320;
                if (split.Width < minLeft + minRight + split.SplitterWidth) return;
                int max = split.Width - minRight - split.SplitterWidth;
                int d = 450;
                if (d > max) d = max;
                if (d < minLeft) d = minLeft;
                try { split.SplitterDistance = d; splitPlaced = true; }
                catch (Exception ex) { AppendLog("[layout] splitter init failed: " + ex.Message); }
            };
            return page;
        }

        // Per-mailbox / per-folder lookups (regional config, statistics, folder
        // permissions) dominate runtime. Warn before the user starts a slow run.
        // Slow is declared on the model (AuditOption.Slow / AuditOptionGroup.Slow),
        // not string-matched here, so new slow options can't silently miss the hint.
        private static bool HasSlowOptions(SectionUi ui)
        {
            foreach (var kv in ui.Checks)
            {
                bool groupSlow = false;
                if (ui.Section != null && ui.Section.Groups != null)
                    foreach (AuditOptionGroup g in ui.Section.Groups)
                        if (g.Key == kv.Key) { groupSlow = g.Slow; break; }
                foreach (CheckBox cb in kv.Value)
                {
                    if (!cb.Checked) continue;
                    if (groupSlow) return true;
                    var opt = cb.Tag as AuditOption;
                    if (opt != null && opt.Slow) return true;
                }
            }
            return false;
        }

        private Control BuildOptionGroup(SectionUi ui, AuditOptionGroup grp)
        {
            var card = new RoundedPanel { Dock = DockStyle.Top, BackColor = UiTheme.Surface2, CornerRadius = 6, Padding = new Padding(12, 8, 12, 10), Margin = new Padding(0, 0, 0, 8), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

            var title = new Label { Text = grp.Title, Dock = DockStyle.Top, Height = 22, ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 9.5F) };
            Label hint = null;
            if (!string.IsNullOrEmpty(grp.Hint))
                hint = new Label { Text = grp.Hint, Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(400, 0), ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8.5F) };

            int cols = Math.Max(1, grp.Columns);
            int rows = (grp.Options.Count + cols - 1) / cols;
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = cols,
                RowCount = rows,
                BackColor = UiTheme.Surface2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 4, 0, 0)
            };
            for (int c = 0; c < cols; c++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / cols));

            if (grp.Mode == GroupMode.MultiCheck)
            {
                var list = new List<CheckBox>();
                foreach (AuditOption opt in grp.Options)
                {
                    bool initChecked = ui.Section.ScopeAwareDefaults ? (ConnectionSettings.IsOnline ? opt.DefOnline : opt.DefOnPrem) : opt.DefaultChecked;
                    var cb = new CheckBox { Text = opt.Label, Checked = initChecked, AutoSize = true, ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 9F), Margin = new Padding(2, 3, 8, 3) };
                    cb.Tag = opt;
                    _optionTip.SetToolTip(cb, opt.Label);
                    list.Add(cb);
                    table.Controls.Add(cb);
                }
                ui.Checks[grp.Key] = list;
            }
            else
            {
                var list = new List<RadioButton>();
                bool anyChecked = false;
                foreach (AuditOption opt in grp.Options)
                {
                    var rb = new RadioButton { Text = opt.Label, Checked = opt.DefaultChecked, AutoSize = true, ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 9F), Margin = new Padding(2, 3, 8, 3) };
                    if (opt.DefaultChecked) anyChecked = true;
                    rb.Tag = opt;
                    _optionTip.SetToolTip(rb, opt.Label);
                    list.Add(rb);
                    table.Controls.Add(rb);
                }
                if (!anyChecked && list.Count > 0) list[0].Checked = true;
                ui.Radios[grp.Key] = list;
            }

            card.Controls.Add(table);
            if (hint != null) card.Controls.Add(hint);
            card.Controls.Add(title);
            return card;
        }

        private async Task RunSectionAsync(SectionUi ui)
        {
            AuditSection section = ui.Section;
            string csv = ui.OutputPath.Text.Trim();
            if (!IsValidCsvPath(csv))
            {
                Warn("Choose a valid output CSV path (e.g. C:\\Exports\\audit.csv).");
                ui.OutputPath.Focus();
                ui.OutputPath.SelectAll();
                return;
            }

            var selection = new AuditSelection();
            foreach (var kv in ui.Checks)
            {
                var vals = new List<string>();
                foreach (CheckBox cb in kv.Value) if (cb.Checked) vals.Add(((AuditOption)cb.Tag).Value);
                selection.Set(kv.Key, vals);
            }
            foreach (var kv in ui.Radios)
            {
                var vals = new List<string>();
                foreach (RadioButton rb in kv.Value) if (rb.Checked) { vals.Add(((AuditOption)rb.Tag).Value); break; }
                selection.Set(kv.Key, vals);
            }

            string body;
            try { body = section.BuildScript(selection, new ScriptContext(csv)); }
            catch (Exception ex) { Warn("Could not build the script: " + ex.Message); return; }

            string prelude = ConnectionSettings.BuildPrelude();

            bool shared = ConnectionSettings.RequiresInteractiveAuth;
            PowerShellSession session;
            SemaphoreSlim gate;
            int cap;
            if (shared)
            {
                session = _ps;
                gate = _serialAuditGate;
                cap = 1;
            }
            else
            {
                if (ui.Ps == null) ui.Ps = new PowerShellSession();
                session = ui.Ps;
                gate = _runSlots;
                cap = MaxParallelAudits;
            }
            var cts = new CancellationTokenSource();
            ui.RunCts = cts;
            ui.ActiveSession = session;

            ui.RunButton.Enabled = false;
            ui.CancelButton.Enabled = true;
            if (Interlocked.Increment(ref _runningAudits) == 1) SetBusy(true);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int streamedLines = 0;
            bool slotTaken = false;
            try
            {
                SetResult(ui, "◷ Queued - waiting for a free run slot (max " + cap + " parallel)...", UiTheme.Orange);
                try { await gate.WaitAsync(cts.Token); }
                catch (OperationCanceledException)
                {
                    SetResult(ui, "✗ Cancelled before start.", UiTheme.Orange);
                    SetFooter(section.NavTitle + " cancelled", UiTheme.Orange);
                    return;
                }
                slotTaken = true;
                using (var tick = new System.Windows.Forms.Timer { Interval = 500 })
                {
                    tick.Tick += delegate
                    {
                        string elapsed = sw.Elapsed.ToString("mm\\:ss");
                        int n = Interlocked.CompareExchange(ref streamedLines, 0, 0);
                        string msg = "Running " + section.NavTitle + "... " + elapsed + " · " + n + " lines";
                        SetFooter(msg, UiTheme.Orange);
                        ui.ResultInfo.Text = "◷ " + msg;
                        ui.ResultInfo.ForeColor = UiTheme.Orange;
                    };
                    tick.Start();
                    try
                    {
                    SetFooter("Running " + section.NavTitle + "... 00:00", UiTheme.Orange);
                    AppendLog("=== " + section.Title + " ===");
                    AppendLog(ConnectionSettings.Summary());
                    LogCommand(section.NavTitle + " - PowerShell", body);

                    try
                    {
                        string dir = Path.GetDirectoryName(csv);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    }
                    catch { }
                    try { if (File.Exists(csv)) File.Delete(csv); } catch { }

                    var result = await Task.Run(delegate
                    {
                        PsResult cr;
                        try { _connectGate.Wait(cts.Token); }
                        catch (OperationCanceledException) { return new PsResult(-1, "[cancelled by user]"); }
                        try
                        {
                            AppendLog("Establishing dedicated session for " + section.NavTitle + " (sign in once - follow-ups reuse it)...");
                            cr = session.Execute(prelude + Environment.NewLine + "Write-Host 'Worker session ready.'", 300000, delegate (string line)
                            {
                                Interlocked.Increment(ref streamedLines);
                                AppendLog(line);
                            });
                        }
                        finally { try { _connectGate.Release(); } catch { } }
                        if (cr.ExitCode != 0) return cr;
                        return session.Execute(body, 1800000, delegate (string line)
                        {
                            Interlocked.Increment(ref streamedLines);
                            AppendLog(line);
                        });
                    });
                    sw.Stop();

                    if (result.ExitCode == 0 && File.Exists(csv))
                    {
                        ui.LastCsv = csv;
                        ui.OpenButton.Enabled = true;
                        int previewed = LoadCsvIntoGrid(ui.Grid, csv, 200);
                        int total = CountCsvDataRows(csv);
                        string size;
                        try { size = FormatBytes(new FileInfo(csv).Length); }
                        catch { size = "?"; }
                        string took = sw.Elapsed.ToString("mm\\:ss");
                        if (total > 200)
                            SetResult(ui, "✓ Exported " + total + " rows (" + size + ") in " + took + " (previewing first 200). Full path: " + csv, UiTheme.Green);
                        else
                            SetResult(ui, "✓ Exported to " + Path.GetFileName(csv) + " (" + size + ") in " + took + "  -  " + previewed + " row(s) previewed. Full path: " + csv, UiTheme.Green);
                        ui.EmptyState.Visible = total == 0;
                        ui.ResultInfo.ForeColor = UiTheme.Green;
                        if (ui.XlsxBox != null && ui.XlsxBox.Checked)
                        {
                            string xlsx = Path.ChangeExtension(csv, ".xlsx");
                            XlsxWriteResult xr = XlsxWriter.WriteFromCsv(csv, xlsx, section.NavTitle);
                            if (xr.Ok)
                            {
                                ui.LastXlsx = xlsx;
                                ui.OpenXlsxButton.Enabled = true;
                                SetResult(ui, ui.ResultInfo.Text + " + XLSX (" + xr.DataRows + " rows" + (xr.Truncated ? ", truncated to Excel limits" : "") + ").", UiTheme.Green);
                            }
                            else AppendLog("[xlsx] workbook not written: " + xr.Error);
                        }
                        SetFooter(section.NavTitle + " done in " + took, UiTheme.Green);
                        if (!_isConnected) await RefreshConnectedAsAsync();
                    }
                    else if (result.Output.Contains("[cancelled by user]"))
                    {
                        SetResult(ui, "✗ Cancelled by user after " + sw.Elapsed.ToString("mm\\:ss") + " - partial output discarded.", UiTheme.Orange);
                        SetFooter(section.NavTitle + " cancelled", UiTheme.Orange);
                    }
                    else
                    {
                        SetResult(ui, "✗ Run failed in " + sw.Elapsed.ToString("mm\\:ss") + " - see the activity log.", UiTheme.Red);
                        SetFooter(section.NavTitle + " failed", UiTheme.Red);
                    }
                    }
                    finally
                    {
                        tick.Stop();
                        sw.Stop();
                    }
                }
            }
            finally
            {
                if (slotTaken) { try { gate.Release(); } catch { } }
                ui.RunCts = null;
                ui.ActiveSession = null;
                try { cts.Dispose(); } catch { }
                if (Interlocked.Decrement(ref _runningAudits) == 0) SetBusy(false);
                ui.RunButton.Enabled = _isConnected;
                ui.CancelButton.Enabled = false;
            }
        }

        private static int CountCsvDataRows(string path)
        {
            try
            {
                int lines = 0;
                using (var reader = new StreamReader(path, Encoding.UTF8, true))
                {
                    while (reader.ReadLine() != null) lines++;
                }
                return Math.Max(0, lines - 1);
            }
            catch { return 0; }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1048576) return (bytes / 1024) + " KB";
            return (bytes / 1048576.0).ToString("0.0") + " MB";
        }

        private void SetResult(SectionUi ui, string text, Color color)
        {
            ui.ResultInfo.Text = text;
            ui.ResultInfo.ForeColor = color;
            _optionTip.SetToolTip(ui.ResultInfo, text);
        }

        private Task<PsResult> RunPowerShellCaptureAsync(string command)
        {
            return Task.Run(delegate { return _ps.Execute(command, 300000, null); });
        }

        private Task<PsResult> RunPowerShellStreamingAsync(string script, int timeoutMs)
        {
            Action<string> live = delegate (string line) { AppendLog(line); };
            return Task.Run(delegate { return _ps.Execute(script, timeoutMs, live); });
        }

        private Task<PsResult> RunPowerShellScriptAsync(string script)
        {
            return RunPowerShellStreamingAsync(script, 1800000);
        }

        private void CloseSession()
        {
            try
            {
                if (_ps.IsAlive)
                {
                    AppendLog("Closing Exchange session...");
                    _ps.Execute(ConnectionSettings.BuildDisconnect(), 15000, null);
                }
            }
            catch { }
            finally { _ps.Dispose(); }
            foreach (var kv in _sectionUi)
            {
                PowerShellSession ps = kv.Value.Ps;
                if (ps == null) continue;
                try
                {
                    if (ps.IsAlive) ps.Execute(ConnectionSettings.BuildDisconnect(), 10000, null);
                }
                catch { }
                try { ps.Dispose(); }
                catch { }
            }
        }

        private int LoadCsvIntoGrid(DataGridView grid, string path, int maxRows)
        {
            grid.Columns.Clear();
            grid.Rows.Clear();
            int dataRows = 0;
            try
            {
                using (var reader = new StreamReader(path, Encoding.UTF8, true))
                {
                    string headerLine = reader.ReadLine();
                    if (headerLine == null) return 0;
                    string[] headers = ParseCsvLine(headerLine);
                    foreach (string h in headers)
                        grid.Columns.Add("c" + grid.Columns.Count, h);
                    string line;
                    while ((line = reader.ReadLine()) != null && dataRows < maxRows)
                    {
                        string[] cells = ParseCsvLine(line);
                        var row = new object[headers.Length];
                        for (int i = 0; i < headers.Length; i++) row[i] = i < cells.Length ? cells[i] : "";
                        grid.Rows.Add(row);
                        dataRows++;
                    }
                }
                foreach (DataGridViewColumn c in grid.Columns)
                {
                    c.SortMode = DataGridViewColumnSortMode.Automatic;
                    c.AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells;
                    if (c.Width > 320) c.Width = 320;
                    if (c.Width < 40) c.Width = 40;
                    c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                }
            }
            catch (Exception ex) { AppendLog("[preview] " + ex.Message); }
            return dataRows;
        }

        // CSV delimiter is ';' (see ScriptContext.ExportCsv).
        internal static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(ch);
                }
                else
                {
                    if (ch == '"') inQuotes = true;
                    else if (ch == ';') { result.Add(sb.ToString()); sb.Length = 0; }
                    else sb.Append(ch);
                }
            }
            result.Add(sb.ToString());
            return result.ToArray();
        }

        private static Panel NewSectionHeader(string iconKey, string title, string subtitle)
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = UiTheme.Surface };
            var icon = new PictureBox { Image = UiAssets.Render(iconKey, 22), Dock = DockStyle.Left, Width = 44, BackColor = UiTheme.Surface, SizeMode = PictureBoxSizeMode.CenterImage };
            var titleLabel = new Label { Text = title, Dock = DockStyle.Top, Height = 28, ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 12F), TextAlign = ContentAlignment.BottomLeft };
            var subLabel = new Label { Text = subtitle, Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8.5F), TextAlign = ContentAlignment.TopLeft };
            panel.Controls.Add(subLabel);
            panel.Controls.Add(titleLabel);
            panel.Controls.Add(icon);
            return panel;
        }

        private static RadioButton NewRadio(string text, bool check)
        {
            return new RadioButton { Text = text, Checked = check, AutoSize = true, ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 8.8F) };
        }

        private static TextBox NewField()
        {
            return new TextBox { BorderStyle = BorderStyle.FixedSingle, BackColor = UiTheme.FieldBack, ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 9F) };
        }

        private static Panel NewLabeledRow(string caption, TextBox box)
        {
            var row = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = UiTheme.Surface };
            var label = new Label { Text = caption, Dock = DockStyle.Left, Width = 150, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 8.8F) };
            if (box == null) box = NewField();
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 9, 8, 9), BackColor = row.BackColor };
            box.Dock = DockStyle.Fill;
            host.Controls.Add(box);
            row.Controls.Add(host);
            row.Controls.Add(label);
            row.Tag = box;
            return row;
        }

        private static string ScopeText(AuditScope scope)
        {
            switch (scope)
            {
                case AuditScope.ExchangeOnline: return "SCOPE: EXCHANGE ONLINE";
                case AuditScope.OnPremises: return "SCOPE: ON-PREMISES";
                default: return "SCOPE: ONLINE OR ON-PREMISES";
            }
        }

        private static string DefaultExportDir()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ExchangeAudit");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        private void Warn(string message)
        {
            AppendLog("[!] " + message);
            MessageBox.Show(this, message, "Exchange Audit Tool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static void OpenPath(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }
    }
}
