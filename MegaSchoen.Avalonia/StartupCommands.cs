using System;
using System.IO;

namespace MegaSchoen.Avalonia;

static class StartupCommands
{
    public static bool TryRun(
        string[] arguments,
        TextWriter output,
        TextWriter error,
        Func<string> version,
        Func<string?> verifyNative,
        out int exitCode)
    {
        if (Array.Exists(arguments, argument =>
                string.Equals(argument, "--version", StringComparison.OrdinalIgnoreCase)))
        {
            output.WriteLine(version());
            exitCode = 0;
            return true;
        }

        if (Array.Exists(arguments, argument =>
                string.Equals(argument, "--verify-native", StringComparison.OrdinalIgnoreCase)))
        {
            var verificationError = verifyNative();
            if (verificationError is null)
            {
                output.WriteLine("DisplayManagerNative P/Invoke verified (x64).");
                exitCode = 0;
                return true;
            }

            error.WriteLine(verificationError);
            exitCode = 1;
            return true;
        }

        exitCode = 0;
        return false;
    }
}
