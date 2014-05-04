﻿using jabber.client;
using jabber.connection;
using jabber.protocol.client;
using LegendaryClient.Controls;
using LegendaryClient.Logic.Region;
using LegendaryClient.Logic.SQLite;
using LegendaryClient.Windows;
using PVPNetConnect;
using PVPNetConnect.RiotObjects.Platform.Catalog.Champion;
using PVPNetConnect.RiotObjects.Platform.Clientfacade.Domain;
using PVPNetConnect.RiotObjects.Platform.Game;
using PVPNetConnect.RiotObjects.Platform.Game.Message;
using PVPNetConnect.RiotObjects.Platform.Messaging;
using PVPNetConnect.RiotObjects.Platform.Statistics;
using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;

namespace LegendaryClient.Logic
{
    /// <summary>
    /// Any logic that needs to be reused over multiple pages
    /// </summary>
    internal static class Client
    {
        /// <summary>
        /// The database of all runes
        /// </summary>
        internal static List<runes> Runes;

        /// <summary>
        /// Latest champion for League of Legends login screen
        /// </summary>
        internal const int LatestChamp = 161;

        /// <summary>
        /// Latest version of League of Legends. Retrieved from ClientLibCommon.dat
        /// </summary>
        internal static string Version = "3.00.00";

        /// <summary>
        /// The current directory the client is running from
        /// </summary>
        internal static string ExecutingDirectory = "";

        /// <summary>
        /// Riot's database with all the client data
        /// </summary>
        internal static SQLiteConnection SQLiteDatabase;

        /// <summary>
        /// The database of all the champions
        /// </summary>
        internal static List<champions> Champions;

        /// <summary>
        /// The database of all the champion abilities
        /// </summary>
        internal static List<championAbilities> ChampionAbilities;

        /// <summary>
        /// The database of all the champion skins
        /// </summary>
        internal static List<championSkins> ChampionSkins;

        /// <summary>
        /// The database of all the items
        /// </summary>
        internal static List<items> Items;

        /// <summary>
        /// The database of all masteries
        /// </summary>
        internal static List<masteries> Masteries;

        /// <summary>
        /// The database of all the search tags
        /// </summary>
        internal static List<championSearchTags> SearchTags;

        /// <summary>
        /// The database of all the keybinding defaults & proper names
        /// </summary>
        internal static List<keybindingEvents> Keybinds;

        internal static ChampionDTO[] PlayerChampions;

        internal static List<string> Whitelist = new List<string>();

        #region Chat

        internal static JabberClient ChatClient;

        internal static PresenceType _CurrentPresence;

        internal static PresenceType CurrentPresence
        {
            get { return _CurrentPresence; }
            set
            {
                if (_CurrentPresence != value)
                {
                    _CurrentPresence = value;
                    if (ChatClient != null)
                    {
                        if (ChatClient.IsAuthenticated)
                        {
                            ChatClientConnect(null);
                        }
                    }
                }
            }
        }

        internal static string _CurrentStatus;

        internal static string CurrentStatus
        {
            get { return _CurrentStatus; }
            set
            {
                if (_CurrentStatus != value)
                {
                    _CurrentStatus = value;
                    if (ChatClient != null)
                    {
                        if (ChatClient.IsAuthenticated)
                        {
                            ChatClientConnect(null);
                        }
                    }
                }
            }
        }

        internal static RosterManager RostManager;
        internal static PresenceManager PresManager;
        internal static ConferenceManager ConfManager;
        internal static bool UpdatePlayers = true;

        internal static Dictionary<string, ChatPlayerItem> AllPlayers = new Dictionary<string, ChatPlayerItem>();

        internal static bool ChatClient_OnInvalidCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        internal static void ChatClient_OnMessage(object sender, jabber.protocol.client.Message msg)
        {
            if (msg.Subject != null)
            {
                MainWin.Dispatcher.BeginInvoke(DispatcherPriority.Input, new ThreadStart(() =>
                {
                    ChatSubjects subject = (ChatSubjects) Enum.Parse(typeof(ChatSubjects), msg.Subject, true);
                    NotificationPopup pop = new NotificationPopup(subject, msg);
                    pop.Height = 230;
                    pop.HorizontalAlignment = HorizontalAlignment.Right;
                    pop.VerticalAlignment = VerticalAlignment.Bottom;
                    Client.NotificationGrid.Children.Add(pop);
                }));

                return;
            }

            if (AllPlayers.ContainsKey(msg.From.User) && !String.IsNullOrWhiteSpace(msg.Body))
            {
                ChatPlayerItem chatItem = AllPlayers[msg.From.User];
                chatItem.Messages.Add(chatItem.Username + "|" + msg.Body);
                MainWin.FlashWindow();
            }
        }

        internal static void ChatClientConnect(object sender)
        {
            SetChatHover();
        }

        internal static void SendMessage(string User, string Message)
        {
            ChatClient.Message(User, Message);
        }

        internal static void SetChatHover()
        {
            ChatClient.Presence(CurrentPresence, GetPresence(), null, 0);
        }

        internal static string GetPresence()
        {
            return "<body>" +
                "<profileIcon>" + LoginPacket.AllSummonerData.Summoner.ProfileIconId + "</profileIcon>" +
                "<level>" + LoginPacket.AllSummonerData.SummonerLevel.Level + "</level>" +
                "<wins>" + AmountOfWins + "</wins>" +
                (IsRanked ?
                "<queueType /><rankedLosses>0</rankedLosses><rankedRating>0</rankedRating><tier>UNRANKED</tier>" + //Unused?
                "<rankedLeagueName>" + LeagueName + "</rankedLeagueName>" +
                "<rankedLeagueDivision>" + Tier + "</rankedLeagueDivision>" +
                "<rankedLeagueTier>" + TierName + "</rankedLeagueTier>" +
                "<rankedLeagueQueue>RANKED_SOLO_5x5</rankedLeagueQueue>" +
                "<rankedWins>" + AmountOfWins + "</rankedWins>" : "") +
                "<gameStatus>" + GameStatus + "</gameStatus>" +
                "<statusMsg>" + CurrentStatus + "∟</statusMsg>" + //Look for "∟" to recognize that LegendaryClient - not shown on normal client
            "</body>";
        }

        internal static void RostManager_OnRosterItem(object sender, jabber.protocol.iq.Item ri)
        {
            UpdatePlayers = true;
            if (!AllPlayers.ContainsKey(ri.JID.User))
            {
                ChatPlayerItem player = new ChatPlayerItem();
                player.Id = ri.JID.User;
                player.Username = ri.Nickname;
                bool PlayerPresence = PresManager.IsAvailable(ri.JID);
                AllPlayers.Add(ri.JID.User, player);
            }
        }

        internal static void PresManager_OnPrimarySessionChange(object sender, jabber.JID bare)
        {
            jabber.protocol.client.Presence[] s = Client.PresManager.GetAll(bare);
            if (s.Length == 0)
                return;
            string Presence = s[0].Status;
            if (Presence == null)
                return;
            Debug.WriteLine(Presence);
            if (Client.AllPlayers.ContainsKey(bare.User))
            {
                UpdatePlayers = true;
                ChatPlayerItem Player = Client.AllPlayers[bare.User];
                using (XmlReader reader = XmlReader.Create(new StringReader(Presence)))
                {
                    while (reader.Read())
                    {
                        if (reader.IsStartElement())
                        {
                            #region Parse Presence

                            switch (reader.Name)
                            {
                                case "profileIcon":
                                    reader.Read();
                                    Player.ProfileIcon = Convert.ToInt32(reader.Value);
                                    break;

                                case "level":
                                    reader.Read();
                                    Player.Level = Convert.ToInt32(reader.Value);
                                    break;

                                case "wins":
                                    reader.Read();
                                    Player.Wins = Convert.ToInt32(reader.Value);
                                    break;

                                case "leaves":
                                    reader.Read();
                                    Player.Leaves = Convert.ToInt32(reader.Value);
                                    break;

                                case "rankedWins":
                                    reader.Read();
                                    Player.RankedWins = Convert.ToInt32(reader.Value);
                                    break;

                                case "timeStamp":
                                    reader.Read();
                                    Player.Timestamp = Convert.ToInt64(reader.Value);
                                    break;

                                case "statusMsg":
                                    reader.Read();
                                    Player.Status = reader.Value;
                                    if (Player.Status.EndsWith("∟"))
                                    {
                                        Player.UsingLegendary = true;
                                    }
                                    break;

                                case "gameStatus":
                                    reader.Read();
                                    Player.GameStatus = reader.Value;
                                    break;

                                case "skinname":
                                    reader.Read();
                                    Player.Champion = reader.Value;
                                    break;

                                case "rankedLeagueName":
                                    reader.Read();
                                    Player.LeagueName = reader.Value;
                                    break;

                                case "rankedLeagueTier":
                                    reader.Read();
                                    Player.LeagueTier = reader.Value;
                                    break;

                                case "rankedLeagueDivision":
                                    reader.Read();
                                    Player.LeagueDivision = reader.Value;
                                    break;
                            }

                            #endregion Parse Presence
                        }
                    }
                }
                if (String.IsNullOrWhiteSpace(Player.Status))
                {
                    Player.Status = "Online";
                }
            }
        }

        internal static void Message(string To, string Message, ChatSubjects Subject)
        {
            Message msg = new Message(Client.ChatClient.Document);
            msg.Type = MessageType.normal;
            msg.To = To + "@pvp.net";
            msg.Subject = ((ChatSubjects)Subject).ToString();
            msg.Body = Message;
            Client.ChatClient.Write(msg);
        }

        //Why do you even have to do this, riot?
        internal static string GetObfuscatedChatroomName(string Subject, string Type)
        {
            int bitHack = 0;
            byte[] data = System.Text.Encoding.UTF8.GetBytes(Subject);
            byte[] result;
            SHA1 sha = new SHA1CryptoServiceProvider();
            result = sha.ComputeHash(data);
            string obfuscatedName = "";
            int incrementValue = 0;
            while (incrementValue < result.Length)
            {
                bitHack = result[incrementValue];
                obfuscatedName = obfuscatedName + Convert.ToString(((uint)(bitHack & 240) >> 4), 16);
                obfuscatedName = obfuscatedName + Convert.ToString(bitHack & 15, 16);
                incrementValue = incrementValue + 1;
            }
            obfuscatedName = Regex.Replace(obfuscatedName, @"/\s+/gx", "");
            obfuscatedName = Regex.Replace(obfuscatedName, @"/[^a-zA-Z0-9_~]/gx", "");
            return Type + "~" + obfuscatedName;
        }

        internal static string GetChatroomJID(string ObfuscatedChatroomName, string password, bool IsTypePublic)
        {
            if (!IsTypePublic)
                return ObfuscatedChatroomName + "@sec.pvp.net";

            if (String.IsNullOrEmpty(password))
                return ObfuscatedChatroomName + "@lvl.pvp.net";

            return ObfuscatedChatroomName + "@conference.pvp.net";
        }

        internal static int AmountOfWins; //Calculate wins for presence
        internal static bool IsRanked;
        internal static string TierName;
        internal static string Tier;
        internal static string LeagueName;
        internal static string GameStatus = "outOfGame";

        #endregion Chat

        internal static Grid MainGrid;
        internal static Grid NotificationGrid;
        internal static Label StatusLabel;
        internal static Label InfoLabel;
        internal static ContentControl OverlayContainer;
        internal static Button PlayButton;
        internal static ContentControl ChatContainer;
        internal static ContentControl StatusContainer;
        internal static ContentControl NotificationOverlayContainer;
        internal static ListView ChatListView;
        internal static ChatItem ChatItem;

        internal static Image MainPageProfileImage;

        #region WPF Tab Change

        /// <summary>
        /// The container that contains the page to display
        /// </summary>
        internal static ContentControl Container;

        /// <summary>
        /// Page cache to stop having to recreate all information if pages are overwritted
        /// </summary>
        internal static List<Page> Pages;

        internal static bool IsOnPlayPage = false;

        /// <summary>
        /// Switches the contents of the frame to the requested page. Also sets background on
        /// the button on the top to show what section you are currently on.
        /// </summary>
        internal static void SwitchPage(Page page)
        {
            IsOnPlayPage = page is PlayPage;
            foreach (Page p in Pages) //Cache pages
            {
                if (p.GetType() == page.GetType())
                {
                    Container.Content = p.Content;
                    return;
                }
            }
            Container.Content = page.Content;
            if (!(page is FakePage))
                Pages.Add(page);
        }

        /// <summary>
        /// Clears the cache of a certain page if not used anymore
        /// </summary>
        internal static void ClearPage(Page page)
        {
            foreach (Page p in Pages.ToArray())
            {
                if (p.GetType() == page.GetType())
                {
                    Pages.Remove(p);
                    return;
                }
            }
        }

        #endregion WPF Tab Change

        #region League Of Legends Logic

        /// <summary>
        /// Main connection to the League of Legends server
        /// </summary>
        internal static PVPNetConnection PVPNet;

        /// <summary>
        /// Packet recieved when initially logged on. Cached so the packet doesn't
        /// need to requested multiple times, causing slowdowns
        /// </summary>
        internal static LoginDataPacket LoginPacket;

        /// <summary>
        /// All enabled game configurations for the user
        /// </summary>
        internal static List<GameTypeConfigDTO> GameConfigs;

        /// <summary>
        /// The region the user is connecting to
        /// </summary>
        internal static BaseRegion Region;

        /// <summary>
        /// Is the client logged in to the League of Legends server
        /// </summary>
        internal static bool IsLoggedIn = false;

        /// <summary>
        /// Is the player in game at the moment
        /// </summary>
        internal static bool InGame = false;

        /// <summary>
        /// GameID of the current game that the client is connected to
        /// </summary>
        internal static double GameID = 0;

        /// <summary>
        /// Game Name of the current game that the client is connected to
        /// </summary>
        internal static string GameName = "";

        /// <summary>
        /// The DTO of the game lobby when connected to a custom game
        /// </summary>
        internal static GameDTO GameLobbyDTO;

        /// <summary>
        /// When going into champion select reuse the last DTO to set up data
        /// </summary>
        internal static GameDTO ChampSelectDTO;

        /// <summary>
        /// When connected to a game retrieve details to connect to
        /// </summary>
        internal static PlayerCredentialsDto CurrentGame;

        internal static bool AutoAcceptQueue = false;
        internal static object LastPageContent;

        /// <summary>
        /// When an error occurs while connected. Currently un-used
        /// </summary>
        internal static void PVPNet_OnError(object sender, PVPNetConnect.Error error)
        {
            ;
        }

        internal static void OnMessageReceived(object sender, object message)
        {
            MainWin.Dispatcher.BeginInvoke(DispatcherPriority.Input, new ThreadStart(async () =>
            {
                if (message is StoreAccountBalanceNotification)
                {
                    StoreAccountBalanceNotification newBalance = (StoreAccountBalanceNotification)message;
                    InfoLabel.Content = "IP: " + newBalance.Ip + " ∙ RP: " + newBalance.Rp;
                    Client.LoginPacket.IpBalance = newBalance.Ip;
                    Client.LoginPacket.RpBalance = newBalance.Rp;
                }
                else if (message is GameNotification)
                {
                    GameNotification notification = (GameNotification)message;
                    MessageOverlay messageOver = new MessageOverlay();
                    messageOver.MessageTitle.Content = notification.Type;
                    switch (notification.Type)
                    {
                        case "PLAYER_BANNED_FROM_GAME":
                            messageOver.MessageTitle.Content = "Banned from custom game";
                            messageOver.MessageTextBox.Text = "You have been banned from this custom game!";
                            break;

                        default:
                            messageOver.MessageTextBox.Text = notification.MessageCode + Environment.NewLine;
                            messageOver.MessageTextBox.Text = Convert.ToString(notification.MessageArgument);
                            break;
                    }
                    Client.OverlayContainer.Content = messageOver.Content;
                    Client.OverlayContainer.Visibility = Visibility.Visible;
                    Client.ClearPage(new CustomGameLobbyPage());
                    Client.SwitchPage(new MainPage());
                }
                else if (message is EndOfGameStats)
                {
                    EndOfGameStats stats = message as EndOfGameStats;
                    EndOfGamePage EndOfGame = new EndOfGamePage(stats);
                    Client.OverlayContainer.Visibility = Visibility.Visible;
                    Client.OverlayContainer.Content = EndOfGame.Content;
                }
                else if (message is StoreFulfillmentNotification)
                {
                    PlayerChampions = await PVPNet.GetAvailableChampions();
                }
            }));
        }

        internal static string InternalQueueToPretty(string InternalQueue)
        {
            switch (InternalQueue)
            {
                case "matching-queue-NORMAL-5x5-game-queue":
                    return "Normal 5v5";

                case "matching-queue-NORMAL-3x3-game-queue":
                    return "Normal 3v3";

                case "matching-queue-NORMAL-5x5-draft-game-queue":
                    return "Draft 5v5";

                case "matching-queue-RANKED_SOLO-5x5-game-queue":
                    return "Ranked 5v5";

                case "matching-queue-RANKED_TEAM-3x3-game-queue":
                    return "Ranked Team 5v5";

                case "matching-queue-RANKED_TEAM-5x5-game-queue":
                    return "Ranked Team 3v3";

                case "matching-queue-ODIN-5x5-game-queue":
                    return "Dominion 5v5";

                case "matching-queue-ARAM-5x5-game-queue":
                    return "ARAM 5v5";

                case "matching-queue-BOT-5x5-game-queue":
                    return "Bot 5v5 Beginner";

                case "matching-queue-ODIN-5x5-draft-game-queue":
                    return "Dominion Draft 5v5";

                case "matching-queue-BOT_TT-3x3-game-queue":
                    return "Bot 3v3 Beginner";

                case "matching-queue-ODINBOT-5x5-game-queue":
                    return "Dominion Bot 5v5 Beginner";

                case "matching-queue-ONEFORALL-5x5-game-queue":
                    return "One For All 5v5";

                default:
                    return InternalQueue;
            }
        }

        internal static string GetGameDirectory()
        {
            string Directory = Path.Combine(ExecutingDirectory, "RADS", "projects", "lol_game_client", "releases");

            DirectoryInfo dInfo = new DirectoryInfo(Directory);
            DirectoryInfo[] subdirs = null;
            try
            {
                subdirs = dInfo.GetDirectories();
            }
            catch { return "0.0.0"; }
            string latestVersion = "0.0.1";
            foreach (DirectoryInfo info in subdirs)
            {
                latestVersion = info.Name;
            }

            Directory = Path.Combine(Directory, latestVersion, "deploy");

            return Directory;
        }

        internal static void LaunchGame()
        {
            string GameDirectory = GetGameDirectory();

            var p = new System.Diagnostics.Process();
            p.StartInfo.WorkingDirectory = GameDirectory;
            p.StartInfo.FileName = Path.Combine(GameDirectory, "League of Legends.exe");
            p.StartInfo.Arguments = "\"8394\" \"LoLLauncher.exe\" \"" + "" + "\" \"" +
                CurrentGame.ServerIp + " " +
                CurrentGame.ServerPort + " " +
                CurrentGame.EncryptionKey + " " +
                CurrentGame.SummonerId + "\"";
            p.Start();
        }

        internal static void LaunchSpectatorGame(string SpectatorServer, string Key, int GameId, string Platform)
        {
            string GameDirectory = GetGameDirectory();

            var p = new System.Diagnostics.Process();
            p.StartInfo.WorkingDirectory = GameDirectory;
            p.StartInfo.FileName = Path.Combine(GameDirectory, "League of Legends.exe");
            p.StartInfo.Arguments = "\"8393\" \"LoLLauncher.exe\" \"\" \"spectator "
                + SpectatorServer + " "
                + Key + " "
                + GameId + " "
                + Platform + "\"";
            p.Start();
        }

        #endregion League Of Legends Logic

        internal static MainWindow MainWin;

        #region Public Helper Methods
        internal static void FocusClient()
        {
            if (MainWin.WindowState == WindowState.Minimized)
            {
                MainWin.WindowState = WindowState.Normal;
            }

            MainWin.Activate();
            MainWin.Topmost = true;  // important
            MainWin.Topmost = false; // important
            MainWin.Focus();         // important
        }

        public static String TitleCaseString(String s)
        {
            if (s == null) return s;

            String[] words = s.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length == 0) continue;

                Char firstChar = Char.ToUpper(words[i][0]);
                String rest = "";
                if (words[i].Length > 1)
                {
                    rest = words[i].Substring(1).ToLower();
                }
                words[i] = firstChar + rest;
            }
            return String.Join(" ", words);
        }

        public static BitmapSource ToWpfBitmap(System.Drawing.Bitmap bitmap)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);

                stream.Position = 0;
                BitmapImage result = new BitmapImage();
                result.BeginInit();
                result.CacheOption = BitmapCacheOption.OnLoad;
                result.StreamSource = stream;
                result.EndInit();
                result.Freeze();
                return result;
            }
        }

        public static DateTime JavaTimeStampToDateTime(double javaTimeStamp)
        {
            // Java timestamp is millisecods past epoch
            System.DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            dtDateTime = dtDateTime.AddSeconds(Math.Round(javaTimeStamp / 1000)).ToLocalTime();
            return dtDateTime;
        }
        public static void Log(String lines, String type = "LOG")
        {
            /*
            System.IO.StreamWriter file = new System.IO.StreamWriter(Path.Combine(ExecutingDirectory, "lcdebug.log"), true);
            file.WriteLine(string.Format("({0} {1}) [{2}]: {3}", DateTime.Now.ToShortDateString(), DateTime.Now.ToShortTimeString(), type, lines));
            file.Close();*/
        }

        //Get Image
        public static BitmapImage GetImage(string Address)
        {
            Uri UriSource = new Uri(Address, UriKind.RelativeOrAbsolute);
            if (!File.Exists(Address) && !Address.StartsWith("/LegendaryClient;component"))
            {
                //Log("Cannot find " + Address, "WARN");
                UriSource = new Uri("/LegendaryClient;component/NONE.png", UriKind.RelativeOrAbsolute);
            }
            return new BitmapImage(UriSource);
        }
        #endregion Public Helper Methods
    }

    public class ChatPlayerItem
    {
        public string Id { get; set; }

        public string Username { get; set; }

        public int ProfileIcon { get; set; }

        public int Level { get; set; }

        public int Wins { get; set; }

        public int RankedWins { get; set; }

        public int Leaves { get; set; }

        public string LeagueTier { get; set; }

        public string LeagueDivision { get; set; }

        public string LeagueName { get; set; }

        public string GameStatus { get; set; }

        public long Timestamp { get; set; }

        public bool Busy { get; set; }

        public string Champion { get; set; }

        public string Status { get; set; }

        public bool UsingLegendary { get; set; }

        public List<string> Messages = new List<string>();
    }
}