using Claude.Core;
using Claude.Core.Models;
using Claude.Core.Tests.Fakes;
using Claude.Core.Tests.Fixtures;

namespace Claude.Core.Tests;

[TestClass]
public class SessionStatusReplayTests
{
    static string FixturesDir =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "sessions");

    public static IEnumerable<object[]> Scenarios()
    {
        foreach (var file in SessionScenario.FixtureFiles(FixturesDir))
        {
            yield return new object[] { Path.GetFileNameWithoutExtension(file), file };
        }
    }

    [TestMethod]
    [DynamicData(nameof(Scenarios), DynamicDataSourceType.Method)]
    public void Replay_StateMatchesExpectationAfterEachStep(string name, string path)
    {
        var scenario = SessionScenario.Load(path);
        using var fixture = new ClaudeProjectsFixture();
        var slug = SlugEncoder.Encode(scenario.Cwd);
        // A transcript must exist so the enumerator surfaces the session.
        fixture.AddSession(slug, scenario.SessionId,
            """{"type":"assistant","message":{}}""", DateTime.UtcNow);

        var store = new StateStore(Path.Combine(fixture.Root, "state"));
        var dispatcher = new HookDispatcher(store);
        var locator = new FakeProcessLocator();
        locator.Sessions.Add(new ClaudeWindow(
            100, WindowToken.Null, "", scenario.Cwd, DateTimeOffset.UtcNow));
        var transcriptPath = Path.Combine(fixture.Root, slug, $"{scenario.SessionId}.jsonl");

        for (var i = 0; i < scenario.Steps.Count; i++)
        {
            var step = scenario.Steps[i];
            dispatcher.Dispatch(new HookPayload
            {
                HookEventName = step.Event,
                NotificationType = step.NotificationType,
                Message = step.Message,
                SessionId = scenario.SessionId,
                Cwd = scenario.Cwd,
                TranscriptPath = transcriptPath
            });

            var snapshot = new ActiveSessionEnumerator(locator, store, fixture.Root)
                .Enumerate()
                .Single(s => s.SessionId == scenario.SessionId);

            Assert.AreEqual(
                Enum.Parse<SessionState>(step.ExpectAfter),
                snapshot.State,
                $"[{name}] step {i} ({step.Event}/{step.NotificationType}) expected {step.ExpectAfter}");
        }
    }
}
