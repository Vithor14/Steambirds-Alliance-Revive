using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

internal enum AiBehaviorResult { Cancel, Embedded, Shared }

// A window of its own for one behavior: how to move, how to shoot and what happens on contact. It opens from a state of an AI
// ("New behavior..." / "Edit behavior..."). A new behavior can stay inside that AI (embedded: only its states can pick it) or be
// saved in the behaviors folder (any AI can pick it).
internal sealed class AiBehaviorWindow : Form
{
    private readonly AiBehaviorEditor editor;

    public AiBehaviorWindow(AiStore store, AiDoc behavior, bool isNew)
    {
        Text = L.F("Behavior \"{0}\"", behavior.DisplayName);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        ClientSize = new Size(560, 640);

        editor = new AiBehaviorEditor(store) { ShowUsedBy = false };
        editor.Bind(behavior);
        editor.Edited += () => Edited = true;

        Label note = Ui.Wrapping(isNew
            ? L.T("A new behavior. \"Use in this AI only\" keeps it inside the AI you are editing (only its states can pick it). \"Save in the behaviors folder\" writes a file that any AI can use.")
            : L.T("This behavior lives inside the AI you are editing. Changes apply as you make them. \"Move to the behaviors folder\" turns it into a shared file that any AI can use."), 520, true);
        note.Dock = DockStyle.Top;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        if (isNew)
        {
            buttons.Controls.Add(Ui.Button(L.T("Cancel"), (_, _) => Finish(AiBehaviorResult.Cancel)));
            buttons.Controls.Add(Ui.Button(L.T("Save in the behaviors folder"), (_, _) => Finish(AiBehaviorResult.Shared)));
            buttons.Controls.Add(Ui.Primary(L.T("Use in this AI only"), (_, _) => Finish(AiBehaviorResult.Embedded)));
        }
        else
        {
            buttons.Controls.Add(Ui.Button(L.T("Move to the behaviors folder"), (_, _) => Finish(AiBehaviorResult.Shared)));
            buttons.Controls.Add(Ui.Primary(L.T("Close"), (_, _) => Finish(AiBehaviorResult.Embedded)));
        }

        Controls.Add(editor);
        Controls.Add(buttons);
        Controls.Add(note);
        Theme.AttachWindow(this);
        // Closing the window with the X is the same as Cancel for a new behavior, and Close for an existing one.
        FormClosing += (_, _) =>
        {
            if (Result == AiBehaviorResult.Cancel && !isNew) Result = AiBehaviorResult.Embedded;
        };
    }

    public AiBehaviorResult Result { get; private set; } = AiBehaviorResult.Cancel;

    // The behavior was changed in this window.
    public bool Edited { get; private set; }

    private void Finish(AiBehaviorResult result)
    {
        Result = result;
        Close();
    }
}
