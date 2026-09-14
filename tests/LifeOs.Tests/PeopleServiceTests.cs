using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Phase 11: the People-association link (attributes.people). A Person is attached to
/// an item in a role, supports several people per item, is validated to be a Person,
/// de-duped, removable, and surfaced by v_person_association for "everything
/// involving X" and the Person detail groupings.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PeopleServiceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private async Task<List<string>> RolesForAsync(NpgsqlConnection c, Guid subjectId, Guid personId)
        => (await c.QueryAsync<string>(new CommandDefinition(
            "SELECT role FROM bsk.v_person_association WHERE subject_id = @s AND person_id = @p ORDER BY role;",
            new { s = subjectId, p = personId }, cancellationToken: Ct))).ToList();

    [Fact]
    public async Task Involving_a_person_surfaces_in_the_association_view()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var people = provider.GetRequiredService<PeopleService>();
        var ns = Guid.NewGuid().ToString("N");

        var task = await subjects.CreateAsync(SubjectTypes.Task, $"waiting task {ns}", cancellationToken: Ct);
        var person = await subjects.CreateAsync(SubjectTypes.Person, $"Alex {ns}", cancellationToken: Ct);

        var result = await people.AddAsync(task.Urn, person.Urn, PersonRoles.WaitingFor, Ct);
        Assert.True(result.Changed);

        await using var c = await OpenAsync();
        Assert.Equal([PersonRoles.WaitingFor], await RolesForAsync(c, task.Id, person.Id));
    }

    [Fact]
    public async Task Adding_the_same_person_and_role_twice_is_idempotent()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var people = provider.GetRequiredService<PeopleService>();
        var ns = Guid.NewGuid().ToString("N");
        var appt = await subjects.CreateAsync(SubjectTypes.Task, $"meeting {ns}", cancellationToken: Ct);
        var person = await subjects.CreateAsync(SubjectTypes.Person, $"Sam {ns}", cancellationToken: Ct);

        await people.AddAsync(appt.Urn, person.Urn, PersonRoles.Attendee, Ct);
        var second = await people.AddAsync(appt.Urn, person.Urn, PersonRoles.Attendee, Ct);
        Assert.False(second.Changed);

        await using var c = await OpenAsync();
        var count = await c.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.v_person_association WHERE subject_id = @s AND person_id = @p;",
            new { s = appt.Id, p = person.Id }, cancellationToken: Ct));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Several_people_and_removal_are_supported()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var people = provider.GetRequiredService<PeopleService>();
        var ns = Guid.NewGuid().ToString("N");
        var appt = await subjects.CreateAsync(SubjectTypes.Task, $"standup {ns}", cancellationToken: Ct);
        var a = await subjects.CreateAsync(SubjectTypes.Person, $"A {ns}", cancellationToken: Ct);
        var b = await subjects.CreateAsync(SubjectTypes.Person, $"B {ns}", cancellationToken: Ct);

        await people.AddAsync(appt.Urn, a.Urn, PersonRoles.Attendee, Ct);
        await people.AddAsync(appt.Urn, b.Urn, PersonRoles.Attendee, Ct);

        await using var c = await OpenAsync();
        var before = await c.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.v_person_association WHERE subject_id = @s;",
            new { s = appt.Id }, cancellationToken: Ct));
        Assert.Equal(2, before);

        var removed = await people.RemoveAsync(appt.Urn, a.Urn, PersonRoles.Attendee, Ct);
        Assert.True(removed.Changed);
        Assert.Empty(await RolesForAsync(c, appt.Id, a.Id));
        Assert.Equal([PersonRoles.Attendee], await RolesForAsync(c, appt.Id, b.Id));
    }

    [Fact]
    public async Task Only_a_person_can_be_involved()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var people = provider.GetRequiredService<PeopleService>();
        var ns = Guid.NewGuid().ToString("N");
        var task = await subjects.CreateAsync(SubjectTypes.Task, $"t {ns}", cancellationToken: Ct);
        var notPerson = await subjects.CreateAsync(SubjectTypes.Goal, $"g {ns}", cancellationToken: Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await people.AddAsync(task.Urn, notPerson.Urn, PersonRoles.Owner, Ct));
    }

    [Fact]
    public async Task An_unknown_role_is_rejected()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var people = provider.GetRequiredService<PeopleService>();
        var ns = Guid.NewGuid().ToString("N");
        var task = await subjects.CreateAsync(SubjectTypes.Task, $"t {ns}", cancellationToken: Ct);
        var person = await subjects.CreateAsync(SubjectTypes.Person, $"P {ns}", cancellationToken: Ct);

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await people.AddAsync(task.Urn, person.Urn, "sidekick", Ct));
    }
}
