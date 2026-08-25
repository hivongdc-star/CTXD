using System.Diagnostics;
using System.Net.Http;

namespace CTXD.Admin;

public sealed class MainForm : Form
{
    readonly Button _start = new() { Text = "Start", Width = 90, Height = 34 };
    readonly Button _stop = new() { Text = "Stop", Width = 90, Height = 34, Enabled = false };
    readonly Button _restart = new() { Text = "Restart", Width = 90, Height = 34 };
    readonly TextBox _url = new() { Text = "http://0.0.0.0:5080", Width = 220 };
    readonly Label _status = new() { Text = "Stopped", AutoSize = true, Padding = new Padding(8, 9, 8, 0) };
    readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(24,24,24), ForeColor = Color.Gainsboro, Font = new Font("Consolas", 9f) };
    readonly System.Windows.Forms.Timer _healthTimer = new() { Interval = 2000 };
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(1) };
    Process? _server;

    public MainForm()
    {
        Text = "CTXD Server";
        Width = 980; Height = 680; MinimumSize = new Size(760, 480);
        StartPosition = FormStartPosition.CenterScreen;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(8), WrapContents = false };
        bar.Controls.Add(_start); bar.Controls.Add(_stop); bar.Controls.Add(_restart);
        bar.Controls.Add(new Label { Text = "Listen:", AutoSize = true, Padding = new Padding(12,9,0,0) });
        bar.Controls.Add(_url); bar.Controls.Add(_status);
        Controls.Add(_log); Controls.Add(bar);

        _start.Click += (_,_) => StartServer();
        _stop.Click += (_,_) => StopServer();
        _restart.Click += (_,_) => { StopServer(); StartServer(); };
        _healthTimer.Tick += async (_,_) => await CheckHealth();
        _healthTimer.Start();
        FormClosing += (_,_) => StopServer();
    }

    void StartServer()
    {
        if (_server is { HasExited: false }) return;
        var launch = FindServer();
        if (launch is null) { Append("[ADMIN] Không tìm thấy CTXD.Server. Build/publish server trước.\n"); return; }

        var psi = new ProcessStartInfo(launch.Value.File, launch.Value.Args)
        {
            WorkingDirectory = launch.Value.WorkDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.Environment["ASPNETCORE_URLS"] = _url.Text.Trim();
        _server = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _server.OutputDataReceived += (_,e) => { if(e.Data != null) Append(e.Data + Environment.NewLine); };
        _server.ErrorDataReceived += (_,e) => { if(e.Data != null) Append("[ERR] " + e.Data + Environment.NewLine); };
        _server.Exited += (_,_) => BeginInvoke(new Action(() => SetRunning(false)));
        try
        {
            _server.Start(); _server.BeginOutputReadLine(); _server.BeginErrorReadLine();
            SetRunning(true); Append("[ADMIN] Server started.\n");
        }
        catch(Exception ex) { Append("[ADMIN] Start failed: " + ex.Message + Environment.NewLine); SetRunning(false); }
    }

    void StopServer()
    {
        try
        {
            if (_server is { HasExited: false })
            {
                _server.Kill(entireProcessTree: true);
                _server.WaitForExit(3000);
            }
        }
        catch { }
        finally { _server?.Dispose(); _server = null; SetRunning(false); }
    }

    async Task CheckHealth()
    {
        try
        {
            var u = _url.Text.Trim().Replace("0.0.0.0", "127.0.0.1").TrimEnd('/') + "/health";
            using var r = await _http.GetAsync(u);
            _status.Text = r.IsSuccessStatusCode ? "Running / Healthy" : "Running / HTTP " + (int)r.StatusCode;
            _status.ForeColor = r.IsSuccessStatusCode ? Color.DarkGreen : Color.DarkOrange;
        }
        catch
        {
            if (_server is { HasExited: false }) { _status.Text = "Starting / Unhealthy"; _status.ForeColor = Color.DarkOrange; }
            else { _status.Text = "Stopped"; _status.ForeColor = Color.DarkRed; }
        }
    }

    (string File,string Args,string WorkDir)? FindServer()
    {
        var baseDir = AppContext.BaseDirectory;
        var exe = Path.Combine(baseDir, "Server", "CTXD.Server.exe");
        if (File.Exists(exe)) return (exe,"",Path.GetDirectoryName(exe)!);
        exe = Path.Combine(baseDir, "CTXD.Server.exe");
        if (File.Exists(exe)) return (exe,"",baseDir);

        var dllCandidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, "..","..","..","..","CTXD.Server","bin","Release","net8.0","CTXD.Server.dll")),
            Path.GetFullPath(Path.Combine(baseDir, "..","..","..","..","CTXD.Server","bin","Debug","net8.0","CTXD.Server.dll"))
        };
        var dll = dllCandidates.FirstOrDefault(File.Exists);
        return dll is null ? null : ("dotnet", '"' + dll + '"', Path.GetDirectoryName(dll)!);
    }

    void SetRunning(bool running)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => SetRunning(running))); return; }
        _start.Enabled = !running; _stop.Enabled = running;
        if (!running) { _status.Text = "Stopped"; _status.ForeColor = Color.DarkRed; }
    }

    void Append(string text)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => Append(text))); return; }
        _log.AppendText(text); _log.SelectionStart = _log.TextLength; _log.ScrollToCaret();
    }
}
