using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Controls;
using LifeOs.Pilot.Dialogs;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Views;

/// <summary>DASHBOARD-1: composed reads — no inline editors except one-click habit adherence.</summary>
internal sealed class DashboardView : UserControl, IPilotView
{
    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly Navigator _navigator;
    private readonly FlowLayoutPanel _body = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Padding = new Padding(16)
    };
    private readonly Label _status = Ui.Muted("");

    public DashboardView(SubjectReader reader, BskCli? bsk, Navigator navigator)
    {
        _reader = reader;
        _bsk = bsk;
        _navigator = navigator;
        var banner = Ui.Toolbar();
        var refresh = Ui.Action("Refresh");
        refresh.Click += (_, _) => Reload();
        banner.Controls.Add(refresh);
        banner.Controls.Add(_status);
        Controls.Add(_body);
        Controls.Add(banner);
    }

    public bool IsDirty => false;

    public bool TrySaveEdits() => true;

    public void DiscardEdits()
    {
    }

    public void SelectSubject(Guid id)
    {
    }

    public void SaveViewState(ViewStateStore store)
    {
    }

    public void RestoreViewState(ViewStateStore store)
    {
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Reload();
    }

    public void Reload()
    {
        _body.Controls.Clear();
        _body.Controls.Add(Placeholder("Primary focus & daily objectives — Pilot phase 2 (needs kernel D6)."));

        try
        {
            AddInbox();
            AddAppointments();
            AddHabits();
            AddTasks();
            AddWork("Active Goals", PilotVocab.Goal, Destinations.Goals);
            AddWork("Active Projects", PilotVocab.Project, Destinations.Projects);
            _status.Text = $"Updated {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            _status.Text = "Not connected";
            _body.Controls.Add(Placeholder($"Could not load dashboard: {ex.Message}"));
        }
    }

    private void AddInbox()
    {
        var count = _reader.GetInboxCount();
        var panel = Section("Inbox");
        if (count == 0)
        {
            panel.Controls.Add(Empty("Inbox is clear — nothing to triage."));
        }
        else
        {
            panel.Controls.Add(Link($"{count} item(s) to triage →", () => _navigator.Go(Destinations.Inbox)));
        }

        _body.Controls.Add(panel);
    }

    private void AddAppointments()
    {
        var today = Ui.TodayIso;
        var items = _reader.GetAppointments(onIso: today).Where(a => !a.IsSeries).ToList();
        var panel = Section("Today's Appointments");
        if (items.Count == 0)
        {
            panel.Controls.Add(Empty("No appointments today."));
        }
        else
        {
            foreach (var item in items)
            {
                var when = item.AllDay is "true" or "t" ? "all day" : $"{item.StartTime}–{item.EndTime}";
                var captured = item;
                panel.Controls.Add(Link($"{when}  {item.Title}  [{item.Status}]",
                    () => _navigator.OpenSubject(PilotVocab.Appointment, captured.Id)));
            }
        }

        _body.Controls.Add(panel);
    }

    private void AddHabits()
    {
        var today = Ui.Today;
        var items = _reader.GetHabitOccurrences(on: today);
        var panel = Section("Today's Habits");
        if (items.Count == 0)
        {
            panel.Controls.Add(Empty("No habit occurrences scheduled today."));
        }
        else
        {
            foreach (var item in items)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
                var captured = item;
                row.Controls.Add(Link($"{item.HabitName}  ({item.State})",
                    () => _navigator.OpenSubject(PilotVocab.Habit, captured.HabitId)));
                row.Controls.Add(AdhereButton(item, "followed"));
                if (item.AllowsPartial)
                {
                    row.Controls.Add(AdhereButton(item, "partial"));
                }

                row.Controls.Add(AdhereButton(item, "missed"));
                panel.Controls.Add(row);
            }
        }

        panel.Controls.Add(Link("Open Habits →", () => _navigator.Go(Destinations.Habits)));
        _body.Controls.Add(panel);
    }

    private Button AdhereButton(HabitOccurrenceRow item, string result)
    {
        var button = Ui.Action(result);
        button.Enabled = _bsk is not null;
        button.Click += (_, _) =>
        {
            Ui.RunWrite(this, _bsk, "Adhere failed", bsk =>
            {
                bsk.Run("adhere", item.HabitUrn, result, "--on", item.OccurrenceDate.ToString("yyyy-MM-dd"));
                Reload();
            });
        };
        return button;
    }

    private void AddTasks()
    {
        var today = Ui.Today;
        var tasks = _reader.GetSubjects(PilotVocab.Task)
            .Where(t => PilotVocab.IsActiveWorkStatus(PilotVocab.Task, t.Status))
            .ToList();
        var overdue = tasks.Where(t => Ui.TryParseDate(t.Due, out var d) && d < today).ToList();
        var dueToday = tasks.Where(t =>
            (Ui.TryParseDate(t.Due, out var d) && d == today)
            || (Ui.TryParseDate(t.Scheduled, out var s) && s == today)).ToList();

        var panel = Section("Due & overdue Tasks");
        if (overdue.Count == 0 && dueToday.Count == 0)
        {
            panel.Controls.Add(Empty("No overdue or due-today Tasks."));
        }
        else
        {
            foreach (var task in overdue)
            {
                var captured = task;
                panel.Controls.Add(Link($"OVERDUE  {task.Title}  (due {task.Due})",
                    () => _navigator.Go(Destinations.Tasks, captured.Id)));
            }

            foreach (var task in dueToday.Where(t => overdue.All(o => o.Id != t.Id)))
            {
                var captured = task;
                panel.Controls.Add(Link($"TODAY  {task.Title}",
                    () => _navigator.Go(Destinations.Tasks, captured.Id)));
            }
        }

        panel.Controls.Add(Link("Open Tasks →", () => _navigator.Go(Destinations.Tasks)));
        _body.Controls.Add(panel);
    }

    private void AddWork(string heading, string type, string destination)
    {
        var items = _reader.GetSubjects(type)
            .Where(s => !s.Archived && PilotVocab.IsActiveWorkStatus(type, s.Status)
                        && !string.Equals(s.DisplayStatus, "developing", StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToList();
        var developing = _reader.GetSubjects(type)
            .Where(s => string.Equals(s.DisplayStatus, "developing", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();
        var panel = Section(heading);
        if (items.Count == 0)
        {
            panel.Controls.Add(Empty(developing.Count > 0
                ? $"No Active {heading.ToLowerInvariant()} (some still developing)."
                : $"No {heading.ToLowerInvariant()}."));
        }
        else
        {
            foreach (var item in items)
            {
                var captured = item;
                panel.Controls.Add(Link($"{item.Title}  [{item.DisplayStatus}]",
                    () => _navigator.Go(destination, captured.Id)));
            }
        }

        panel.Controls.Add(Link($"Open {heading} →", () => _navigator.Go(destination)));
        _body.Controls.Add(panel);
    }

    private static FlowLayoutPanel Section(string title)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Width = 720,
            Margin = new Padding(0, 0, 0, 12)
        };
        panel.Controls.Add(Ui.Heading(title));
        return panel;
    }

    private static Label Empty(string text)
        => new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(680, 0) };

    private static Label Placeholder(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            Padding = new Padding(8),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Info,
            Margin = new Padding(0, 0, 0, 12)
        };

    private static LinkLabel Link(string text, Action click)
    {
        var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        link.LinkClicked += (_, _) => click();
        return link;
    }
}
