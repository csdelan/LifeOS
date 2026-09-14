using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Dialogs;

/// <summary>Type-specific status picker (D7). Null status is offered as the type default.</summary>
internal sealed class StatusPickDialog : Form
{
    private readonly ComboBox _status = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly TextBox _note = new() { Width = 360, PlaceholderText = "Optional note (not required)" };

    private StatusPickDialog(string type, string label)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Change status";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        foreach (var status in PilotVocab.StatusesFor(type))
        {
            _status.Items.Add(status);
        }

        if (_status.Items.Count > 0)
        {
            _status.SelectedIndex = 0;
        }

        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1 };
        root.Controls.Add(new Label { Text = label, AutoSize = true, MaximumSize = new Size(360, 0) }, 0, 0);
        root.Controls.Add(new Label { Text = "Status:", AutoSize = true, Margin = new Padding(0, 8, 0, 2) }, 0, 1);
        root.Controls.Add(_status, 0, 2);
        root.Controls.Add(new Label { Text = "Note (optional):", AutoSize = true, Margin = new Padding(0, 8, 0, 2) }, 0, 3);
        root.Controls.Add(_note, 0, 4);
        root.Controls.Add(buttons, 0, 5);
        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Selected => _status.SelectedItem?.ToString() ?? "";

    public static string? Show(IWin32Window owner, string type, string label)
    {
        if (!PilotVocab.HasStatus(type))
        {
            return null;
        }

        using var dialog = new StatusPickDialog(type, label);
        return dialog.ShowDialog(owner) == DialogResult.OK && dialog.Selected.Length > 0 ? dialog.Selected : null;
    }
}
