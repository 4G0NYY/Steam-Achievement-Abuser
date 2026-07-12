using SAM.API;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.XPath;

namespace Steam_Achievement_Abuser
{
    internal static class Program
    {
        // Delay between each game so Steam doesn't fall over around ~800 games.
        private static int _PauseBetweenAbuse = 5000;

        // Bounds for the per-game unlock worker so an unresponsive game can't hang the run.
        private const int WorkerTimeoutMs = 10000;
        private const int CallbackPumpIntervalMs = 100;

        private const string GamesListUrl = "https://gib.me/sam/games.xml";

        private static Client _SteamClient;
        private static readonly List<GameInfo> _Games = new List<GameInfo>();

        private static int Main(string[] args)
        {
            // Hidden per-game worker mode: the interactive run re-launches this same
            // executable once per game so each child sets its own SteamAppId before
            // steamclient.dll is loaded. (This is what the separate "App" exe used to do.)
            if (args.Length >= 2 && args[0] == "--unlock")
            {
                if (long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long workerAppId))
                {
                    return RunUnlockWorker(workerAppId);
                }
                return 1;
            }

            // Read-only diagnostic: request stats, load the schema, report the achievement count.
            if (args.Length >= 2 && args[0] == "--check")
            {
                if (long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long checkApp))
                {
                    return RunCheck(checkApp);
                }
                return 1;
            }

            // Always pause before the window closes so the user can read the
            // summary (or any error) — no matter which path we exit through.
            try
            {
                return RunInteractive();
            }
            finally
            {
                PauseBeforeExit();
            }
        }

        // Read-only: drive the modern flow and report how many achievements load (sets nothing).
        private static int RunCheck(long appId)
        {
            Client client;
            try
            {
                client = new Client();
                client.Initialize(appId);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Init failed for app " + appId + ": " + ex.Message);
                return 1;
            }

            bool finished = false;
            int count = 0;
            SAM.API.Callbacks.UserStatsReceived received =
                client.CreateAndRegisterCallback<SAM.API.Callbacks.UserStatsReceived>();
            received.OnRun += param =>
            {
                if (param.Result == 1)
                {
                    List<AchievementDefinition> achievements;
                    bool ok = LoadUserGameStatsSchema(client, out achievements, (uint)appId);
                    count = achievements.Count;
                    Console.WriteLine("Schema loaded=" + ok + ", achievements=" + count);
                    int shown = 0;
                    foreach (AchievementDefinition a in achievements)
                    {
                        if (shown++ >= 6) break;
                        client.SteamUserStats.GetAchievement(a.Id, out bool got);
                        Console.WriteLine("  [" + (got ? "x" : " ") + "] " + a.Id + " : " + a.Name);
                    }
                }
                else
                {
                    Console.WriteLine("Steam returned no stats (result=" + param.Result + ")");
                }
                finished = true;
            };

            ulong steamId = client.SteamUser.GetSteamId();
            if (client.SteamUserStats.RequestUserStats(steamId) == CallHandle.Invalid)
            {
                Console.WriteLine("RequestUserStats failed");
                return 1;
            }

            int waited = 0;
            while (finished == false && waited < WorkerTimeoutMs)
            {
                client.RunCallbacks(false);
                Thread.Sleep(CallbackPumpIntervalMs);
                waited += CallbackPumpIntervalMs;
            }

            return count > 0 ? 0 : 1;
        }

        private static void PauseBeforeExit()
        {
            Console.WriteLine();
            Console.Write("Press Enter to exit...");
            Console.ReadLine();
        }

        private static int RunInteractive()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch
            {
                // Some terminals reject encoding changes; the art just won't render as nicely.
            }

            try
            {
                Console.SetWindowSize(Math.Min(140, Console.LargestWindowWidth), Math.Min(40, Console.LargestWindowHeight));
            }
            catch
            {
                // Windows Terminal / redirected output doesn't allow resizing. Ignore.
            }

            Console.Title = "Steam Achievement Abuser | 2026 Revamp by 4G0NYY <3";

            ShowBootSplash();

            Console.WriteLine("Init...");
            try
            {
                _SteamClient = new Client();
                _SteamClient.Initialize(0);
            }
            catch (DllNotFoundException)
            {
                Console.WriteLine("Could not find steamclient.dll. Make sure Steam is installed and running.");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not initialize Steam. Is the Steam client running and logged in? (" + ex.Message + ")");
                return 1;
            }

            Console.WriteLine("Downloading game list...");
            try
            {
                AddGames();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to download or parse the game list: " + ex.Message);
                return 1;
            }

            if (_Games.Count == 0)
            {
                Console.WriteLine("No games with achievements were found on this account. Nothing to do.");
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine("How long should the pause between each game be, in milliseconds?");
            Console.WriteLine("(Lower = faster but Steam may get unstable / Higher = slower but stable. Leave empty for the default: 5000)");
            Console.Write("> ");
            string pauseInput = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(pauseInput))
            {
                Console.WriteLine("Using the default pause of " + _PauseBetweenAbuse + " ms.");
            }
            else if (int.TryParse(pauseInput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPause) && parsedPause >= 0)
            {
                _PauseBetweenAbuse = parsedPause;
                Console.WriteLine("The pause between each game will be " + _PauseBetweenAbuse + " ms.");
            }
            else
            {
                Console.WriteLine("That wasn't a valid number, sticking with the default of " + _PauseBetweenAbuse + " ms.");
            }

            Console.WriteLine();
            Console.Write(_Games.Count + " games found, want to start? (y/n) ");
            string answer = Console.ReadLine();
            if (answer == null || answer.Trim().Length == 0 ||
                (answer.Trim()[0] != 'y' && answer.Trim()[0] != 'Y'))
            {
                Console.WriteLine();
                Console.WriteLine("Hmpf! It's... it's not like I wanted to help you anyway, baka! ＞︿＜");
                Console.WriteLine("(No achievements were touched.)");
                return 0;
            }

            StartAbuse();
            return 0;
        }

        private static void ShowBootSplash()
        {
            string art = LoadBootArt();
            if (art != null)
            {
                ConsoleColor previous = Console.ForegroundColor;
                try
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine(art);
                }
                finally
                {
                    Console.ForegroundColor = previous;
                }
            }

            Console.WriteLine("  Steam Achievement Abuser  —  2026 Revamp by 4G0NYY <3");
            Console.WriteLine("  Based on: https://github.com/gibbed/SteamAchievementManager");
            Console.WriteLine();
        }

        private static string LoadBootArt()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("steamabuuus.txt", StringComparison.OrdinalIgnoreCase));
                if (resourceName == null)
                {
                    return null;
                }

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }

        private static void StartAbuse()
        {
            string self = Environment.ProcessPath;

            Console.WriteLine();
            Console.WriteLine("Starting abuse...");
            Console.WriteLine();

            int completed = 0;
            int succeeded = 0;
            var failed = new List<string>();
            DrawProgress(completed, _Games.Count, "");

            foreach (GameInfo game in _Games)
            {
                var startInfo = new ProcessStartInfo(self, "--unlock " + game.Id.ToString(CultureInfo.InvariantCulture))
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                };

                bool ok = false;
                try
                {
                    using (Process worker = Process.Start(startInfo))
                    {
                        worker.WaitForExit();
                        ok = worker.ExitCode == 0;
                    }
                }
                catch
                {
                    // If a single game's worker fails to launch, keep going with the rest.
                }

                completed++;
                if (ok)
                {
                    succeeded++;
                }
                else
                {
                    failed.Add(game.Name);
                }

                DrawProgress(completed, _Games.Count, game.Name);

                if (completed < _Games.Count)
                {
                    Thread.Sleep(_PauseBetweenAbuse);
                }
            }

            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("Done! " + succeeded + "/" + _Games.Count + " games processed successfully.");
            if (failed.Count > 0)
            {
                Console.WriteLine(failed.Count + " game(s) were skipped (Steam returned no stats for them, or they have no achievements):");
                foreach (string name in failed.Take(15))
                {
                    Console.WriteLine("  - " + name);
                }
                if (failed.Count > 15)
                {
                    Console.WriteLine("  ...and " + (failed.Count - 15) + " more.");
                }
            }
            Console.WriteLine("Enjoy! :)");
        }

        private static void DrawProgress(int done, int total, string label)
        {
            const int barWidth = 28;
            double fraction = total == 0 ? 1d : (double)done / total;
            int filled = (int)Math.Round(fraction * barWidth);
            if (filled > barWidth)
            {
                filled = barWidth;
            }

            string bar = new string('█', filled) + new string('░', barWidth - filled);
            int percent = (int)Math.Round(fraction * 100);

            // Pad the label to a fixed width so a shorter name fully overwrites a longer previous one.
            const int labelWidth = 40;
            string shown = label ?? string.Empty;
            shown = shown.Length > labelWidth ? shown.Substring(0, labelWidth - 1) + "…" : shown.PadRight(labelWidth);

            string line = string.Format(CultureInfo.InvariantCulture,
                "  [{0}] {1}/{2} ({3,3}%)  {4}", bar, done, total, percent, shown);

            // Always keep the line within the console width so it can never wrap onto a
            // second row (which would break the in-place '\r' redraw and cascade lines).
            int width = 100;
            try
            {
                if (Console.WindowWidth > 1)
                {
                    width = Console.WindowWidth - 1;
                }
            }
            catch
            {
                // No real console (redirected output) — keep the fixed fallback.
            }

            if (line.Length > width)
            {
                line = line.Substring(0, width);
            }

            Console.Write("\r" + line);
        }

        private static void AddGames()
        {
            var pairs = new List<KeyValuePair<uint, string>>();
            byte[] bytes = DownloadGamesXml();

            using (var stream = new MemoryStream(bytes, false))
            {
                var document = new XPathDocument(stream);
                XPathNavigator navigator = document.CreateNavigator();
                XPathNodeIterator nodes = navigator.Select("/games/game");
                while (nodes.MoveNext())
                {
                    string type = nodes.Current.GetAttribute("type", "");
                    if (type == string.Empty)
                    {
                        type = "normal";
                    }
                    pairs.Add(new KeyValuePair<uint, string>((uint)nodes.Current.ValueAsLong, type));
                }

                foreach (KeyValuePair<uint, string> kv in pairs)
                {
                    AddGame(kv.Key, kv.Value);
                }
            }
        }

        // Download the game list, falling back to the last cached copy if gib.me is unreachable.
        private static byte[] DownloadGamesXml()
        {
            try
            {
                byte[] bytes;
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(30);
                    bytes = http.GetByteArrayAsync(new Uri(GamesListUrl)).GetAwaiter().GetResult();
                }
                TrySaveGamesCache(bytes);
                return bytes;
            }
            catch (Exception ex)
            {
                byte[] cached = TryLoadGamesCache();
                if (cached != null)
                {
                    Console.WriteLine("Couldn't reach the online game list (" + ex.Message + ").");
                    Console.WriteLine("Falling back to the cached copy from a previous run.");
                    return cached;
                }
                throw;
            }
        }

        private static string GamesCachePath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteamAchievementAbuser");
            return Path.Combine(dir, "games.xml");
        }

        private static void TrySaveGamesCache(byte[] bytes)
        {
            try
            {
                string path = GamesCachePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, bytes);
            }
            catch
            {
                // A missing cache is not fatal — it just means no offline fallback next time.
            }
        }

        private static byte[] TryLoadGamesCache()
        {
            try
            {
                string path = GamesCachePath();
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch
            {
                return null;
            }
        }

        private static void AddGame(uint id, string type)
        {
            if (_Games.Any(g => g.Id == id))
            {
                return;
            }

            if (!_SteamClient.SteamApps008.IsSubscribedApp(id))
            {
                return;
            }

            var info = new GameInfo(id, type);
            info.Name = _SteamClient.SteamApps001.GetAppData(info.Id, "name");
            if (info.Type == "demo" || info.Type == "mod" || info.Type == "junk")
            {
                return;
            }

            _Games.Add(info);
        }

        // --- Per-game unlock worker (formerly the separate "Steam Achievement Abuser App") ---

        private static int RunUnlockWorker(long appId)
        {
            Client client;
            try
            {
                client = new Client();
                client.Initialize(appId);
            }
            catch
            {
                return 1;
            }

            int resultCode = 1;
            bool finished = false;

            SAM.API.Callbacks.UserStatsReceived received =
                client.CreateAndRegisterCallback<SAM.API.Callbacks.UserStatsReceived>();
            received.OnRun += param =>
            {
                if (param.Result == 1)
                {
                    List<AchievementDefinition> achievements;
                    LoadUserGameStatsSchema(client, out achievements, (uint)appId);
                    foreach (AchievementDefinition achievement in achievements)
                    {
                        client.SteamUserStats.SetAchievement(achievement.Id, true);
                    }

                    // Persist the changes — without StoreStats() nothing actually unlocks.
                    resultCode = client.SteamUserStats.StoreStats() ? 0 : 1;
                }
                finished = true;
            };

            // Modern Steam: request this user's stats for the app. This is what makes
            // Steam deliver (and cache) the achievement schema and fire UserStatsReceived.
            ulong steamId = client.SteamUser.GetSteamId();
            if (client.SteamUserStats.RequestUserStats(steamId) == CallHandle.Invalid)
            {
                return 1;
            }

            int waited = 0;
            while (finished == false && waited < WorkerTimeoutMs)
            {
                client.RunCallbacks(false);
                Thread.Sleep(CallbackPumpIntervalMs);
                waited += CallbackPumpIntervalMs;
            }

            return resultCode;
        }

        private static bool LoadUserGameStatsSchema(Client client, out List<AchievementDefinition> achievements, uint gameId)
        {
            achievements = new List<AchievementDefinition>();
            string path;
            try
            {
                path = Steam.GetInstallPath();
                path = Path.Combine(path, "appcache");
                path = Path.Combine(path, "stats");
                path = Path.Combine(path, string.Format(CultureInfo.InvariantCulture, "UserGameStatsSchema_{0}.bin", gameId));

                if (File.Exists(path) == false)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            KeyValue kv = KeyValue.LoadAsBinary(path);
            if (kv == null)
            {
                return false;
            }

            string currentLanguage = client.SteamApps008.GetCurrentGameLanguage();
            KeyValue stats = kv[gameId.ToString(CultureInfo.InvariantCulture)]["stats"];
            if (stats.Valid == false || stats.Children == null)
            {
                return false;
            }

            foreach (KeyValue stat in stats.Children)
            {
                if (stat.Valid == false)
                {
                    continue;
                }

                // Modern schemas store "type" as a string ("achievements"); older ones
                // use an integer in "type_int" (or "type"). Handle both.
                SAM.API.Types.UserStatType type;
                KeyValue typeNode = stat["type"];
                if (typeNode.Valid && typeNode.Type == KeyValueType.String)
                {
                    if (Enum.TryParse((string)typeNode.Value, true, out type) == false)
                    {
                        type = SAM.API.Types.UserStatType.Invalid;
                    }
                }
                else
                {
                    type = SAM.API.Types.UserStatType.Invalid;
                }

                if (type == SAM.API.Types.UserStatType.Invalid)
                {
                    KeyValue typeIntNode = stat["type_int"];
                    int rawType = typeIntNode.Valid ? typeIntNode.AsInteger(0) : typeNode.AsInteger(0);
                    type = (SAM.API.Types.UserStatType)rawType;
                }
                switch (type)
                {
                    case SAM.API.Types.UserStatType.Invalid:
                        break;

                    case SAM.API.Types.UserStatType.Achievements:
                    case SAM.API.Types.UserStatType.GroupAchievements:
                        if (stat.Children != null)
                        {
                            foreach (KeyValue bits in stat.Children.Where(b => b.Name.ToLowerInvariant() == "bits"))
                            {
                                if (bits.Valid == false || bits.Children == null)
                                {
                                    continue;
                                }

                                foreach (KeyValue bit in bits.Children)
                                {
                                    string id = bit["name"].AsString("");
                                    string name = GetLocalizedString(bit["display"]["name"], currentLanguage, id);
                                    string desc = GetLocalizedString(bit["display"]["desc"], currentLanguage, "");
                                    achievements.Add(new AchievementDefinition
                                    {
                                        Id = id,
                                        Name = name,
                                        Description = desc,
                                        IconNormal = bit["display"]["icon"].AsString(""),
                                        IconLocked = bit["display"]["icon_gray"].AsString(""),
                                        IsHidden = bit["display"]["hidden"].AsBoolean(false),
                                        Permission = bit["permission"].AsInteger(0),
                                    });
                                }
                            }
                        }
                        break;

                    default:
                        break;
                }
            }

            return true;
        }

        private static string GetLocalizedString(KeyValue kv, string language, string defaultValue)
        {
            string name = kv[language].AsString("");
            if (string.IsNullOrEmpty(name) == false)
            {
                return name;
            }

            if (language != "english")
            {
                name = kv["english"].AsString("");
                if (string.IsNullOrEmpty(name) == false)
                {
                    return name;
                }
            }

            name = kv.AsString("");
            if (string.IsNullOrEmpty(name) == false)
            {
                return name;
            }

            return defaultValue;
        }
    }

    internal class GameInfo
    {
        private string _Name;
        public uint Id;
        public string Type;

        public string Name
        {
            get { return _Name; }
            set { _Name = value ?? "App " + Id.ToString(CultureInfo.InvariantCulture); }
        }

        public GameInfo(uint id, string type)
        {
            Id = id;
            Type = type;
            Name = null;
        }
    }

    internal class AchievementDefinition
    {
        public string Id;
        public string Name;
        public string Description;
        public string IconNormal;
        public string IconLocked;
        public bool IsHidden;
        public int Permission;

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}: {1}", Name ?? Id ?? base.ToString(), Permission);
        }
    }

    internal static class StreamHelpers
    {
        public static byte ReadValueU8(this Stream stream)
        {
            return (byte)stream.ReadByte();
        }

        public static int ReadValueS32(this Stream stream)
        {
            var data = new byte[4];
            stream.Read(data, 0, 4);
            return BitConverter.ToInt32(data, 0);
        }

        public static uint ReadValueU32(this Stream stream)
        {
            var data = new byte[4];
            stream.Read(data, 0, 4);
            return BitConverter.ToUInt32(data, 0);
        }

        public static ulong ReadValueU64(this Stream stream)
        {
            var data = new byte[8];
            stream.Read(data, 0, 8);
            return BitConverter.ToUInt64(data, 0);
        }

        public static float ReadValueF32(this Stream stream)
        {
            var data = new byte[4];
            stream.Read(data, 0, 4);
            return BitConverter.ToSingle(data, 0);
        }

        internal static string ReadStringInternalDynamic(this Stream stream, Encoding encoding, char end)
        {
            int characterSize = encoding.GetByteCount("e");
            string characterEnd = end.ToString(CultureInfo.InvariantCulture);

            int i = 0;
            var data = new byte[128 * characterSize];

            while (true)
            {
                if (i + characterSize > data.Length)
                {
                    Array.Resize(ref data, data.Length + (128 * characterSize));
                }

                stream.Read(data, i, characterSize);

                if (encoding.GetString(data, i, characterSize) == characterEnd)
                {
                    break;
                }

                i += characterSize;
            }

            if (i == 0)
            {
                return "";
            }

            return encoding.GetString(data, 0, i);
        }

        public static string ReadStringAscii(this Stream stream)
        {
            return stream.ReadStringInternalDynamic(Encoding.ASCII, '\0');
        }

        public static string ReadStringUnicode(this Stream stream)
        {
            return stream.ReadStringInternalDynamic(Encoding.UTF8, '\0');
        }
    }

    internal enum KeyValueType : byte
    {
        None = 0,
        String = 1,
        Int32 = 2,
        Float32 = 3,
        Pointer = 4,
        WideString = 5,
        Color = 6,
        UInt64 = 7,
        End = 8,
    }

    internal class KeyValue
    {
        private static readonly KeyValue _Invalid = new KeyValue();
        public string Name = "<root>";
        public KeyValueType Type = KeyValueType.None;
        public object Value;
        public bool Valid;

        public List<KeyValue> Children;

        public KeyValue this[string key]
        {
            get
            {
                if (Children == null)
                {
                    return _Invalid;
                }

                KeyValue child = Children.SingleOrDefault(
                    c => c.Name.ToLowerInvariant() == key.ToLowerInvariant());

                if (child == null)
                {
                    return _Invalid;
                }

                return child;
            }
        }

        public string AsString(string defaultValue)
        {
            if (Valid == false)
            {
                return defaultValue;
            }

            if (Value == null)
            {
                return defaultValue;
            }

            return Value.ToString();
        }

        public int AsInteger(int defaultValue)
        {
            if (Valid == false)
            {
                return defaultValue;
            }

            switch (Type)
            {
                case KeyValueType.String:
                case KeyValueType.WideString:
                    return int.TryParse((string)Value, out int value) ? value : defaultValue;

                case KeyValueType.Int32:
                    return (int)Value;

                case KeyValueType.Float32:
                    return (int)((float)Value);

                case KeyValueType.UInt64:
                    return (int)((ulong)Value & 0xFFFFFFFF);
            }

            return defaultValue;
        }

        public float AsFloat(float defaultValue)
        {
            if (Valid == false)
            {
                return defaultValue;
            }

            switch (Type)
            {
                case KeyValueType.String:
                case KeyValueType.WideString:
                    return float.TryParse((string)Value, out float value) ? value : defaultValue;

                case KeyValueType.Int32:
                    return (int)Value;

                case KeyValueType.Float32:
                    return (float)Value;

                case KeyValueType.UInt64:
                    return (ulong)Value & 0xFFFFFFFF;
            }

            return defaultValue;
        }

        public bool AsBoolean(bool defaultValue)
        {
            if (Valid == false)
            {
                return defaultValue;
            }

            switch (Type)
            {
                case KeyValueType.String:
                case KeyValueType.WideString:
                    return int.TryParse((string)Value, out int value) ? value != 0 : defaultValue;

                case KeyValueType.Int32:
                    return ((int)Value) != 0;

                case KeyValueType.Float32:
                    return ((int)((float)Value)) != 0;

                case KeyValueType.UInt64:
                    return ((ulong)Value) != 0;
            }

            return defaultValue;
        }

        public override string ToString()
        {
            if (Valid == false)
            {
                return "<invalid>";
            }

            if (Type == KeyValueType.None)
            {
                return Name;
            }

            return string.Format(CultureInfo.InvariantCulture, "{0} = {1}", Name, Value);
        }

        public static KeyValue LoadAsBinary(string path)
        {
            if (File.Exists(path) == false)
            {
                return null;
            }

            try
            {
                var kv = new KeyValue();
                using (FileStream input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (kv.ReadAsBinary(input) == false)
                    {
                        return null;
                    }
                }
                return kv;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool ReadAsBinary(Stream input)
        {
            Children = new List<KeyValue>();

            try
            {
                while (true)
                {
                    var type = (KeyValueType)input.ReadValueU8();

                    if (type == KeyValueType.End)
                    {
                        break;
                    }

                    var current = new KeyValue
                    {
                        Type = type,
                        Name = input.ReadStringUnicode(),
                    };

                    switch (type)
                    {
                        case KeyValueType.None:
                            current.ReadAsBinary(input);
                            break;

                        case KeyValueType.String:
                            current.Valid = true;
                            current.Value = input.ReadStringUnicode();
                            break;

                        case KeyValueType.WideString:
                            throw new FormatException("wstring is unsupported");

                        case KeyValueType.Int32:
                            current.Valid = true;
                            current.Value = input.ReadValueS32();
                            break;

                        case KeyValueType.UInt64:
                            current.Valid = true;
                            current.Value = input.ReadValueU64();
                            break;

                        case KeyValueType.Float32:
                            current.Valid = true;
                            current.Value = input.ReadValueF32();
                            break;

                        case KeyValueType.Color:
                            current.Valid = true;
                            current.Value = input.ReadValueU32();
                            break;

                        case KeyValueType.Pointer:
                            current.Valid = true;
                            current.Value = input.ReadValueU32();
                            break;

                        default:
                            throw new FormatException();
                    }

                    if (input.Position >= input.Length)
                    {
                        throw new FormatException();
                    }

                    Children.Add(current);
                }

                Valid = true;
                return input.Position == input.Length;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
