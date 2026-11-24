using System.Diagnostics;
using System.Text;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// Runs SteambirdsGameFiles.exe (GameFiles\README.md) on the game folder and shows what it prints: it copies configBinary,
// installs BepInEx and the plugin, opens the game once and draws the pictures for the map editor. The first-run window
// opens it right after the game is downloaded. The tool writes its own log (logs\game-files.log).
internal sealed class GameFilesRunForm : Form
{
    private readonly LauncherContext context;
    private readonly string gameFolder;
    private readonly Label status;
    private readonly RichTextBox output;
    private readonly ThemedButton closeButton;
    private readonly ThemedButton cancelButton;
    private Process? process;
    private bool finished;
    private bool cancelled;

    // True when the tool ended with exit code 0.
    public bool Succeeded { get; private set; }

    public GameFilesRunForm(LauncherContext context, string gameFolder)
    {
        this.context = context;
        this.gameFolder = gameFolder;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = L.T("Preparing the game files");
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(820, 560);
        MinimumSize = new Size(600, 420);
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;

        Label intro = Ui.Wrapping(L.T("SteambirdsGameFiles.exe takes the files the server and the map editor need from your copy of the game: it copies configBinary, installs BepInEx and the Steambirds Quieter plugin, opens the game once and closes it when the data and the pictures are written. This takes a few minutes: leave the game alone, it closes by itself. Steam must stay open."), 760, gray: false);
        status = Ui.Bold(L.T("Starting..."));

        Theme.Palette p = Theme.P;
        output = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = p.Input,
            ForeColor = p.Text,
            DetectUrls = false,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            WordWrap = true
        };

        cancelButton = Ui.Button(L.T("Cancel"), (_, _) => CancelRun());
        closeButton = Ui.Primary(L.T("Close"), (_, _) => Close());
        closeButton.Enabled = false;
        FlowLayoutPanel buttons = Ui.Flow(closeButton, cancelButton);
        buttons.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(root, intro);
        Ui.AddRow(root, status);
        root.RowCount++;
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.Controls.Add(Ui.Framed(output), 0, root.RowCount - 1);
        Ui.AddRow(root, buttons);
        Controls.Add(root);
        ResumeLayout(true);
        Theme.AttachWindow(this);

        output.BackColor = p.Input;
        output.ForeColor = p.Text;
        Shown += (_, _) => Run();
        FormClosing += (_, e) =>
        {
            if (!finished && !cancelled)
            {
                // Closing with the X while it runs is the same as Cancel, after asking.
                if (!Ui.Confirm(this, L.T("Stop preparing the game files? You can run SteambirdsGameFiles.exe again later."))) e.Cancel = true;
                else CancelRun();
            }
        };
    }

    // Where the tool is: next to the launcher.
    public static string ToolPath => Path.Combine(LauncherPaths.BaseFolder, "SteambirdsGameFiles.exe");

    public static bool ToolExists => File.Exists(ToolPath);

    private void Run()
    {
        var startInfo = new ProcessStartInfo(ToolPath)
        {
            WorkingDirectory = LauncherPaths.BaseFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("--game");
        startInfo.ArgumentList.Add(gameFolder);
        startInfo.ArgumentList.Add("--no-pause");

        try
        {
            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) Append(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) Append(e.Data); };
            process.Exited += (_, _) => OnExited();
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            Ui.SetStatus(status, L.T("Running... the game opens by itself and closes when it is done."), TextRole.Warn);
            context.ClientNotice(L.T("SteambirdsGameFiles.exe started"));
        }
        catch (Exception e)
        {
            Finish(false, L.F("Could not start SteambirdsGameFiles.exe: {0}", e.Message));
        }
    }

    private void Append(string line)
    {
        string clean = StripAnsi(line);
        if (clean.Length == 0 || IsDisposed) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                output.AppendText(clean + "\n");
                output.SelectionStart = output.TextLength;
                output.ScrollToCaret();
            }));
        }
        catch (InvalidOperationException)
        {
            // The window is closing.
        }
    }

    private static string StripAnsi(string text)
    {
        if (text.IndexOf('\x1B') < 0) return text;
        return System.Text.RegularExpressions.Regex.Replace(text, "\x1B\\[[0-9;]*[A-Za-z]", "");
    }

    private void OnExited()
    {
        int code;
        try
        {
            process!.WaitForExit();
            code = process.ExitCode;
        }
        catch
        {
            code = -1;
        }
        if (IsDisposed) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (cancelled) return;
                if (code == 0) Finish(true, L.T("Done: the server data and the map editor pictures are ready."));
                else Finish(false, L.F("SteambirdsGameFiles.exe ended with an error (code {0}). The output above and logs\\game-files.log say why. You can run it again later.", code));
            }));
        }
        catch (InvalidOperationException)
        {
            // The window is closing.
        }
    }

    private void Finish(bool ok, string text)
    {
        finished = true;
        Succeeded = ok;
        Ui.SetStatus(status, text, ok ? TextRole.Good : TextRole.Bad);
        cancelButton.Enabled = false;
        closeButton.Enabled = true;
        if (ok)
        {
            context.ClientNotice(L.T("SteambirdsGameFiles.exe finished: the game data is ready"));
            context.ReloadGameData();
        }
        else
        {
            context.ClientNotice(text);
        }
    }

    private void CancelRun()
    {
        if (finished) return;
        cancelled = true;
        try
        {
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone.
        }
        finished = true;
        Ui.SetStatus(status, L.T("Stopped. Run SteambirdsGameFiles.exe again later to finish."), TextRole.Warn);
        cancelButton.Enabled = false;
        closeButton.Enabled = true;
        context.ClientNotice(L.T("SteambirdsGameFiles.exe was stopped"));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) process?.Dispose();
        base.Dispose(disposing);
    }
}
