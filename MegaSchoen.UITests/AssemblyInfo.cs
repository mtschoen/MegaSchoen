using Microsoft.VisualStudio.TestTools.UnitTesting;

// Screenshot tests launch the actual app and capture its window — they must not run
// concurrently (explicit choice required by MSTEST0001).
[assembly: DoNotParallelize]
