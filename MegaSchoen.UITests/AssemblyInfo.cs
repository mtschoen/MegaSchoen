using Microsoft.VisualStudio.TestTools.UnitTesting;

// UI/screenshot tests drive a single live app instance and shared window state, so
// they must not run concurrently within the assembly. DoNotParallelize is the
// explicit choice MSTEST0001 requires.
[assembly: DoNotParallelize]
