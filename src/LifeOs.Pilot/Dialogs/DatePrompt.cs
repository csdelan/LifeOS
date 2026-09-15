using LifeOs.Pilot.Controls;

namespace LifeOs.Pilot.Dialogs;

/// <summary>Picks a single calendar date (used e.g. for <c>bsk materialize --through</c>).</summary>
internal sealed class DatePrompt : Form
{
    private readonly DateField _date = new(optional: false);

    private DatePrompt(string title, string prompt, DateOnly initial)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        _date.IsoDate = initial.ToString("yyyy-MM-dd");
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1 };
        root.Controls.Add(new Label { Text = prompt, AutoSize = true, MaximumSize = new Size(360, 0) }, 0, 0);
        root.Controls.Add(_date, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public static string? Show(IWin32Window owner, string title, string prompt, DateOnly initial)
    {
        using var dialog = new DatePrompt(title, prompt, initial);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._date.IsoDate : null;
    }
}
