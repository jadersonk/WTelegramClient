﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TL;

namespace WTelegramClientTest
{
	static class Program_SecretChats
	{
		static WTelegram.Client Client;
		static WTelegram.SecretChats Secrets;
		static InputEncryptedChat ActiveChat; // the secret chat currently selected
		static readonly Dictionary<long, User> Users = new();
		static readonly Dictionary<long, ChatBase> Chats = new();

		// go to Project Properties > Debug > Environment variables and add at least these: api_id, api_hash, phone_number
		static async Task Main()
		{
			WTelegram.Helpers.Log = (l, s) => System.Diagnostics.Debug.WriteLine(s);
			Client = new WTelegram.Client(Environment.GetEnvironmentVariable);
			Secrets = new WTelegram.SecretChats(Client, "Secrets.bin");
			AppDomain.CurrentDomain.ProcessExit += (s, e) => { Secrets.Dispose(); Client.Dispose(); };
			SelectActiveChat();

			Client.OnUpdate += Client_OnUpdate;
			var myself = await Client.LoginUserIfNeeded();
			Users[myself.id] = myself;
			Console.WriteLine($"We are logged-in as {myself}");
				
			var dialogs = await Client.Messages_GetAllDialogs(); // load the list of users/chats
			dialogs.CollectUsersChats(Users, Chats);
			Console.WriteLine(@"Available commands:
/request <UserID>   Initiate Secret Chat with user
/discard [delete]   Terminate active secret chat (and delete history)
/select <ChatID>    Select another Secret Chat
/read               Mark active discussion as read
/users              List collected users and their IDs
Type a command, or a message to send to the active secret chat:");
			do
			{
				var line = Console.ReadLine();
				if (line.StartsWith('/'))
				{
					if (line == "/discard delete") { await Secrets.Discard(ActiveChat, true); SelectActiveChat(); }
					else if (line == "/discard") { await Secrets.Discard(ActiveChat, false); SelectActiveChat(); }
					else if (line == "/read") await Client.Messages_ReadEncryptedHistory(ActiveChat, DateTime.UtcNow);
					else if (line == "/users") foreach (var user in Users.Values) Console.WriteLine($"{user.id,-10} {user}");
					else if (line.StartsWith("/select ")) SelectActiveChat(int.Parse(line[8..]));
					else if (line.StartsWith("/request "))
						if (Users.TryGetValue(long.Parse(line[9..]), out var user))
							SelectActiveChat(await Secrets.Request(user));
						else
							Console.WriteLine("User not found");
					else Console.WriteLine("Unrecognized command");
				}
				else if (ActiveChat == null) Console.WriteLine("No active secret chat");
				else await Secrets.SendMessage(ActiveChat, new TL.Layer73.DecryptedMessage { message = line, random_id = WTelegram.Helpers.RandomLong() });
			} while (true);
		}

		private static async Task Client_OnUpdate(IObject arg)
		{
			if (arg is not UpdatesBase updates) return;
			updates.CollectUsersChats(Users, Chats);
			foreach (var update in updates.UpdateList)
				switch (update)
				{
					case UpdateEncryption ue: // Change in Secret Chat status
						await Secrets.HandleUpdate(ue);
						break;
					case UpdateNewEncryptedMessage unem: // Encrypted message or service message:
						if (unem.message.ChatId != ActiveChat) SelectActiveChat(unem.message.ChatId);
						foreach (var msg in Secrets.DecryptMessage(unem.message))
						{
							if (msg.Action == null) Console.WriteLine($"{unem.message.ChatId}> {msg.Message}");
							else Console.WriteLine($"{unem.message.ChatId}> Service Message {msg.Action.GetType().Name[22..]}");
						}
						break;
					case UpdateEncryptedChatTyping:
					case UpdateEncryptedMessagesRead:
						//Console.WriteLine(update.GetType().Name);
						break;
				}
		}

		private static void SelectActiveChat(int newActiveChat = 0)
		{
			ActiveChat = Secrets.Peers.FirstOrDefault(sc => newActiveChat == 0 || sc.chat_id == newActiveChat);
			Console.WriteLine("Active secret chat ID: " + ActiveChat?.chat_id);
		}
	}
}