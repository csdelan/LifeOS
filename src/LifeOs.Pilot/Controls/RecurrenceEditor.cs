using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Controls;

/// <summary>Structured recurrence editor for Habits / Appointments (<c>bsk recur</c> args).</summary>
internal sealed class RecurrenceEditor : UserControl
{
    private readonly CheckBox _enabled = new() { Text = "Recurring", AutoSize = true };
    private readonly ComboBox _freq = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly TextBox _on = new() { Width = 180, PlaceholderText = "sun,wed  or  last" };
    private readonly NumericUpDown _every = new() { Minimum = 1, Maximum = 365, Value = 1, Width = 60 };
    private readonly ComboBox _unit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly NumericUpDown _day = new() { Minimum = 1, Maximum = 31, Value = 1, Width = 60 };
    private readonly TextBox _cue = new() { Width = 180, PlaceholderText = "cue (trigger)" };
    private readonly Label _onLabel = new() { Text = "On", AutoSize = true };
    private readonly Label _everyLabel = new() { Text = "Every", AutoSize = true };
    private readonly Label _dayLabel = new() { Text = "Day", AutoSize = true };

    public RecurrenceEditor()
    {
        AutoSize = true;
        _freq.Items.AddRange(["daily", "weekly", "interval", "monthly", "trigger"]);
        _freq.SelectedIndex = 0;
        _unit.Items.AddRange(["days", "weeks", "months"]);
        _unit.SelectedIndex = 0;
        _enabled.CheckedChanged += (_, _) => UpdateEnabled();
        _freq.SelectedIndexChanged += (_, _) => UpdateEnabled();

        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };
        row.Controls.Add(_enabled);
        row.Controls.Add(_freq);
        row.Controls.Add(_onLabel);
        row.Controls.Add(_on);
        row.Controls.Add(_everyLabel);
        row.Controls.Add(_every);
        row.Controls.Add(_unit);
        row.Controls.Add(_dayLabel);
        row.Controls.Add(_day);
        row.Controls.Add(_cue);
        Controls.Add(row);
        UpdateEnabled();
    }

    public bool IsRecurring => _enabled.Checked;

    public void LoadFromJson(string? json)
    {
        _enabled.Checked = !string.IsNullOrWhiteSpace(json) && json is not "{}" and not "null";
        if (!_enabled.Checked || string.IsNullOrWhiteSpace(json))
        {
            UpdateEnabled();
            return;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var freq = root.TryGetProperty("freq", out var f) ? f.GetString() : null;
            if (freq is not null)
            {
                var index = _freq.Items.IndexOf(freq);
                if (index >= 0)
                {
                    _freq.SelectedIndex = index;
                }
            }

            if (root.TryGetProperty("on", out var on))
            {
                _on.Text = on.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? string.Join(",", on.EnumerateArray().Select(e => e.GetString()))
                    : on.GetString() ?? "";
            }

            if (root.TryGetProperty("every", out var every) && every.TryGetInt32(out var n))
            {
                _every.Value = n;
            }

            if (root.TryGetProperty("unit", out var unit) && unit.GetString() is { } u)
            {
                var index = _unit.Items.IndexOf(u);
                if (index >= 0)
                {
                    _unit.SelectedIndex = index;
                }
            }

            if (root.TryGetProperty("day", out var day) && day.TryGetInt32(out var d))
            {
                _day.Value = d;
            }

            if (root.TryGetProperty("cue", out var cue))
            {
                _cue.Text = cue.GetString() ?? "";
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Leave defaults.
        }

        UpdateEnabled();
    }

    /// <summary>CLI args after the subject, or <c>["--clear"]</c>, or null when unchanged/disabled-and-empty.</summary>
    public string[]? ToCliArgs()
    {
        if (!_enabled.Checked)
        {
            return ["--clear"];
        }

        var freq = _freq.SelectedItem?.ToString() ?? "daily";
        return freq switch
        {
            "daily" => ["--freq", "daily"],
            "weekly" => ["--freq", "weekly", "--on", _on.Text.Trim()],
            "interval" => ["--freq", "interval", "--every", ((int)_every.Value).ToString(), "--unit", _unit.SelectedItem?.ToString() ?? "days"],
            "monthly" when _on.Text.Trim().Equals("last", StringComparison.OrdinalIgnoreCase)
                => ["--freq", "monthly", "--on", "last"],
            "monthly" => ["--freq", "monthly", "--day", ((int)_day.Value).ToString()],
            "trigger" => ["--freq", "trigger", "--cue", _cue.Text.Trim()],
            _ => ["--freq", freq]
        };
    }

    private void UpdateEnabled()
    {
        var on = _enabled.Checked;
        _freq.Enabled = on;
        var freq = _freq.SelectedItem?.ToString() ?? "daily";
        _on.Enabled = on && freq is "weekly" or "monthly";
        _onLabel.Enabled = _on.Enabled;
        _every.Enabled = on && freq == "interval";
        _unit.Enabled = _every.Enabled;
        _everyLabel.Enabled = _every.Enabled;
        _day.Enabled = on && freq == "monthly";
        _dayLabel.Enabled = _day.Enabled;
        _cue.Enabled = on && freq == "trigger";
    }
}
