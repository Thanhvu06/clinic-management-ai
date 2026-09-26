using System;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class AiSessionCleanupWorkerTests
{
    [Fact]
    public async Task RunsFirstCleanupImmediatelyAfterFirstRunDelay()
    {
        var firstPurge = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var initialDelayReturned = false;
        var delayCalls = 0;
        var snapshotStore = new Mock<IAiSessionSnapshotStore>();
        snapshotStore
            .Setup(x => x.PurgeExpiredRecordsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, CancellationToken>((_, _) =>
            {
                Assert.True(initialDelayReturned);
                firstPurge.TrySetResult();
            })
            .Returns(Task.CompletedTask);
        var confirmationStore = new Mock<IAiBookingConfirmationStore>();
        confirmationStore
            .Setup(x => x.PurgeExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var services = BuildServices(snapshotStore.Object, confirmationStore.Object);
        using var stopping = new CancellationTokenSource();
        var worker = new AiSessionCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiSessionCleanupWorker>.Instance,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1),
            (delay, token) =>
            {
                var call = Interlocked.Increment(ref delayCalls);
                if (call == 1)
                {
                    initialDelayReturned = true;
                    return Task.CompletedTask;
                }

                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            });

        await worker.StartAsync(stopping.Token);
        await firstPurge.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(delayCalls >= 1);
        snapshotStore.Verify(x => x.PurgeExpiredRecordsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        confirmationStore.Verify(x => x.PurgeExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

        stopping.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ContinuesWithNextCleanupCycleAfterCycleFailure()
    {
        var secondPurge = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var purgeCount = 0;
        using var stopping = new CancellationTokenSource();
        var snapshotStore = new Mock<IAiSessionSnapshotStore>();
        snapshotStore
            .Setup(x => x.PurgeExpiredRecordsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, CancellationToken>((_, _) =>
            {
                var current = Interlocked.Increment(ref purgeCount);
                if (current == 1)
                {
                    throw new InvalidOperationException("simulated cleanup failure");
                }

                secondPurge.TrySetResult();
                stopping.Cancel();
            })
            .Returns(Task.CompletedTask);
        var confirmationStore = new Mock<IAiBookingConfirmationStore>();
        confirmationStore
            .Setup(x => x.PurgeExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var services = BuildServices(snapshotStore.Object, confirmationStore.Object);
        var worker = new AiSessionCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiSessionCleanupWorker>.Instance,
            TimeSpan.Zero,
            TimeSpan.Zero,
            (_, _) => Task.CompletedTask);

        await worker.StartAsync(stopping.Token);
        await secondPurge.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, purgeCount);
        confirmationStore.Verify(x => x.PurgeExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

        stopping.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    private static ServiceProvider BuildServices(
        IAiSessionSnapshotStore snapshotStore,
        IAiBookingConfirmationStore confirmationStore)
    {
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(DateTime.UtcNow);

        return new ServiceCollection()
            .AddSingleton(clock.Object)
            .AddScoped(_ => snapshotStore)
            .AddScoped(_ => confirmationStore)
            .BuildServiceProvider();
    }
}
