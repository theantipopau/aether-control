using AetherControl.Services.Infrastructure;

namespace AetherControl.Tests;

public sealed class SingleInstanceGuardTests
{
    private static string UniqueName() => $@"Local\AetherControl.Tests.SingleInstance.{Guid.NewGuid():N}";

    [Fact]
    public void FirstGuard_IsFirstInstance()
    {
        using var guard = new SingleInstanceGuard(UniqueName());

        Assert.True(guard.IsFirstInstance);
    }

    [Fact]
    public void DifferentNames_AreIndependent()
    {
        using var first = new SingleInstanceGuard(UniqueName());
        using var second = new SingleInstanceGuard(UniqueName());

        Assert.True(first.IsFirstInstance);
        Assert.True(second.IsFirstInstance);
    }

    [Fact]
    public void SameName_SecondHolderOnAnotherThread_IsNotFirst()
    {
        // NOTE: the second holder must be a different thread — a Mutex is reentrant, so the same
        // thread acquiring twice always succeeds. Cross-thread is the real second-process shape.
        var name = UniqueName();
        using var first = new SingleInstanceGuard(name);
        Assert.True(first.IsFirstInstance);

        var secondIsFirst = Task.Run(() =>
        {
            using var second = new SingleInstanceGuard(name);
            return second.IsFirstInstance;
        }).GetAwaiter().GetResult();

        Assert.False(secondIsFirst);
    }

    [Fact]
    public void AbandonedByDeadOwner_IsAcquirable()
    {
        // A crashed previous instance must never wedge startup: the OS marks the mutex abandoned
        // on owner death, and the guard treats that as acquirable.
        var name = UniqueName();
        var holder = new Thread(() =>
        {
            var mutex = new Mutex(false, name);
            mutex.WaitOne();
            // Deliberately no release — simulates dying while holding it.
        });
        holder.Start();
        holder.Join();

        using var guard = new SingleInstanceGuard(name);
        Assert.True(guard.IsFirstInstance);
    }

    [Fact]
    public void AfterDispose_NameIsAcquirableAgain()
    {
        var name = UniqueName();
        using (var first = new SingleInstanceGuard(name))
        {
            Assert.True(first.IsFirstInstance);
        }

        using var again = new SingleInstanceGuard(name);
        Assert.True(again.IsFirstInstance);
    }
}
