using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace VoskCompanion {

    // UDP server matching the VAM plugin's sandbox-safe protocol.
    // The VAM plugin can only use UdpClient with raw byte buffers (System.IO and TCP
    // stream readers are blocked by VAM's plugin security sandbox), so we mirror that here.
    //
    // Protocol (UTF-8 datagrams):
    //   Plugin    → Companion:  PHRASES:phrase one|phrase two   (also a heartbeat)
    //   Companion → Plugin:     MATCH:phrase one
    //
    // We remember the endpoint of the last PHRASES packet and send MATCH datagrams back
    // to it. The plugin re-sends PHRASES once a second, so the return address stays fresh.
    public class SocketServer : IDisposable {

        public event Action<List<string>> OnPhrasesReceived;
        public event Action<string>       OnStatusChanged;
        public event Action<string>       OnLog;

        private UdpClient  _udp;
        private Thread     _listenThread;
        private bool       _running;
        private IPEndPoint _lastClient;       // where to send matches
        private readonly object _clientLock = new object();
        private int        _port;
        private DateTime   _lastHeartbeat = DateTime.MinValue;

        public void Start(int port) {
            _port = port;
            _running = true;
            _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            _listenThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "UdpReceive" };
            _listenThread.Start();
            OnStatusChanged?.Invoke($"Waiting for VAM  (UDP port {port})");
            OnLog?.Invoke($"UDP server listening on 127.0.0.1:{port}");
        }

        public void SendMatch(string phrase) {
            IPEndPoint target;
            lock (_clientLock) { target = _lastClient; }
            if (target == null) {
                OnLog?.Invoke($"MATCH \"{phrase}\" but no VAM endpoint known yet (is the plugin running?)");
                return;
            }
            try {
                byte[] bytes = Encoding.UTF8.GetBytes("MATCH:" + phrase);
                _udp.Send(bytes, bytes.Length, target);
            }
            catch (Exception ex) {
                OnLog?.Invoke("Send error: " + ex.Message);
            }
        }

        private void ReceiveLoop() {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running) {
                byte[] data;
                try {
                    data = _udp.Receive(ref remote); // blocking; fine on this thread
                }
                catch (SocketException) {
                    if (!_running) break;
                    continue;
                }
                catch (ObjectDisposedException) {
                    break;
                }

                try {
                    string message = Encoding.UTF8.GetString(data).Trim();
                    HandleMessage(message, remote);
                }
                catch (Exception ex) {
                    OnLog?.Invoke("Receive-handler error (recovered): " + ex.Message);
                }
            }
        }

        private void HandleMessage(string message, IPEndPoint sender) {
            if (message.StartsWith("PHRASES:")) {
                bool isNewClient;
                lock (_clientLock) {
                    isNewClient = _lastClient == null || !_lastClient.Equals(sender);
                    _lastClient = new IPEndPoint(sender.Address, sender.Port);
                }
                if (isNewClient) {
                    OnStatusChanged?.Invoke("VAM connected");
                    OnLog?.Invoke($"VAM plugin connected from {sender}");
                }

                string payload = message.Substring(8);
                var phrases = new List<string>();
                if (!string.IsNullOrWhiteSpace(payload)) {
                    foreach (string p in payload.Split('|')) {
                        string trimmed = p.Trim();
                        if (trimmed.Length > 0) phrases.Add(trimmed);
                    }
                }

                // Heartbeats arrive every second; only log phrase changes, not every beat
                OnPhrasesReceived?.Invoke(phrases);
                _lastHeartbeat = DateTime.Now;
            }
        }

        public void Dispose() {
            _running = false;
            try { _udp?.Close(); } catch { }
        }
    }
}
