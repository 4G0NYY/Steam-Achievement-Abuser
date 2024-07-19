using SAM.API;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Xml.XPath;

namespace Steam_Achievement_Abuser
{
    class Program
    {
        private static int pausebetweenabuse = 5000;
        private static Client _SteamClient = null;
        private static List<GameInfo> _Games = new List<GameInfo>();

        static void Main()
        {
            Console.SetWindowSize(140, 36);
            Console.Title = "Fixed Steam Achievement Abuser | Luv, 4G0NYY <3";
            Console.WriteLine("   _____ _                                      _     _                                     _              _                         \n  / ____| |                           /\\       | |   (_)                                   | |       /\\   | |                        \n | (___ | |_ ___  __ _ _ __ ___      /  \\   ___| |__  _  _____   _____ _ __ ___   ___ _ __ | |_     /  \\  | |__  _   _ ___  ___ _ __ \n  \\___ \\| __/ _ \\/ _` | '_ ` _ \\    / /\\ \\ / __| '_ \\| |/ _ \\ \\ / / _ \\ '_ ` _ \\ / _ \\ '_ \\| __|   / /\\ \\ | '_ \\| | | / __|/ _ \\ '__|\n  ____) | ||  __/ (_| | | | | | |  / ____ \\ (__| | | | |  __/\\ V /  __/ | | | | |  __/ | | | |_   / ____ \\| |_) | |_| \\__ \\  __/ |   \n |_____/ \\__\\___|\\__,_|_| |_| |_| /_/    \\_\\___|_| |_|_|\\___| \\_/ \\___|_| |_| |_|\\___|_| |_|\\__| /_/    \\_\\_.__/ \\__,_|___/\\___|_|   \n");
            Console.WriteLine("Welcome to the fixed Steam Achievement Abuser by 4G0NYY");
            Console.WriteLine("Based on: https://github.com/gibbed/SteamAchievementManager");
            Console.WriteLine("Init...");
            try
            {
                _SteamClient = new Client();
                if (_SteamClient.Initialize(0) == false)  
                    return;             
            }
            catch (DllNotFoundException)
            {
                throw;
            }
            AddGames();
            Console.WriteLine($"Found {_Games.Count()} games...");
            Console.WriteLine("");
            Console.WriteLine("How long should the pause between each game be? (Lower Value = Faster but maybe unstable / Higher Value = Slower but stable (Leave Empty for Default: 5000)");
            string helpmeIwanttodie = Console.ReadLine();
            if (int.TryParse(helpmeIwanttodie, out int pausebetweenabuse))
            {
                Console.WriteLine($"The pause in between the abuse will be: {pausebetweenabuse}");
            }
            else
            {
                Console.WriteLine("Your Input is invalid. Please type a number between 1000 and 5000.");
            }
            Console.WriteLine("Press any key to start abusing Steam...");
            Console.ReadKey();
            StartAbuse();
            Console.ReadKey();
        }
        static void StartAbuse()
        {
            Console.WriteLine("Starting abuse...");
            int i = 1;
            foreach (var Game in _Games)
            {
                ProcessStartInfo ps = new ProcessStartInfo("Steam Achievement Abuser App.exe", Game.Id.ToString());
                ps.CreateNoWindow = true;
                ps.UseShellExecute = false;
                Console.WriteLine($"{i}/{_Games.Count()} | {Game.Name}");
                using (Process p = Process.Start(ps)) 
                    p.WaitForExit();
                i++;
                Thread.Sleep(pausebetweenabuse);
            }
            Console.WriteLine("");
            Console.WriteLine("Done!");
        }
        static void AddGames()
        {
            Console.WriteLine("Downloading base...");
            var pairs = new List<KeyValuePair<uint, string>>();
            byte[] bytes;
            using (var downloader = new WebClient())
            {
                bytes = downloader.DownloadData(new Uri(string.Format("http://gib.me/sam/games.xml")));
            }
            using (var stream = new MemoryStream(bytes, false))
            {
                var document = new XPathDocument(stream);
                var navigator = document.CreateNavigator();
                var nodes = navigator.Select("/games/game");
                while (nodes.MoveNext())
                {
                    string type = nodes.Current.GetAttribute("type", "");
                    if (type == string.Empty)
                    {
                        type = "normal";
                    }
                    pairs.Add(new KeyValuePair<uint, string>((uint)nodes.Current.ValueAsLong, type));
                }
                foreach (var kv in pairs)
                {
                    AddGame(kv.Key, kv.Value);
                }
            }
        }
        private static void AddGame(uint id, string type)
        {
            if (_Games.Any(i => i.Id == id))     
                return;
            
            if (!_SteamClient.SteamApps003.IsSubscribedApp(id))        
                return;
            
            var info = new GameInfo(id, type);
            info.Name = _SteamClient.SteamApps001.GetAppData(info.Id, "name");
            if (info.Type == "demo" || info.Type == "mod" || info.Type == "junk")  
                return;
            _Games.Add(info);
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
            set { _Name = value ?? "App " + this.Id.ToString(CultureInfo.InvariantCulture); }
        }
        public GameInfo(uint id, string type)
        {
            this.Id = id;
            this.Type = type;
            this.Name = null;
        }
    }
}
