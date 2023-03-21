using OpenAI_API.Chat;
using System;
using System.Threading;

namespace TelegramChatGPTExample
{
    internal class AIChatContext
    {
        private Conversation conversation;
        private Func<Conversation> conversationFactory;

        public SemaphoreSlim conversationSemaphore = new(1, 1);

        public Conversation GetConversation(Func<Conversation> conversationFactory)
        {
            _ = Interlocked.CompareExchange(ref this.conversationFactory, conversationFactory, null);
            if (Interlocked.CompareExchange(ref conversation, null, null) == null)
            {
                ReInitialize();
            }

            return conversation;
        }

        public async void ReInitialize()
        {
            _ = Interlocked.Exchange(ref conversation, conversationFactory());
            await conversationSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                Initialize(conversation);
            }
            finally
            {
                _ = conversationSemaphore.Release();
            }
        }

        private void Initialize(Conversation newConversation)
        {
            newConversation.AppendSystemMessage($"{DateTime.Now}");
            // newConversation.AppendExampleChatbotOutput($"Place facts and desired behaviour here");
        }
    }
}