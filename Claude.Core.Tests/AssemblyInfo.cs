using Microsoft.VisualStudio.TestTools.UnitTesting;

// Tests use isolated per-test temp directories, but enabling parallelism here is not
// worth the shared-filesystem risk for a suite this size — run sequentially (explicit
// choice required by MSTEST0001).
[assembly: DoNotParallelize]
