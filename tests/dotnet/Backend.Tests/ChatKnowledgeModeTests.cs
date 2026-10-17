using System.Text.Json;
using Backend.Models.DTOs;
using Backend.Services;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 9.7b (décision 18.21) : le mode « base de connaissance » du chat est
    /// désactivé par défaut et transmis à FastAPI sous « knowledge_base ».
    /// Le coût de la recherche est imputé côté FastAPI (tokens_used du message,
    /// voir python/RAG/tests/test_chat_knowledge_mode.py).
    /// </summary>
    public class ChatKnowledgeModeTests
    {
        [Fact]
        public void KnowledgeBase_IsDisabledByDefault()
        {
            Assert.False(new SendMessageRequest().KnowledgeBase);
            Assert.False(new StreamChatRequest().KnowledgeBase);
            Assert.False(new FastApiChatRequest().KnowledgeBase);
        }

        [Fact]
        public void KnowledgeBase_IsSentAsSnakeCaseToFastApi()
        {
            var json = JsonSerializer.Serialize(new FastApiChatRequest { KnowledgeBase = true }, FastApiClient.SnakeCaseJson);
            Assert.Contains("\"knowledge_base\":true", json);

            var off = JsonSerializer.Serialize(new FastApiChatRequest(), FastApiClient.SnakeCaseJson);
            Assert.Contains("\"knowledge_base\":false", off);
        }

        [Fact]
        public void KnowledgeBase_IsReadFromWebAndMobilePayload()
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var req = JsonSerializer.Deserialize<StreamChatRequest>("{\"knowledgeBase\":true}", opts)!;
            Assert.True(req.KnowledgeBase);
        }
    }
}
