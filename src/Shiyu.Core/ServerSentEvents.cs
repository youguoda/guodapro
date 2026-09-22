using System.Runtime.CompilerServices;
using System.Text;

namespace Shiyu.Core;

/// <summary>
/// Reads the data payloads out of a server-sent events stream.
///
/// Separated from the backend so the fiddly part — chunk boundaries landing
/// mid-line, comment frames, the sentinel that ends the stream — can be tested
/// against a plain stream rather than a live endpoint.
/// </summary>
public static class ServerSentEvents
{
    /// <summary>The payload every OpenAI-compatible endpoint closes its stream with.</summary>
    public const string Done = "[DONE]";

    public static async IAsyncEnumerable<string> ReadDataAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!cancellation.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellation);
            if (line is null)
            {
                yield break;
            }

            // Blank lines separate frames and comment lines are keep-alives.
            // Neither carries anything the caller wants.
            if (line.Length == 0 || line[0] == ':')
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line[5..].TrimStart();

            if (payload == Done)
            {
                yield break;
            }

            yield return payload;
        }
    }
}
