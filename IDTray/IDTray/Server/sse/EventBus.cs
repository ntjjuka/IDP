using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace IDTray.Server.Sse
{
    public static class EventBus
    {
        private static readonly ConcurrentDictionary<Guid, SseClient> _clients =
            new ConcurrentDictionary<Guid, SseClient>();

        private static readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private static Timer _heartbeatTimer;
        private static readonly object _timerLock = new object();

        public static void Start()
        {
            lock (_timerLock)
            {
                if (_heartbeatTimer == null)
                {
                    // 15초마다 핑(주석 라인) 전송
                    _heartbeatTimer = new Timer(async _ => await HeartbeatAsync(), null, 15000, 15000);
                }
            }
        }

        public static void Stop()
        {
            lock (_timerLock)
            {
                if (_heartbeatTimer != null)
                {
                    _heartbeatTimer.Dispose();
                    _heartbeatTimer = null;
                }
            }
            CloseAll();
        }

        public static void CloseAll()
        {
            foreach (var kv in _clients.ToArray())
            {
                Remove(kv.Key);
            }
        }

        public static Guid Add(SseClient client)
        {
            Start();
            _clients[client.Id] = client;
            return client.Id;
        }

        public static void Remove(Guid id)
        {
            SseClient c;
            if (_clients.TryRemove(id, out c))
            {
                try { c.Dispose(); } catch { }
            }
        }

        public static async Task BroadcastAsync(string eventName, object payload)
        {
            var json = _json.Serialize(payload ?? new { });
            var list = _clients.Values.ToArray();
            foreach (var c in list)
            {
                await c.SendEventAsync(eventName, json);
            }
        }

        private static async Task HeartbeatAsync()
        {
            var list = _clients.Values.ToArray();
            foreach (var c in list)
            {
                await c.SendCommentAsync("ping " + DateTime.UtcNow.ToString("o"));
            }
        }
    }
}
