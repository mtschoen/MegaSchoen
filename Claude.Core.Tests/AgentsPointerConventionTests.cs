namespace Claude.Core.Tests;

// Guards the AGENTS.md source-of-truth convention: CLAUDE.md and GEMINI.md MUST be bare
// `@AGENTS.md` import pointers - no title heading, no other content. AGENTS.md holds the
// real guidance; the tool files just import it so Claude Code / Gemini CLI auto-load it.
//
// The bare form has no top-level heading, which trips markdownlint MD041 (first-line-heading).
// That is handled by exempting these two files in .markdownlint-cli2.jsonc - NOT by adding a
// heading. If this test fails because a heading or extra content crept back in, fix the lint
// config, do not pollute the pointer file.
[TestClass]
public class AgentsPointerConventionTests
{
    const string ExpectedPointer = "@AGENTS.md\n";

    [TestMethod]
    [DataRow("CLAUDE.md")]
    [DataRow("GEMINI.md")]
    public void PointerFile_IsExactlyBareAgentsImport(string fileName)
    {
        var path = Path.Combine(RepositoryRoot(), fileName);
        Assert.IsTrue(File.Exists(path), $"{fileName} is missing at the repository root.");

        // Normalize CRLF so the byte assertion holds regardless of git's line-ending checkout.
        var actual = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.AreEqual(ExpectedPointer, actual,
            $"{fileName} must be exactly `@AGENTS.md` with a trailing newline and nothing else. " +
            "If a linter forced a heading, exempt the file in .markdownlint-cli2.jsonc instead.");
    }

    // Walk up from the test output directory to the repository root, identified by the AGENTS.md
    // source-of-truth file sitting next to the solution. Mirrors TestBinaries.LocateExecutable.
    static string RepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        for (var depth = 0; depth < 8 && current is not null; depth++)
        {
            if (File.Exists(Path.Combine(current, "AGENTS.md")) &&
                File.Exists(Path.Combine(current, "MegaSchoen.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Repository root (AGENTS.md + MegaSchoen.sln) not found above '{AppContext.BaseDirectory}'.");
    }
}
