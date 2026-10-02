using Shiyu.Core;

namespace Shiyu.Core.Tests.Fakes;

/// <summary>Stands in for the file sink so the facade's contract can be asserted
/// without touching a disk.</summary>
public sealed class MemoryLogSink : ILogSink
{
    public List<string> Lines { get; } = [];

    public void WriteLine(string line) => Lines.Add(line);
}
