using System;
using System.Collections.Generic;
using System.Text;
using NAudio.Wave;
using Vosk;

namespace VoskCompanion {

    public class SpeechEngine : IDisposable {

        public event Action<string> OnPhraseMatched;
        public event Action<string> OnStatusChanged;
        public event Action<string> OnLog;

        private Model          _model;
        private VoskRecognizer _recognizer;
        private WaveInEvent    _waveIn;
        private List<string>   _phrases = new List<string>();
        private string         _currentGrammar;       // grammar the live recognizer was built with
        private readonly object _recognizerLock = new object();
        private bool  _running;
        private float _minConfidence;

        public void Init(string modelPath, float minConfidence) {
            _minConfidence = minConfidence;
            Vosk.Vosk.SetLogLevel(-1); // suppress Vosk console noise
            _model = new Model(modelPath);

            _waveIn = new WaveInEvent();
            _waveIn.WaveFormat = new WaveFormat(16000, 16, 1);
            _waveIn.BufferMilliseconds = 100;
            _waveIn.DataAvailable += OnDataAvailable;
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
            if (_waveIn == null) return;
            _running = true;
            _waveIn.StartRecording();
        }

        public void Stop() {
            _running = false;
            _waveIn?.StopRecording();
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e) {
            if (!_running) return;
            lock (_recognizerLock) {
                if (_recognizer == null) return;
                if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded)) {
                    string text = ExtractText(_recognizer.Result());
                    if (!string.IsNullOrWhiteSpace(text)) {
                        OnLog?.Invoke("Vosk heard: \"" + text + "\"");
                        CheckForMatch(text);
                    }
                }
                // else: partial result — wait for the final result on silence
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
            Stop();
            _waveIn?.Dispose();
            lock (_recognizerLock) {
                _recognizer?.Dispose();
                _recognizer = null;
            }
            _model?.Dispose();
        }
    }
}
