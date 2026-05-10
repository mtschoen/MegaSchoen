using ClaudeSessionsCLI.Commands;

if (args.Length == 0)
{
    PrintUsage();
    return 0;
}

return args[0].ToLowerInvariant() switch
{
    "list" => await ListCommand.Run(args[1..]),
    "focus" => await FocusCommand.Run(args[1..]),
    _ => PrintUnknown(args[0])
};

static int PrintUsage()
{
    Console.WriteLine("Usage: ClaudeSessionsCLI <command> [arguments]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  list  [--json] [--json-stream] [--interval 1.5]   List active Claude sessions");
    Console.WriteLine("  focus <session-id-prefix>                          Bring matching window to foreground");
    return 0;
}

static int PrintUnknown(string verb)
{
    Console.Error.WriteLine($"Unknown command: {verb}");
    Console.Error.WriteLine("Run with no arguments to see usage.");
    return 1;
}
