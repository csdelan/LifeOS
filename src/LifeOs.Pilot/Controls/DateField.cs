using System.ComponentModel;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Controls;

/// <summary>A <see cref="DateTimePicker"/> for optional or required calendar dates (ISO <c>yyyy-MM-dd</c> on write).</summary>
internal sealed class DateField : DateTimePicker
{
    public DateField(bool optional = true)
    {
        Format = DateTimePickerFormat.Short;
        ShowCheckBox = optional;
        Checked = !optional;
        Width = 160;
        Value = DateTime.Today;
    }

    /// <summary>ISO date, or null when the optional picker is unchecked.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? IsoDate
    {
        get
        {
            if (ShowCheckBox && !Checked)
            {
                return null;
            }

            return DateOnly.FromDateTime(Value.Date).ToString("yyyy-MM-dd");
        }
        set
        {
            if (Ui.TryParseDate(value, out var date))
            {
                Value = date.ToDateTime(TimeOnly.MinValue);
                Checked = true;
            }
            else if (ShowCheckBox)
            {
                Checked = false;
            }
        }
    }
}

/// <summary>A time-only picker storing <c>HH:mm</c>.</summary>
internal sealed class TimeField : DateTimePicker
{
    public TimeField(bool optional = false)
    {
        Format = DateTimePickerFormat.Custom;
        CustomFormat = "HH:mm";
        ShowUpDown = true;
        ShowCheckBox = optional;
        Checked = !optional;
        Width = 90;
        Value = DateTime.Today.AddHours(9);
    }

    /// <summary>ISO time, or null when disabled, optional-unchecked, or unset.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? IsoTime
    {
        get
        {
            if (!Enabled || (ShowCheckBox && !Checked))
            {
                return null;
            }

            return Value.ToString("HH:mm");
        }
    }

    public void SetTime(string? hhmm)
    {
        if (string.IsNullOrWhiteSpace(hhmm))
        {
            if (ShowCheckBox)
            {
                Checked = false;
            }

            return;
        }

        if (TimeOnly.TryParse(hhmm.Trim(), out var time))
        {
            Value = DateTime.Today.Add(time.ToTimeSpan());
            Checked = true;
        }
        else if (ShowCheckBox)
        {
            Checked = false;
        }
    }
}
