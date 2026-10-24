// Made by PapaGordon
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;

namespace WindowsGSM.Plugins
{
    public class DayZ : SteamCMDAgent
    {
        public Plugin Plugin = new Plugin
        {
            name = "WindowsGSM.DayZ",
            author = "MeFriendos",
            description = "DayZ Dedicated Server with synchronized WindowsGSM settings",
            version = "0.1.0",
            url = "https://github.com/PapaGordon/WindowsGSM.DayZ",
            color = "#62734B"
        };
        private readonly ServerConfig _serverData;
        private static readonly object ConsoleLock = new object();
        public string FullName = "DayZ Dedicated Server";
        public override string AppId => "223350";
        public override bool loginAnonymous => false;
        public override string StartPath => _serverData != null && File.Exists(ServerPath.GetServersServerFiles(_serverData.ServerID, "DZSALModServer.exe"))
            ? "DZSALModServer.exe" : "DayZServer_x64.exe";
        public bool AllowsEmbedConsole = false;
        public int PortIncrements = 10;
        public object QueryMethod = new WindowsGSM.GameServer.Query.A2S();
        public string Port = "2302";
        public string QueryPort = "2305";
        public string Defaultmap = "dayzOffline.chernarusplus";
        public string Maxplayers = "60";
        public string Additional = "-config=serverDZ.cfg -profiles=profile -doLogs -adminLog -freezeCheck";

        public DayZ(ServerConfig serverData) : base(serverData) { _serverData = serverData; }

        public new async Task<Process> Update(bool validate = false, string custom = null)
        {
            var result = await WindowsGSM.Installer.SteamCMD.UpdateEx(_serverData.ServerID, AppId, validate,
                custom: custom, loginAnonymous: loginAnonymous);
            Error = result.Item2;
            return result.Item1;
        }

        public new bool IsInstallValid()
        {
            return File.Exists(ServerPath.GetServersServerFiles(_serverData.ServerID, "DayZServer_x64.exe"));
        }

        public new bool IsImportValid(string path)
        {
            Error = "Invalid Path! Fail to find DayZServer_x64.exe";
            return File.Exists(Path.Combine(path, "DayZServer_x64.exe"));
        }

        public void CreateServerCFG()
        {
            try { PrepareStart(); }
            catch (Exception e) { Error = e.Message; }
        }

        public Task<Process> Start()
        {
            Error = null;
            Notice = null;
            try
            {
                string executable = ServerPath.GetServersServerFiles(_serverData.ServerID, "DayZServer_x64.exe");
                if (!File.Exists(executable))
                    throw new FileNotFoundException("DayZServer_x64.exe not found.", executable);

                var startInfo = PrepareStart();
                startInfo.FileName = ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
                var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.Start();
                return Task.FromResult(process);
            }
            catch (Exception e)
            {
                Error = e.Message;
                return Task.FromResult<Process>(null);
            }
        }

        // Kept separate so configuration and arguments can be checked without starting DayZ.
        internal ProcessStartInfo PrepareStart()
        {
            Error = null;
            string root = ServerPath.GetServersServerFiles(_serverData.ServerID);
            int port = ParseNumber(_serverData.ServerPort, Port, "Server port", 1, 65525);
            int query = ParseNumber(_serverData.ServerQueryPort, QueryPort, "Query port", 1, 65535);
            int players = ParseNumber(_serverData.ServerMaxPlayer, Maxplayers, "Slots", 1, int.MaxValue);
            if (query >= port && query <= port + 2)
                throw new InvalidOperationException("Query port must be outside the game port range (server port through server port + 2).");
            string ip = _serverData.ServerIP;
            IPAddress parsedIP;
            if (!string.IsNullOrWhiteSpace(ip) && (!IPAddress.TryParse(ip, out parsedIP) || parsedIP.AddressFamily != AddressFamily.InterNetwork))
                throw new InvalidOperationException("Server IP must be an IPv4 address.");
            string name = string.IsNullOrWhiteSpace(_serverData.ServerName) ? FullName : _serverData.ServerName;
            if (name.Any(char.IsControl))
                throw new InvalidOperationException("Server name must not contain control characters.");
            string map = string.IsNullOrWhiteSpace(_serverData.ServerMap) ? Defaultmap : _serverData.ServerMap.Trim();
            if (!Regex.IsMatch(map, @"\A[A-Za-z0-9_.-]+\z") || map == "." || map == "..")
                throw new InvalidOperationException("Map must be a mission folder name, for example dayzOffline.chernarusplus.");
            string mission = Path.Combine(root, "mpmissions", map);
            if (!Directory.Exists(mission))
                throw new DirectoryNotFoundException("Mission folder not found: " + mission);

            var arguments = SplitArguments(_serverData.ServerParam ?? string.Empty);
            string config = GetOption(arguments, "config") ?? "serverDZ.cfg";
            string profiles = GetOption(arguments, "profiles") ?? "profile";
            string configPath = ResolvePath(root, config);
            string profilesPath = ResolvePath(root, profiles);
            var mods = new List<string>();
            foreach (var argument in arguments.Where(a => IsOption(a, "mod")))
                AddMods(mods, OptionValue(argument));
            string activated = ServerPath.GetServersConfigs(_serverData.ServerID, "DayZActivatedMods.cfg");
            if (File.Exists(activated))
                foreach (string mod in File.ReadLines(activated))
                    AddMods(mods, mod.Trim());
            if (mods.Any(m => m.IndexOf('"') >= 0 || m.Any(char.IsControl)))
                throw new InvalidOperationException("Mod paths must not contain quotes or control characters.");

            string original;
            if (File.Exists(configPath))
                original = File.ReadAllText(configPath);
            else if (string.Equals(config, "serverDZ.cfg", StringComparison.OrdinalIgnoreCase))
                original = DefaultConfiguration(map);
            else
                throw new FileNotFoundException("Custom DayZ configuration not found: " + configPath);
            string updated = SetRootSetting(original, "hostname", "\"" + name.Replace("\"", "\"\"") + "\"");
            updated = SetRootSetting(updated, "maxPlayers", players.ToString(CultureInfo.InvariantCulture));
            updated = SetRootSetting(updated, "steamQueryPort", query.ToString(CultureInfo.InvariantCulture));
            WriteConfiguration(configPath, original, updated);
            Directory.CreateDirectory(profilesPath);

            var result = new List<string>();
            foreach (string argument in arguments)
            {
                if (new[] { "config", "profiles", "port", "ip", "mission", "mod" }.Any(key => IsOption(argument, key)))
                    continue;
                result.Add(argument);
            }
            result.Add(Quote("-config=" + configPath));
            result.Add(Quote("-profiles=" + profilesPath));
            result.Add("-port=" + port.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(ip)) result.Add("-ip=" + ip);
            result.Add(Quote("-mission=" + mission));
            if (mods.Count != 0) result.Add(Quote("-mod=" + string.Join(";", mods)));
            return new ProcessStartInfo
            {
                WorkingDirectory = root,
                Arguments = string.Join(" ", result),
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }

        private static int ParseNumber(string value, string fallback, string label, int minimum, int maximum)
        {
            int number;
            if (!int.TryParse(string.IsNullOrWhiteSpace(value) ? fallback : value, NumberStyles.None, CultureInfo.InvariantCulture, out number)
                || number < minimum || number > maximum)
                throw new InvalidOperationException(label + " must be between " + minimum + " and " + maximum + ".");
            return number;
        }

        private static string ResolvePath(string root, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOf('"') >= 0 || value.Any(char.IsControl))
                throw new InvalidOperationException("Configuration and profile paths must not be empty or contain quotes or control characters.");
            string expanded = Environment.ExpandEnvironmentVariables(value);
            return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(root, expanded));
        }

        private static List<string> SplitArguments(string parameters)
        {
            if (parameters.Any(char.IsControl))
                throw new InvalidOperationException("Start parameters must not contain control characters.");
            var arguments = new List<string>();
            bool quoted = false;
            int start = 0;
            for (int i = 0; i <= parameters.Length; i++)
            {
                if (i < parameters.Length && parameters[i] == '"') quoted = !quoted;
                if (i == parameters.Length || (!quoted && char.IsWhiteSpace(parameters[i])))
                {
                    if (i > start) arguments.Add(parameters.Substring(start, i - start));
                    start = i + 1;
                }
            }
            if (quoted) throw new InvalidOperationException("Unclosed quote in start parameters.");
            return arguments;
        }

        private static bool IsOption(string argument, string key)
        {
            string decoded = argument.Replace("\"", string.Empty);
            return decoded.Equals("-" + key, StringComparison.OrdinalIgnoreCase)
                || decoded.StartsWith("-" + key + "=", StringComparison.OrdinalIgnoreCase);
        }

        private static string OptionValue(string argument)
        {
            string decoded = argument.Replace("\"", string.Empty);
            int equals = decoded.IndexOf('=');
            if (equals < 0 || equals == decoded.Length - 1)
                throw new InvalidOperationException("Use -option=value for DayZ start parameters.");
            return decoded.Substring(equals + 1);
        }

        private static string GetOption(List<string> arguments, string key)
        {
            var matches = arguments.Where(a => IsOption(a, key)).ToList();
            if (matches.Count > 1) throw new InvalidOperationException("Duplicate -" + key + " parameter.");
            return matches.Count == 0 ? null : OptionValue(matches[0]);
        }

        private static void AddMods(List<string> mods, string value)
        {
            foreach (string mod in value.Split(';').Select(m => m.Trim()).Where(m => m.Length != 0))
                if (!mods.Contains(mod, StringComparer.OrdinalIgnoreCase)) mods.Add(mod);
        }

        private static string Quote(string value)
        {
            // Windows doubles trailing backslashes before a closing quote.
            int trailing = value.Length - value.TrimEnd('\\').Length;
            return "\"" + value + new string('\\', trailing) + "\"";
        }

        private static string DefaultConfiguration(string map)
        {
            return "// Made by PapaGordon\r\n"
                + "hostname = \"DayZ Dedicated Server\";\r\npassword = \"\";\r\npasswordAdmin = \"\";\r\nmaxPlayers = 60;\r\n"
                + "verifySignatures = 2;\r\nforceSameBuild = 1;\r\ndisableVoN = 0;\r\nvonCodecQuality = 20;\r\n"
                + "disable3rdPerson = 0;\r\ndisableCrosshair = 0;\r\nserverTime = \"SystemTime\";\r\n"
                + "serverTimeAcceleration = 1;\r\nserverNightTimeAcceleration = 1;\r\nserverTimePersistent = 1;\r\n"
                + "guaranteedUpdates = 1;\r\nloginQueueConcurrentPlayers = 5;\r\nloginQueueMaxPlayers = 500;\r\n"
                + "instanceId = 1;\r\nstorageAutoFix = 1;\r\nsteamQueryPort = 2305;\r\nlogFile = \"server_console.log\";\r\n"
                + "class Missions\r\n{\r\n    class DayZ\r\n    {\r\n        template = \"" + map + "\";\r\n    };\r\n};\r\n";
        }

        private static string MaskConfiguration(string text)
        {
            char[] masked = text.ToCharArray();
            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '"' || text[i] == '\'')
                {
                    char quote = text[i];
                    masked[i] = ' ';
                    bool closed = false;
                    while (++i < text.Length)
                    {
                        masked[i] = ' ';
                        if (text[i] != quote) continue;
                        if (i + 1 < text.Length && text[i + 1] == quote) { masked[++i] = ' '; continue; }
                        closed = true;
                        break;
                    }
                    if (!closed) throw new InvalidOperationException("Unclosed string in DayZ configuration.");
                }
                else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') masked[i++] = ' ';
                }
                else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0) throw new InvalidOperationException("Unclosed comment in DayZ configuration.");
                    while (i <= end + 1) masked[i++] = ' ';
                    i--;
                }
                else if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth < 0) throw new InvalidOperationException("Unbalanced braces in DayZ configuration.");
            }
            if (depth != 0) throw new InvalidOperationException("Unbalanced braces in DayZ configuration.");
            return new string(masked);
        }

        private static string SetRootSetting(string text, string key, string value)
        {
            string mask = MaskConfiguration(text);
            var replacements = new List<Tuple<int, int>>();
            int depth = 0, cursor = 0;
            foreach (Match match in Regex.Matches(mask, @"\b" + key + @"\s*=", RegexOptions.IgnoreCase))
            {
                while (cursor < match.Index)
                {
                    if (mask[cursor] == '{') depth++;
                    else if (mask[cursor] == '}') depth--;
                    cursor++;
                }
                if (depth != 0) continue;
                int start = match.Index + match.Length;
                int end = mask.IndexOf(';', start);
                if (end < 0 || mask.Substring(start, end - start).IndexOfAny(new[] { '=', '{', '}' }) >= 0)
                    throw new InvalidOperationException("Invalid " + key + " assignment in DayZ configuration.");
                replacements.Add(Tuple.Create(start, end - start));
            }
            for (int i = replacements.Count - 1; i >= 0; i--)
                text = text.Remove(replacements[i].Item1, replacements[i].Item2).Insert(replacements[i].Item1, " " + value);
            return replacements.Count == 0 ? text + Environment.NewLine + key + " = " + value + ";" + Environment.NewLine : text;
        }

        private static void WriteConfiguration(string path, string original, string updated)
        {
            bool exists = File.Exists(path);
            if (exists && original == updated) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, updated, new UTF8Encoding(false));
                if (exists) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public async Task Stop(Process process)
        {
            if (process == null) return;
            await Task.Run(() =>
            {
                // Give DayZ time to handle CTRL+C before WindowsGSM's final fallback.
                lock (ConsoleLock)
                {
                    bool attached = false, ignored = false;
                    try
                    {
                        if (process.HasExited) return;
                        attached = AttachConsole((uint)process.Id);
                        if (attached)
                        {
                            ignored = SetConsoleCtrlHandler(null, true);
                            if (ignored && GenerateConsoleCtrlEvent(0, 0) && process.WaitForExit(30000)) return;
                        }
                    }
                    finally
                    {
                        if (attached) FreeConsole();
                        if (ignored) SetConsoleCtrlHandler(null, false);
                    }
                }
                if (!process.HasExited && process.CloseMainWindow()) process.WaitForExit(10000);
            });
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint processId);
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)] private static extern bool FreeConsole();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);
        private delegate bool ConsoleCtrlDelegate(uint ctrlType);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);
    }
}
