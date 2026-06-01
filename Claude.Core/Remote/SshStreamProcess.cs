using System.Diagnostics;

namespace Claude.Core.Remote;

public sealed class SshStreamProcess : IStreamProcess
{
    readonly Process _process;

    public SshStreamProcess(string sshTarget, string remoteCli)
    {
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ssh",
                // -T no pty; BatchMode so a missing key fails fast instead of prompting.
                ArgumentList =
                {
                    "-T", "-o", "BatchMode=yes", "-o", "ServerAliveInterval=15",
                    sshTarget, remoteCli, "list", "--json-stream"
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
    }

    public void Start() => _process.Start();

    public async IAsyncEnumerable<string> ReadLinesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? line;
        while ((line = await _process.StandardOutput.ReadLineAsync(cancellationToken)) is not null)
        {
            yield return line;
        }
    }

    public void Kill() { try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { /* best-effort teardown; the process may already have exited or be unkillable */ } }

    public void Dispose() { Kill(); _process.Dispose(); }
}
