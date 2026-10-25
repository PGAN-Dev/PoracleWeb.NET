using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Tests.Helpers;

namespace Pgan.PoracleWebNet.Tests.Services;

public class PairedHumanWriterTests
{
    [Fact]
    public async Task Unlinked_OneCall()
    {
        var calls = new List<string>();
        var sut = PairTestSupport.UnlinkedWriter();

        var result = await sut.WriteAsync("42", id =>
        {
            calls.Add(id);
            return Task.FromResult(7);
        });

        Assert.Equal(["42"], calls);
        Assert.Equal(7, result.Primary);
        Assert.Equal(0, result.Other);
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task Linked_WritesCallerThenOther()
    {
        var calls = new List<string>();
        var sut = PairTestSupport.Writer(PairTestSupport.Linked(PairTestSupport.Pair(PairTestSupport.TelegramId)));

        var result = await sut.WriteAsync(PairTestSupport.TelegramId, id =>
        {
            calls.Add(id);
            return Task.FromResult(id);
        });

        Assert.Equal([PairTestSupport.TelegramId, PairTestSupport.DiscordId], calls);
        Assert.Equal(PairTestSupport.TelegramId, result.Primary);
        Assert.Equal(PairTestSupport.DiscordId, result.Other);
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task FirstFails_SecondNotTried()
    {
        var calls = new List<string>();
        var sut = PairTestSupport.Writer(PairTestSupport.Linked(PairTestSupport.Pair(PairTestSupport.DiscordId)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.WriteAsync(PairTestSupport.DiscordId, id =>
        {
            calls.Add(id);
            throw new InvalidOperationException("boom");
#pragma warning disable CS0162
            return Task.CompletedTask;
#pragma warning restore CS0162
        }));

        Assert.Equal([PairTestSupport.DiscordId], calls);
    }

    [Fact]
    public async Task SecondFails_ReturnsWarning()
    {
        var sut = PairTestSupport.Writer(PairTestSupport.Linked(PairTestSupport.Pair(PairTestSupport.DiscordId)));

        var result = await sut.WriteAsync(PairTestSupport.DiscordId, id =>
        {
            if (id == PairTestSupport.TelegramId)
            {
                throw new HttpRequestException("PoracleNG said no");
            }

            return Task.CompletedTask;
        });

        Assert.True(result.Primary);
        Assert.NotNull(result.Warning);
        Assert.Equal(LinkSide.Telegram, result.Warning!.Side);
        Assert.Equal("Poracle couldn't be reached.", result.Warning.Reason);
        Assert.Equal(PairWarningCodes.Unreachable, result.Warning.Code);
        Assert.Equal("telegram", result.Warning.ToBody().Side);
        Assert.Equal("unreachable", result.Warning.ToBody().Code);
    }

    [Fact]
    public async Task SecondCancelled_Propagates()
    {
        var sut = PairTestSupport.Writer(PairTestSupport.Linked(PairTestSupport.Pair(PairTestSupport.DiscordId)));

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.WriteAsync(PairTestSupport.DiscordId, id =>
            id == PairTestSupport.TelegramId ? throw new OperationCanceledException() : Task.CompletedTask));
    }

    [Fact]
    public async Task OtherSideNotRegistered_OneCall()
    {
        var calls = new List<string>();
        var sut = PairTestSupport.Writer(PairTestSupport.Linked(PairTestSupport.Pair(PairTestSupport.DiscordId, telegramExists: false)));

        var result = await sut.WriteAsync(PairTestSupport.DiscordId, id =>
        {
            calls.Add(id);
            return Task.CompletedTask;
        });

        Assert.Equal([PairTestSupport.DiscordId], calls);
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task PausedOtherSide_StillWritten()
    {
        var calls = new List<string>();
        var pair = PairTestSupport.Pair(PairTestSupport.DiscordId) with
        {
            Telegram = new PairSide(LinkSide.Telegram, PairTestSupport.TelegramId, true, PauseReason.AdminDisabled),
        };
        var sut = PairTestSupport.Writer(PairTestSupport.Linked(pair));

        await sut.WriteAsync(PairTestSupport.DiscordId, id =>
        {
            calls.Add(id);
            return Task.CompletedTask;
        });

        Assert.Equal([PairTestSupport.DiscordId, PairTestSupport.TelegramId], calls);
    }

    [Fact]
    public async Task SecondFails_HttpFailureReasonNeverNamesAHostOrPort()
    {
        var sut = PairTestSupport.Writer(PairTestSupport.Linked(PairTestSupport.Pair(PairTestSupport.DiscordId)));

        var result = await sut.WriteAsync(PairTestSupport.DiscordId, id =>
            id == PairTestSupport.TelegramId ? throw new HttpRequestException("connect to 192.0.2.10:3030 refused") : Task.CompletedTask);

        Assert.Equal("Poracle couldn't be reached.", result.Warning!.Reason);
    }

    [Fact]
    public async Task SecondFails_PoracleRefusalDetailIsPassedOn()
    {
        var sut = PairTestSupport.Writer(PairTestSupport.Linked(PairTestSupport.Pair(PairTestSupport.DiscordId)));

        var result = await sut.WriteAsync(PairTestSupport.DiscordId, id =>
            id == PairTestSupport.TelegramId ? throw new PoracleRequestRefusedException("no such place") : Task.CompletedTask);

        Assert.Equal("no such place", result.Warning!.Reason);
        Assert.Equal(PairWarningCodes.Refused, result.Warning.Code);
    }

    [Fact]
    public async Task SecondRefusedOnPurpose_CarriesItsOwnCode()
    {
        var sut = PairTestSupport.Writer(PairTestSupport.Linked(PairTestSupport.Pair(PairTestSupport.DiscordId)));

        var result = await sut.WriteAsync(PairTestSupport.DiscordId, id =>
            id == PairTestSupport.TelegramId ? throw new PairWriteRefusedException("drifted", PairWarningCodes.OutOfStep) : Task.CompletedTask);

        Assert.Equal(PairWarningCodes.OutOfStep, result.Warning!.Code);
        Assert.Equal("drifted", result.Warning.Reason);
    }
}
