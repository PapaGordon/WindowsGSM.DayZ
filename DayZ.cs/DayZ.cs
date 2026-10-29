// Made by PapaGordon
using System;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
            description = "DayZ Dedicated Server with synchronized WindowsGSM settings and RPT console",
            version = "0.1.2",
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
        public bool AllowsEmbedConsole = true;
        public bool AllowsConsoleInput = false;
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

        public async Task<Process> Start()
        {
            Error = null;
            Notice = null;
            try
            {
                string executable = ServerPath.GetServersServerFiles(_serverData.ServerID, "DayZServer_x64.exe");
                if (!File.Exists(executable))
                    throw new FileNotFoundException("DayZServer_x64.exe not found.", executable);

                var startInfo = PrepareStart();
                var workshop = await DayZWorkshopUpdater.Update(startInfo.WorkingDirectory,
                    ServerPath.GetServersConfigs(_serverData.ServerID), ServerPath.GetBin("steamcmd"), "221100");
                startInfo.Arguments = DayZWorkshopUpdater.MergeArguments(startInfo.Arguments, workshop);
                startInfo.FileName = ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
                string profiles = ResolvePath(startInfo.WorkingDirectory,
                    GetOption(SplitArguments(_serverData.ServerParam ?? string.Empty), "profiles") ?? "profile");
                var snapshot = AllowsEmbedConsole ? SnapshotRptFiles(profiles) : null;
                var console = AllowsEmbedConsole ? new ServerConsole(_serverData.ServerID) : null;
                var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.Start();
#pragma warning disable 4014
                Task.Run(() => MonitorNativeWindow(process));
                if (AllowsEmbedConsole)
                {
                    if (SplitArguments(startInfo.Arguments).Any(argument => IsOption(argument, "noLogs")))
                        AddEmbeddedConsoleLine(console, "[WindowsGSM] Remove -noLogs to enable DayZ RPT output.");
                    else
                        Task.Run(() => FollowRptLog(process, profiles, snapshot, line => AddEmbeddedConsoleLine(console, line)));
                }
#pragma warning restore 4014
                return process;
            }
            catch (Exception e)
            {
                Error = e.Message;
                return null;
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
            if (AllowsEmbedConsole && !arguments.Any(argument => IsOption(argument, "noLogs") || IsOption(argument, "doLogs")))
                result.Add("-doLogs");
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
                WindowStyle = ProcessWindowStyle.Minimized
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

        private static readonly ConstructorInfo OutputEventConstructor = typeof(DataReceivedEventArgs).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);

        private void AddEmbeddedConsoleLine(ServerConsole console, string line)
        {
            if (console == null || line == null) return;
            try
            {
                if (OutputEventConstructor != null)
                    console.AddOutput(null, (DataReceivedEventArgs)OutputEventConstructor.Invoke(new object[] { line }));
            }
            catch { /* Log output must not interrupt the server. */ }
        }

        private static IEnumerable<string> GetRptFiles(string directory)
        {
            var files = new List<string>();
            try
            {
                files.AddRange(Directory.GetFiles(directory, "*.rpt", SearchOption.TopDirectoryOnly));
                foreach (string child in Directory.GetDirectories(directory))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            files.AddRange(Directory.GetFiles(child, "*.rpt", SearchOption.TopDirectoryOnly));
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return files.Where(file => Path.GetFileName(file).StartsWith("DayZ", StringComparison.OrdinalIgnoreCase));
        }

        private static Dictionary<string, Tuple<long, DateTime>> SnapshotRptFiles(string directory)
        {
            var snapshot = new Dictionary<string, Tuple<long, DateTime>>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in GetRptFiles(directory))
            {
                try
                {
                    var info = new FileInfo(file);
                    snapshot[file] = Tuple.Create(info.Length, info.LastWriteTimeUtc);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return snapshot;
        }

        private static string FindActiveRptFile(string directory, Dictionary<string, Tuple<long, DateTime>> snapshot)
        {
            string active = null;
            DateTime newest = DateTime.MinValue;
            foreach (string file in GetRptFiles(directory))
            {
                try
                {
                    var info = new FileInfo(file);
                    Tuple<long, DateTime> old;
                    if (snapshot.TryGetValue(file, out old) && info.Length == old.Item1 && info.LastWriteTimeUtc == old.Item2)
                        continue;
                    if (active == null || info.LastWriteTimeUtc > newest)
                    {
                        active = file;
                        newest = info.LastWriteTimeUtc;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return active;
        }

        private static bool ProcessEnded(Process process)
        {
            try { return process.HasExited; }
            catch (InvalidOperationException) { return true; }
        }

        private static async Task FollowRptLog(Process process, string directory,
            Dictionary<string, Tuple<long, DateTime>> snapshot, Action<string> output)
        {
            string current = null;
            long offset = 0;
            DateTime created = DateTime.MinValue;
            var decoder = Encoding.UTF8.GetDecoder();
            var pending = new StringBuilder();
            var bytes = new byte[8192];
            var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
            bool exitDrained = false;
            bool waitingNotice = false;
            DateTime noticeAt = DateTime.UtcNow.AddSeconds(15);
            output("[WindowsGSM] Waiting for DayZ RPT output in " + directory);
            try
            {
                while (true)
                {
                    string active = FindActiveRptFile(directory, snapshot);
                    if (active != null)
                    {
                        try
                        {
                            var info = new FileInfo(active);
                            bool changedFile = !string.Equals(active, current, StringComparison.OrdinalIgnoreCase);
                            using (var stream = new FileStream(active, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            {
                                if (changedFile || info.CreationTimeUtc != created || stream.Length < offset)
                                {
                                    current = active;
                                    created = info.CreationTimeUtc;
                                    offset = 0;
                                    Tuple<long, DateTime> old;
                                    if (changedFile && snapshot.TryGetValue(active, out old) && stream.Length > old.Item1)
                                        offset = old.Item1;
                                    decoder.Reset();
                                    pending.Clear();
                                    output("[WindowsGSM] Reading " + Path.GetFileName(active));
                                }
                                stream.Position = offset;
                                int read;
                                // Bound each pass so shutdown and rotation checks cannot starve.
                                int remaining = 1024 * 1024;
                                while (remaining > 0 && (read = stream.Read(bytes, 0, Math.Min(bytes.Length, remaining))) > 0)
                                {
                                    int count = decoder.GetChars(bytes, 0, read, chars, 0, false);
                                    for (int i = 0; i < count; i++)
                                    {
                                        if (chars[i] == '\n')
                                        {
                                            output(pending.ToString().TrimEnd('\r').TrimStart('\uFEFF'));
                                            pending.Clear();
                                        }
                                        else pending.Append(chars[i]);
                                    }
                                    offset = stream.Position;
                                    remaining -= read;
                                }
                            }
                        }
                        catch (IOException) { /* Retry files that are being replaced or opened. */ }
                        catch (UnauthorizedAccessException)
                        {
                            output("[WindowsGSM] Cannot read the DayZ RPT file. Check the profile folder permissions.");
                            return;
                        }
                    }
                    if (!waitingNotice && current == null && DateTime.UtcNow >= noticeAt)
                    {
                        waitingNotice = true;
                        output("[WindowsGSM] No DayZ RPT file yet. Check -profiles and -doLogs.");
                    }
                    if (ProcessEnded(process))
                    {
                        if (exitDrained) break;
                        exitDrained = true;
                        await Task.Delay(300);
                    }
                    else await Task.Delay(150);
                }
                if (pending.Length > 0) output(pending.ToString().TrimEnd('\r').TrimStart('\uFEFF'));
            }
            catch (Exception e) { output("[WindowsGSM] DayZ RPT output stopped: " + e.Message); }
        }

        private static bool IsNativeWindow(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindow(window)) return false;
            var name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            return !string.Equals(name.ToString(), "PseudoConsoleWindow", StringComparison.OrdinalIgnoreCase);
        }

        private static IntPtr FindNativeWindow(Process process)
        {
            if (ProcessEnded(process)) return IntPtr.Zero;
            process.Refresh();
            IntPtr window = process.MainWindowHandle;
            if (IsNativeWindow(window)) return window;
            EnumWindows(delegate(IntPtr candidate, IntPtr unused)
            {
                uint owner;
                GetWindowThreadProcessId(candidate, out owner);
                if (owner != (uint)process.Id || GetWindow(candidate, 4) != IntPtr.Zero
                    || GetWindowTextLength(candidate) == 0 || (GetWindowLong(candidate, -16) & 0x00C00000) != 0x00C00000 || !IsNativeWindow(candidate)) return true;
                window = candidate;
                return false;
            }, IntPtr.Zero);
            if (window != IntPtr.Zero) return window;
            lock (ConsoleLock)
            {
                // Keep any console WindowsGSM already owns attached.
                if (GetConsoleWindow() != IntPtr.Zero || !AttachConsole((uint)process.Id)) return IntPtr.Zero;
                try
                {
                    window = GetConsoleWindow();
                    return IsNativeWindow(window) ? window : IntPtr.Zero;
                }
                finally { FreeConsole(); }
            }
        }

        private async Task MonitorNativeWindow(Process process)
        {
            int id;
            if (!int.TryParse(_serverData.ServerID, out id)) return;
            IntPtr window = IntPtr.Zero;
            bool registered = false;
            bool? lastVisible = null;
            try
            {
                while (!ProcessEnded(process))
                {
                    if (!IsNativeWindow(window)) window = FindNativeWindow(process);
                    WindowsGSM.MainWindow.ServerMetadata metadata;
                    if (WindowsGSM.MainWindow._serverMetadata.TryGetValue(id, out metadata))
                    {
                        bool matching = metadata.Process != null && metadata.Process.Id == process.Id;
                        if (registered && !matching) return;
                        if (matching && window != IntPtr.Zero)
                        {
                            registered = true;
                            if (metadata.MainWindow != window)
                            {
                                metadata.MainWindow = window;
                                string cache = ServerPath.GetServersCache(_serverData.ServerID);
                                try
                                {
                                    Directory.CreateDirectory(cache);
                                    File.WriteAllText(Path.Combine(cache, "windowsIntPtr"), window.ToInt64().ToString(CultureInfo.InvariantCulture));
                                }
                                catch (IOException) { }
                                catch (UnauthorizedAccessException) { }
                            }
                            FieldInfo visibility = metadata.GetType().GetField("ShowConsole");
                            if (visibility != null && visibility.FieldType == typeof(bool))
                            {
                                bool show = (bool)visibility.GetValue(metadata);
                                if (lastVisible != show || IsWindowVisible(window) != show)
                                {
                                    ShowWindow(window, show ? 1 : 0);
                                    lastVisible = show;
                                }
                            }
                        }
                    }
                    await Task.Delay(250);
                }
            }
            catch (InvalidOperationException) { /* The process was stopped or replaced. */ }
        }

        private delegate bool EnumWindowCallback(IntPtr window, IntPtr state);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr state);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int state);
        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();


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

namespace WindowsGSM.Plugins
{
    internal static class DayZWorkshopUpdater
    {
        internal sealed class Item
        {
            internal string Id, Folder, Mode;
        }

        internal static List<Item> Read(string path)
        {
            var items = new List<Item>();
            if (!File.Exists(path)) return items;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                string[] parts = line.Split('|').Select(p => p.Trim()).ToArray();
                ulong id;
                if (parts.Length != 3 || !Regex.IsMatch(parts[0], @"\A[1-9][0-9]*\z")
                    || !ulong.TryParse(parts[0], out id)
                    || !Regex.IsMatch(parts[1], @"\A@[A-Za-z0-9_-]+\z")
                    || (parts[2] != "mod" && parts[2] != "servermod"))
                    throw new InvalidOperationException("Invalid Workshop entry. Use itemId|@Folder|mod or itemId|@Folder|servermod.");
                if (items.Any(i => i.Id == parts[0] || string.Equals(i.Folder, parts[1], StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Duplicate Workshop ID or folder: " + parts[0]);
                items.Add(new Item { Id = parts[0], Folder = parts[1], Mode = parts[2] });
            }
            return items;
        }

        internal static string MergeArguments(string arguments, List<Item> items)
        {
            if (items.Count == 0) return arguments;
            var tokens = Regex.Matches(arguments ?? "", "(?:\"[^\"]*\"|[^\\s\"]+)+").Cast<Match>().Select(m => m.Value).ToList();
            foreach (string mode in new[] { "mod", "servermod" })
            {
                var added = items.Where(i => i.Mode == mode).Select(i => i.Folder).ToList();
                if (added.Count == 0) continue;
                var folders = new List<string>();
                var keep = new List<string>();
                foreach (string token in tokens)
                {
                    string clean = token.Replace("\"", "");
                    if (clean.StartsWith("-" + mode + "=", StringComparison.OrdinalIgnoreCase))
                        folders.AddRange(clean.Substring(mode.Length + 2).Split(';').Where(p => !string.IsNullOrWhiteSpace(p)));
                    else keep.Add(token);
                }
                folders.AddRange(added);
                keep.Add("\"-" + mode + "=" + string.Join(";", folders.Distinct(StringComparer.OrdinalIgnoreCase)) + "\"");
                tokens = keep;
            }
            return string.Join(" ", tokens);
        }

        internal static string Parameters(string root, string appId, List<Item> items)
        {
            string parameters = WindowsGSM.Installer.SteamCMD.GetParameter(root, appId, false, false);
            string suffix = " +app_update " + appId + " +quit";
            if (parameters == null)
                throw new InvalidOperationException("Workshop login is unavailable. Check Steam credentials and the Windows account used to encrypt them.");
            if (!parameters.EndsWith(suffix, StringComparison.Ordinal))
                throw new InvalidOperationException("This WGSM host has an unsupported SteamCMD parameter format.");
            return "+@ShutdownOnFailedCommand 1 +@NoPromptForPassword 1 " + parameters.Substring(0, parameters.Length - suffix.Length)
                + string.Concat(items.Select(i => " +workshop_download_item " + appId + " " + i.Id + " validate")) + " +quit";
        }

        internal static async Task<List<Item>> Update(string serverRoot, string configRoot, string steamRoot, string appId,
            Func<string, string, Task<Tuple<int, string>>> runner = null)
        {
            var items = Read(Path.Combine(configRoot, "WorkshopMods.cfg"));
            string state = Path.Combine(configRoot, "workshop-state-" + appId);
            string pending = Path.Combine(state, "pending.txt");
            if (File.Exists(pending))
                throw new InvalidOperationException("An interrupted Workshop deployment needs recovery. See " + pending);
            if (items.Count == 0) return items;
            if (appId != "221100" && appId != "107410") throw new InvalidOperationException("Unsupported Workshop application.");
            NoLinks(serverRoot); NoLinks(configRoot); NoLinks(steamRoot);
            string exe = Path.Combine(steamRoot, "steamcmd.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("SteamCMD is missing. Install or update the server first.", exe);
            Directory.CreateDirectory(state);
            string cache = Path.Combine(state, "download");
            Directory.CreateDirectory(cache);
            NoLinks(state); NoLinks(cache);
            NoLinks(Path.Combine(steamRoot, "wgsm-workshop.lock"));
            NoLinks(Path.Combine(state, "update.lock"));
            // Shared across both plugins and WGSM instances using this SteamCMD directory.
            using (var cacheLock = new FileStream(Path.Combine(steamRoot, "wgsm-workshop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            using (var serverLock = new FileStream(Path.Combine(state, "update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                string parameters = Parameters(cache, appId, items);
                var result = await (runner == null ? Run(exe, parameters) : runner(exe, parameters));
                if (result.Item1 != 0) throw new InvalidOperationException("SteamCMD Workshop update failed. Check account access, Steam Guard and network connectivity.");
                var sources = new Dictionary<string, string>();
                foreach (var item in items)
                {
                    // Exit code zero also occurs on failed Workshop commands. Require an item-specific receipt.
                    if (!Regex.IsMatch(result.Item2, @"Success\.\s+Downloaded item " + item.Id + @"\s+to\s+" , RegexOptions.IgnoreCase))
                        throw new InvalidOperationException("SteamCMD did not confirm Workshop item " + item.Id + ". Server start was cancelled.");
                    var paths = new[] { cache, steamRoot }.Select(root => Path.Combine(root, "steamapps", "workshop", "content", appId, item.Id));
                    string source = paths.FirstOrDefault(path => Directory.Exists(path)
                        && result.Item2.IndexOf(path, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (source == null) throw new DirectoryNotFoundException("Confirmed Workshop content not found for item " + item.Id + ".");
                    sources.Add(item.Id, source);
                }
                Deploy(serverRoot, state, appId, items, sources);
            }
            return items;
        }

        private static async Task<Tuple<int, string>> Run(string exe, string parameters)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(exe, parameters)
                {
                    WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
                };
                process.Start();
                process.StandardInput.Close();
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                bool exited = await Task.Run(() => process.WaitForExit(30 * 60 * 1000));
                if (!exited)
                {
                    process.Kill();
                    await Task.Run(() => process.WaitForExit());
                    throw new TimeoutException("Workshop download timed out after 30 minutes. Server start was cancelled.");
                }
                // Keep Steam output in memory. Login details must not be copied into the server log.
                return Tuple.Create(process.ExitCode, await stdout + "\n" + await stderr);
            }
        }

        private static void NoLinks(string path)
        {
            for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Workshop paths must not contain links or junctions: " + current);
        }

        private static void CopyTree(string source, string destination)
        {
            NoLinks(source); NoLinks(destination);
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                NoLinks(file);
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
            }
            foreach (string directory in Directory.GetDirectories(source))
                CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        // Back up every replaced folder/key. A failed batch is rolled back before starting the game.
        internal static void Deploy(string root, string state, string appId, List<Item> items, Dictionary<string, string> sources)
        {
            NoLinks(root); NoLinks(state);
            string pending = Path.Combine(state, "pending.txt");
            if (File.Exists(pending)) throw new IOException("Workshop recovery is pending: " + pending);
            Directory.CreateDirectory(state);
            string transaction = Path.Combine(root, ".wgsm-workshop-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            string stage = Path.Combine(transaction, "prepared"), backup = Path.Combine(transaction, "previous");
            NoLinks(transaction);
            Directory.CreateDirectory(stage); Directory.CreateDirectory(backup);
            var changes = new List<Tuple<string, string>>();
            string manifest = Path.Combine(state, "keys.txt");
            NoLinks(manifest); NoLinks(pending);
            if (Directory.Exists(manifest) || Directory.Exists(pending)) throw new IOException("Workshop state path is a directory.");
            var oldKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(manifest))
                foreach (string line in File.ReadAllLines(manifest))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length != 2 || !Regex.IsMatch(parts[0], @"\Awgsm_" + appId + @"_[1-9][0-9]*_[A-Za-z0-9_.-]+\.bikey\z")
                        || !Regex.IsMatch(parts[1], @"\A[0-9a-f]{64}\z") || oldKeys.ContainsKey(parts[0]))
                        throw new IOException("Invalid Workshop key inventory: " + manifest);
                    oldKeys.Add(parts[0], parts[1]);
                }
            var newKeys = new Dictionary<string, string>(oldKeys, StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                string source = sources[item.Id];
                string target = Path.Combine(root, item.Folder);
                NoLinks(target);
                if (File.Exists(target)) throw new IOException("Workshop folder is a file: " + target);
                string prepared = Path.Combine(stage, item.Folder);
                CopyTree(source, prepared);
                if (!Directory.EnumerateFiles(prepared, "*.pbo", SearchOption.AllDirectories).Any())
                    throw new IOException("Workshop item " + item.Id + " has no PBO content. Collections and non-server items are not supported.");
                changes.Add(Tuple.Create(target, prepared));
                string prefix = "wgsm_" + appId + "_" + item.Id + "_";
                var itemKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string key in Directory.GetFiles(prepared, "*.bikey", SearchOption.AllDirectories))
                {
                    string name = prefix + Path.GetFileName(key);
                    if (!Regex.IsMatch(name, @"\Awgsm_" + appId + @"_[1-9][0-9]*_[A-Za-z0-9_.-]+\.bikey\z"))
                        throw new IOException("Unsupported Workshop key filename: " + Path.GetFileName(key));
                    if (itemKeys.ContainsKey(name) && Hash(itemKeys[name]) != Hash(key))
                        throw new IOException("Conflicting Workshop keys: " + name);
                    itemKeys[name] = key;
                }
                foreach (string name in oldKeys.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Union(itemKeys.Keys, StringComparer.OrdinalIgnoreCase))
                {
                    string keyTarget = Path.Combine(root, "keys", name);
                    NoLinks(keyTarget);
                    if (Directory.Exists(keyTarget)) throw new IOException("Workshop key path is a directory: " + name);
                    if (File.Exists(keyTarget) && (!oldKeys.ContainsKey(name) || Hash(keyTarget) != oldKeys[name]))
                        throw new IOException("Workshop key was changed or is not owned by WGSM: " + name);
                    string keyPrepared = null;
                    if (itemKeys.ContainsKey(name))
                    {
                        keyPrepared = Path.Combine(stage, name);
                        File.Copy(itemKeys[name], keyPrepared);
                        newKeys[name] = Hash(keyPrepared);
                    }
                    else newKeys.Remove(name);
                    changes.Add(Tuple.Create(keyTarget, keyPrepared));
                }
            }
            string inventory = Path.Combine(stage, "keys.txt");
            File.WriteAllLines(inventory, newKeys.OrderBy(p => p.Key).Select(p => p.Key + "|" + p.Value), new UTF8Encoding(false));
            changes.Add(Tuple.Create(manifest, inventory));
            File.WriteAllText(pending, "Workshop deployment interrupted. Backup/prepared files: " + transaction + "\n"
                + string.Join("\n", changes.Select(c => c.Item1)) + "\n", new UTF8Encoding(false));
            var moved = new List<Tuple<string, string, bool>>();
            try
            {
                foreach (var change in changes)
                {
                    NoLinks(change.Item1);
                    string saved = Path.Combine(backup, moved.Count.ToString());
                    bool existed = File.Exists(change.Item1) || Directory.Exists(change.Item1);
                    if (existed) Move(change.Item1, saved);
                    moved.Add(Tuple.Create(change.Item1, saved, existed));
                    if (change.Item2 != null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(change.Item1));
                        Move(change.Item2, change.Item1);
                    }
                }
                File.Delete(pending);
            }
            catch
            {
                bool restored = true;
                foreach (var change in moved.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (Directory.Exists(change.Item1)) Directory.Delete(change.Item1, true);
                        else if (File.Exists(change.Item1)) File.Delete(change.Item1);
                        if (change.Item3) Move(change.Item2, change.Item1);
                    }
                    catch { restored = false; }
                }
                if (restored) File.Delete(pending);
                throw new IOException("Workshop activation failed. " + (restored ? "Previous files were restored." : "Recovery required: " + pending));
            }
        }

        private static void Move(string source, string destination)
        {
            if (Directory.Exists(source)) Directory.Move(source, destination);
            else File.Move(source, destination);
        }
    }
}
