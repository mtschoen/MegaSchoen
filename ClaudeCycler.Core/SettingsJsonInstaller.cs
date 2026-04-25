using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeCycler.Core;

public enum InstallState
{
    NotInstalled,
    InstalledHere,
    InstalledElsewhere
}

public sealed class EventInstallStatus
{
    public InstallState Notification { get; set; }
    public InstallState UserPromptSubmit { get; set; }
    public InstallState Stop { get; set; }
    public InstallState PostToolUse { get; set; }
    public InstallState SessionEnd { get; set; }

    public string? NotificationPath { get; set; }
    public string? UserPromptSubmitPath { get; set; }
    public string? StopPath { get; set; }
    public string? PostToolUsePath { get; set; }
    public string? SessionEndPath { get; set; }
}

public sealed class SettingsJsonInstaller
{
    static readonly string[] EventNames =
        { "Notification", "UserPromptSubmit", "Stop", "PostToolUse", "SessionEnd" };
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    readonly string _settingsPath;

    public SettingsJsonInstaller(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public SettingsJsonInstaller() : this(Paths.ClaudeSettingsFile) { }

    public void Install(string bridgeExePath)
    {
        // Claude Code passes hook commands to /usr/bin/bash which eats backslashes
        // as escapes. Forward slashes work on Windows in both JSON and bash contexts.
        var normalizedPath = bridgeExePath.Replace('\\', '/');

        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

        JsonObject root;
        if (File.Exists(_settingsPath))
        {
            File.Copy(_settingsPath, _settingsPath + ".bak", overwrite: true);
            var existing = File.ReadAllText(_settingsPath);
            root = JsonNode.Parse(existing)?.AsObject() ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        if (!root.ContainsKey("hooks"))
        {
            root["hooks"] = new JsonObject();
        }
        var hooksObj = root["hooks"]!.AsObject();

        foreach (var eventName in EventNames)
        {
            if (!hooksObj.ContainsKey(eventName))
            {
                hooksObj[eventName] = new JsonArray();
            }
            var eventArray = hooksObj[eventName]!.AsArray();

            var alreadyInstalled = false;
            foreach (var group in eventArray)
            {
                if (group is JsonObject groupObj && groupObj["hooks"] is JsonArray handlers)
                {
                    foreach (var handler in handlers)
                    {
                        if (handler is JsonObject h
                            && h["type"]?.GetValue<string>() == "command"
                            && PathsEqual(h["command"]?.GetValue<string>(), normalizedPath))
                        {
                            alreadyInstalled = true;
                            break;
                        }
                    }
                }
                if (alreadyInstalled) break;
            }

            if (!alreadyInstalled)
            {
                eventArray.Add(new JsonObject
                {
                    ["hooks"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = normalizedPath
                    })
                });
            }
        }

        File.WriteAllText(_settingsPath, root.ToJsonString(Options));
    }

    public EventInstallStatus GetStatus(string bridgeExePath)
    {
        var status = new EventInstallStatus();

        if (!File.Exists(_settingsPath))
        {
            return status;
        }

        var root = JsonNode.Parse(File.ReadAllText(_settingsPath))?.AsObject();
        if (root is null || root["hooks"] is not JsonObject hooksObj)
        {
            return status;
        }

        foreach (var eventName in EventNames)
        {
            var (state, path) = EvaluateEvent(hooksObj, eventName, bridgeExePath);
            switch (eventName)
            {
                case "Notification":
                    status.Notification = state;
                    status.NotificationPath = path;
                    break;
                case "UserPromptSubmit":
                    status.UserPromptSubmit = state;
                    status.UserPromptSubmitPath = path;
                    break;
                case "Stop":
                    status.Stop = state;
                    status.StopPath = path;
                    break;
                case "PostToolUse":
                    status.PostToolUse = state;
                    status.PostToolUsePath = path;
                    break;
                case "SessionEnd":
                    status.SessionEnd = state;
                    status.SessionEndPath = path;
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled event name in GetStatus switch: {eventName}");
            }
        }

        return status;
    }

    static (InstallState, string?) EvaluateEvent(JsonObject hooksObj, string eventName, string bridgeExePath)
    {
        if (hooksObj[eventName] is not JsonArray eventArray || eventArray.Count == 0)
        {
            return (InstallState.NotInstalled, null);
        }

        string? firstCommandPath = null;
        foreach (var group in eventArray)
        {
            if (group is JsonObject groupObj && groupObj["hooks"] is JsonArray handlers)
            {
                foreach (var handler in handlers)
                {
                    if (handler is JsonObject h && h["type"]?.GetValue<string>() == "command")
                    {
                        var commandPath = h["command"]?.GetValue<string>();
                        firstCommandPath ??= commandPath;
                        if (PathsEqual(commandPath, bridgeExePath))
                        {
                            return (InstallState.InstalledHere, commandPath);
                        }
                    }
                }
            }
        }

        return (InstallState.InstalledElsewhere, firstCommandPath);
    }

    static bool PathsEqual(string? a, string? b) =>
        a is not null && b is not null
        && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
