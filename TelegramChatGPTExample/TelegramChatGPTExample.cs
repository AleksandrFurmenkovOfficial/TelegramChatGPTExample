using OpenAI_API;
using OpenAI_API.Chat;
using RxTelegram.Bot.Interface.BaseTypes;
using RxTelegram.Bot.Interface.BaseTypes.Requests.Messages;
using System;
using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramChatGPTExample
{
    internal class APIUsageException : Exception
    {
        public long chatId;
        public APIUsageException(string message, long chatId) : base(message)
        {
            this.chatId = chatId;
        }
    }

    internal class ContextSizeExceededException : APIUsageException
    {
        public ContextSizeExceededException(string message, long chatId) : base(message, chatId)
        {
        }
    }

    internal class Visitor
    {
        public bool access;
        public string who;

        public Visitor(bool access, string who)
        {
            this.access = access;
            this.who = who;
        }
    }

    internal static class TelegramChatGPTExample
    {
        const long maxUniqueVisitors = 15;  // Restriction to prevent users sharing
        const long adminId = 0;             // Place your chatId here to enable admin commands
        const string groupChatPrefix = "-"; // Prefix for a message in a group chat to allow the bot to distinguish between a message that should be treated as a question and side talks.

        public static RxTelegram.Bot.TelegramBot Bot;
        internal static ConcurrentDictionary<long, Visitor> visitors;
        internal static ConcurrentDictionary<long, AIChatContext> contextByChats;
        private static OpenAIAPI AI { get; set; }

        static TelegramChatGPTExample()
        {
            Console.OutputEncoding = Encoding.Unicode;
            Console.InputEncoding = Encoding.Unicode;

            visitors = new ConcurrentDictionary<long, Visitor>();
            Bot = new RxTelegram.Bot.TelegramBot("PLACE TELEGRAM BOT TOKEN HERE");
            contextByChats = new ConcurrentDictionary<long, AIChatContext>();
            AI = new OpenAIAPI("PLACE OPENAI API KEY HERE");
        }

        public static async Task Main()
        {
            await Run();
        }

        private static async Task Run(int attempt = 0)
        {
            var me = await Bot.GetMe();
            Console.WriteLine($"Bot name: @{me.Username}");

            var messageListener = Bot.Updates.Message.Subscribe(HandleMessage, exception =>
            {
                Console.WriteLine($"An error has occured: {exception.Message}");
            });

            _ = Console.ReadLine();
            messageListener.Dispose();
        }

        private static async void HandleMessage(Message message)
        {
            if (message.Text == null)
                return;

            var chatId = message.Chat.Id;
            try
            {
                if (!HasAccess(message, chatId))
                {
                    _ = Bot.SendMessage(new SendMessage
                    {
                        ChatId = chatId,
                        Text = "You do not have access to ChatGPT, please contact the bot administrator to get access."
                    });
                    return;
                }

                bool isExecuted = CommandProcessor.ExecuteIfAICommand(message);
                if (isExecuted)
                    return;

                bool isPersonalChat = chatId == message.From.Id;
                bool isExplicitAICall = !isPersonalChat && message.Text.StartsWith(groupChatPrefix);
                if (isPersonalChat || isExplicitAICall)
                {
                    var chatContext = contextByChats.GetOrAdd(chatId, new AIChatContext());
                    await chatContext.conversationSemaphore.WaitAsync();
                    string response = "";
                    try
                    {
                        var conversation = chatContext.GetConversation(() => { return AI.Chat.CreateConversation(); });
                        conversation.AppendMessage(new ChatMessage(ChatMessageRole.User, message.Text));
                        response = await conversation.GetResponseFromChatbot();

                    }
                    finally
                    {
                        _ = chatContext.conversationSemaphore.Release();
                    }

                    _ = Bot.SendMessage(new SendMessage
                    {
                        ChatId = chatId,
                        Text = response
                    });
                }
            }
            catch (Exception exception)
            {
                var contextLimitTag = "This model's maximum context length is";
                if (exception.Message.Contains(contextLimitTag))
                {
                    _ = Bot.SendMessage(new SendMessage
                    {
                        ChatId = chatId,
                        Text = "Allowed dialogue length exceeded, press (Re)start in the menu (left striped button) to start a new dialogue."
                    });
                }
            }
        }

        public static bool IsAdmin(Message message)
        {
            return message.Chat.Id == adminId;
        }

        private static bool HasAccess(Message message, long chatId)
        {
            var isAdmin = IsAdmin(message);
            if (isAdmin)
            {
                _ = visitors.TryAdd(chatId, new Visitor(isAdmin, message.From.Username));
                return isAdmin;
            }

            var visitor = visitors.GetOrAdd(chatId, (long id) => { Visitor arg = new(false, message.From.Username); return arg; });
            if (visitors.Count < maxUniqueVisitors)
                return true;
            return visitor.access;
        }
    }
}