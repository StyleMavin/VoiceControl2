// Backends.cs — speech recognition backends for VoiceControl2.
// Part of the VoiceControl2.cslist plugin assembly.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine.Windows.Speech;

namespace StyleMavin {

    // ── Backend interface ─────────────────────────────────────────────────

    public interface IBackend {
        string Name     { get; }
        bool   IsRunning { get; }
        void   Start(List<string> commands, ConfidenceLevel confidence);
        void   Stop();
        event Action<string> OnPhraseRecognized;
        event Action<string> OnStatusMessage;
    }

    // ── Windows Speech Recognition (legacy) backend ───────────────────────

    public class WSRBackend : IBackend {
        public string Name      => "Windows Speech (Legacy)";
        public bool   IsRunning => _recognizer != null && _recognizer.IsRunning;

        public event Action<string> OnPhraseRecognized;
        public event Action<string> OnStatusMessage;

        private KeywordRecognizer _recognizer;

        public void Start(List<string> commands, ConfidenceLevel confidence) {
            Stop();
            if (commands == null || commands.Count == 0) return;
            try {
                _recognizer = new KeywordRecognizer(commands.ToArray(), confidence);
                _recognizer.OnPhraseRecognized += OnWSRPhrase;
                _recognizer.Start();
                OnStatusMessage?.Invoke($"WSR started ({commands.Count} commands, confidence: {confidence})");
            }
            catch (Exception e) {
                OnStatusMessage?.Invoke("WSR failed to start: " + e.Message);
            }
        }

        public void Stop() {
            if (_recognizer == null) return;
            try {
                if (_recognizer.IsRunning) _recognizer.Stop();
                _recognizer.OnPhraseRecognized -= OnWSRPhrase;
                _recognizer.Dispose();
            }
            catch { }
            finally { _recognizer = null; }
        }

        private void OnWSRPhrase(PhraseRecognizedEventArgs args) {
            OnPhraseRecognized?.Invoke(args.text);
        }
    }

    // ── Vosk Companion backend ────────────────────────────────────────────

    // Communicates with VoskCompanion.exe over localhost UDP.
    // Uses a NON-BLOCKING UdpClient polled from Unity's main thread (in Poll(), called
    // from Update) — no System.IO, no threads, no queues. This mirrors the proven
    // pattern used by Yoooi's BusDriver / ToySerialController plugins, which is the
    // only networking approach that passes VAM's plugin security sandbox.
    //
    // Protocol (UTF-8 datagrams):
    //   Plugin    → Companion:  PHRASES:phrase one|phrase two   (also acts as heartbeat)
    //   Companion → Plugin:     MATCH:phrase one
    public class VoskCompanionBackend : IBackend {
        public string Name      => "Vosk Companion";
        public bool   IsRunning => _udp != null;

        public event Action<string> OnPhraseRecognized;
        public event Action<string> OnStatusMessage;

        private int          _companionPort;
        private UdpClient    _udp;
        private EndPoint     _recvEndpoint = new IPEndPoint(IPAddress.Any, 0);
        private readonly byte[] _readBuffer = new byte[8192];
        private List<string> _phrases = new List<string>();
        private float        _heartbeatTimer;
        private const float  HeartbeatInterval = 1.0f;

        public VoskCompanionBackend(int port) { _companionPort = port; }

        public void SetPort(int port) { _companionPort = port; }

        public void Start(List<string> commands, ConfidenceLevel confidence) {
            _phrases = new List<string>(commands ?? new List<string>());
            try {
                _udp = new UdpClient();
                _udp.ExclusiveAddressUse = false;
                _udp.Client.Blocking = false;
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                // Bind to an ephemeral loopback port so the companion's replies reach us
                _udp.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                _heartbeatTimer = HeartbeatInterval; // force an immediate send on first Poll
                OnStatusMessage?.Invoke($"Vosk Companion backend ready (sending to port {_companionPort}).");
            }
            catch (Exception e) {
                OnStatusMessage?.Invoke("Vosk UDP socket failed to start: " + e.Message);
                _udp = null;
            }
        }

        public void Stop() {
            if (_udp == null) return;
            try { _udp.Close(); } catch { }
            _udp = null;
        }

        public void UpdatePhrases(List<string> commands) {
            _phrases = new List<string>(commands ?? new List<string>());
            SendPhrases();
        }

        // Called from Unity's Update() every frame. Drains incoming matches and
        // periodically re-sends the phrase list as a heartbeat. Main-thread only.
        public void Poll(float deltaTime) {
            if (_udp == null) return;

            // Drain all pending datagrams without blocking
            while (true) {
                int received;
                try {
                    received = _udp.Client.ReceiveFrom(_readBuffer, ref _recvEndpoint);
                }
                catch (SocketException e) {
                    if (e.SocketErrorCode == SocketError.WouldBlock) break; // nothing waiting
                    break;
                }
                if (received <= 0) break;
                string msg = Encoding.UTF8.GetString(_readBuffer, 0, received);
                if (msg.StartsWith("MATCH:"))
                    OnPhraseRecognized?.Invoke(msg.Substring(6).Trim());
            }

            // Heartbeat / re-sync
            _heartbeatTimer += deltaTime;
            if (_heartbeatTimer >= HeartbeatInterval) {
                _heartbeatTimer = 0f;
                SendPhrases();
            }
        }

        private void SendPhrases() {
            if (_udp == null) return;
            try {
                byte[] bytes = Encoding.UTF8.GetBytes("PHRASES:" + string.Join("|", _phrases.ToArray()));
                _udp.Send(bytes, bytes.Length, "127.0.0.1", _companionPort);
            }
            catch { /* companion may not be running yet; heartbeat will retry */ }
        }
    }
}
