using System.Text.Json;
using ClaudeCycler.Core;
using ClaudeCycler.Core.Models;

namespace ClaudeHookBridge;

public static class Program
{
    public static int Main(string[] arguments)
    {
        try
        {
            if (arguments.Length == 0)
            {
                return RunHookMode();
            }

            // Inspection-mode dispatch added in later tasks.
            Console.Error.WriteLine($"Unknown subcommand: {arguments[0]}");
            return 1;
        }
        catch (Exception exception)
        {
            Logger.Log($"Program.Main unhandled: {exception}");
            return 0; // hook mode must never fail Claude
        }
    }

    static int RunHookMode()
    {
        try
        {
            var stdin = Console.In.ReadToEnd();
            if (string.IsNullOrWhiteSpace(stdin))
            {
                Logger.Log("Hook mode: empty stdin");
                return 0;
            }

            var payload = JsonSerializer.Deserialize<HookPayload>(stdin);
            if (payload is null)
            {
                Logger.Log("Hook mode: null payload after deserialization");
                return 0;
            }

            var dispatcher = new HookDispatcher(new StateStore());
            dispatcher.Dispatch(payload);
            return 0;
        }
        catch (Exception exception)
        {
            Logger.Log($"RunHookMode failed: {exception.Message}");
            return 0;
        }
    }
}

