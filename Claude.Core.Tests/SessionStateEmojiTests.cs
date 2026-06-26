using Claude.Core;
using Claude.Core.Models;

namespace Claude.Core.Tests;

[TestClass]
public class SessionStateEmojiTests
{
    [TestMethod]
    public void For_PendingPermission_ReturnsRaisedHand()
    {
        Assert.AreEqual("🙋", SessionStateEmoji.For(SessionState.PendingPermission));
    }

    [TestMethod]
    public void For_AwaitingInput_ReturnsKeyboard()
    {
        Assert.AreEqual("⌨️", SessionStateEmoji.For(SessionState.AwaitingInput));
    }

    [TestMethod]
    public void For_Working_ReturnsSpinner()
    {
        Assert.AreEqual("🔄", SessionStateEmoji.For(SessionState.Working));
    }

    [TestMethod]
    public void For_Idle_ReturnsSleeping()
    {
        Assert.AreEqual("😴", SessionStateEmoji.For(SessionState.Idle));
    }

    [TestMethod]
    public void For_Unknown_ReturnsQuestion()
    {
        Assert.AreEqual("❓", SessionStateEmoji.For(SessionState.Unknown));
    }

    [TestMethod]
    public void For_UndefinedState_FallsBackToQuestion()
    {
        // Enums can hold values outside the declared set; the fallback keeps the
        // display safe rather than throwing.
        Assert.AreEqual("❓", SessionStateEmoji.For((SessionState)999));
    }

    [TestMethod]
    public void For_EveryState_ReturnsNonEmptyGlyph()
    {
        foreach (var state in Enum.GetValues<SessionState>())
        {
            var glyph = SessionStateEmoji.For(state);
            Assert.IsFalse(string.IsNullOrWhiteSpace(glyph), $"No glyph mapped for {state}.");
        }
    }

    [TestMethod]
    public void For_DistinctStates_HaveDistinctGlyphs()
    {
        var glyphs = Enum.GetValues<SessionState>().Select(SessionStateEmoji.For).ToArray();
        var distinct = glyphs.Distinct().Count();
        Assert.AreEqual(glyphs.Length, distinct, "Two states share a glyph, making them indistinguishable.");
    }
}
