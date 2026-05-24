namespace Claude.Core.Remote;

public interface IStreamProcess : IDisposable
{
    void Start();
    IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken);
    void Kill();
}
