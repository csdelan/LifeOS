using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Measurements;

/// <summary>
/// What to change about a metric. Each field left null keeps its value; <paramref name="ClearTarget"/>
/// removes the target.
/// </summary>
public sealed record MetricChange(
    string? Name = null,
    string? Unit = null,
    int? Decimals = null,
    decimal? Target = null,
    bool ClearTarget = false,
    string? Better = null)
{
    /// <summary>Whether anything is asked for.</summary>
    public bool IsEmpty => Name is null && Unit is null && Decimals is null && Target is null && !ClearTarget && Better is null;
}

/// <summary>
/// The metrics Chris measures (MEASURE-1, D20, measurements plan §4.1), behind <c>bsk metric</c>
/// and <c>/api/measurements/metrics</c>. A metric is a <c>Metric</c> subject: its title is its
/// name, and <c>unit</c>, <c>decimals</c>, <c>target</c>, <c>better</c> and <c>catalog</c> are
/// attributes only this service writes, so <c>bsk set</c> refuses them. A name from the starter
/// list (<see cref="MetricCatalog"/>) fills in what isn't given. A metric has no status; archiving
/// it (D9) stops new readings. A name is unique among the metrics not archived, and the unit is
/// locked once there are readings. Refusals are in words, with nothing written.
/// </summary>
public sealed class MetricService(
    SubjectService subjects, ISubjectRepository repository, IMetricRepository metrics, ArchiveService archive)
{
    /// <summary>The metrics by name; archived ones only when asked.</summary>
    public async Task<IReadOnlyList<Metric>> ListAsync(
        bool includeArchived = false, CancellationToken cancellationToken = default)
        => (await metrics.GetMetricsAsync(cancellationToken)).Where(m => includeArchived || !m.Archived).ToList();

    /// <summary>
    /// Adds a metric called <paramref name="name"/>. A starter-list name (Weight) fills in the
    /// unit, decimals and which way is better when they aren't given; any other needs a unit.
    /// </summary>
    public async Task<Metric> AddAsync(
        string name, string? unit = null, int? decimals = null, decimal? target = null, string? better = null,
        CancellationToken cancellationToken = default)
    {
        var title = MetricLimits.CheckName(name);
        var template = MetricCatalog.Find(title);
        var keptUnit = MetricLimits.CheckUnit(unit ?? template?.Unit);
        var places = MetricLimits.CheckDecimals(decimals ?? template?.Decimals ?? MetricLimits.DefaultDecimals);
        var direction = better is null ? template?.Better : MetricLimits.CheckBetter(better);

        var attributes = new JsonObject
        {
            [MetricLimits.UnitKey] = keptUnit,
            [MetricLimits.DecimalsKey] = places
        };
        if (target is { } t)
        {
            attributes[MetricLimits.TargetKey] = MetricLimits.CheckValue(t, places, "The target");
        }

        if (direction is not null)
        {
            attributes[MetricLimits.BetterKey] = direction;
        }

        if (template is not null)
        {
            attributes[MetricLimits.CatalogKey] = template.Key;
        }

        await CheckFreeAsync(title, except: null, cancellationToken);
        var subject = await subjects.CreateAsync(
            SubjectTypes.Metric, title, attributes.ToJsonString(), cancellationToken: cancellationToken);
        return await ReadAsync(subject.Id, cancellationToken);
    }

    /// <summary>
    /// Renames a metric or changes its unit, decimals, target or which way is better. Refused
    /// when nothing is asked, for a unit change once the metric has readings, and for a target
    /// with more decimal places than the metric takes.
    /// </summary>
    public async Task<Metric> SetAsync(
        string reference, MetricChange change, CancellationToken cancellationToken = default)
    {
        if (change.IsEmpty)
        {
            throw new ArgumentException("Say what to change: a name, a unit, decimals, a target or which way is better.");
        }

        if (change.Target is not null && change.ClearTarget)
        {
            throw new ArgumentException("Set a target or clear it, not both.");
        }

        var metric = await ResolveAsync(reference, cancellationToken);
        var title = change.Name is null ? null : MetricLimits.CheckName(change.Name);
        var places = change.Decimals is { } d ? MetricLimits.CheckDecimals(d) : metric.Decimals;
        var patch = new JsonObject();
        var removed = new List<string>();

        if (change.Unit is not null)
        {
            var unit = MetricLimits.CheckUnit(change.Unit);
            if (unit != metric.Unit)
            {
                var count = (await metrics.CountReadingsAsync(cancellationToken)).GetValueOrDefault(metric.Id);
                if (count > 0)
                {
                    throw new InvalidOperationException(
                        $"{metric.Name} has {Plural(count, "reading")} in {metric.Unit}, so its unit stays: make a new metric for {unit}.");
                }

                patch[MetricLimits.UnitKey] = unit;
            }
        }

        if (change.Decimals is not null)
        {
            patch[MetricLimits.DecimalsKey] = places;
        }

        if (change.Target is { } target)
        {
            patch[MetricLimits.TargetKey] = MetricLimits.CheckValue(target, places, "The target");
        }
        else if (change.ClearTarget)
        {
            removed.Add(MetricLimits.TargetKey);
        }
        else if (change.Decimals is not null && metric.Target is { } kept)
        {
            // Fewer decimal places mustn't leave a target the metric can't show.
            MetricLimits.CheckValue(kept, places, "The target");
        }

        if (change.Better is not null)
        {
            patch[MetricLimits.BetterKey] = MetricLimits.CheckBetter(change.Better);
        }

        if (title is not null && title != metric.Name)
        {
            await CheckFreeAsync(title, except: metric.Id, cancellationToken);
            if (!await repository.RenameAsync(metric.Id, title, cancellationToken))
            {
                throw new SubjectNotFoundException(metric.Urn);
            }
        }

        if ((patch.Count > 0 || removed.Count > 0)
            && !await repository.UpdateAttributesAsync(metric.Id, patch.ToJsonString(), removed, cancellationToken))
        {
            throw new SubjectNotFoundException(metric.Urn);
        }

        return await ReadAsync(metric.Id, cancellationToken);
    }

    /// <summary>Archives a metric: it takes no new readings, and its history stays.</summary>
    public async Task<Metric> ArchiveAsync(string reference, CancellationToken cancellationToken = default)
    {
        var metric = await ResolveAsync(reference, cancellationToken);
        if (!metric.Archived)
        {
            await archive.ArchiveAsync(metric.Urn, cancellationToken);
        }

        return await ReadAsync(metric.Id, cancellationToken);
    }

    /// <summary>
    /// Brings an archived metric back. Refused while another metric uses its name;
    /// <see cref="ArchiveService"/> checks that, so <c>bsk restore</c> and <c>/api/restore</c>
    /// refuse it too.
    /// </summary>
    public async Task<Metric> RestoreAsync(string reference, CancellationToken cancellationToken = default)
    {
        var metric = await ResolveAsync(reference, cancellationToken, preferArchived: true);
        if (metric.Archived)
        {
            await archive.RestoreAsync(metric.Urn, cancellationToken);
        }

        return await ReadAsync(metric.Id, cancellationToken);
    }

    /// <summary>
    /// The metric <paramref name="reference"/> names: its id, its urn, its short id (the urn's hex
    /// tail) or its name in any case — one not archived first, then an archived one. Throws
    /// <see cref="SubjectNotFoundException"/> when none matches.
    /// </summary>
    public async Task<Metric> ResolveAsync(
        string reference, CancellationToken cancellationToken = default, bool preferArchived = false)
        => Resolve(await metrics.GetMetricsAsync(cancellationToken), reference, preferArchived);

    /// <summary>The metric among <paramref name="all"/> that <paramref name="reference"/> names, as <see cref="ResolveAsync"/>.</summary>
    public static Metric Resolve(IReadOnlyList<Metric> all, string reference, bool preferArchived = false)
    {
        var wanted = reference?.Trim() ?? "";
        if (wanted.Length == 0)
        {
            throw new ArgumentException("Say which metric, by its name.");
        }

        if (Guid.TryParse(wanted, out var id))
        {
            return all.FirstOrDefault(m => m.Id == id) ?? throw new SubjectNotFoundException(reference!);
        }

        var byUrn = all.FirstOrDefault(m =>
            string.Equals(m.Urn, wanted, StringComparison.OrdinalIgnoreCase)
            || (Urns.IsShortId(wanted) && m.Urn.EndsWith("-" + wanted, StringComparison.OrdinalIgnoreCase)));
        if (byUrn is not null)
        {
            return byUrn;
        }

        return all.Where(m => Same(m.Name, wanted)).OrderBy(m => m.Archived != preferArchived).FirstOrDefault()
               ?? throw new SubjectNotFoundException(reference!);
    }

    private async Task CheckFreeAsync(string name, Guid? except, CancellationToken cancellationToken)
    {
        var taken = (await ListAsync(cancellationToken: cancellationToken))
            .FirstOrDefault(m => m.Id != except && Same(m.Name, name));
        if (taken is not null)
        {
            throw new InvalidOperationException($"You already have a metric called '{taken.Name}'.");
        }
    }

    private async Task<Metric> ReadAsync(Guid id, CancellationToken cancellationToken)
        => await metrics.GetMetricAsync(id, cancellationToken) ?? throw new SubjectNotFoundException(id.ToString());

    private static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
