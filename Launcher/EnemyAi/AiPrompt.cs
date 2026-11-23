using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// A small "type a name" dialog.
internal static class AiPrompt
{
    public static string? Ask(IWin32Window owner, string title, string label, string initial)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(380, 112),
            AutoScaleMode = AutoScaleMode.Dpi
        };
        var caption = new Label { Text = label, AutoSize = false, Left = 12, Top = 12, Width = 356, Height = 20 };
        var box = new ThemedTextBox { Text = initial, Left = 12, Top = 36, Width = 356 };
        var ok = new ThemedButton { Text = L.T("OK"), DialogResult = DialogResult.OK, Left = 212, Top = 72, Width = 75 };
        var cancel = new ThemedButton { Text = L.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 293, Top = 72, Width = 75 };
        form.Controls.AddRange(new Control[] { caption, box, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        Theme.AttachWindow(form);
        box.SelectAll();
        return form.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
    }
}
