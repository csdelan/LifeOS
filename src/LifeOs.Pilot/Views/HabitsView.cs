using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Controls;
using LifeOs.Pilot.Dialogs;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Views;

/// <summary>GEN-3 Habit viewer: streak/adherence grid, one-click record, recurrence via the detail editor.</summary>
internal sealed class HabitsView : UserControl, IPilotView
{
    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly DataGridView _grid = new();
    private readonly DataGridView _occurrences = new();
    private readonly DetailPane _detail;
    private readonly CheckBox _archived = new() { Text = "Archived", AutoSize = true };
    private readonly Label _status = Ui.Muted("");
    private Guid _pendingSelect;
    private bool _restored;

    public HabitsView(SubjectReader reader, BskCli? bsk, Navigator navigator)
    {
        _reader = reader;
        _bsk = bsk;
        _detail = new DetailPane(reader, bsk, navigator);
        _detail.Changed += (_, _) => Reload();
        Ui.ConfigureGrid(_grid);
        Ui.ConfigureGrid(_occurrences);
        _grid.SelectionChanged += OnSelect;
        _archived.CheckedChanged += (_, _) => Reload();

        var followed = Ui.Action("Followed");
        var partial = Ui.Action("Partial");
        var missed = Ui.Action("Missed");
        followed.Enabled = bsk is not null;
        partial.Enabled = bsk is not null;
        missed.Enabled = bsk is not null;
        followed.Click += (_, _) => Adhere("followed");
        partial.Click += (_, _) => Adhere("partial");
        missed.Click += (_, _) => Adhere("missed");

        var create = Ui.Action("New Habit…");
        create.Enabled = bsk is not null;
        create.Click += (_, _) =>
        {
            if (_bsk is null)
            {
                return;
            }

            var created = NewSubjectDialog.ShowNewOfType(this, _reader, _bsk, PilotVocab.Habit);
            if (created is not null)
            {
                Reload();
                SelectSubject(created.Id);
            }
        };

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        right.Panel1.Controls.Add(_occurrences);
        right.Panel2.Controls.Add(_detail);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(right);

        var banner = Ui.Toolbar();
        banner.Controls.Add(create);
        banner.Controls.Add(followed);
        banner.Controls.Add(partial);
        banner.Controls.Add(missed);
        banner.Controls.Add(_archived);
        banner.Controls.Add(_status);
        Controls.Add(split);
        Controls.Add(banner);
    }

    public bool IsDirty => false;

    public bool TrySaveEdits() => true;

    public void DiscardEdits()
    {
    }

    public void Reload()
    {
        try
        {
            var keep = CurrentHabit()?.Id ?? _pendingSelect;
            var habits = _reader.GetHabits(_archived.Checked).ToList();
            _grid.DataSource = habits;
            BindHabits();
            _status.Text = $"{habits.Count} habit(s)";
            if (keep != Guid.Empty)
            {
                SelectSubject(keep);
            }
            else
            {
                _occurrences.DataSource = null;
                _detail.Clear();
            }
        }
        catch (Exception ex)
        {
            Ui.ShowError(this, "Read failed", ex);
        }
    }

    public void SelectSubject(Guid id)
    {
        _pendingSelect = id;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is HabitRow item && item.Id == id)
            {
                var visible = _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible);
                if (visible is not null)
                {
                    _grid.CurrentCell = row.Cells[visible.Index];
                }

                ShowHabit(item);
                return;
            }
        }
    }

    public void SaveViewState(ViewStateStore store)
    {
        store.Set(Destinations.Habits, "archived", _archived.Checked ? "1" : "0");
        store.Set(Destinations.Habits, "selected", CurrentHabit()?.Id.ToString());
    }

    public void RestoreViewState(ViewStateStore store)
    {
        if (_restored)
        {
            return;
        }

        _restored = true;
        _archived.Checked = store.Get(Destinations.Habits, "archived") == "1";
        if (Guid.TryParse(store.Get(Destinations.Habits, "selected"), out var id))
        {
            _pendingSelect = id;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Reload();
    }

    private void OnSelect(object? sender, EventArgs e)
    {
        if (CurrentHabit() is { } habit)
        {
            ShowHabit(habit);
        }
    }

    private void ShowHabit(HabitRow habit)
    {
        var occ = _reader.GetHabitOccurrences(habit.Id).Take(60).ToList();
        _occurrences.DataSource = occ;
        Ui.HideAllColumns(_occurrences);
        Ui.ShowColumn(_occurrences, "OccurrenceDate", 110, 0, "Date");
        Ui.ShowColumn(_occurrences, "State", 110, 1);
        _detail.LoadSubject(habit.Id);
    }

    private HabitRow? CurrentHabit() => _grid.CurrentRow?.DataBoundItem as HabitRow;

    private void Adhere(string result)
    {
        if (_bsk is null || CurrentHabit() is not { } habit)
        {
            return;
        }

        var on = _occurrences.CurrentRow?.DataBoundItem is HabitOccurrenceRow occ
            ? occ.OccurrenceDate.ToString("yyyy-MM-dd")
            : Ui.TodayIso;
        Ui.RunWrite(this, _bsk, "Adhere failed", bsk =>
        {
            bsk.Run("adhere", habit.Urn, result, "--on", on);
            ShowHabit(habit);
            Reload();
            SelectSubject(habit.Id);
        });
    }

    private void BindHabits()
    {
        Ui.HideAllColumns(_grid);
        Ui.ShowColumn(_grid, "Name", 220, 0);
        Ui.ShowColumn(_grid, "CurrentStreak", 70, 1, "Streak");
        Ui.ShowColumn(_grid, "LastState", 90, 2, "Last");
        Ui.ShowColumn(_grid, "Cue", 160, 3);
        Ui.ShowColumn(_grid, "AllowsPartial", 70, 4, "Partial");
    }
}
