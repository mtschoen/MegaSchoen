namespace Claude.Core;

// Parses the client source port out of an SSH_CONNECTION value found in a
// process's environment block. SSH_CONNECTION format (set by sshd, inherited by
// children incl. claude): "<clientIp> <clientPort> <serverIp> <serverPort>".
// The client source port is unique per ssh connection, so it is the key used to
// correlate a remote session back to the local ssh.exe that hosts it.
public static class SshConnectionParser
{
    public static bool TryParseClientPort(string? environ, out int clientPort)
    {
        clientPort = 0;
        if (string.IsNullOrEmpty(environ)) return false;

        foreach (var pair in environ.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!pair.StartsWith("SSH_CONNECTION=", StringComparison.Ordinal)) continue;
            var value = pair["SSH_CONNECTION=".Length..];
            var fields = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // fields: [0]=clientIp [1]=clientPort [2]=serverIp [3]=serverPort
            if (fields.Length >= 2 && int.TryParse(fields[1], out var port) && port > 0)
            {
                clientPort = port;
                return true;
            }
            return false; // SSH_CONNECTION present but malformed
        }
        return false;
    }
}
