using System.Net.Http;

namespace Shiyu.Core;

/// <summary>
/// One HttpClient for the whole process (O-23). A client per translation was
/// paying a full DNS+TCP+TLS handshake on every keystroke-sized call — a
/// visible slice of the free dictionary's 600 ms budget — and leaving sockets
/// to the garbage collector. Connections live five minutes, so DNS changes
/// are still picked up. No timeout is set here on purpose: each caller owns
/// its own (first byte, idle-between-chunks, budget), and one global timeout
/// would re-introduce the very truncation this split removed.
/// </summary>
public static class HttpClients
{
    public static readonly HttpClient Shared = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    });
}
