using Microsoft.VisualStudio.TestTools.UnitTesting;

// These tests exercise shared filesystem state (the StateStore under
// %LOCALAPPDATA%\MegaSchoen, sharded needy-sessions files, settings.json installs),
// so they must not run concurrently within the assembly. DoNotParallelize is the
// explicit choice MSTEST0001 requires.
[assembly: DoNotParallelize]
