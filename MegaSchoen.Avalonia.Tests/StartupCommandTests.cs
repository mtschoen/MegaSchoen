using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MegaSchoen.Avalonia.Tests;

[TestClass]
public sealed class StartupCommandTests
{
    [TestMethod]
    public void VersionCommandWritesVersionAndShortCircuits()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handled = StartupCommands.TryRun(
            ["--version"],
            output,
            error,
            () => "0.1.0+abc1234",
            () => null,
            out var exitCode);

        Assert.IsTrue(handled);
        Assert.AreEqual(0, exitCode);
        Assert.AreEqual("0.1.0+abc1234", output.ToString().Trim());
        Assert.AreEqual("", error.ToString());
    }

    [TestMethod]
    public void NativeVerificationFailureReturnsNonzero()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handled = StartupCommands.TryRun(
            ["--verify-native"],
            output,
            error,
            () => "unused",
            () => "DisplayManagerNative.dll was not loaded",
            out var exitCode);

        Assert.IsTrue(handled);
        Assert.AreEqual(1, exitCode);
        StringAssert.Contains(error.ToString(), "was not loaded");
    }

    [TestMethod]
    public void NativeVerificationSuccessWritesConfirmation()
    {
        using var output = new StringWriter();

        var handled = StartupCommands.TryRun(
            ["--verify-native"],
            output,
            TextWriter.Null,
            () => "unused",
            () => null,
            out var exitCode);

        Assert.IsTrue(handled);
        Assert.AreEqual(0, exitCode);
        StringAssert.Contains(output.ToString(), "P/Invoke verified");
    }

    [TestMethod]
    public void UnknownArgumentsContinueNormalStartup()
    {
        var handled = StartupCommands.TryRun(
            ["--hidden"],
            TextWriter.Null,
            TextWriter.Null,
            () => "unused",
            () => null,
            out var exitCode);

        Assert.IsFalse(handled);
        Assert.AreEqual(0, exitCode);
    }

    [TestMethod]
    public void PackagingVerifierAcceptsX64NativeJson()
    {
        var error = WindowsPackagingVerifier.Verify(Architecture.X64, () => "[]");

        Assert.IsNull(error);
    }

    [TestMethod]
    public void PackagingVerifierRejectsWrongArchitecture()
    {
        var error = WindowsPackagingVerifier.Verify(Architecture.Arm64, () => "[]");

        StringAssert.Contains(error, "X64");
    }

    [TestMethod]
    public void PackagingVerifierRejectsNativeQueryError()
    {
        var error = WindowsPackagingVerifier.Verify(
            Architecture.X64,
            () => "Error getting raw JSON: DisplayManagerNative.dll not found");

        StringAssert.Contains(error, "DisplayManagerNative.dll");
    }

    [TestMethod]
    public void PackagingVerifierAcceptsNativeErrorCodeFromHeadlessHost()
    {
        var error = WindowsPackagingVerifier.Verify(
            Architecture.X64,
            () => "Error: Native call failed. Error code: -205");

        Assert.IsNull(error);
    }

    [TestMethod]
    public void CurrentPackagingVerifierReturnsAWellFormedResult()
    {
        var error = WindowsPackagingVerifier.VerifyCurrent();

        if (!OperatingSystem.IsWindows())
        {
            StringAssert.Contains(error, "only supported on Windows");
            return;
        }

        Assert.IsTrue(error is null || error.Length > 0);
    }
}
