using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ExodvsBot.Services.Telegram
{
    public class TelegramSender
    {

        public static async Task SendMessage(string token, string chatId, string message)
        {
            if(token == String.Empty || chatId == string.Empty) return;

            var botClient = new TelegramBotClient(token);
            await botClient.SendMessage(
                    chatId: chatId,
                    text: message
                );
        }
    }
}
