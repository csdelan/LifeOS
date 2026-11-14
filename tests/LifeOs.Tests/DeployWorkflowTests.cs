using System.Text.RegularExpressions;

namespace LifeOs.Tests;

/// <summary>
/// The release workflows deploy the one image CI built, by its digest, and check
/// <c>/health/ready</c> after each deploy (code review CR90). Read from the workflow files, since
/// no test can run them: a deploy that builds its own image again, or names one by a tag that can
/// move, or ends without the readiness check, fails here.
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

    private static string? Condition(string step) => StepCondition().Match(step) is { Success: true } m ? m.Groups[1].Value : null;

    [GeneratedRegex(@"\n(?=      - (?:name|uses):)")]
    private static partial Regex StepStart();

    [GeneratedRegex(@"\bfly(?:ctl)? deploy\b(?:[^\n]*\\\n)*[^\n]*")]
    private static partial Regex DeployCommand();

    [GeneratedRegex(@"--image\s+""?registry\.fly\.io/[^""\s@]+@")]
    private static partial Regex ByDigest();

    [GeneratedRegex(@"\n        if: (.+)")]
    private static partial Regex StepCondition();
}
