using Microsoft.VisualStudio.TestTools.UnitTesting;

// These tests exercise shared filesystem state (configs.json + layout-drafts under
// the per-test temp roots) and the native display pipeline, so they must not run
// concurrently within the assembly. DoNotParallelize is the explicit choice
// MSTEST0001 requires.
[assembly: DoNotParallelize]
