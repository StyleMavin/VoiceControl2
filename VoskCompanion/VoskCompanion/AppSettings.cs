using System;
using System.IO;
using System.Text;

namespace VoskCompanion {

    public class AppSettings {

        public int    Port           = 19547;
        public string ModelPath      = "";
        public float  MinConfidence  = 0.75f;
        public bool   AutoStart      = false;
        public bool   LogRecognition = true;

        private static readonly string SettingsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings Load() {
            try {
                if (File.Exists(SettingsPath)) {
                    string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                    return ParseJson(json);
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save() {
            try {
                string json = ToJson();
                File.WriteAllText(SettingsPath, json, Encoding.UTF8);
            }
            catch { }
        }

        // Hand-rolled JSON to avoid a Newtonsoft dependency
        private string ToJson() {
            return "{\n" +
                $"  \"port\": {Port},\n" +
                $"  \"modelPath\": \"{Escape(ModelPath)}\",\n" +
                $"  \"minConfidence\": {MinConfidence.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},\n" +
                $"  \"autoStart\": {AutoStart.ToString().ToLower()},\n" +
                $"  \"logRecognition\": {LogRecognition.ToString().ToLower()}\n" +
                "}";
        }

        private static AppSettings ParseJson(string json) {
            var s = new AppSettings();
            s.Port          = ReadInt(json,   "port",           s.Port);
            s.ModelPath     = ReadString(json, "modelPath",     s.ModelPath);
            s.MinConfidence = ReadFloat(json,  "minConfidence", s.MinConfidence);
            s.AutoStart     = ReadBool(json,   "autoStart",     s.AutoStart);
            s.LogRecognition= ReadBool(json,   "logRecognition",s.LogRecognition);
            return s;
        }

        private static string Escape(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static int ReadInt(string json, string key, int fallback) {
            try {
                int i = json.IndexOf($"\"{key}\"");
                if (i < 0) return fallback;
                int colon = json.IndexOf(':', i);
                int start = colon + 1;
                while (start < json.Length && json[start] == ' ') start++;
                int end = start;
                while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
                return int.Parse(json.Substring(start, end - start));
            } catch { return fallback; }
        }

        private static float ReadFloat(string json, string key, float fallback) {
            try {
                int i = json.IndexOf($"\"{key}\"");
                if (i < 0) return fallback;
                int colon = json.IndexOf(':', i);
                int start = colon + 1;
                while (start < json.Length && json[start] == ' ') start++;
                int end = start;
                while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-')) end++;
                return float.Parse(json.Substring(start, end - start),
                    System.Globalization.CultureInfo.InvariantCulture);
            } catch { return fallback; }
        }

        private static string ReadString(string json, string key, string fallback) {
            try {
                int i = json.IndexOf($"\"{key}\"");
                if (i < 0) return fallback;
                int colon = json.IndexOf(':', i);
                int open = json.IndexOf('"', colon + 1);
                int close = open + 1;
                while (close < json.Length) {
                    if (json[close] == '\\') { close += 2; continue; }
                    if (json[close] == '"') break;
                    close++;
                }
                return json.Substring(open + 1, close - open - 1)
                           .Replace("\\\\", "\\").Replace("\\\"", "\"");
            } catch { return fallback; }
        }

        private static bool ReadBool(string json, string key, bool fallback) {
            try {
                int i = json.IndexOf($"\"{key}\"");
                if (i < 0) return fallback;
                int colon = json.IndexOf(':', i);
                int start = colon + 1;
                while (start < json.Length && json[start] == ' ') start++;
                return json.Substring(start, 4).StartsWith("true");
            } catch { return fallback; }
        }
    }
}
