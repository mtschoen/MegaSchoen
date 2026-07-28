using System;
using Claude.Core;
using Claude.Core.Linux;

namespace MegaSchoen.Avalonia;

// Composition root for the sessions backend, mirroring the seams the CLI's
// ListCommand.BuildEnumerator exposes:
//   MEGASCHOEN_FAKE_PROCESSES → run with no real claude processes
//   MEGASCHOEN_STATE_DIR      → isolate the needy-sessions state directory
// This app compiles Claude.Core's net10.0 flavor, so window focus and ssh
// window resolution are the Null implementations (Focus renders disabled);
// the Windows MAUI app remains the window-integrated UI on Windows.
static class SessionServices
{
    public static ActiveSessionEnumerator BuildEnumerator()
    {
        IClaudeProcessLocator locator =
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentProcessLocator.EnvironmentVariable))
                ? new EnvironmentProcessLocator()
                : new LinuxClaudeProcessLocator();

        var stateDirectory = Environment.GetEnvironmentVariable("MEGASCHOEN_STATE_DIR");
        var store = string.IsNullOrWhiteSpace(stateDirectory) ? new StateStore() : new StateStore(stateDirectory);
        return new ActiveSessionEnumerator([new ClaudeSessionSource(locator, store)]);
    }
}
