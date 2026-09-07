using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace ExchangeAuditTool
{
    internal sealed class PowerShellSession : IDisposable
    {
        private Process _proc;
        private readonly object _sync = new object();
        private StringBuilder _capture;
        private string _endMarker;
        private bool _hadError;
        private ManualResetEvent _done;
        private Action<string> _onLine;

        public bool IsAlive { get { return _proc != null && !_proc.HasExited; } }

        public void Start()
        {
            if (IsAlive) return;
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NoLogo -ExecutionPolicy Bypass -Command -")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            _proc = new Process { StartInfo = psi };
            _proc.OutputDataReceived += OnData;
            _proc.ErrorDataReceived += OnData;
            _proc.Start();
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();

            _proc.StandardInput.WriteLine("function prompt { '' }");
            _proc.StandardInput.Flush();
        }

        private void OnData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            lock (_sync)
            {
                if (_capture == null) return;
                if (_endMarker != null && e.Data.Contains(_endMarker))
                {
                    if (_done != null) _done.Set();
                    return;
                }
                if (e.Data.Contains("<<<EAT_ERROR>>>"))
                {
                    _hadError = true;
                    string msg = e.Data.Replace("<<<EAT_ERROR>>>", "ERROR:").Trim();
                    _capture.AppendLine(msg);
                    if (_onLine != null) { try { _onLine(msg); } catch { } }
                    return;
                }
                _capture.AppendLine(e.Data);
                if (_onLine != null) { try { _onLine(e.Data); } catch { } }
            }
        }

        public PsResult Execute(string script, int timeoutMs, Action<string> onLine)
        {
            if (!IsAlive) Start();

            string marker = "<<<EAT_END_" + Guid.NewGuid().ToString("N") + ">>>";
            string tempFile = Path.Combine(Path.GetTempPath(), "ExAudit-" + Guid.NewGuid().ToString("N") + ".ps1");

            var wrapped = new StringBuilder();
            wrapped.AppendLine("$ErrorActionPreference = 'Stop'");
            wrapped.AppendLine("try {");
            wrapped.AppendLine(script);
            wrapped.AppendLine("} catch { Write-Host ('<<<EAT_ERROR>>> ' + $_.Exception.Message) }");

            try
            {
                File.WriteAllText(tempFile, wrapped.ToString(), new UTF8Encoding(true));

                lock (_sync)
                {
                    _capture = new StringBuilder();
                    _endMarker = marker;
                    _hadError = false;
                    _done = new ManualResetEvent(false);
                    _onLine = onLine;
                }

                _proc.StandardInput.WriteLine(". '" + tempFile.Replace("'", "''") + "'; Write-Host '" + marker + "'");
                _proc.StandardInput.Flush();

                bool finished = _done.WaitOne(timeoutMs);

                string output;
                bool error;
                lock (_sync)
                {
                    output = _capture != null ? _capture.ToString().Trim() : "";
                    error = _hadError;
                    _capture = null;
                    _endMarker = null;
                    _onLine = null;
                }

                if (!finished)
                    return new PsResult(-1, output + Environment.NewLine + "[timed out waiting for the command to finish]");

                return new PsResult(error ? 1 : 0, output);
            }
            catch (Exception ex)
            {
                return new PsResult(-1, ex.Message);
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        public void Dispose()
        {
            try
            {
                if (IsAlive)
                {
                    try { _proc.StandardInput.WriteLine("exit"); _proc.StandardInput.Flush(); } catch { }
                    if (!_proc.WaitForExit(3000)) { try { _proc.Kill(); } catch { } }
                }
            }
            catch { }
            finally
            {
                try { if (_proc != null) _proc.Dispose(); } catch { }
                _proc = null;
            }
        }
    }

    internal sealed class PsResult
    {
        public int ExitCode;
        public string Output;
        public PsResult(int code, string output) { ExitCode = code; Output = output == null ? "" : output.Trim(); }
    }
}
