using LifeOs.Api.Read;
using LifeOs.Domain;

namespace LifeOs.Api.Mcp;

/// <summary>
/// The Map's alignment forest, ported from <c>buildAlignmentForest</c> in
/// <c>web/src/lib/derive.ts</c> for the <c>alignment</c> tool: an item's children are the items
/// that serve it (or result in it). Identity statements are roots, and so is any Goal, Project
/// or Task that serves nothing. An item that serves two things shows under each, naming the
/// other in <c>also_serves</c>. Lists stop at a budget of nodes, and say so.
/// </summary>
public sealed class AlignmentForest
{
    /// <summary>The relations that make the tree; <c>supersedes</c> isn't alignment.</summary>
    public static readonly IReadOnlySet<string> Relations = new HashSet<string>(StringComparer.Ordinal) { "serves", "results_in" };

    private static readonly string[] AlignmentTypes = [SubjectTypes.Value, SubjectTypes.Goal, SubjectTypes.Project, SubjectTypes.Task];

    private readonly Dictionary<Guid, SubjectListItem> by;
    private readonly Dictionary<Guid, List<(Guid Id, string Relation)>> childrenOf = [];
    private readonly Dictionary<Guid, List<(Guid Id, string Relation)>> parentsOf = [];
    private readonly bool includeDone;
    private int budget;

    public AlignmentForest(
        IEnumerable<SubjectListItem> subjects, IEnumerable<AlignmentEdge> edges, bool includeDone, int maxNodes)
    {
        by = subjects.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        this.includeDone = includeDone;
        budget = maxNodes;
        foreach (var edge in edges)
        {
            if (!Relations.Contains(edge.Relation) || !by.ContainsKey(edge.FromId) || !by.ContainsKey(edge.ToId))
            {
                continue;
            }

            Add(childrenOf, edge.ToId, (edge.FromId, edge.Relation));
            Add(parentsOf, edge.FromId, (edge.ToId, edge.Relation));
        }
    }

    /// <summary>True when a list stopped at the node budget.</summary>
    public bool Truncated { get; private set; }

    public bool Contains(Guid id) => by.ContainsKey(id);

    /// <summary>Every root and what serves it, identity statements first.</summary>
    public IReadOnlyList<AlignmentNodeRead> Forest()
    {
        var roots = by.Values
            .Where(s => s.Type == SubjectTypes.Value
                        || (AlignmentTypes.Contains(s.Type) && !parentsOf.ContainsKey(s.Id)))
            .Where(s => s.Type == SubjectTypes.Value || Shows(s))
            .OrderBy(s => Rank(s.Type))
            .ThenBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase);
        return roots.Select(r => Down(r.Id, null, null, [])).OfType<AlignmentNodeRead>().ToList();
    }

    /// <summary>The item and everything serving it, down to Tasks.</summary>
    public AlignmentNodeRead? Below(Guid id) => Down(id, null, null, [], force: true);

    /// <summary>What the item serves, up to identity statements: each parent with its own parents.</summary>
    public IReadOnlyList<AlignmentNodeRead> Above(Guid id) => Up(id, [id]);

    private AlignmentNodeRead? Down(Guid id, Guid? parentId, string? relation, HashSet<Guid> path, bool force = false)
    {
        if (!by.TryGetValue(id, out var subject) || path.Contains(id) || (!force && !Shows(subject)))
        {
            return null;
        }

        if (budget <= 0)
        {
            Truncated = true;
            return null;
        }

        budget--;
        var nextPath = new HashSet<Guid>(path) { id };
        var children = (childrenOf.GetValueOrDefault(id) ?? [])
            .Select(c => (Child: c, Subject: by[c.Id]))
            .OrderBy(c => Rank(c.Subject.Type))
            .ThenBy(c => c.Subject.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => Down(c.Child.Id, id, c.Child.Relation, nextPath))
            .OfType<AlignmentNodeRead>()
            .ToList();
        var others = (parentsOf.GetValueOrDefault(id) ?? [])
            .Where(p => p.Id != parentId)
            .Select(p => by[p.Id])
            .Select(p => new AlignmentLinkRead(p.Id, p.Type, p.Title))
            .ToList();
        return Node(subject, relation) with
        {
            AlsoServes = others.Count > 0 ? others : null,
            Children = children.Count > 0 ? children : null,
        };
    }

    private List<AlignmentNodeRead> Up(Guid id, HashSet<Guid> path)
    {
        var parents = new List<AlignmentNodeRead>();
        foreach (var (parentId, relation) in parentsOf.GetValueOrDefault(id) ?? [])
        {
            if (path.Contains(parentId))
            {
                continue;
            }

            if (budget <= 0)
            {
                Truncated = true;
                break;
            }

            budget--;
            var above = Up(parentId, new HashSet<Guid>(path) { parentId });
            parents.Add(Node(by[parentId], relation) with { Serves = above.Count > 0 ? above : null });
        }

        return parents;
    }

    private bool Shows(SubjectListItem subject) => includeDone || !StatusVocabulary.IsTerminal(subject.Status);

    private static AlignmentNodeRead Node(SubjectListItem s, string? relation)
        => new(s.Id, s.Urn, s.Type, s.Title)
        {
            Status = s.Status,
            Area = s.AreaName ?? s.Area,
            Due = s.Due,
            TargetDate = s.TargetDate,
            Relation = relation,
            Archived = s.Archived ? true : null,
        };

    private static int Rank(string type) => type switch
    {
        SubjectTypes.Value => 0,
        SubjectTypes.Goal => 1,
        SubjectTypes.Project => 2,
        _ => 3,
    };

    private static void Add(Dictionary<Guid, List<(Guid, string)>> map, Guid key, (Guid, string) value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }

        list.Add(value);
    }
}

/// <summary>
/// One item in the tree. <c>relation</c> is how it ties to the node above it in the answer
/// (<c>serves</c> or <c>results_in</c>); <c>children</c> serve it; <c>serves</c> (going up) is
/// what it serves.
/// </summary>
public sealed record AlignmentNodeRead(Guid Id, string Urn, string Type, string Title)
{
    public string? Status { get; init; }
    public string? Area { get; init; }
    public string? Due { get; init; }
    public string? TargetDate { get; init; }
    public string? Relation { get; init; }
    public bool? Archived { get; init; }
    public IReadOnlyList<AlignmentLinkRead>? AlsoServes { get; init; }
    public IReadOnlyList<AlignmentNodeRead>? Serves { get; init; }
    public IReadOnlyList<AlignmentNodeRead>? Children { get; init; }
}

public sealed record AlignmentLinkRead(Guid Id, string Type, string Title);
