using Claude.Core.Models;

namespace Claude.Core;

public interface ISessionSource
{
    IReadOnlyList<SessionSnapshot> Enumerate();
}
