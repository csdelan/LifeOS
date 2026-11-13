using LifeOs.Infrastructure;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR59: a connection string left in the environment can turn a "dev" <c>bsk</c> call
/// into a write to staging, so <c>bsk</c> says where it points before it runs.
/// </summary>
public sealed class KernelConnectionStringTests
{
    private const string Pooler =
        "Host=aws-0-us-west-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.abcdefgh;Password=s3cret-value;SSL Mode=Require";

    [Fact]
    public void A_remote_database_is_described_by_its_user_host_and_database_without_the_password()
    {
        var described = KernelConnectionString.Describe(Pooler);

        Assert.Equal("postgres.abcdefgh@aws-0-us-west-1.pooler.supabase.com:5432/postgres", described);
        Assert.DoesNotContain("s3cret", described, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(KernelConnectionString.LocalDevelopmentDefault, true)]
    [InlineData("Host=127.0.0.1;Database=lifeos_check_x;Username=lifeos;Password=lifeos", true)]
    [InlineData("Host=::1;Database=lifeos;Username=lifeos", true)]
    [InlineData("Host=/var/run/postgresql;Database=lifeos;Username=lifeos", true)]
    [InlineData(Pooler, false)]
    [InlineData("Host=localhost,db.example.com;Database=lifeos;Username=lifeos", false)]
    [InlineData("not a connection string", false)]
    public void Only_a_database_on_this_machine_counts_as_local(string connectionString, bool local)
        => Assert.Equal(local, KernelConnectionString.IsLocal(connectionString));

    [Fact]
    public void An_environment_variable_pointing_off_this_machine_is_said_without_its_password()
    {
        var notice = KernelConnectionString.AmbientRemoteNotice(explicitValue: null, environmentValue: Pooler);

        Assert.Equal(
            "bsk: BSK_CONNECTION_STRING points at postgres.abcdefgh@aws-0-us-west-1.pooler.supabase.com:5432/postgres, not this machine.",
            notice);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(KernelConnectionString.LocalDevelopmentDefault)]
    public void Nothing_is_said_when_the_environment_points_nowhere_or_here(string? environmentValue)
        => Assert.Null(KernelConnectionString.AmbientRemoteNotice(explicitValue: null, environmentValue));

    [Fact]
    public void Nothing_is_said_when_the_command_line_names_its_own_database()
        => Assert.Null(KernelConnectionString.AmbientRemoteNotice(
            explicitValue: "Host=localhost;Database=lifeos_check_x;Username=lifeos;Password=lifeos",
            environmentValue: Pooler));
}
