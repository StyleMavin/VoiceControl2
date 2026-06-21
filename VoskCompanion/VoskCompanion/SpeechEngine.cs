using System;
using System.Collections.Generic;
using System.Text;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Vosk;

namespace VoskCompanion {

    public class SpeechEngine : IDisposable {

        public event Action<string> OnPhraseMatched;
        public event Action<string> OnStatusChanged;
        public event Action<string> OnLog;

        private Model          _model;
        private VoskRecognizer _recognizer;
        private List<string>   _phrases = new List<string>();
        private string         _currentGrammar;       // grammar the live recognizer was built with
        private readonly object _recognizerLock = new object();
        private bool  _running;
        private float _minConfidence;

        // Audio capture uses WASAPI (modern Windows audio API). The legacy WaveInEvent
        // (WinMM waveInOpen) requesting an exact 16 kHz format was wedging some audio
        // drivers — breaking both mic AND playback system-wide. WASAPI captures at the
        // device's native format; we resample to the 16 kHz mono PCM that Vosk needs.
        private WasapiCapture        _capture;
        private BufferedWaveProvider _captureBuffer;
        private IWaveProvider        _provider16k;     // 16 kHz mono 16-bit, fed to Vosk
        private byte[]               _readBuf;

        public void Init(string modelPath, float minConfidence) {
            _minConfidence = minConfidence;
            Vosk.Vosk.SetLogLevel(-1); // suppress Vosk console noise
            _model = new Model(modelPath);
        }

        // Rebuilds the recognizer with a grammar limited to the current phrases.
        // Grammar-constrained recognition is FAR more accurate for short keywords than
        // free dictation — it mirrors how the legacy KeywordRecognizer works.
        // Only rebuilds when the phrase set actually changes (the plugin re-sends the
        // list every second as a heartbeat, so we must ignore unchanged updates).
        public void SetPhrases(List<string> phrases) {
            var clean = new List<string>();
            if (phrases != null) {
                foreach (string p in phrases) {
                    string t = (p ?? "").ToLowerInvariant().Trim();
                    if (t.Length > 0) clean.Add(t);
                }
            }

            string grammar = BuildGrammar(clean);

            lock (_recognizerLock) {
                if (grammar == _currentGrammar && _recognizer != null) return; // unchanged & live

                _phrases = clean;

                _recognizer?.Dispose();
                _recognizer = null;

                if (clean.Count > 0) {
                    try {
                        // Grammar JSON: ["red", "white", "[unk]"]  ([unk] absorbs off-list speech)
                        _recognizer = new VoskRecognizer(_model, 16000f, grammar);
                        _recognizer.SetWords(true);
                        _currentGrammar = grammar; // only mark applied AFTER a successful build
                        OnLog?.Invoke("Recognizer built with grammar: " + grammar);
                        OnStatusChanged?.Invoke($"Listening  ({clean.Count} phrase{(clean.Count == 1 ? "" : "s")})");
                    }
                    catch (Exception ex) {
                        _currentGrammar = null; // allow retry on next update
                        OnLog?.Invoke("GRAMMAR BUILD FAILED: " + ex.Message + " — falling back to free dictation.");
                        // Fallback: free-dictation recognizer; matching still filters to phrases
                        try {
                            _recognizer = new VoskRecognizer(_model, 16000f);
                            _recognizer.SetWords(true);
                            OnStatusChanged?.Invoke($"Listening (free mode, {clean.Count} phrases)");
                        }
                        catch (Exception ex2) {
                            OnLog?.Invoke("FREE RECOGNIZER ALSO FAILED: " + ex2.Message);
                        }
                    }
                } else {
                    _currentGrammar = grammar;
                    OnStatusChanged?.Invoke("No phrases set");
                }
            }
        }

        private static string BuildGrammar(List<string> phrases) {
            if (phrases.Count == 0) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < phrases.Count; i++) {
                if (i > 0) sb.Append(", ");
                sb.Append('"').Append(EscapeJson(phrases[i])).Append('"');
            }
            sb.Append(", \"[unk]\"]"); // allow Vosk to report unrecognized speech instead of forcing a match
            return sb.ToString();
        }

        private static string EscapeJson(string s) {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public void Start() {
            try {
                // Default capture device, shared mode (won't block other apps' audio)
                _capture = new WasapiCapture();
                WaveFormat src = _capture.WaveFormat;
                OnLog?.Invoke($"Microphone format: {src.SampleRate} Hz, {src.Channels} ch, {src.BitsPerSample}-bit ({src.Encoding})");

                _captureBuffer = new BufferedWaveProvider(src) {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromSeconds(2),
                    // CRITICAL: default is true, which makes Read() return silence-padded
                    // full buffers forever — turning our drain loop into an infinite loop
                    // (and freezing the UI, since WASAPI raises the callback on this thread).
                    // false => Read() returns only what's available, and 0 when empty.
                    ReadFully = false
                };

                // Build a conversion chain -> mono -> 16 kHz -> 16-bit PCM (what Vosk wants)
                ISampleProvider samples = _captureBuffer.ToSampleProvider();
                if (src.Channels == 2)
                    samples = new StereoToMonoSampleProvider(samples) { LeftVolume = 0.5f, RightVolume = 0.5f };
                else if (src.Channels > 2)
                    samples = new MultiplexingSampleProvider(new[] { samples }, 1); // take channel 0
                var resampled = new WdlResamplingSampleProvider(samples, 16000);
                _provider16k = new SampleToWaveProvider16(resampled);
                _readBuf = new byte[3200]; // ~0.1 s of 16 kHz mono 16-bit audio

                _capture.DataAvailable += OnCaptureData;
                _running = true;
                _capture.StartRecording();
            }
            catch (Exception ex) {
                _running = false;
                try { _capture?.Dispose(); } catch { }
                _capture = null;
                throw new InvalidOperationException(
                    "Could not open the microphone. It may be in use, disabled, or unavailable. " +
                    "Check Windows Sound settings (set a default input device), close other apps using the mic, then click Restart. " +
                    "(If audio is glitchy, run the Windows 'Troubleshoot sound problems' tool.) [" + ex.Message + "]", ex);
            }
        }

        public void Stop() {
            _running = false;
            try { _capture?.StopRecording(); } catch { }
            try { _capture?.Dispose(); } catch { }
            _capture       = null;
            _captureBuffer = null;
            _provider16k   = null;
        }

        private void OnCaptureData(object sender, WaveInEventArgs e) {
            if (!_running || _provider16k == null) return;

            // Feed raw captured audio into the conversion chain
            _captureBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

            lock (_recognizerLock) {
                // Pull all available converted (16 kHz mono 16-bit) audio and feed Vosk.
                // The guard cap is belt-and-suspenders: even if a provider ever misbehaves
                // and won't return 0, we can never spin forever and freeze again.
                int read;
                int guard = 0;
                while ((read = _provider16k.Read(_readBuf, 0, _readBuf.Length)) > 0 && guard++ < 1000) {
                    if (_recognizer == null) continue; // no phrases yet — just drain
                    if (_recognizer.AcceptWaveform(_readBuf, read)) {
                        string text = ExtractText(_recognizer.Result());
                        if (!string.IsNullOrWhiteSpace(text)) {
                            OnLog?.Invoke("Vosk heard: \"" + text + "\"");
                            CheckForMatch(text);
                        }
                    }
                    // else: partial result — wait for the final result on silence
                }
            }
        }

        private void CheckForMatch(string transcribed) {
            string normalised = transcribed.ToLowerInvariant().Trim();
            if (normalised.Length == 0 || normalised == "[unk]") return;

            // Longest match wins so a short phrase doesn't shadow a longer one
            string bestMatch = null;
            foreach (string phrase in _phrases) {
                if (phrase.Length == 0) continue;
                if (IsWholeWordMatch(normalised, phrase)) {
                    if (bestMatch == null || phrase.Length > bestMatch.Length)
                        bestMatch = phrase;
                }
            }

            if (bestMatch != null) {
                OnLog?.Invoke("MATCH: \"" + bestMatch + "\"");
                OnPhraseMatched?.Invoke(bestMatch);
            } else {
                OnLog?.Invoke("No phrase matched \"" + normalised + "\"");
            }
        }

        private bool IsWholeWordMatch(string text, string phrase) {
            int idx = text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            bool leftOk  = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            bool rightOk = idx + phrase.Length == text.Length
                           || !char.IsLetterOrDigit(text[idx + phrase.Length]);
            return leftOk && rightOk;
        }

        // Extract "text" field from Vosk JSON: {"text": "hello world"}
        private string ExtractText(string json) {
            int i = json.IndexOf("\"text\"");
            if (i < 0) return null;
            int colon = json.IndexOf(':', i);
            int open  = json.IndexOf('"', colon + 1);
            int close = json.IndexOf('"', open + 1);
            if (open < 0 || close < 0) return null;
            return json.Substring(open + 1, close - open - 1);
        }

        public void Dispose() {
            Stop(); // stops + disposes the WASAPI capture, releasing the mic
            lock (_recognizerLock) {
                _recognizer?.Dispose();
                _recognizer = null;
            }
            _model?.Dispose();
        }
    }
}
