using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Backend.Tests
{
    // Remise en état (Module 26) puis mise à jour (Module 23) :
    //  - les types "Mock*" locaux, jamais utilisés, ont été retirés ;
    //  - les tests de GenerateQuizAsync ont été retirés (méthode supprimée,
    //    remplacée par POST /quizzes/me/generate) ;
    //  - Module 23 : GetRecommendationsAsync, AnalyzeProgressAsync et
    //    GetPerformanceMetricsAsync n'existent plus (AIController relaie la
    //    réponse Python brute ; /ai/performance supprimé, route Python inexistante).

    /// <summary>
    /// Tests unitaires de AIService (vrai service de production, client FastAPI simulé).
    /// </summary>
    public class AIServiceLearningPathTests
    {
        private readonly Mock<IFastApiClient> _mockFastApiClient = new();
        private readonly AIService _aiService;

        public AIServiceLearningPathTests()
        {
            _aiService = new AIService(_mockFastApiClient.Object, new Mock<ILogger<AIService>>().Object);
        }

        [Fact]
        public async Task GeneratePersonalizedPathAsync_WithValidInput_ReturnsPhases()
        {
            _mockFastApiClient
                .Setup(x => x.GenerateLearningPathAsync(1))
                .ReturnsAsync(new LearningPathResponse
                {
                    Success = true,
                    UserId = 1,
                    Phases = new List<LearningPathPhase> { new() { Phase = 1, Name = "Fondations" } }
                });

            var result = await _aiService.GeneratePersonalizedPathAsync(1, "Math", 4, 10);

            Assert.NotNull(result);
            Assert.Single(result!.Phases);
            _mockFastApiClient.Verify(x => x.GenerateLearningPathAsync(1), Times.Once);
        }

        [Fact]
        public async Task GeneratePersonalizedPathAsync_WhenPythonFails_ReturnsNull()
        {
            _mockFastApiClient.Setup(x => x.GenerateLearningPathAsync(1)).ReturnsAsync((LearningPathResponse?)null);

            Assert.Null(await _aiService.GeneratePersonalizedPathAsync(1, "Math", 4, 10));
        }

        [Theory]
        [InlineData(0, 4, 10)]
        [InlineData(-1, 4, 10)]
        [InlineData(1, 0, 10)]
        [InlineData(1, 53, 10)]
        [InlineData(1, -1, 10)]
        [InlineData(1, 4, 0)]
        [InlineData(1, 4, 169)]
        [InlineData(1, 4, -5)]
        public async Task GeneratePersonalizedPathAsync_WithOutOfRangeInput_ThrowsArgumentException(int userId, int weeks, int hours)
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _aiService.GeneratePersonalizedPathAsync(userId, "Math", weeks, hours));
        }
    }

    /// <summary>
    /// Tests de AIController (vrai contrôleur de production, client FastAPI simulé).
    /// </summary>
    public class AIControllerTests
    {
        private readonly Mock<IAIService> _mockAIService = new();
        private readonly Mock<IFastApiClient> _mockFastApi = new();
        private readonly ApplicationDbContext _db;

        public AIControllerTests()
        {
            _db = TestApplicationDbContext.Create();
        }

        private AIController CreateController(int currentUserId, string role = "student")
        {
            var controller = new AIController(
                _mockAIService.Object,
                new Mock<IHttpClientFactory>().Object,
                new Mock<ILogger<AIController>>().Object,
                _db,
                _mockFastApi.Object);
            var identity = new ClaimsIdentity(new[]
            {
                new Claim("sub", currentUserId.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim("role", role),
            }, "test");
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            };
            return controller;
        }

        [Fact]
        public async Task GetRecommendations_SendsUserIdAsQueryParameter_AndRelaysPythonBody()
        {
            _mockFastApi
                .Setup(x => x.PostRawJsonAsync("/api/recommend?user_id=1&limit=5", null))
                .ReturnsAsync((200, "{\"success\":true,\"recommendations\":[]}"));

            var result = await CreateController(1).GetRecommendations(new RecommendationRequest { UserId = 1, NumberOfRecommendations = 5 });

            var content = Assert.IsType<ContentResult>(result);
            Assert.Equal(200, content.StatusCode);
            Assert.Contains("recommendations", content.Content);
        }

        [Fact]
        public async Task GetRecommendations_ForAnotherUser_IsForbidden()
        {
            var result = await CreateController(2).GetRecommendations(new RecommendationRequest { UserId = 1, NumberOfRecommendations = 5 });

            Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
            _mockFastApi.Verify(x => x.PostRawJsonAsync(It.IsAny<string>(), It.IsAny<object?>()), Times.Never);
        }

        [Fact]
        public async Task AnalyzeProgress_ForLinkedChild_IsAllowed()
        {
            _db.ParentStudentLinks.Add(new ParentStudentLink { ParentId = 10, StudentId = 1, Status = "accepted" });
            await _db.SaveChangesAsync();
            _mockFastApi
                .Setup(x => x.PostRawJsonAsync("/api/analyze-progress?user_id=1", null))
                .ReturnsAsync((200, "{\"success\":true,\"user_id\":1}"));

            var result = await CreateController(10, "parent").AnalyzeProgress(new ProgressAnalysisRequest { UserId = 1, SubjectId = 0 });

            Assert.Equal(200, Assert.IsType<ContentResult>(result).StatusCode);
        }

        [Fact]
        public async Task AnalyzeProgress_RelaysPython404()
        {
            _mockFastApi
                .Setup(x => x.PostRawJsonAsync("/api/analyze-progress?user_id=1", null))
                .ReturnsAsync((404, "{\"detail\":\"Aucun enrollment trouvé\"}"));

            var result = await CreateController(1).AnalyzeProgress(new ProgressAnalysisRequest { UserId = 1, SubjectId = 0 });

            Assert.Equal(404, Assert.IsType<ContentResult>(result).StatusCode);
        }

        [Fact]
        public async Task GeneratePersonalizedPath_WhenPythonFails_Returns502()
        {
            _mockAIService
                .Setup(x => x.GeneratePersonalizedPathAsync(1, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((LearningPathResponse?)null);

            var result = await CreateController(1).GeneratePersonalizedPath(
                new LearningPathRequest { UserId = 1, GoalSubject = "Math", TimeframeWeeks = 4, AvailableHoursPerWeek = 10 });

            Assert.Equal(502, Assert.IsType<ObjectResult>(result).StatusCode);
        }
    }
}
