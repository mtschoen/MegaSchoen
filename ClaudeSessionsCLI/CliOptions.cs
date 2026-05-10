using System.Globalization;

namespace ClaudeSessionsCLI;

sealed class CliOptions
{
    public bool Json { get; init; }
    public bool JsonStream { get; init; }
    public double IntervalSeconds { get; init; } = 1.5;

    public static CliOptions Parse(string[] arguments)
    {
        var json = false;
        var jsonStream = false;
        var interval = 1.5;
        for (var i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case "--json": json = true; break;
                case "--json-stream": jsonStream = true; break;
                case "--interval" when i + 1 < arguments.Length:
                    if (double.TryParse(arguments[++i], CultureInfo.InvariantCulture, out var parsed))
                    {
                        interval = Math.Max(0.1, parsed);
                    }
                    break;
            }
        }
        return new CliOptions { Json = json, JsonStream = jsonStream, IntervalSeconds = interval };
    }
}
