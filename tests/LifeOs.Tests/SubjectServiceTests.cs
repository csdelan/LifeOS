using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// The shared resolve-or-create used by <c>bsk ideas</c> (and later the M3
/// subject commands): a new title creates a subject; the same title reuses it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SubjectServiceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private SubjectService Service()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString)
            .BuildServiceProvider().GetRequiredService<SubjectService>();

    [Fact]
    public async Task Creates_a_new_subject_then_reuses_it_by_title()
    {
        var service = Service();
        var title = $"How do I sleep better? {Guid.NewGuid():N}";

        var first = await service.ResolveOrCreateAsync(SubjectTypes.Problem, title, Ct);
        Assert.True(first.Created);
        Assert.Equal(SubjectTypes.Problem, first.Subject.Type);
        Assert.StartsWith("urn:bsk:problem:", first.Subject.Urn);

        var second = await service.ResolveOrCreateAsync(SubjectTypes.Problem, title, Ct);
        Assert.False(second.Created);
        Assert.Equal(first.Subject.Id, second.Subject.Id);
        Assert.Equal(first.Subject.Urn, second.Subject.Urn);
    }

    [Fact]
    public async Task Resolves_an_existing_subject_by_urn()
    {
        var service = Service();
        var created = await service.ResolveOrCreateAsync(
            SubjectTypes.Problem, $"resolve by urn {Guid.NewGuid():N}", Ct);

        var byUrn = await service.ResolveOrCreateAsync(SubjectTypes.Problem, created.Subject.Urn, Ct);

        Assert.False(byUrn.Created);
        Assert.Equal(created.Subject.Id, byUrn.Subject.Id);
    }

    [Fact]
    public async Task An_unknown_urn_is_an_error()
    {
        var service = Service();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.ResolveOrCreateAsync(SubjectTypes.Problem, "urn:bsk:problem:does-not-exist", Ct));
    }
}
