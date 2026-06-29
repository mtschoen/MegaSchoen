namespace Claude.Core;

public static class SlugEncoder
{
    // Claude Code encodes a cwd into a directory name under ~/.claude/projects/
    // by replacing path separator characters (':', '\\', '/') with '-'.
    // Adjacent separators produce adjacent hyphens (e.g. "C:\foo" -> "C--foo").
    // Trailing separators are trimmed before encoding.
    public static string Encode(string cwd)
    {
        var trimmed = cwd.TrimEnd('\\', '/');
        var chars = trimmed.Select(c => c is ':' or '\\' or '/' ? '-' : c).ToArray();
        return new string(chars);
    }
}
