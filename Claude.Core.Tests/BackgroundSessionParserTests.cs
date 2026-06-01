using Claude.Core;

namespace Claude.Core.Tests;

[TestClass]
public class BackgroundSessionParserTests
{
    [TestMethod]
    public void Worker_CommandLine_YieldsSessionId()
    {
        const string cmd = @"C:\Users\me\.local\bin\claude.exe --session-id 375e9c68-62ae-4146-a52c-be6645a6575c --agent claude";
        Assert.IsTrue(BackgroundSessionParser.TryParseWorkerSessionId(cmd, out var id));
        Assert.AreEqual("375e9c68-62ae-4146-a52c-be6645a6575c", id);
    }

    [TestMethod]
    public void PtyHost_CommandLine_IsNotAWorker()
    {
        // Contains --session-id after the `--`, but it is the host, not the session.
        const string cmd = @"C:\x\claude.exe --bg-pty-host \\.\pipe\cc-daemon-x-pty-375e9c68 120 30 -- C:\x\claude.exe --session-id 375e9c68-62ae-4146-a52c-be6645a6575c --agent claude";
        Assert.IsFalse(BackgroundSessionParser.TryParseWorkerSessionId(cmd, out _));
    }

    [TestMethod]
    public void DaemonAndForegroundAndNull_AreNotWorkers()
    {
        Assert.IsFalse(BackgroundSessionParser.TryParseWorkerSessionId(@"C:\x\claude.exe daemon run --origin transient", out _));
        Assert.IsFalse(BackgroundSessionParser.TryParseWorkerSessionId("claude", out _));
        Assert.IsFalse(BackgroundSessionParser.TryParseWorkerSessionId(null, out _));
    }
}
