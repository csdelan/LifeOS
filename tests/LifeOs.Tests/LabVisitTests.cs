using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LifeOs.Application.Measurements;
using LifeOs.Cli.Commands;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// MEASURE-4, D20 (measurements plan M4, §4.9, migration 0053): lab visits. A visit is a versioned
/// <c>lab_visit</c> event read through <c>bsk.v_lab_visit</c>; its results are metric readings
/// carrying the visit and the range printed beside them, flagged against that range for good, and
/// each test's own range follows its newest report. Every value here is made up, not Chris's.
/// Each test runs in a database of its own (<see cref="DomainTestDatabase"/>, home zone UTC),
/// since a test's name is unique.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LabVisitTests(PostgresFixture postgres)
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% a made-up lab report\n%%EOF\n");

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_visit_makes_its_tests_from_the_starter_list_and_flags_each_result_by_its_printed_range()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var visits = db.Get<LabVisitService>();
        var collected = DateTimeOffset.UtcNow.AddDays(-3);

        var read = await visits.LogAsync(new LabVisitEntry(collected, " Quest Diagnostics ", "Fasting", [
            new("glucose", 104m, 65m, 99m),
            new("Creatinine", 1.02m, 0.6m, 1.29m),
            new("LDL", 131m, null, 99m),
            new("HDL", 52m, 39m),
            new("A1c", 5.4m),
            new("TSH", 2.1m, 0.45m, 4.5m, Unit: "mIU/L")
        ]), Ct);

        Assert.Equal(("Quest Diagnostics", "Fasting", 0), (read.Visit.Lab, read.Visit.Note, read.Visit.Versions - 1));
        Assert.Equal(collected.ToUnixTimeMilliseconds(), read.Visit.CollectedAt.ToUnixTimeMilliseconds());

        // As the report prints them: the CMP, the lipid panel, A1c, then the others.
        Assert.Equal(
            ["Glucose", "Creatinine", "HDL", "LDL", "A1c", "TSH"],
            read.Results.Select(r => r.Metric.Name));
        Assert.Equal(
            [("high", "cmp"), (null, "cmp"), (null, "lipids"), ("high", "lipids"), (null, "a1c"), (null, "other")],
            read.Results.Select(r => (r.Flag, r.Metric.Panel)));
        Assert.Equal(2, read.Outside);
        Assert.All(read.Results, r => Assert.Equal((read.Visit.Id, read.Visit.CollectedAt), (r.Reading.VisitId!.Value, r.Reading.TakenAt)));

        // Each test came from the starter list with its unit and decimals, or with the unit given
        // and the places its values use; each takes the range printed on this, its newest, report.
        var metrics = await db.Get<MetricService>().ListAsync(cancellationToken: Ct);
        var glucose = metrics.Single(m => m.Name == "Glucose");
        Assert.Equal(("mg/dL", 0, "glucose", 65m, 99m), (glucose.Unit, glucose.Decimals, glucose.Catalog, glucose.Low, glucose.High));
        var tsh = metrics.Single(m => m.Name == "TSH");
        Assert.Equal(("mIU/L", 2, null, "other", 0.45m, 4.5m), (tsh.Unit, tsh.Decimals, tsh.Catalog, tsh.Panel, tsh.Low, tsh.High));

        // A1c was given no range, so it's flagged against the starter list's (up to 5.6).
        var a1c = metrics.Single(m => m.Name == "A1c");
        Assert.Equal((null, 5.6m), (a1c.Low, a1c.High));

        // A reading that isn't a lab result follows its metric's range as it is now.
        var weight = await db.Get<MetricService>().AddAsync("Weight", cancellationToken: Ct);
        Assert.False(weight.IsLabTest);
        Assert.True(glucose.IsLabTest);
        var meter = await db.Get<MetricReadingService>().LogAsync("Glucose", 120m, cancellationToken: Ct);
        Assert.Equal(("high", null), (glucose.Flag(meter), meter.VisitId));
    }

    [Fact]
    public async Task A_bad_visit_is_refused_in_words_and_writes_nothing()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var visits = db.Get<LabVisitService>();
        await db.Get<MetricService>().AddAsync("Sodium", cancellationToken: Ct);
        await db.Get<MetricService>().ArchiveAsync("Sodium", Ct);

        async Task Refused<T>(LabVisitEntry entry, string words) where T : Exception
        {
            var refusal = await Assert.ThrowsAsync<T>(() => visits.LogAsync(entry, Ct));
            Assert.Contains(words, refusal.Message);
            Assert.Empty((await visits.ListAsync(Ct)).Visits);
            Assert.Equal(["Sodium"], (await db.Get<MetricService>().ListAsync(true, Ct)).Select(m => m.Name));
        }

        LabVisitEntry Visit(params LabResultEntry[] results) => new(null, "Quest", null, results);

        await Refused<ArgumentException>(Visit(new LabResultEntry("Glucose", 104.5m)), "Glucose takes a whole number");
        await Refused<ArgumentException>(Visit(new LabResultEntry("Glucose", 104m), new LabResultEntry("glucose", 105m)), "Glucose is given twice");
        await Refused<ArgumentException>(Visit(new LabResultEntry("Glucose", 104m, 99m, 65m)), "Glucose: The normal range's low end (99) is above its high end (65)");
        await Refused<ArgumentException>(Visit(new LabResultEntry("Glucose", 104m), new LabResultEntry("TSH", 2.1m)), "'TSH' isn't a test you track yet: give its unit");
        await Refused<InvalidOperationException>(Visit(new LabResultEntry("Glucose", 104m), new LabResultEntry("Sodium", 140m)), "'Sodium' is archived");
        await Refused<ArgumentException>(
            new LabVisitEntry(DateTimeOffset.UtcNow.AddHours(1), null, null, [new("Glucose", 104m)]), "not in the future");
        await Refused<ArgumentException>(Visit(new LabResultEntry("Glucose", 104m)) with { Lab = new string('q', 61) }, "A lab's name is up to 60 characters");
        await Refused<ArgumentException>(Visit(new LabResultEntry("Glucose", 104m)) with { Note = "two\nlines" }, "A visit's note is one line");
        await Refused<ArgumentException>(
            Visit(Enumerable.Range(0, 51).Select(i => new LabResultEntry($"Test {i}", 1m, Unit: "U")).ToArray()),
            "A visit takes up to 50 results");
    }

    [Fact]
    public async Task An_edit_moves_its_results_keeps_unchanged_ones_and_deletes_those_left_out()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var visits = db.Get<LabVisitService>();
        var first = await visits.LogAsync(new LabVisitEntry(DateTimeOffset.UtcNow.AddDays(-2), "Quest", "Fasting", [
            new("Glucose", 104m, 65m, 99m), new("LDL", 131m, null, 99m), new("HDL", 52m, 39m)
        ]), Ct);
        var glucose = first.Results.Single(r => r.Metric.Name == "Glucose").Reading;
        var ldl = first.Results.Single(r => r.Metric.Name == "LDL").Reading;

        // The same visit given back writes nothing.
        var same = await visits.SaveAsync(first.Visit.Id.ToString()[..6], new LabVisitEntry(null, null, null,
            first.Results.Select(r => new LabResultEntry(r.Metric.Name, r.Reading.Value, r.Reading.Low, r.Reading.High, Reading: r.Reading.Id.ToString())).ToList()), Ct);
        Assert.Equal((1, 1), (same.Visit.Versions, same.Results.Single(r => r.Metric.Name == "Glucose").Reading.Versions));

        // A new time moves every result; LDL's value changes in place (by test name), HDL is left
        // out, so it's deleted; a blank note clears it.
        var moved = DateTimeOffset.UtcNow.AddDays(-1);
        var edited = await visits.SaveAsync(first.Visit.Id.ToString(), new LabVisitEntry(moved, null, " ", [
            new("Glucose", 104m, 65m, 99m, Reading: glucose.Id.ToString()),
            new("LDL", 128m, null, 99m)
        ]), Ct);
        Assert.Equal(("Quest", null, 2), (edited.Visit.Lab, edited.Visit.Note, edited.Visit.Versions));
        Assert.Equal(["Glucose", "LDL"], edited.Results.Select(r => r.Metric.Name));
        Assert.All(edited.Results, r => Assert.Equal(moved.ToUnixTimeMilliseconds(), r.Reading.TakenAt.ToUnixTimeMilliseconds()));
        Assert.Equal((ldl.Id, 128m, 2), (edited.Results[1].Reading.Id, edited.Results[1].Reading.Value, edited.Results[1].Reading.Versions));
        Assert.Empty((await db.Get<MetricReadingService>().ListAsync("HDL", null, Ct)).Readings);

        // A result named by another visit's id is refused.
        var other = await visits.LogAsync(new LabVisitEntry(null, null, null, [new("A1c", 5.4m)]), Ct);
        Assert.Contains("isn't one of this visit's results", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            visits.SaveAsync(first.Visit.Id.ToString(), new LabVisitEntry(null, null, null,
                [new("A1c", 5.5m, Reading: other.Results[0].Reading.Id.ToString())]), Ct))).Message);

        // A result's time is its visit's; its value can still change alone, keeping its visit and range.
        var readings = db.Get<MetricReadingService>();
        Assert.Contains("part of a lab visit", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            readings.EditAsync(glucose.Id.ToString(), new ReadingEdit(TakenAt: DateTimeOffset.UtcNow), Ct))).Message);
        var fixedValue = await readings.EditAsync(glucose.Id.ToString(), new ReadingEdit(Value: 98m), Ct);
        Assert.Equal((first.Visit.Id, 65m, 99m), (fixedValue.VisitId!.Value, fixedValue.Low!.Value, fixedValue.High!.Value));

        // Deleting the visit deletes its results.
        await visits.DeleteAsync(first.Visit.Id.ToString(), Ct);
        Assert.Equal([other.Visit.Id], (await visits.ListAsync(Ct)).Visits.Select(v => v.Visit.Id));
        Assert.Empty((await readings.ListAsync("Glucose", null, Ct)).Readings);
    }

    [Fact]
    public async Task A_tests_range_follows_its_newest_report_while_old_results_keep_theirs()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var visits = db.Get<LabVisitService>();
        var service = db.Get<MetricService>();
        var lastYear = await visits.LogAsync(
            new LabVisitEntry(DateTimeOffset.UtcNow.AddDays(-365), "Quest", null, [new("Glucose", 99m, 65m, 99m)]), Ct);
        var now = await visits.LogAsync(
            new LabVisitEntry(DateTimeOffset.UtcNow.AddDays(-1), "LabCorp", null, [new("Glucose", 99m, 70m, 98m)]), Ct);
        Assert.Equal((70m, 98m), ((await service.ResolveAsync("Glucose", Ct)).Low, (await service.ResolveAsync("Glucose", Ct)).High));

        // The same 99 is normal on last year's report and high on this one.
        Assert.Null(lastYear.Results[0].Flag);
        Assert.Equal("high", now.Results[0].Flag);
        var list = await visits.ListAsync(Ct);
        Assert.Equal([1, 0], list.Visits.Select(v => v.Outside));

        // Correcting the older report leaves the range alone; deleting the newer one goes back to it.
        await visits.SaveAsync(lastYear.Visit.Id.ToString(), new LabVisitEntry(null, null, null,
            [new("Glucose", 99m, 65m, 100m)]), Ct);
        Assert.Equal(98m, (await service.ResolveAsync("Glucose", Ct)).High);
        await visits.DeleteAsync(now.Visit.Id.ToString(), Ct);
        var glucose = await service.ResolveAsync("Glucose", Ct);
        Assert.Equal((65m, 100m), (glucose.Low, glucose.High));
    }

    [Fact]
    public async Task A_report_is_a_pdf_or_photo_kept_through_edits_and_taken_off()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var visits = db.Get<LabVisitService>();
        var visit = await visits.LogAsync(new LabVisitEntry(DateTimeOffset.UtcNow.AddDays(-1), "Quest", null, [new("A1c", 5.4m)]), Ct);
        var id = visit.Visit.Id.ToString();

        var attached = await visits.AttachReportAsync(id, Pdf, @"C:\Downloads\labs.pdf", Ct);
        Assert.Equal(("labs.pdf", "application/pdf", (long)Pdf.Length), (attached.Visit.Report!.Filename, attached.Visit.Report.ContentType, attached.Visit.Report.ByteSize));
        Assert.Equal(visit.Visit.CollectedAt, attached.Visit.CollectedAt);

        // The same bytes again change nothing; an edit keeps the report.
        var again = await visits.AttachReportAsync(id, Pdf, "labs.pdf", Ct);
        Assert.Equal((attached.Visit.Report.ArtifactId, attached.Visit.Versions), (again.Visit.Report!.ArtifactId, again.Visit.Versions));
        var edited = await visits.SaveAsync(id, new LabVisitEntry(null, "Quest Diagnostics", null, [new("A1c", 5.4m)]), Ct);
        Assert.Equal(("Quest Diagnostics", attached.Visit.Report.ArtifactId), (edited.Visit.Lab, edited.Visit.Report!.ArtifactId));

        Assert.Contains("isn't a PDF or a photo", (await Assert.ThrowsAsync<ArgumentException>(() =>
            visits.AttachReportAsync(id, Encoding.ASCII.GetBytes("plain text"), "labs.txt", Ct))).Message);
        Assert.Contains("a report can be at most 10 MB", (await Assert.ThrowsAsync<ArgumentException>(() =>
            visits.AttachReportAsync(id, new byte[LabLimits.MaxReportBytes + 1], "big.pdf", Ct))).Message);

        var removed = await visits.RemoveReportAsync(id, Ct);
        Assert.Null(removed.Visit.Report);

        // The same bytes come back as the report, pointing at what's stored.
        var back = await visits.AttachReportAsync(id, Pdf, "labs.pdf", Ct);
        Assert.Equal(attached.Visit.Report.ArtifactId, back.Visit.Report!.ArtifactId);
    }

    [Fact]
    public void Bsk_lab_reads_results_as_test_value_and_range()
    {
        var units = LabCommand.ParseUnits(["TSH = mIU/L"]);
        Assert.Equal(new LabResultEntry("Glucose", 104m, 70m, 100m), LabCommand.ParseResult(" Glucose = 104@70..100 ", units));
        Assert.Equal(new LabResultEntry("LDL", 131m, null, 99m), LabCommand.ParseResult("LDL=131@..99", units));
        Assert.Equal(new LabResultEntry("eGFR", 92m, 60m), LabCommand.ParseResult("eGFR=92@60..", units));
        Assert.Equal(new LabResultEntry("tsh", 2.1m, Unit: "mIU/L"), LabCommand.ParseResult("tsh=2.1", units));
        Assert.Contains("A result is test=value", Assert.Throws<ArgumentException>(() => LabCommand.ParseResult("Glucose 104", units)).Message);
        Assert.Contains("low..high", Assert.Throws<ArgumentException>(() => LabCommand.ParseResult("Glucose=104@70-100", units)).Message);
        Assert.Contains("isn't a number", Assert.Throws<ArgumentException>(() => LabCommand.ParseResult("Glucose=high", units)).Message);
        Assert.Contains("--unit takes test=unit", Assert.Throws<ArgumentException>(() => LabCommand.ParseUnits(["TSH"])).Message);
    }

    [Fact]
    public async Task The_api_logs_lists_edits_and_deletes_a_visit_with_its_report()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var collected = DateTimeOffset.UtcNow.AddDays(-2);

        var logged = (await JsonAsync(await host.Client.PostAsJsonAsync("/api/measurements/visits", new
        {
            collectedAt = collected,
            lab = "Quest",
            results = new object[]
            {
                new { metric = "Glucose", value = 104, low = 65, high = 99 },
                new { metric = "LDL", value = 131, high = 99 },
                new { metric = "Vitamin D", value = 32.5, low = 30, high = 100, unit = "ng/mL" }
            }
        }, Ct))).GetProperty("visit");
        var id = logged.GetProperty("id").GetString()!;
        Assert.Equal(("Quest", 2, 3), (logged.GetProperty("lab").GetString(), logged.GetProperty("outside").GetInt32(),
            logged.GetProperty("results").GetArrayLength()));
        var glucose = logged.GetProperty("results")[0];
        Assert.Equal((id, 65m, 99m, "high"), (glucose.GetProperty("visitId").GetString(), glucose.GetProperty("low").GetDecimal(),
            glucose.GetProperty("high").GetDecimal(), glucose.GetProperty("flag").GetString()));

        // Lab tests carry their panel.
        var metrics = (await JsonAsync(await host.Client.GetAsync("/api/measurements/metrics", Ct))).EnumerateArray().ToList();
        Assert.Equal(["cmp", "lipids", "other"],
            new[] { "Glucose", "LDL", "Vitamin D" }.Select(n => metrics.Single(m => m.GetProperty("name").GetString() == n).GetProperty("panel").GetString()));

        // A result with no value is a 400, naming it.
        var noValue = await host.Client.PostAsJsonAsync("/api/measurements/visits", new { results = new[] { new { metric = "HDL" } } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noValue.StatusCode);
        Assert.Contains("Give HDL's result", await noValue.Content.ReadAsStringAsync(Ct));

        // The report uploads as multipart, and a file that isn't one is a 400.
        using (var form = Report(Pdf, "application/pdf", "labs.pdf"))
        {
            var withReport = (await JsonAsync(await host.Client.PostAsync($"/api/measurements/visits/{id}/report", form, Ct))).GetProperty("visit");
            Assert.Equal("application/pdf", withReport.GetProperty("report").GetProperty("contentType").GetString());
        }

        using (var form = Report(Encoding.ASCII.GetBytes("not a report"), "application/pdf", "labs.pdf"))
        {
            var bad = await host.Client.PostAsync($"/api/measurements/visits/{id}/report", form, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }

        // An edit keeps the results it names and deletes the rest.
        var edited = (await JsonAsync(await host.Client.PostAsJsonAsync($"/api/measurements/visits/{id}", new
        {
            note = "Fasting",
            results = new object[] { new { metric = "Glucose", value = 96, low = 65, high = 99, reading = glucose.GetProperty("id").GetString() } }
        }, Ct))).GetProperty("visit");
        Assert.Equal(("Fasting", 0, 1), (edited.GetProperty("note").GetString(), edited.GetProperty("outside").GetInt32(),
            edited.GetProperty("results").GetArrayLength()));
        Assert.Equal(JsonValueKind.Object, edited.GetProperty("report").ValueKind);

        var list = await JsonAsync(await host.Client.GetAsync("/api/measurements/visits", Ct));
        Assert.Equal([id], list.GetProperty("visits").EnumerateArray().Select(v => v.GetProperty("id").GetString()));

        await JsonAsync(await host.Client.PostAsync($"/api/measurements/visits/{id}/report/delete", null, Ct));
        var deleted = await JsonAsync(await host.Client.PostAsync($"/api/measurements/visits/{id}/delete", null, Ct));
        Assert.Equal(JsonValueKind.Null, deleted.GetProperty("visit").ValueKind);
        Assert.Equal(0, (await JsonAsync(await host.Client.GetAsync("/api/measurements/visits", Ct))).GetProperty("visits").GetArrayLength());
        var gone = await host.Client.GetAsync($"/api/measurements/visits/{id}", Ct);
        Assert.False(gone.IsSuccessStatusCode);
    }

    private static MultipartFormDataContent Report(byte[] bytes, string contentType, string filename)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "report", filename);
        return form;
    }

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, text);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
