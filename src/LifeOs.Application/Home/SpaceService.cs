using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Home;

/// <summary>
/// The home's spaces (HOME-1, D18, environment plan §4.1), behind <c>bsk space</c> and
/// <c>/api/environment/spaces</c>. A space is a <c>Space</c> subject: its title is its name, and
/// <c>weight</c> (1–10) and <c>pace</c> (1–5) are attributes only this service writes, so
/// <c>bsk set</c> refuses them. A space has no status; archiving it (D9) takes it out of the Home
/// Score from that day. A name is unique among the spaces not archived. Refusals are in words,
/// with nothing written.
/// </summary>
public sealed class SpaceService(
    SubjectService subjects, ISubjectRepository repository, ISpaceRepository spaces, ArchiveService archive)
{
    /// <summary>The spaces by name; archived ones only when asked.</summary>
    public async Task<IReadOnlyList<Space>> ListAsync(
        bool includeArchived = false, CancellationToken cancellationToken = default)
        => (await spaces.GetSpacesAsync(cancellationToken)).Where(s => includeArchived || !s.Archived).ToList();

    /// <summary>
    /// Adds a space called <paramref name="name"/>, weighted <paramref name="weight"/> (3 when
    /// not given) and paced <paramref name="pace"/> (3 when not given).
    /// </summary>
    public async Task<Space> AddAsync(
        string name, int? weight = null, int? pace = null, CancellationToken cancellationToken = default)
    {
        var title = SpaceLimits.CheckName(name);
        var attributes = new JsonObject
        {
            [SpaceLimits.WeightKey] = SpaceLimits.CheckWeight(weight ?? SpaceLimits.DefaultWeight),
            [SpaceLimits.PaceKey] = SpaceLimits.CheckPace(pace ?? SpaceLimits.DefaultPace)
        };
        await CheckFreeAsync(title, except: null, cancellationToken);

        var subject = await subjects.CreateAsync(
            SubjectTypes.Space, title, attributes.ToJsonString(), cancellationToken: cancellationToken);
        return await ReadAsync(subject.Id, cancellationToken);
    }

    /// <summary>
    /// Renames, reweights or re-paces a space. A weight or pace applies to all of history, as
    /// any retune does (environment plan §2.2). Refused when nothing is given.
    /// </summary>
    public async Task<Space> SetAsync(
        string reference, string? name = null, int? weight = null, int? pace = null,
        CancellationToken cancellationToken = default)
    {
        if (name is null && weight is null && pace is null)
        {
            throw new ArgumentException("Say what to change: a name, a weight or a pace.");
        }

        var space = await ResolveAsync(reference, cancellationToken);
        var title = name is null ? null : SpaceLimits.CheckName(name);
        var patch = new JsonObject();
        if (weight is { } w)
        {
            patch[SpaceLimits.WeightKey] = SpaceLimits.CheckWeight(w);
        }

        if (pace is { } p)
        {
            patch[SpaceLimits.PaceKey] = SpaceLimits.CheckPace(p);
        }

        if (title is not null && title != space.Name)
        {
            await CheckFreeAsync(title, except: space.Id, cancellationToken);
            if (!await repository.RenameAsync(space.Id, title, cancellationToken))
            {
                throw new SubjectNotFoundException(space.Urn);
            }
        }

        if (patch.Count > 0 && !await repository.UpdateAttributesAsync(space.Id, patch.ToJsonString(), [], cancellationToken))
        {
            throw new SubjectNotFoundException(space.Urn);
        }

        return await ReadAsync(space.Id, cancellationToken);
    }

    /// <summary>Archives a space: it leaves the Home Score from today, and its ratings stay.</summary>
    public async Task<Space> ArchiveAsync(string reference, CancellationToken cancellationToken = default)
    {
        var space = await ResolveAsync(reference, cancellationToken);
        if (!space.Archived)
        {
            await archive.ArchiveAsync(space.Urn, cancellationToken);
        }

        return await ReadAsync(space.Id, cancellationToken);
    }

    /// <summary>
    /// Brings an archived space back: it counts again for all of history. Refused while another
    /// space uses its name; <see cref="ArchiveService"/> checks that, so <c>bsk restore</c> and
    /// <c>/api/restore</c> refuse it too.
    /// </summary>
    public async Task<Space> RestoreAsync(string reference, CancellationToken cancellationToken = default)
    {
        var space = await ResolveAsync(reference, cancellationToken, preferArchived: true);
        if (space.Archived)
        {
            await archive.RestoreAsync(space.Urn, cancellationToken);
        }

        return await ReadAsync(space.Id, cancellationToken);
    }

    /// <summary>
    /// The space <paramref name="reference"/> names: its id, its urn, its short id (the urn's
    /// hex tail) or its name in any case — a space not archived first, then an archived one.
    /// Throws <see cref="SubjectNotFoundException"/> when none matches.
    /// </summary>
    public async Task<Space> ResolveAsync(
        string reference, CancellationToken cancellationToken = default, bool preferArchived = false)
    {
        var wanted = reference?.Trim() ?? "";
        if (wanted.Length == 0)
        {
            throw new ArgumentException("Say which space, by its name.");
        }

        var all = await spaces.GetSpacesAsync(cancellationToken);
        if (Guid.TryParse(wanted, out var id))
        {
            return all.FirstOrDefault(s => s.Id == id) ?? throw new SubjectNotFoundException(reference!);
        }

        var byUrn = all.FirstOrDefault(s =>
            string.Equals(s.Urn, wanted, StringComparison.OrdinalIgnoreCase)
            || (Urns.IsShortId(wanted) && s.Urn.EndsWith(":" + wanted, StringComparison.OrdinalIgnoreCase))
            || (Urns.IsShortId(wanted) && s.Urn.EndsWith("-" + wanted, StringComparison.OrdinalIgnoreCase)));
        if (byUrn is not null)
        {
            return byUrn;
        }

        var named = all.Where(s => Same(s.Name, wanted)).OrderBy(s => s.Archived != preferArchived).ToList();
        return named.FirstOrDefault()
               ?? throw new SubjectNotFoundException(reference!);
    }

    private async Task CheckFreeAsync(string name, Guid? except, CancellationToken cancellationToken)
    {
        var taken = (await ListAsync(cancellationToken: cancellationToken))
            .FirstOrDefault(s => s.Id != except && Same(s.Name, name));
        if (taken is not null)
        {
            throw new InvalidOperationException($"You already have a space called '{taken.Name}'.");
        }
    }

    private async Task<Space> ReadAsync(Guid id, CancellationToken cancellationToken)
        => await spaces.GetSpaceAsync(id, cancellationToken) ?? throw new SubjectNotFoundException(id.ToString());

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
