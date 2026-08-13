namespace DisplayManager.Core.Tests;

[TestClass]
public class DisplayApplyGateTests
{
    [TestMethod]
    public void Apply_WhenAvailable_ReturnsActionResult()
    {
        var expected = SuccessfulResult();
        var gate = CreateGate();

        var actual = gate.Apply(() => expected);

        Assert.AreSame(expected, actual);
    }

    [TestMethod]
    public async Task Apply_WhileAnotherApplyIsInFlight_DropsAndLogsRequest()
    {
        var applyEntered = new TaskCompletionSource();
        var releaseApply = new TaskCompletionSource();
        var log = new List<string>();
        var gate = CreateGate(log: log.Add);

        var firstApply = Task.Run(() => gate.Apply(() =>
        {
            applyEntered.SetResult();
            releaseApply.Task.Wait();
            return SuccessfulResult();
        }));

        ApplyResult? dropped;
        var droppedApplyExecuted = false;
        try
        {
            Assert.IsTrue(applyEntered.Task.Wait(TimeSpan.FromSeconds(5)));
            dropped = gate.Apply(() =>
            {
                droppedApplyExecuted = true;
                return SuccessfulResult();
            });
        }
        finally
        {
            releaseApply.SetResult();
        }

        var accepted = await firstApply;
        Assert.IsTrue(accepted.Success);
        Assert.IsNotNull(dropped);
        Assert.IsFalse(dropped.Success);
        Assert.IsFalse(droppedApplyExecuted);
        Assert.HasCount(1, dropped.Errors);
        Assert.HasCount(1, log);
        StringAssert.Contains(log[0], "in flight");
    }

    [TestMethod]
    public void Apply_DuringCooldown_DropsAndLogsRequest()
    {
        var elapsed = TimeSpan.Zero;
        var log = new List<string>();
        var gate = CreateGate(() => elapsed, log.Add);

        gate.Apply(() =>
        {
            elapsed = TimeSpan.FromSeconds(5);
            return SuccessfulResult();
        });
        var dropped = gate.Apply(SuccessfulResult);

        Assert.IsFalse(dropped.Success);
        Assert.HasCount(1, dropped.Errors);
        Assert.HasCount(1, log);
        StringAssert.Contains(log[0], "cooldown");
    }

    [TestMethod]
    public void Apply_WhenCooldownExpires_AcceptsNextRequest()
    {
        var elapsed = new FakeElapsed();
        var applyCount = 0;
        var gate = CreateGate(() => elapsed.Value);

        gate.Apply(CountApply);
        elapsed.Value = TimeSpan.FromSeconds(1.5);
        var result = gate.Apply(CountApply);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, applyCount);

        ApplyResult CountApply()
        {
            applyCount++;
            return SuccessfulResult();
        }
    }

    [TestMethod]
    public void Apply_WhenActionThrows_ReleasesGateIntoCooldown()
    {
        var elapsed = new FakeElapsed();
        var gate = CreateGate(() => elapsed.Value);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            gate.Apply(() => throw new InvalidOperationException("native failure")));
        var dropped = gate.Apply(SuccessfulResult);
        elapsed.Value = TimeSpan.FromSeconds(1.5);
        var accepted = gate.Apply(SuccessfulResult);

        Assert.IsFalse(dropped.Success);
        Assert.IsTrue(accepted.Success);
    }

    static DisplayApplyGate CreateGate(
        Func<TimeSpan>? getElapsed = null,
        Action<string>? log = null) =>
        new(TimeSpan.FromSeconds(1.5), getElapsed ?? (() => TimeSpan.Zero), log ?? (_ => { }));

    static ApplyResult SuccessfulResult() => new() { Success = true };

    // Holder so tests can advance the fake clock without reassigning the
    // captured variable itself (jb AccessToModifiedClosure).
    sealed class FakeElapsed
    {
        public TimeSpan Value { get; set; }
    }
}
