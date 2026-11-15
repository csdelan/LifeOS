using System.Text.RegularExpressions;

namespace LifeOs.Tests;

/// <summary>
/// The release workflows deploy the one image CI built, by its digest, and check
/// <c>/health/ready</c> after each deploy (code review CR90). Read from the workflow files, since
/// no test can run them: a deploy that builds its own image again, or names one by a tag that can
/// move, or ends without the readiness check, fails here. A release migrates only when asked, finds
/// an image named by its digest before it migrates, deploys with its environment's Fly config, and
/// takes a tag only on main (code review CR117).
/// </summary>
public sealed partial class DeployWorkflowTests
{
    private static readonly string[] Files = Directory
        .GetFiles(Path.Combine(RepoRoot.Path, ".github", "workflows"), "*.yml")
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static string Read(string file) => File.ReadAllText(file).Replace("\r\n", "\n");

    private static string Workflow(string name) => Read(Path.Combine(RepoRoot.Path, ".github", "workflows", name));

    /// <summary>A workflow's steps, in order: each from its <c>- name:</c> (or <c>- uses:</c>) line to the next.</summary>
    private static List<string> Steps(string yaml) => StepStart().Split(yaml).Skip(1).ToList();

    /// <summary>Each <c>flyctl deploy</c> or <c>fly deploy</c> command, its continued lines joined.</summary>
    private static IEnumerable<(string File, string Command)> Deploys()
        => Files.SelectMany(file => DeployCommand().Matches(Read(file))
            .Select(m => (Path.GetFileName(file), m.Value.Replace("\\\n", " "))));

    [Fact]
    public void Only_ci_builds_the_image_and_each_deploy_runs_it_by_digest()
    {
        var builds = Files
            .Where(file => Read(file).Contains("docker/build-push-action", StringComparison.Ordinal))
            .Select(Path.GetFileName);
        Assert.Equal(["ci.yml"], builds);

        var deploys = Deploys().ToList();
        Assert.NotEmpty(deploys);
        foreach (var (file, command) in deploys)
        {
            Assert.True(
                ByDigest().IsMatch(command),
                $"{file}: '{command}' should deploy an image by its digest (--image registry.fly.io/<app>@<digest>), not build one.");
            Assert.DoesNotMatch(@"--(remote-only|local-only|build-only|dockerfile)\b", command);
        }

        // CI keeps the image it built when Deploy asks, as one manifest, and names its digest.
        var ci = Workflow("ci.yml");
        Assert.Contains("keep_image:", ci, StringComparison.Ordinal);
        Assert.Contains("provenance: false", ci, StringComparison.Ordinal);
        Assert.Contains("name: lifeos-image", ci, StringComparison.Ordinal);
        Assert.Contains("image_digest:", ci, StringComparison.Ordinal);

        // Deploy asks for it, and hands it and its digest to every environment it deploys.
        var deploy = Workflow("deploy.yml");
        Assert.Contains("keep_image: true", deploy, StringComparison.Ordinal);
        var releases = Regex.Matches(deploy, @"uses: \./\.github/workflows/migrate\.yml").Count;
        Assert.True(releases > 0, "deploy.yml should release through migrate.yml.");
        Assert.Equal(releases, Regex.Matches(deploy, @"image_artifact: lifeos-image\n").Count);
        Assert.Equal(releases, Regex.Matches(deploy, @"image_digest: \$\{\{ needs\.ci\.outputs\.image_digest \}\}\n").Count);

        // The release pushes those bytes as they are, and refuses a digest other than CI's.
        var push = Assert.Single(Steps(Workflow("migrate.yml")), s => s.Contains("skopeo copy", StringComparison.Ordinal));
        Assert.Contains("--preserve-digests", push, StringComparison.Ordinal);
        Assert.Contains("\"$pushed\" != \"$IMAGE_DIGEST\"", push, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_deploy_is_followed_by_a_check_of_health_ready()
    {
        foreach (var file in Files)
        {
            var steps = Steps(Read(file));
            for (var i = 0; i < steps.Count; i++)
            {
                if (!DeployCommand().IsMatch(steps[i]))
                {
                    continue;
                }

                var name = Path.GetFileName(file);
                var check = steps.Skip(i + 1).FirstOrDefault(s => s.Contains("/health/ready", StringComparison.Ordinal));
                Assert.True(check is not null, $"{name}: the deploy step should be followed by a step that checks /health/ready.");
                Assert.Equal(Condition(steps[i]), Condition(check!));
                Assert.Contains("exit 1", check, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void A_release_migrates_only_when_asked_and_deploys_only_what_its_environment_holds()
    {
        // Code review CR117 (fifth review R10): a rollback by hand ran main's newest migrations
        // before deploying the old image, never checked the digest was there, deployed with
        // production's fly.toml whatever the environment, and took any v* tag.
        var release = Workflow("migrate.yml");
        var steps = Steps(release);

        // Both triggers take migrate, off unless asked; Deploy and Migrate staging ask for it.
        Assert.Equal(2, MigrateInputOff().Matches(release).Count);
        foreach (var caller in new[] { "deploy.yml", "migrate-staging.yml" })
        {
            var yaml = Workflow(caller);
            var calls = Regex.Matches(yaml, @"uses: \./\.github/workflows/migrate\.yml").Count;
            Assert.True(calls > 0, $"{caller} should release through migrate.yml.");
            Assert.Equal(calls, Regex.Matches(yaml, @"\n      migrate: true\n").Count);
        }

        // bsk migrate applies under the input only; without it, --pending says what it would.
        var migrate = Assert.Single(steps, s => s.Contains("-- migrate", StringComparison.Ordinal));
        Assert.Contains("MIGRATE: ${{ inputs.migrate }}", migrate, StringComparison.Ordinal);
        Assert.Matches(
            @"if \[ ""\$MIGRATE"" = ""true"" \]; then\n\s+dotnet run [^\n]+-- migrate --json\n\s+else\n(?:[^\n]*\n)*?[^\n]*-- migrate --pending --json",
            migrate);

        // An image named by its digest is found in the app's registry before anything migrates.
        var found = steps.FindIndex(s => s.Contains("skopeo inspect", StringComparison.Ordinal));
        Assert.True(found >= 0 && found < steps.IndexOf(migrate), "The image should be found in the registry before the migration.");
        Assert.Contains("registry.fly.io/$APP@$IMAGE_DIGEST", steps[found], StringComparison.Ordinal);
        Assert.Contains("exit 1", steps[found], StringComparison.Ordinal);

        // The Fly config is the environment's: no input names one, and each config is its own app.
        Assert.All(Files, f => Assert.DoesNotContain("fly_config", Read(f), StringComparison.Ordinal));
        var target = Assert.Single(steps, s => s.Contains("staging) config=fly.staging.toml ;;", StringComparison.Ordinal));
        Assert.Contains("production) config=fly.toml ;;", target, StringComparison.Ordinal);
        Assert.Contains("config=$config", target, StringComparison.Ordinal);
        var deploy = Assert.Single(steps, s => DeployCommand().IsMatch(s));
        Assert.Contains("FLY_CONFIG: ${{ steps.target.outputs.config }}", deploy, StringComparison.Ordinal);
        Assert.EndsWith("-staging", FlyApp("fly.staging.toml"), StringComparison.Ordinal);
        Assert.DoesNotContain("staging", FlyApp("fly.toml"), StringComparison.Ordinal);

        // A tag releases only when it's on main, checked against the whole history.
        Assert.Contains("fetch-depth: 0", steps[0], StringComparison.Ordinal);
        var refs = Assert.Single(steps, s => s.Contains("refs/tags/v*)", StringComparison.Ordinal));
        Assert.Contains("git merge-base --is-ancestor \"$GITHUB_SHA\" refs/remotes/origin/main", refs, StringComparison.Ordinal);
    }

    private static string FlyApp(string config)
        => Regex.Match(File.ReadAllText(Path.Combine(RepoRoot.Path, config)), @"^app = ""([^""]+)""", RegexOptions.Multiline).Groups[1].Value;

    private static string? Condition(string step) => StepCondition().Match(step) is { Success: true } m ? m.Groups[1].Value : null;

    [GeneratedRegex(@"\n(?=      - (?:name|uses):)")]
    private static partial Regex StepStart();

    [GeneratedRegex(@"\bfly(?:ctl)? deploy\b(?:[^\n]*\\\n)*[^\n]*")]
    private static partial Regex DeployCommand();

    [GeneratedRegex(@"--image\s+""?registry\.fly\.io/[^""\s@]+@")]
    private static partial Regex ByDigest();

    [GeneratedRegex(@"\n        if: (.+)")]
    private static partial Regex StepCondition();

    [GeneratedRegex(@"\n      migrate:\n(?:        [^\n]+\n)*?        default: false\n")]
    private static partial Regex MigrateInputOff();
}
