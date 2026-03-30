using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Claude.UnityMCP.Communication
{
    /// <summary>
    /// Bare-bones TCP HTTP server. Single background thread.
    /// Socket.Poll with 1s timeout = zero CPU when idle, clean exit on stop.
    /// </summary>
    public class StreamableHttpServer : IDisposable
    {
        public int Port { get; private set; }
        public bool IsRunning { get; private set; }
        public Func<string, string> OnRequestSync;

        private TcpListener _tcp;
        private Thread _thread;
        private volatile bool _stopping;

        public StreamableHttpServer(int port) { Port = port; }

        public void Start()
        {
            if (IsRunning) return;
            Stop();
            _stopping = false;

            _tcp = new TcpListener(IPAddress.Loopback, Port);
            _tcp.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _tcp.Start();
            IsRunning = true;

            _thread = new Thread(Loop) { IsBackground = true, Name = "MCP" };
            _thread.Start();
        }

        public void Stop()
        {
            _stopping = true;
            IsRunning = false;
            if (_tcp != null) { try { _tcp.Stop(); } catch { } _tcp = null; }
            if (_thread != null && _thread.IsAlive)
            {
                if (!_thread.Join(2000)) try { _thread.Abort(); } catch { }
            }
            _thread = null;
        }

        public void Dispose() { Stop(); OnRequestSync = null; }

        private void Loop()
        {
            while (!_stopping)
            {
                try
                {
                    if (_tcp == null) break;
                    if (!_tcp.Server.Poll(1000000, SelectMode.SelectRead)) continue;
                    if (_stopping) break;

                    using (var client = _tcp.AcceptTcpClient())
                    {
                        client.NoDelay = true;
                        client.ReceiveTimeout = 30000;
                        client.SendTimeout = 10000;
                        Handle(client);
                    }
                }
                catch when (_stopping) { break; }
                catch (Exception ex)
                {
                    if (!_stopping) Debug.LogError($"[MCP] {ex.Message}");
                }
            }
        }

        private void Handle(TcpClient client)
        {
            var ns = client.GetStream();

            // Read headers
            var hdr = new byte[8192];
            int len = 0;
            while (len < hdr.Length)
            {
                int b = ns.ReadByte();
                if (b < 0) return;
                hdr[len++] = (byte)b;
                if (len >= 4 && hdr[len-4]=='\r' && hdr[len-3]=='\n' && hdr[len-2]=='\r' && hdr[len-1]=='\n') break;
            }

            string h = Encoding.UTF8.GetString(hdr, 0, len);
            var lines = h.Split(new[]{ "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0) return;
            var rq = lines[0].Split(' ');
            if (rq.Length < 2) return;

            int cl = 0;
            for (int i = 1; i < lines.Length; i++)
                if (lines[i].StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(lines[i].Substring(15).Trim(), out cl);

            string body = null;
            if (cl > 0)
            {
                var buf = new byte[cl];
                int rd = 0;
                while (rd < cl) { int n = ns.Read(buf, rd, cl - rd); if (n <= 0) break; rd += n; }
                body = Encoding.UTF8.GetString(buf, 0, rd);
            }

            string method = rq[0], path = rq[1];

            if (method == "OPTIONS") { Reply(ns, 204, null); return; }
            if (method == "GET" && path == "/health") { Reply(ns, 200, "{\"status\":\"ok\"}"); return; }

            if (method == "POST" && (path == "/mcp" || path == "/"))
            {
                if (string.IsNullOrEmpty(body)) { Reply(ns, 400, "{\"error\":\"empty\"}"); return; }
                string json;
                try { json = OnRequestSync?.Invoke(body) ?? "{\"error\":\"no handler\"}"; }
                catch (Exception ex) { json = $"{{\"error\":\"{ex.Message.Replace("\"","'")}\"}}"; }
                Reply(ns, 200, json);
                return;
            }

            Reply(ns, 404, "{\"error\":\"POST /mcp\"}");
        }

        private static void Reply(NetworkStream s, int code, string json)
        {
            string st = code == 200 ? "OK" : code == 204 ? "No Content" : code == 400 ? "Bad Request" : "Not Found";
            byte[] bb = json != null ? Encoding.UTF8.GetBytes(json) : null;
            string r = $"HTTP/1.1 {code} {st}\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: POST,GET,OPTIONS\r\nAccess-Control-Allow-Headers: Content-Type\r\nConnection: close\r\nContent-Length: {bb?.Length ?? 0}\r\n" +
                (json != null ? "Content-Type: application/json\r\n" : "") + "\r\n";
            var hb = Encoding.UTF8.GetBytes(r);
            s.Write(hb, 0, hb.Length);
            if (bb != null) s.Write(bb, 0, bb.Length);
            s.Flush();
        }
    }
}
