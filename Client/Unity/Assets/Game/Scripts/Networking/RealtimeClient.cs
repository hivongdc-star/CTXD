using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CTXD.Client.Networking
{
    public sealed class RealtimeClient : IDisposable
    {
        readonly ConcurrentQueue<string> _messages = new();
        ClientWebSocket _socket;
        CancellationTokenSource _cts;

        public bool TryDequeue(out string message) => _messages.TryDequeue(out message);

        public async Task ConnectAsync(string httpBaseUrl, string token)
        {
            await StopAsync();
            _cts = new CancellationTokenSource();
            _socket = new ClientWebSocket();
            var baseUri = new Uri(httpBaseUrl.TrimEnd('/'));
            var scheme = baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
            var uri = new UriBuilder(baseUri) { Scheme = scheme, Path = "/ws", Query = "token=" + Uri.EscapeDataString(token) }.Uri;
            await _socket.ConnectAsync(uri, _cts.Token);
            _ = ReceiveLoop(_cts.Token);
        }

        async Task ReceiveLoop(CancellationToken ct)
        {
            var buffer = new byte[16 * 1024];
            var builder = new StringBuilder();
            try
            {
                while (!ct.IsCancellationRequested && _socket != null && _socket.State == WebSocketState.Open)
                {
                    builder.Clear();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);
                    _messages.Enqueue(builder.ToString());
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (ObjectDisposedException) { }
        }

        public async Task StopAsync()
        {
            if (_cts != null) _cts.Cancel();
            if (_socket != null)
            {
                try
                {
                    if (_socket.State == WebSocketState.Open)
                        await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client close", CancellationToken.None);
                }
                catch { }
                _socket.Dispose();
            }
            _socket = null;
            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose() { _ = StopAsync(); }
    }
}
