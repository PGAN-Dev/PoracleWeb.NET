using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Api.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The bounds on the impersonation check during an outage, and what they must not change outside one.
/// </summary>
public class ImpersonationRoleProbeTests
{
    private const string Delegate = "delegate-1";
    private static readonly UserRoles Granted = new(false, ["webhook-1"]);
    private static readonly UserRoles Unresolved = new(false, null, Resolved: false);

    private readonly Mock<IUserRoleResolver> _resolver = new();
    private int _calls;

    private ImpersonationRoleProbe Probe(TimeSpan? budget = null, TimeSpan? quietFor = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => this._resolver.Object);
        return new ImpersonationRoleProbe(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ImpersonationRoleProbe>.Instance)
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

    [Fact]
    public async Task AHealthySourceIsAskedEveryTimeSoRevocationIsImmediate()
    {
        var probe = this.Probe();
        this.Answers(() => Task.FromResult(Granted));
        Assert.True((await probe.ResolveAsync(Delegate)).Resolved);

        this.Answers(() => Task.FromResult(new UserRoles(false, null)));

        Assert.Null((await probe.ResolveAsync(Delegate)).ManagedWebhooks);
    }

    [Fact]
    public async Task AResolveThatOutlivesTheBudgetIsUnresolved()
    {
        var probe = this.Probe();
        this.Answers(async () => { await Task.Delay(TimeSpan.FromSeconds(5)); return Granted; });

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var roles = await probe.ResolveAsync(Delegate);

        Assert.False(roles.Resolved);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.Elapsed}");
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

        await Task.Delay(200);
        this.Answers(() => Task.FromResult(Granted));

        Assert.True((await probe.ResolveAsync(Delegate)).Resolved);
    }

    [Fact]
    public async Task TheWindowClosesTheMomentALateResolveAnswers()
    {
        var late = new TaskCompletionSource<UserRoles>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = this.Probe();
        this.Answers(() => late.Task);
        Assert.False((await probe.ResolveAsync(Delegate)).Resolved);

        late.SetResult(Granted);
        await Task.Delay(100);
        this.Answers(() => Task.FromResult(new UserRoles(false, null)));

        var roles = await probe.ResolveAsync(Delegate);
        Assert.True(roles.Resolved);
        Assert.Null(roles.ManagedWebhooks);
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneResolve()
    {
        var gate = new TaskCompletionSource<UserRoles>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = this.Probe(budget: TimeSpan.FromSeconds(5));
        this.Answers(() => gate.Task);

        var waiting = Enumerable.Range(0, 5).Select(_ => probe.ResolveAsync(Delegate)).ToList();
        await Task.Delay(100);
        gate.SetResult(Granted);
        await Task.WhenAll(waiting);

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
