using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ExchangeAuditTool
{
    // A named set of connection-page inputs. Passwords are never stored:
    // Basic-remote still prompts via Get-Credential at connect time, and a
    // thumbprint/AppId/UPN is not a secret.
    internal sealed class ConnectionProfile
    {
        public string Name = "";
        public ConnectionMode Mode = ConnectionMode.ExchangeOnlineInteractive;
        public string Upn = "";
        public bool DisableWam = true;
        public string AppId = "";
        public string Organization = "";
        public string CertThumbprint = "";
        public string RemoteServer = "";
        public string RemoteUser = "";
        public RemoteAuthMode RemoteAuth = RemoteAuthMode.Kerberos;
        public bool RemoteUseHttps;
    }

    // Flat-file store beside the activity log. Hand-rolled minimal JSON keeps
    // the zero-dependency rule (no NuGet, csc fallback safe); the shape is
    // fixed and fully covered by round-trip tests.
    internal static class ConnectionProfileStore
    {
        public static string ProfilesPath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ExchangeAudit");
            try { Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "profiles.json");
        }

        public static void Load(out List<ConnectionProfile> profiles, out string lastUsed)
        {
            LoadFrom(ProfilesPath(), out profiles, out lastUsed);
        }

        internal static void LoadFrom(string path, out List<ConnectionProfile> profiles, out string lastUsed)
        {
            profiles = new List<ConnectionProfile>();
            lastUsed = "";
            string text;
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch { return; }
            try
            {
                Dictionary<string, string> root = MiniJson.ParseObject(text);
                string lu;
                if (root.TryGetValue("lastUsed", out lu)) lastUsed = lu;
                string arr;
                if (!root.TryGetValue("profiles", out arr)) return;
                foreach (string obj in MiniJson.SplitArray(arr))
                {
                    Dictionary<string, string> m = MiniJson.ParseObject(obj);
                    var p = new ConnectionProfile();
                    string s;
                    if (m.TryGetValue("name", out s)) p.Name = s;
                    if (m.TryGetValue("mode", out s)) p.Mode = ParseMode(s, p.Mode);
                    if (m.TryGetValue("upn", out s)) p.Upn = s;
                    if (m.TryGetValue("appId", out s)) p.AppId = s;
                    if (m.TryGetValue("organization", out s)) p.Organization = s;
                    if (m.TryGetValue("thumbprint", out s)) p.CertThumbprint = s;
                    if (m.TryGetValue("server", out s)) p.RemoteServer = s;
                    if (m.TryGetValue("user", out s)) p.RemoteUser = s;
                    if (m.TryGetValue("auth", out s)) p.RemoteAuth = ParseAuth(s, p.RemoteAuth);
                    if (m.TryGetValue("disableWam", out s)) p.DisableWam = ParseBool(s, true);
                    if (m.TryGetValue("https", out s)) p.RemoteUseHttps = ParseBool(s, false);
                    if (p.Name.Length > 0) profiles.Add(p);
                }
            }
            catch { }
        }

        public static void Save(List<ConnectionProfile> profiles, string lastUsed)
        {
            SaveTo(ProfilesPath(), profiles, lastUsed);
        }

        internal static void SaveTo(string path, List<ConnectionProfile> profiles, string lastUsed)
        {
            var sb = new StringBuilder();
            sb.Append("{\"lastUsed\":");
            MiniJson.AppendString(sb, lastUsed ?? "");
            sb.Append(",\"profiles\":[");
            for (int i = 0; i < profiles.Count; i++)
            {
                if (i > 0) sb.Append(',');
                ConnectionProfile p = profiles[i];
                sb.Append('{');
                Field(sb, "name", p.Name, true);
                Field(sb, "mode", ModeName(p.Mode), false);
                Field(sb, "upn", p.Upn, false);
                Field(sb, "disableWam", p.DisableWam ? "true" : "false", false);
                Field(sb, "appId", p.AppId, false);
                Field(sb, "organization", p.Organization, false);
                Field(sb, "thumbprint", p.CertThumbprint, false);
                Field(sb, "server", p.RemoteServer, false);
                Field(sb, "user", p.RemoteUser, false);
                Field(sb, "auth", p.RemoteAuth == RemoteAuthMode.Basic ? "Basic" : "Kerberos", false);
                Field(sb, "https", p.RemoteUseHttps ? "true" : "false", false);
                sb.Append('}');
            }
            sb.Append("]}");
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        private static void Field(StringBuilder sb, string key, string value, bool first)
        {
            if (!first) sb.Append(',');
            MiniJson.AppendString(sb, key);
            sb.Append(':');
            MiniJson.AppendString(sb, value ?? "");
        }

        internal static string ModeName(ConnectionMode mode)
        {
            switch (mode)
            {
                case ConnectionMode.ExchangeOnlineApp: return "AppOnly";
                case ConnectionMode.OnPremisesLocal: return "Local";
                case ConnectionMode.OnPremisesRemote: return "Remote";
                default: return "Interactive";
            }
        }

        internal static ConnectionMode ParseMode(string s, ConnectionMode fallback)
        {
            if (s == "AppOnly") return ConnectionMode.ExchangeOnlineApp;
            if (s == "Local") return ConnectionMode.OnPremisesLocal;
            if (s == "Remote") return ConnectionMode.OnPremisesRemote;
            if (s == "Interactive") return ConnectionMode.ExchangeOnlineInteractive;
            return fallback;
        }

        internal static RemoteAuthMode ParseAuth(string s, RemoteAuthMode fallback)
        {
            if (s == "Basic") return RemoteAuthMode.Basic;
            if (s == "Kerberos") return RemoteAuthMode.Kerberos;
            return fallback;
        }

        internal static bool ParseBool(string s, bool fallback)
        {
            if (s == "true") return true;
            if (s == "false") return false;
            return fallback;
        }
    }

    // Minimal JSON reader/writer for the flat profile shape: objects of
    // string keys to string values, true/false literals, and arrays of
    // objects. Anything else is rejected, never executed.
    internal static class MiniJson
    {
        public static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (s != null)
            {
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < ' ') sb.Append("\\u" + ((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
        }

        public static Dictionary<string, string> ParseObject(string text)
        {
            var map = new Dictionary<string, string>();
            int i = Skip(text, 0);
            if (i >= text.Length || text[i] != '{') throw new FormatException("Not an object.");
            i++;
            for (;;)
            {
                i = Skip(text, i);
                if (i < text.Length && text[i] == '}') break;
                string key = ParseString(text, ref i);
                i = Skip(text, i);
                if (i >= text.Length || text[i] != ':') throw new FormatException("Expected ':'.");
                i++;
                i = Skip(text, i);
                map[key] = ParseValue(text, ref i);
                i = Skip(text, i);
                if (i < text.Length && text[i] == ',') { i++; continue; }
                if (i < text.Length && text[i] == '}') break;
                throw new FormatException("Expected ',' or '}'.");
            }
            return map;
        }

        public static List<string> SplitArray(string text)
        {
            var items = new List<string>();
            int i = Skip(text, 0);
            if (i >= text.Length || text[i] != '[') throw new FormatException("Not an array.");
            i++;
            for (;;)
            {
                i = Skip(text, i);
                if (i < text.Length && text[i] == ']') break;
                if (i >= text.Length || text[i] != '{') throw new FormatException("Expected object.");
                int depth = 0;
                bool inStr = false;
                int start = i;
                while (i < text.Length)
                {
                    char c = text[i];
                    if (inStr)
                    {
                        if (c == '\\') i++;
                        else if (c == '"') inStr = false;
                    }
                    else
                    {
                        if (c == '"') inStr = true;
                        else if (c == '{') depth++;
                        else if (c == '}')
                        {
                            depth--;
                            if (depth == 0) { i++; break; }
                        }
                    }
                    i++;
                }
                items.Add(text.Substring(start, i - start));
                i = Skip(text, i);
                if (i < text.Length && text[i] == ',') { i++; continue; }
                if (i < text.Length && text[i] == ']') break;
                throw new FormatException("Expected ',' or ']'.");
            }
            return items;
        }

        private static string ParseValue(string text, ref int i)
        {
            if (i < text.Length && text[i] == '"') return ParseString(text, ref i);
            if (StartsWith(text, i, "true")) { i += 4; return "true"; }
            if (StartsWith(text, i, "false")) { i += 5; return "false"; }
            if (i < text.Length && text[i] == '[')
            {
                int start = i;
                int depth = 0;
                bool inStr = false;
                while (i < text.Length)
                {
                    char c = text[i];
                    if (inStr)
                    {
                        if (c == '\\') i++;
                        else if (c == '"') inStr = false;
                    }
                    else
                    {
                        if (c == '"') inStr = true;
                        else if (c == '[') depth++;
                        else if (c == ']')
                        {
                            depth--;
                            if (depth == 0) { i++; break; }
                        }
                    }
                    i++;
                }
                return text.Substring(start, i - start);
            }
            throw new FormatException("Unsupported value.");
        }

        private static string ParseString(string text, ref int i)
        {
            if (i >= text.Length || text[i] != '"') throw new FormatException("Expected string.");
            i++;
            var sb = new StringBuilder();
            while (i < text.Length)
            {
                char c = text[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (i >= text.Length) break;
                    char e = text[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= text.Length)
                            {
                                sb.Append((char)Convert.ToInt32(text.Substring(i, 4), 16));
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            throw new FormatException("Unterminated string.");
        }

        private static int Skip(string text, int i)
        {
            while (i < text.Length)
            {
                char c = text[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') i++;
                else break;
            }
            return i;
        }

        private static bool StartsWith(string text, int i, string s)
        {
            if (i + s.Length > text.Length) return false;
            for (int k = 0; k < s.Length; k++)
                if (text[i + k] != s[k]) return false;
            return true;
        }
    }
}
