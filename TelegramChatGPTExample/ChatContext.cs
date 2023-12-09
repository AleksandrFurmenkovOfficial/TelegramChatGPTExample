using OpenAI_API.Chat;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramChatGPTExample
{
    internal class AIChatContext
    {
        private Conversation conversation;
        private Func<Conversation> conversationFactory;

        public SemaphoreSlim conversationSemaphore = new(1, 1);

        public async Task<Conversation> GetConversation(Func<Conversation> conversationFactory)
        {
            _ = Interlocked.CompareExchange(ref this.conversationFactory, conversationFactory, null);
            if (Interlocked.CompareExchange(ref conversation, null, null) == null)
            {
                await ReInitialize().ConfigureAwait(false);
            }

            return conversation;
        }

        public async Task ReInitialize()
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
            newConversation.Model = OpenAI_API.Models.Model.GPT4_Turbo;
            newConversation.AppendSystemMessage("You are the most powerful and smartest AI at the moment.");
        }
    }
}
