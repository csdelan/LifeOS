using System.Text;
using System.Text.Json;
using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Dialogs;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Controls;

/// <summary>
/// BROWSE-2 detail: read-only first, explicit Edit / Save / Cancel, dirty-nav, and
/// sections Overview / Relationships / Tags / Journal / History. Reused by Browse
/// and every dedicated screen.
/// </summary>
internal sealed class DetailPane : UserControl
{
    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly Navigator _navigator;
    private readonly Panel _head = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(0, 0, 4, 0)
    };
    private readonly Label _header = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Button _edit = Ui.Action("Edit…");
    private readonly Button _status = Ui.Action("Change status…");
    private readonly Button _archive = Ui.Action("Archive");
    private readonly Button _child = Ui.Action("New child…");
    private readonly Button _materialize = Ui.Action("Materialize…");
    private readonly Button _open = Ui.Action("Open in tab");
    private readonly FlowLayoutPanel _toolbar = new()
    {
        AutoSize = true,
        WrapContents = true,
        Dock = DockStyle.Top,
        Margin = new Padding(0),
        Padding = new Padding(0, 2, 0, 4)
    };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox _overview = ReadOnlyBox();
    private readonly RelationshipEditor _relationships;
    private readonly TagEditor _tags;
    private readonly RichTextBox _journal = ReadOnlyBox();
    private readonly TextBox _journalDraft = new() { Dock = DockStyle.Fill, Multiline = true, Height = 70, Visible = false };
    private readonly Button _journalAppend = Ui.Action("Append");
    private readonly Button _journalReveal = Ui.Action("Append…");
    private readonly RichTextBox _history = ReadOnlyBox();
    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = SystemColors.GrayText,
        Text = "Select an item to see its detail."
    };

    private Guid _id;
    private string? _urn;
    private string? _type;
    private string? _title;
    private bool _archived;
    private bool _loaded;

    public DetailPane(SubjectReader reader, BskCli? bsk, Navigator navigator)
    {
        _reader = reader;
        _bsk = bsk;
        _navigator = navigator;
        _relationships = new RelationshipEditor(reader, bsk) { Dock = DockStyle.Fill };
        _tags = new TagEditor(reader, bsk) { Dock = DockStyle.Fill };

        _header.Font = new Font(Font, FontStyle.Bold);
        _header.AutoSize = false;
        _header.AutoEllipsis = true;
        _edit.Click += (_, _) => DoEdit();
        _status.Click += (_, _) => DoStatus();
        _archive.Click += (_, _) => DoArchive();
        _child.Click += (_, _) => DoChild();
        _materialize.Click += (_, _) => DoMaterialize();
        _open.Click += (_, _) =>
        {
            if (_id != Guid.Empty && _type is not null)
            {
                _navigator.OpenSubject(_type, _id);
            }
        };
        _journalReveal.Click += (_, _) =>
        {
            _journalDraft.Visible = true;
            _journalAppend.Visible = true;
            _journalReveal.Visible = false;
            _journalDraft.Focus();
        };
        _journalAppend.Click += (_, _) => DoJournal();
        _journalAppend.Visible = false;

        Dock = DockStyle.Fill;
        AutoSize = false;
        Padding = new Padding(8, 4, 8, 4);

        var toolbar = _toolbar;
        toolbar.Controls.Add(_edit);
        toolbar.Controls.Add(_status);
        toolbar.Controls.Add(_archive);
        toolbar.Controls.Add(_child);
        toolbar.Controls.Add(_materialize);
        toolbar.Controls.Add(_open);

        var overviewPage = Page("Overview", _overview);
        var relPage = Page("Relationships", _relationships);
        var tagPage = Page("Tags", _tags);

        var journalHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        journalHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        journalHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        journalHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        journalHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var journalBar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top };
        journalBar.Controls.Add(_journalReveal);
        journalBar.Controls.Add(_journalAppend);
        journalHost.Controls.Add(_journal, 0, 0);
        journalHost.Controls.Add(_journalDraft, 0, 1);
        journalHost.Controls.Add(journalBar, 0, 2);
        var journalPage = new TabPage("Journal") { Padding = new Padding(4) };
        journalPage.Controls.Add(journalHost);

        var historyPage = Page("History", _history);
        _tabs.TabPages.Add(overviewPage);
        _tabs.TabPages.Add(relPage);
        _tabs.TabPages.Add(tagPage);
        _tabs.TabPages.Add(journalPage);
        _tabs.TabPages.Add(historyPage);

        // Dock.Top panel stretches to the parent width, so the title can ellipsize
        // and the toolbar can wrap. An AutoSize TableLayoutPanel grows to the
        // unwrapped button row instead, and the parent clips the overflow.
        _header.Dock = DockStyle.Top;
        _header.Height = 28;
        _head.Controls.Add(_toolbar);
        _head.Controls.Add(_header);

        Controls.Add(_tabs);
        Controls.Add(_head);
        Controls.Add(_empty);
        Clear();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        // FlowLayoutPanel AutoSize reports the unwrapped button row; pin the
        // header block to the pane's client width so the title ellipsizes and
        // buttons wrap instead of painting past the right edge.
        var inner = Math.Max(0, DisplayRectangle.Width);
        if (inner <= 0 || _head.MaximumSize.Width == inner)
        {
            return;
        }

        _head.MaximumSize = new Size(inner, 4096);
        _head.Width = inner;
        var content = Math.Max(0, inner - _head.Padding.Horizontal);
        _header.AutoSize = false;
        _header.MaximumSize = new Size(content, 28);
        _header.Width = content;
        _toolbar.MaximumSize = new Size(content, 4096);
        _toolbar.Width = content;
    }

    public bool HasSelection => _id != Guid.Empty;

    public Guid SelectedId => _id;

    public event EventHandler? Changed;

    public void Clear()
    {
        _id = Guid.Empty;
        _urn = null;
        _type = null;
        _title = null;
        _loaded = false;
        _header.Text = "— select a subject —";
        _overview.Clear();
        _journal.Clear();
        _history.Clear();
        _empty.Visible = true;
        _tabs.Visible = false;
        SetWriteEnabled(false);
    }

    public void LoadSubject(Guid id)
    {
        try
        {
            var subject = _reader.GetSubject(id);
            if (subject is null)
            {
                Clear();
                return;
            }

            _id = subject.Id;
            _urn = subject.Urn;
            _type = subject.Type;
            _title = subject.Title;
            _archived = subject.Archived;
            _loaded = true;
            _empty.Visible = false;
            _tabs.Visible = true;
            _header.Text = $"{PilotVocab.Label(subject.Type)} — {subject.Title}";
            _overview.Text = FormatOverview(subject);
            _relationships.Bind(subject.Id, subject.Urn);
            _tags.Bind(subject.Urn, _reader.GetTags(subject.Id));
            LoadJournal();
            LoadHistory(subject);
            SetWriteEnabled(_bsk is not null);
            _status.Enabled = _bsk is not null && PilotVocab.HasStatus(subject.Type);
            _child.Enabled = _bsk is not null && PilotVocab.ChildTypesFor(subject.Type).Count > 0;
            _materialize.Enabled = _bsk is not null && subject.Type == PilotVocab.Appointment
                && !string.IsNullOrWhiteSpace(Attr(subject.Attributes, "recurrence"));
            _archive.Enabled = _bsk is not null && !PilotVocab.AreasArePermanent(subject.Type);
            _archive.Text = subject.Archived ? "Restore" : "Archive";
            _open.Enabled = true;
        }
        catch (Exception ex)
        {
            Ui.ShowError(this, "Read failed", ex);
        }
    }

    private void LoadJournal()
    {
        var entries = _reader.GetJournal(_id);
        if (entries.Count == 0)
        {
            _journal.Text = "(no journal entries)";
            return;
        }

        var text = new StringBuilder();
        foreach (var entry in entries)
        {
            text.AppendLine($"— {entry.OccurredAt:yyyy-MM-dd HH:mm} —");
            text.AppendLine(entry.Content);
            text.AppendLine();
        }

        _journal.Text = text.ToString();
    }

    private void LoadHistory(SubjectDetail subject)
    {
        var text = new StringBuilder();
        text.AppendLine($"Created {subject.CreatedAt:yyyy-MM-dd}");
        text.AppendLine();
        var history = _reader.GetStatusHistory(_id);
        if (history.Count == 0)
        {
            text.AppendLine("No status changes.");
        }
        else
        {
            text.AppendLine("Status history");
            foreach (var entry in history)
            {
                text.AppendLine($"  {entry.OccurredAt:yyyy-MM-dd}  →  {entry.Status}");
            }
        }

        text.AppendLine();
        var concerns = _reader.GetConcerningEvents(_id);
        text.AppendLine($"Concerning events ({concerns.Count})");
        foreach (var concern in concerns)
        {
            text.AppendLine($"  {concern.OccurredAt:yyyy-MM-dd}  {concern.Kind}");
        }

        _history.Text = text.ToString();
    }

    private void SetWriteEnabled(bool on)
    {
        _edit.Enabled = on && _loaded;
        _journalReveal.Enabled = on && _loaded;
        _journalAppend.Enabled = on && _loaded;
    }

    private void DoEdit()
    {
        if (_bsk is null || _urn is null || _type is null)
        {
            return;
        }

        using var dialog = new SubjectEditDialog(_reader, _bsk, _id);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            LoadSubject(_id);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DoStatus()
    {
        if (_bsk is null || _urn is null || _type is null)
        {
            return;
        }

        var next = StatusPickDialog.Show(this, _type, _title ?? _urn);
        if (next is null)
        {
            return;
        }

        if (_type == PilotVocab.Goal && next == "Active")
        {
            var subject = _reader.GetSubject(_id);
            var target = Attr(subject?.Attributes, "target_date");
            if (string.IsNullOrWhiteSpace(target))
            {
                MessageBox.Show(this, "A Goal cannot become Active without a target date. Edit the Goal first.",
                    "Activate Goal", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        Ui.RunWrite(this, _bsk, "Status change failed", bsk =>
        {
            BskWrites.ChangeStatus(bsk, _urn, next);
            LoadSubject(_id);
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void DoArchive()
    {
        if (_bsk is null || _urn is null)
        {
            return;
        }

        var restoring = _archived;
        if (!Ui.ConfirmArchive(this, _title ?? _urn, restoring))
        {
            return;
        }

        var verb = restoring ? "restore" : "archive";
        Ui.RunWrite(this, _bsk, restoring ? "Restore failed" : "Archive failed", bsk =>
        {
            bsk.Run(verb, _urn);
            // The owning list reloads via Changed and drops hidden items; do not
            // keep the archived subject selected in the detail pane.
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void DoChild()
    {
        if (_bsk is null || _urn is null || _type is null)
        {
            return;
        }

        var created = NewSubjectDialog.ShowChild(this, _reader, _bsk, _urn, _type, _title ?? _urn);
        if (created is not null)
        {
            LoadSubject(_id);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DoMaterialize()
    {
        if (_bsk is null || _urn is null)
        {
            return;
        }

        var through = DatePrompt.Show(this, "Materialize occurrences", "Create occurrences through:", Ui.Today.AddMonths(3));
        if (through is null)
        {
            return;
        }

        Ui.RunWrite(this, _bsk, "Materialize failed", bsk =>
        {
            bsk.Run("materialize", _urn, "--through", through);
            LoadSubject(_id);
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void DoJournal()
    {
        if (_bsk is null || _urn is null)
        {
            return;
        }

        var text = _journalDraft.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        Ui.RunWrite(this, _bsk, "Journal failed", bsk =>
        {
            bsk.AppendJournal(_urn, text);
            _journalDraft.Clear();
            _journalDraft.Visible = false;
            _journalAppend.Visible = false;
            _journalReveal.Visible = true;
            LoadJournal();
        });
    }

    private static string FormatOverview(SubjectDetail subject)
    {
        var text = new StringBuilder();
        text.AppendLine(subject.Urn);
        text.AppendLine();
        if (!string.IsNullOrWhiteSpace(subject.Statement))
        {
            text.AppendLine("Statement");
            text.AppendLine($"  {subject.Statement}");
            text.AppendLine();
        }

        if (PilotVocab.HasStatus(subject.Type))
        {
            text.AppendLine($"Status    {subject.DisplayStatus}");
        }

        if (!string.IsNullOrWhiteSpace(subject.AreaName) || !string.IsNullOrWhiteSpace(subject.Area))
        {
            text.AppendLine($"Area      {subject.AreaName ?? subject.Area}");
        }

        if (subject.Archived)
        {
            text.AppendLine("Archived  yes");
        }

        foreach (var (key, value) in ParseAttrs(subject.Attributes))
        {
            if (key is "statement" or "area" or "expected_cadence" or "next_review_at" or "people" or "recurrence")
            {
                continue;
            }

            text.AppendLine($"{key,-10}{value}");
        }

        text.AppendLine($"Created   {subject.CreatedAt:yyyy-MM-dd}");
        return text.ToString();
    }

    internal static string? Attr(string? json, string key)
    {
        foreach (var (k, v) in ParseAttrs(json))
        {
            if (k == key)
            {
                return v;
            }
        }

        return null;
    }

    internal static IEnumerable<(string Key, string Value)> ParseAttrs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            yield break;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                yield break;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var value = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? "",
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Number => prop.Value.ToString(),
                    _ => prop.Value.ToString()
                };
                yield return (prop.Name, value);
            }
        }
    }

    private static TabPage Page(string title, Control body)
    {
        var page = new TabPage(title) { Padding = new Padding(4) };
        body.Dock = DockStyle.Fill;
        page.Controls.Add(body);
        return page;
    }

    private static RichTextBox ReadOnlyBox()
        => new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = new Font(FontFamily.GenericMonospace, 9f)
        };
}
