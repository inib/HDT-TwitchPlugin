#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Enums;

#endregion

namespace TwitchPlugin
{
	public class Core
	{
		private static IRC _irc;
		private static readonly Dictionary<string, ChatCommand> Commands;

		static Core()
		{
			Commands = new Dictionary<string, ChatCommand>();
			AddCommand("commands", ChatCommands.CommandsCommand, "ChatCommandCommands");
			AddCommand("deck", ChatCommands.DeckCommand, "ChatCommandDeck");
			AddCommand("alldecks", ChatCommands.AllDecksCommand, "ChatCommandAllDecks");
			AddCommand("hdt", ChatCommands.HdtCommand, "ChatCommandHdt");
			AddCommand("stats", () => ChatCommands.StatsCommand(Config.Instance.ChatCommandStatsDefault), "ChatCommandStatsGeneral");
			AddCommand("stats heute", () => ChatCommands.StatsCommand("heute"), "ChatCommandStatsToday", "ChatCommandStatsGeneral");
			AddCommand("stats woche", () => ChatCommands.StatsCommand("woche"), "ChatCommandStatsWeek", "ChatCommandStatsGeneral");
			AddCommand("stats saison", () => ChatCommands.StatsCommand("saison"), "ChatCommandStatsSeason", "ChatCommandStatsGeneral");
			AddCommand("stats insgesamt", () => ChatCommands.StatsCommand("insgesamt"), "ChatCommandStatsTotal", "ChatCommandStatsGeneral");
			AddCommand("arena", () => ChatCommands.ArenaCommand(Config.Instance.ChatCommandArenaDefault), "ChatCommandArenaGeneral");
			AddCommand("arena heute", () => ChatCommands.ArenaCommand("heute"), "ChatCommandArenaToday", "ChatCommandArenaGeneral");
			AddCommand("arena woche", () => ChatCommands.ArenaCommand("woche"), "ChatCommandArenaWeek", "ChatCommandArenaGeneral");
			AddCommand("arena saison", () => ChatCommands.ArenaCommand("saison"), "ChatCommandArenaSeason", "ChatCommandArenaGeneral");
			AddCommand("arena insgesamt", () => ChatCommands.ArenaCommand("insgesamt"), "ChatCommandArenaTotal", "ChatCommandArenaGeneral");
			AddCommand("bestdeck", () => ChatCommands.BestDeckCommand(Config.Instance.ChatCommandBestDeckDefault), "ChatCommandBestDeckGeneral");
			AddCommand("bestdeck heute", () => ChatCommands.BestDeckCommand("heute"), "ChatCommandBestDeckToday", "ChatCommandBestDeckGeneral");
			AddCommand("bestdeck woche", () => ChatCommands.BestDeckCommand("woche"), "ChatCommandBestDeckWeek", "ChatCommandBestDeckGeneral");
			AddCommand("bestdeck saison", () => ChatCommands.BestDeckCommand("saison"), "ChatCommandBestDeckSeason",
			           "ChatCommandBestDeckGeneral");
			AddCommand("bestdeck insgesamt", () => ChatCommands.BestDeckCommand("insgesamt"), "ChatCommandBestDeckTotal", "ChatCommandBestDeckGeneral");
			AddCommand("mostplayed", () => ChatCommands.MostPlayedCommand(Config.Instance.ChatCommandMostPlayedDefault),
			           "ChatCommandMostPlayedGeneral");
			AddCommand("mostplayed heute", () => ChatCommands.MostPlayedCommand("heute"), "ChatCommandMostPlayedToday",
			           "ChatCommandMostPlayedGeneral");
			AddCommand("mostplayed woche", () => ChatCommands.MostPlayedCommand("woche"), "ChatCommandMostPlayedWeek",
			           "ChatCommandMostPlayedGeneral");
			AddCommand("mostplayed saison", () => ChatCommands.MostPlayedCommand("saison"), "ChatCommandMostPlayedSeason",
			           "ChatCommandMostPlayedGeneral");
			AddCommand("mostplayed insgesamt", () => ChatCommands.MostPlayedCommand("insgesamt"), "ChatCommandMostPlayedTotal",
			           "ChatCommandMostPlayedGeneral");
		}

		public static string TwitchTag
		{
			get { return "TwitchPlugin"; }
		}

		public static List<string> GetCommandNames()
		{
			return Commands.Select(x => x.Key).ToList();
		}

		public static void AddCommand(string command, Action action, string propName, string generalPropName = null)
		{
			Commands.Add(command, new ChatCommand(command, action, propName, generalPropName));
		}

		internal static void Send(string message)
		{
			if(_irc == null)
				return;
			_irc.SendMessage(Config.Instance.Channel, message);
			Logger.WriteLine(message, "TwitchPlugin");
		}

		public static bool Connect()
		{
			Logger.WriteLine("Logging in as " + Config.Instance.User);
			_irc = new IRC(Config.Instance.User, Config.Instance.User, Config.Instance.OAuth);
			var success = _irc.Connect("irc.twitch.tv", 6667);
			if(success)
			{
				_irc.JoinChannel(Config.Instance.Channel);
				_irc.OnChatMsg += HandleChatMessage;
				Send("Servus! (Hearthstone Deck Tracker connected)");
			}
			return success;
		}

		private static void HandleChatMessage(TwitchChatMessage msg)
		{
			if(!msg.Message.StartsWith("!"))
				return;
			var cmd = msg.Message.Substring(1);
			ChatCommand chatCommand;
			if(Commands.TryGetValue(cmd, out chatCommand))
				chatCommand.Execute(msg);
			else
				Logger.WriteLine(string.Format("Unknown command by {0}: {1}", msg.User, msg.Message), "TwitchPlugin");
		}

		public static void Disconnect()
		{
			if(_irc != null && _irc.Connected)
			{
				Send("Wiederschaun! (Hearthstone Deck Tracker disconnected)");
				_irc.LeaveChannel(Config.Instance.Channel);
				_irc.Quit();
			}
		}

		private static string _currentFileContent;
		public static void Update()
		{
			if(Config.Instance.SaveStatsToFile)
			{
				if(DeckList.Instance.ActiveDeckVersion == null)
					return;
				var games = DeckList.Instance.ActiveDeckVersion.GetRelevantGames();
                var wins = games.Count(g => g.Result == GameResult.Win);
				var losses = games.Count(g => g.Result == GameResult.Loss);
				var resultString = string.Format("{0} - {1}", wins, losses);
				if(_currentFileContent == resultString)
					return;
				try
				{
					using(var sr = new StreamWriter(Config.Instance.StatsFileFullPath))
						sr.WriteLine(resultString);
					_currentFileContent = resultString;
				}
				catch(Exception ex)
				{
					//uncomment for v0.11.5 ?
					//Hearthstone_Deck_Tracker.API.Errors.ShowErrorMessage("TwitchPlugin", ex.ToString());
					Logger.WriteLine("Error writing to stats file: " + ex, "TwitchPlugin");
				}
			}
		}
	}

	public class ChatCommand
	{
		private readonly Action _action;
		private readonly string _command;
		private readonly string _configItem;
		private readonly string _generalConfigItem;
		private DateTime _lastExecute;

		public ChatCommand(string command, Action action, string configItem, string generalConfigItem = null)
		{
			_command = command;
			_action = action;
			_lastExecute = DateTime.MinValue;
			_configItem = configItem;
			_generalConfigItem = generalConfigItem;
		}

		public void Execute(TwitchChatMessage msg)
		{
			Logger.WriteLine(string.Format("Command \"{0}\" requested by {1}.", _command, msg.User), "TwitchPlugin");
			if(_generalConfigItem != null && !Config.GetConfigItem<bool>(_generalConfigItem))
			{
				Logger.WriteLine(string.Format("Command \"{0}\" is disabled (general).", _command), "TwitchPlugin");
				return;
			}
			if(!Config.GetConfigItem<bool>(_configItem))
			{
				Logger.WriteLine(string.Format("Command \"{0}\" is disabled.", _command), "TwitchPlugin");
				return;
			}
			if((DateTime.Now - _lastExecute).TotalSeconds < 10)
			{
				Logger.WriteLine(string.Format("Time since last execute of {0} is less than 10 seconds. Not executing.", _command), "TwitchPlugin");
				return;
			}
			_lastExecute = DateTime.Now;
			_action.Invoke();
		}
	}
}