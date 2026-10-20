using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;
using Npgsql;

namespace LifeOs.Pilot.Controls;

/// <summary>
/// TASKS-2: the optional, Toggl-style timer on a Task's detail. Unused, it is one quiet
/// row with Start; once running it shows the session clock with Pause / Resume, Stop,
/// and Complete (stop, then mark the Task Completed). Writes go through
/// <c>bsk timer</c>; recorded time is read back from <c>bsk.v_task_timer</c>.
/// </summary>
internal sealed class TaskTimerBar : FlowLayoutPanel
{
    internal static readonly Color RunningColor = Color.FromArgb(0x1E, 0x7B, 0x3A);
    internal static readonly Color PausedColor = Color.FromArgb(0xA8, 0x5F, 0x00);

    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly Navigator _navigator;
    private readonly Label _state = new() { AutoSize = true, Margin = new Padding(0, 9, 6, 0) };
    private readonly Label _clock = new() { AutoSize = true, Margin = new Padding(0, 4, 10, 0) };
    private readonly Button _start = Ui.Action("Start");
    private readonly Button _pause = Ui.Action("Pause");
    private readonly Button _resume = Ui.Action("Resume");
    private readonly Button _stop = Ui.Action("Stop");
    private readonly Button _complete = Ui.Action("Complete");
    // Zero-size and always visible, so the summary starts its own line whichever
    // buttons are showing (a flow break on a hidden button is ignored).
    private readonly Panel _break = new() { Size = Size.Empty, Margin = Padding.Empty };
    private readonly Label _summary = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(0, 2, 0, 0)
    };
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private readonly ToolTip _tips = new();

    private Guid _id;
    private string? _urn;
    private string? _title;
    private long? _estimate;
    private TaskTimerRow? _timer;

    public TaskTimerBar(SubjectReader reader, BskCli? bsk, Navigator navigator)
    {
        _reader = reader;
        _bsk = bsk;
        _navigator = navigator;

        AutoSize = true;
        WrapContents = true;
        Dock = DockStyle.Top;
        Margin = new Padding(0);
        Padding = new Padding(0, 0, 0, 6);
        Visible = false;

        _state.Font = new Font(Font, FontStyle.Bold);
        _clock.Font = new Font(Font.FontFamily, Font.Size * 1.6f, FontStyle.Bold);
        _start.Click += (_, _) => Run("start");
        _pause.Click += (_, _) => Run("pause");
        _resume.Click += (_, _) => Run("resume");
        _stop.Click += (_, _) => Run("stop");
        _complete.Click += (_, _) => Run("complete");
        _tips.SetToolTip(_start, "Start timing this Task. A timer running on another Task is paused.");
        _tips.SetToolTip(_resume, "Resume this session. A timer running on another Task is paused.");
        _tips.SetToolTip(_pause, "Pause, keeping this session open.");
        _tips.SetToolTip(_stop, "Stop and close this session. The time stays recorded.");
        _tips.SetToolTip(_complete, "Stop the timer and mark the Task Completed.");
        _tick.Tick += (_, _) => RenderTimes();
        _navigator.TimersChanged += OnTimersChanged;

        Controls.Add(_state);
        Controls.Add(_clock);
        Controls.Add(_start);
        Controls.Add(_pause);
        Controls.Add(_resume);
        Controls.Add(_stop);
        Controls.Add(_complete);
        Controls.Add(_break);
        Controls.Add(_summary);
        SetFlowBreak(_break, true);
    }

    /// <summary>Raised after a timer write, so the owning detail and list reload.</summary>
    public event EventHandler? Changed;

    public void Bind(Guid id, string urn, string title, string? estimatedDuration)
    {
        _id = id;
        _urn = urn;
        _title = title;
        _estimate = TimerFormat.ParseEstimateSeconds(estimatedDuration);
        Visible = true;
        Reload();
    }

    public void Unbind()
    {
        _id = Guid.Empty;
        _urn = null;
        _title = null;
        _timer = null;
        _tick.Enabled = false;
        Visible = false;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        // A long summary wraps inside the pane instead of painting past its edge.
        var width = Math.Max(120, ClientSize.Width - Padding.Horizontal - 4);
        if (_summary.MaximumSize.Width != width)
        {
            _summary.MaximumSize = new Size(width, 0);
        }

        base.OnLayout(levent);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _navigator.TimersChanged -= OnTimersChanged;
            _tick.Dispose();
            _tips.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnTimersChanged()
    {
        // Another surface (the shell's running timer, or a start here pausing that
        // one) changed a timer; refresh if this Task's detail is on screen.
        if (_id != Guid.Empty && Visible)
        {
            Reload();
        }
    }

    private void Reload()
    {
        try
        {
            _timer = _reader.GetTimer(_id);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // This database predates the timer (staging before CI migrates it).
            _timer = null;
            _tick.Enabled = false;
            Visible = false;
            return;
        }
        catch (Exception ex)
        {
            _timer = null;
            _summary.Text = $"Timer unavailable: {ex.Message}";
            return;
        }

        Render();
    }

    private void Render()
    {
        var state = _timer?.State ?? TimerFormat.Stopped;
        var running = state == TimerFormat.Running;
        var paused = state == TimerFormat.Paused;
        var active = running || paused;

        _state.Text = running ? "● Running" : paused ? "● Paused" : "Timer";
        _state.ForeColor = running ? RunningColor : paused ? PausedColor : SystemColors.GrayText;
        _clock.Visible = active;
        _start.Visible = !active;
        _pause.Visible = running;
        _resume.Visible = paused;
        _stop.Visible = active;
        _complete.Visible = active;
        foreach (var button in new[] { _start, _pause, _resume, _stop, _complete })
        {
            button.Enabled = _bsk is not null;
        }

        _tick.Enabled = running;
        RenderTimes();
    }

    private void RenderTimes()
    {
        var state = _timer?.State ?? TimerFormat.Stopped;
        var (total, session) = _timer is null
            ? (0L, 0L)
            : TimerFormat.Live(state, _timer.RunningSince, _timer.TotalSeconds, _timer.SessionSeconds, DateTime.UtcNow);
        _clock.Text = TimerFormat.Clock(session);
        _summary.Text = TimerFormat.Summary(
            total, _timer?.Sessions ?? 0, showSessions: state == TimerFormat.Stopped, _estimate);
    }

    private void Run(string action)
    {
        if (_urn is null)
        {
            return;
        }

        var urn = _urn;
        var title = _title ?? urn;
        var changed = false;
        UseWaitCursor = true;
        try
        {
            Ui.RunWrite(this, _bsk, "Timer", bsk =>
            {
                var result = bsk.RunJson<TimerResultJson>("timer", action, urn);
                changed = true;
                if (result.Completed)
                {
                    // Status is read from v_subject_current, rebuilt like any status change.
                    bsk.Run("rebuild");
                    _navigator.Notify(result.TotalSeconds > 0
                        ? $"Completed “{title}” · {TimerFormat.Tracked(result.TotalSeconds)} recorded."
                        : $"Completed “{title}”.");
                }
                else if (result.Paused.Count > 0)
                {
                    _navigator.Notify(
                        $"Paused the timer on {string.Join(", ", result.Paused.Select(p => $"“{p.Title}”"))}.");
                }
            });
        }
        finally
        {
            UseWaitCursor = false;
        }

        // Refresh on a refusal too: it usually means the timer changed elsewhere (the web).
        Reload();
        _navigator.SignalTimersChanged();
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The subset of <c>bsk timer … --json</c> the Pilot uses.</summary>
    private sealed class TimerResultJson
    {
        public long TotalSeconds { get; set; }
        public List<PausedJson> Paused { get; set; } = [];
        public bool Completed { get; set; }
    }

    private sealed class PausedJson
    {
        public string Urn { get; set; } = "";
        public string Title { get; set; } = "";
    }
}
