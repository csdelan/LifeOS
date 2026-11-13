using LifeOs.Api.Http;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Microsoft.AspNetCore.Http;

namespace LifeOs.Tests;

/// <summary>
/// One table says how /api and /mcp answer a kernel refusal (code review CR26): each refusal's
/// kind is its status, a plain ArgumentException or InvalidOperationException is a refusal only
/// when LifeOS's own code threw it, and a fault's text never reaches the caller.
/// </summary>
public sealed class KernelRefusalTests
{
    private static Exception Thrown(Action act)
    {
        try
        {
            act();
        }
        catch (Exception e)
        {
            return e;
        }

        throw new InvalidOperationException("Nothing was thrown.");
    }

    public static TheoryData<Exception, int> Refusals => new()
    {
        { new SubjectNotFoundException("urn:bsk:task:nope-000000"), StatusCodes.Status404NotFound },
        { new DuplicateSubjectException(SubjectTypes.Problem, "Sleep"), StatusCodes.Status409Conflict },
        { new TimerConflictException("Write", "running", "paused"), StatusCodes.Status409Conflict },
        { new FoodConflictException("A fast started elsewhere."), StatusCodes.Status409Conflict },
        { new LedgerConflictException("habit:x:2026-11-12"), StatusCodes.Status409Conflict },
        { new InsufficientBalanceException(Currencies.Coins, 3, 10), StatusCodes.Status400BadRequest },
        { new OuraConnectionException("Oura isn't connected."), StatusCodes.Status409Conflict },
        { new OuraUnavailableException("Oura timed out."), StatusCodes.Status502BadGateway },
        { new ObjectStorageException("Storage refused the upload."), StatusCodes.Status502BadGateway },
        { UnsplashException.NotSetUp(), StatusCodes.Status409Conflict },
        { new UnsplashException(UnsplashFailure.NotFound, "Gone."), StatusCodes.Status404NotFound },
        { new UnsplashException(UnsplashFailure.RateLimited, "Later."), StatusCodes.Status502BadGateway },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Each_refusal_answers_its_status_in_its_own_words(Exception refusal, int status)
    {
        var (answered, body) = KernelExceptionHandler.Map(refusal);

        Assert.Equal(status, answered);
        Assert.Equal(refusal.Message, body.Error);
        Assert.Equal(status, KernelRefusals.Describe(refusal)!.Status);
    }

    [Fact]
    public void An_ambiguous_name_is_a_conflict_that_lists_the_candidates()
    {
        var candidates = new[]
        {
            new SubjectRef(Guid.NewGuid(), "urn:bsk:task:run-aaaaaa", SubjectTypes.Task, "Run"),
            new SubjectRef(Guid.NewGuid(), "urn:bsk:habit:run-bbbbbb", SubjectTypes.Habit, "Run"),
        };

        var (status, body) = KernelExceptionHandler.Map(new AmbiguousSubjectException("Run", candidates));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Equal(2, Assert.IsAssignableFrom<System.Collections.IEnumerable>(body.Candidates).Cast<object>().Count());
    }

    [Fact]
    public void A_plain_refusal_from_LifeOS_code_is_a_400_in_its_words()
    {
        var argument = Thrown(() => throw new ArgumentException("Ask for at most 90 days; got 91."));
        var state = Thrown(() => throw new InvalidOperationException("No fast is running, so there is none to end."));

        Assert.Equal((StatusCodes.Status400BadRequest, "Ask for at most 90 days; got 91."), Answer(argument));
        Assert.Equal((StatusCodes.Status400BadRequest, "No fast is running, so there is none to end."), Answer(state));
    }

    [Fact]
    public void The_same_types_thrown_by_a_library_are_faults_whose_text_stays_in_the_log()
    {
        // An empty sequence (the framework's InvalidOperationException), a null guard, and an
        // exception nobody threw (no TargetSite) are all faults, not refusals.
        var empty = Thrown(() => _ = Array.Empty<int>().First());
        var guard = Thrown(() => ArgumentNullException.ThrowIfNull((object?)null, "bytes"));
        var neverThrown = new InvalidOperationException("Connection is not open.");

        foreach (var fault in new[] { empty, guard, neverThrown })
        {
            Assert.Null(KernelRefusals.Describe(fault));
            Assert.Equal((StatusCodes.Status500InternalServerError, KernelRefusals.FaultMessage), Answer(fault));
        }

        Assert.Equal(
            (StatusCodes.Status500InternalServerError, KernelRefusals.FaultMessage),
            Answer(new FormatException("The input string 'x' was not in a correct format.")));
    }

    [Fact]
    public void Every_exception_the_Application_layer_defines_is_a_refusal_with_a_status()
    {
        var exceptions = typeof(KernelRefusalException).Assembly.GetTypes()
            .Where(t => typeof(Exception).IsAssignableFrom(t) && t != typeof(KernelRefusalException))
            .ToList();

        Assert.NotEmpty(exceptions);
        Assert.All(exceptions, t => Assert.True(
            typeof(KernelRefusalException).IsAssignableFrom(t),
            $"{t.Name} isn't a KernelRefusalException, so the doors can't tell how to answer it."));
        Assert.All(Enum.GetValues<RefusalKind>(), kind => Assert.InRange(KernelRefusals.StatusFor(kind), 400, 599));
    }

    private static (int Status, string Message) Answer(Exception exception)
    {
        var (status, body) = KernelExceptionHandler.Map(exception);
        return (status, body.Error);
    }
}
