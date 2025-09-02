using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IDPSsoAgent.Server.Sse
{
    public class SseClient : IDisposable
    {
        public Guid Id { get; private set; }
        private readonly HttpListenerContext _ctx;
        private readonly Stream _stream;
        private readonly StreamWriter _writer;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly TaskCompletionSource<bool> _completed = new TaskCompletionSource<bool>();

        public Task Completed => _completed.Task;

        public SseClient(HttpListenerContext ctx)
        {
            Id = Guid.NewGuid();
            _ctx = ctx;

            var res = _ctx.Response;
            res.StatusCode = 200;
            res.ContentType = "text/event-stream";
            res.SendChunked = true;
            res.KeepAlive = true;
            res.Headers["Cache-Control"] = "no-cache";
            res.Headers["X-Accel-Buffering"] = "no"; // nginx 등 버퍼링 방지(있으면)
            // CORS 필요시 아래 추가
            // res.Headers["Access-Control-Allow-Origin"] = "*";

            _stream = res.OutputStream;
            _writer = new StreamWriter(_stream, new UTF8Encoding(false)) { AutoFlush = true };

            // 최초 더미 줄(일부 프락시가 바디 없으면 닫아버리는 문제 회피)
            _writer.WriteLine(": connected");
            _writer.WriteLine();
        }

        public async Task SendEventAsync(string eventName, string json)
        {
            if (_cts.IsCancellationRequested) return;
            try
            {
                await _writer.WriteLineAsync("event: " + eventName);
                await _writer.WriteLineAsync("data: " + json);
                await _writer.WriteLineAsync(); // 빈 줄
                await _writer.FlushAsync();
            }
            catch
            {
                TryComplete();
            }
        }

        public async Task SendCommentAsync(string comment)
        {
            if (_cts.IsCancellationRequested) return;
            try
            {
                await _writer.WriteLineAsync(": " + comment);
                await _writer.WriteLineAsync();
            }
            catch
            {
                TryComplete();
            }
        }

        public void TryComplete()
        {
            if (!_cts.IsCancellationRequested)
            {
                try { _cts.Cancel(); } catch { }
            }
            _completed.TrySetResult(true);
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _writer.Dispose(); } catch { }
            try { _stream.Dispose(); } catch { }
            try { _ctx.Response.Close(); } catch { }
        }
    }
}
