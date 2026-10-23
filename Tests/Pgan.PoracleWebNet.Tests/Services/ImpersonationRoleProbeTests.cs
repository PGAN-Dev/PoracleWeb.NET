using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Pgan.PoracleWebNet.Api.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The bounds on the impersonation check during an outage, and what they must not change outside one.
/// </summary>
/// <remarks>
/// The probe's budget and quiet window run on a <see cref="FakeTimeProvider"/>, so neither elapses unless a
/// test advances it. On the wall clock a 200 ms budget raced the probe's own <c>Task.Run</c>, and on a loaded
/// CI runner the timer won often enough to bounce merge-queue runs: a healthy resolve came back unresolved.
/// </remarks>
public class ImpersonationRoleProbeTests
{
    private const string Delegate = "delegate-1";
    private static readonly UserRoles Granted = new(false, ["webhook-1"]);
    private static readonly UserRoles Unresolved = new(false, null, Resolved: false);

    /// <summary>A real-time cap on any wait below, so a broken probe fails the test instead of hanging it.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Mock<IUserRoleResolver> _resolver = new();
    private readonly FakeTimeProvider _time = new();
    private int _calls;

    private ImpersonationRoleProbe Probe(TimeSpan? budget = null, TimeSpan? quietFor = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => this._resolver.Object);
        return new ImpersonationRoleProbe(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ImpersonationRoleProbe>.Instance,
            this._time)
        {
            Budget = budget ?? TimeSpan.FromMilliseconds(200),
            QuietFor = quietFor ?? TimeSpan.FromSeconds(30),
        };
    }

    private void Answers(Func<Task<UserRoles>> answer) =>
        this._resolver.Setup(r => r.ResolveAsync(Delegate)).Returns(() =>
        {
            Interlocked.Increment(ref this._calls);
            return answer();
        });

    /// <summary>
    /// Advances the fake clock past the budget until <paramref name="pending"/> finishes. The probe registers
    /// its budget timer only once it reaches its <c>await</c>, so a single advance can land before the timer
    /// exists; advancing again until the call returns removes that race.
    /// </summary>
    private async Task<UserRoles> OutlastTheBudgetAsync(Task<UserRoles> pending, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!pending.IsCompleted && DateTime.UtcNow < deadline)
        {
            this._time.Advance(budget);
            await Task.Delay(10);
        }

        return await pending.WaitAsync(Patience);
    }

    /// <summary>Polls until <paramref name="condition"/> holds, failing the test after <see cref="Patience"/>.</summary>
    private static async Task EventuallyAsync(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"timed out waiting for {what}");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task AHealthySourceIsAskedEveryTimeSoRevocationIsImmediate()
    {
        var probe = this.Probe();
        this.Answers(() => Task.FromResult(Granted));
        Assert.True((await probe.ResolveAsync(Delegate)).Resolved);

        this.Answers(() => Task.FromResult(new UserRoles(false, null)));

        Assert.Null((await probe.ResolveAsync(Delegate)).ManagedDestinations);
    }

    /// <summary>
    /// Runs <paramref name="checks"/> for many impersonators at once, each making its own checks in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The defect these guard against is a race: a resolver that answers at once let the probe's resolve
    /// finish on the thread pool before the caller had stored it as in flight, and the finished entry was
    /// then never removed, so every later check reused it. One impersonator checking in a loop on an idle
    /// pool almost never lost it; many at once on a busy pool lost it several times in every run.
    /// </para>
    /// <para>
    /// So the two tests that use this are a probabilistic regression guard: they surface the old race under
    /// load rather than force it. On the fixed probe nothing in them depends on timing, so they cannot flake.
    /// </para>
    /// </remarks>
    private static Task RaceAsync(Func<string, Task> checks) =>
        Task.WhenAll(Enumerable.Range(0, Math.Max(16, Environment.ProcessorCount * 2))
            .Select(i => Task.Run(() => checks($"impersonator-{i}"))))
            .WaitAsync(Patience * 3);

    private const int ChecksPerImpersonator = 1000;

    [Fact]
    public async Task ASecondCheckAfterTheFirstFinishesAsksAgain()
    {
        var probe = this.Probe(budget: TimeSpan.FromSeconds(5));
        var asked = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        this._resolver.Setup(r => r.ResolveAsync(It.IsAny<string>())).Returns((string id) =>
            Task.FromResult(new UserRoles(false, [$"answer-{asked.AddOrUpdate(id, 1, (_, n) => n + 1)}"])));

        await RaceAsync(async id =>
        {
            for (var i = 1; i <= ChecksPerImpersonator; i++)
            {
                Assert.Equal([$"answer-{i}"], (await probe.ResolveAsync(id)).ManagedDestinations!);
            }
        });
    }

    [Fact]
    public async Task ARevokedGrantIsSeenOnTheNextCheck()
    {
        var probe = this.Probe(budget: TimeSpan.FromSeconds(5));
        var granted = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        this._resolver.Setup(r => r.ResolveAsync(It.IsAny<string>())).Returns((string id) =>
            Task.FromResult(new UserRoles(false, granted[id] ? ["webhook-1"] : [])));

        await RaceAsync(async id =>
        {
            for (var i = 0; i < ChecksPerImpersonator / 2; i++)
            {
                granted[id] = true;
                Assert.Contains("webhook-1", (await probe.ResolveAsync(id)).ManagedDestinations!);

                // The admin revokes the grant; the session's very next request must see it.
                granted[id] = false;
                Assert.DoesNotContain("webhook-1", (await probe.ResolveAsync(id)).ManagedDestinations!);
            }
        });
    }

    [Fact]
    public async Task AResolveThatOutlivesTheBudgetIsUnresolved()
    {
        var never = new TaskCompletionSource<UserRoles>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = this.Probe();
        this.Answers(() => never.Task);

        var roles = await this.OutlastTheBudgetAsync(probe.ResolveAsync(Delegate), TimeSpan.FromMilliseconds(200));

        Assert.False(roles.Resolved);
    }

    [Fact]
    public async Task AResolveWithinTheBudgetIsNotCutShortByASlowScheduler()
    {
        // The clock never moves, so however long the resolve takes to be scheduled, the budget cannot expire.
        var answer = new TaskCompletionSource<UserRoles>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = this.Probe();
        this.Answers(() => answer.Task);

        var pending = probe.ResolveAsync(Delegate);
        await Task.Delay(300);
        answer.SetResult(Granted);

        Assert.True((await pending.WaitAsync(Patience)).Resolved);
    }

    [Fact]
    public async Task AfterAnOutageIsSeenTheSourcesAreNotReaskedForTheQuietWindow()
    {
        var probe = this.Probe();
        this.Answers(() => Task.FromResult(Unresolved));

        await probe.ResolveAsync(Delegate);
        await probe.ResolveAsync(Delegate);
        await probe.ResolveAsync(Delegate);

        Assert.Equal(1, this._calls);
    }

    [Fact]
    public async Task AfterTheQuietWindowTheSourcesAreAskedAgain()
    {
        var probe = this.Probe(quietFor: TimeSpan.FromMilliseconds(100));
        this.Answers(() => Task.FromResult(Unresolved));
        await probe.ResolveAsync(Delegate);

        this._time.Advance(TimeSpan.FromMilliseconds(200));
        this.Answers(() => Task.FromResult(Granted));

        Assert.True((await probe.ResolveAsync(Delegate)).Resolved);
    }

    [Fact]
    public async Task TheWindowClosesTheMomentALateResolveAnswers()
    {
        var late = new TaskCompletionSource<UserRoles>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = this.Probe();
        this.Answers(() => late.Task);
        Assert.False((await this.OutlastTheBudgetAsync(probe.ResolveAsync(Delegate), TimeSpan.FromMilliseconds(200))).Resolved);

        this.Answers(() => Task.FromResult(new UserRoles(false, null)));
        late.SetResult(Granted);

        // The 30 s quiet window cannot run out on its own: the clock is no longer advanced. Only the late
        // answer closing it lets a resolve through.
        UserRoles roles = Unresolved;
        await EventuallyAsync(async () => (roles = await probe.ResolveAsync(Delegate)).Resolved, "the quiet window to close");
        Assert.Null(roles.ManagedDestinations);
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneResolve()
    {
        var gate = new TaskCompletionSource<UserRoles>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = this.Probe(budget: TimeSpan.FromSeconds(5));
        this.Answers(() => gate.Task);

        var waiting = Enumerable.Range(0, 5).Select(_ => probe.ResolveAsync(Delegate)).ToList();
        gate.SetResult(Granted);
        await Task.WhenAll(waiting).WaitAsync(Patience);

        Assert.Equal(1, this._calls);
        Assert.All(waiting, w => Assert.True(w.Result.Resolved));
    }

    [Fact]
    public async Task OneImpersonatorsOutageDoesNotQuietAnother()
    {
        var probe = this.Probe();
        this.Answers(() => Task.FromResult(Unresolved));
        await probe.ResolveAsync(Delegate);

        this._resolver.Setup(r => r.ResolveAsync("admin-2")).ReturnsAsync(new UserRoles(true, null));

        Assert.True((await probe.ResolveAsync("admin-2")).IsAdmin);
    }
}
