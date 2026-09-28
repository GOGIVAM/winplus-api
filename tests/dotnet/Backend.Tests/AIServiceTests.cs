using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Backend.Tests
{
    // Remise en état (Module 26) : les types "Mock*" locaux et les interfaces
    // IMockFastApiClient/IMockAIService, jamais utilisés, ont été retirés. Les
    // tests de GenerateQuizAsync ont été retirés : la méthode n'existe plus sur
    // IAIService ni IFastApiClient (remplacée par POST /quizzes/me/generate,
    // voir le commentaire "generate-quiz a été retiré" dans AIController).

    /// <summary>
    /// Tests unitaires de AIService (vrai service de production, client FastAPI simulé).
    /// </summary>
    public class AIServiceRecommendationTests
    {
        private readonly Mock<IFastApiClient> _mockFastApiClient;
        private readonly AIService _aiService;

        public AIServiceRecommendationTests()
        {
            _mockFastApiClient = new Mock<IFastApiClient>();
            _aiService = new AIService(_mockFastApiClient.Object, new Mock<ILogger<AIService>>().Object);
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithValidInput_ReturnsRecommendations()
        {
            var mockResponse = new RecommendationResponse
            {
                UserId = 1,
                Recommendations = new List<RecommendationItem>
                {
                    new RecommendationItem { SubjectName = "Math", MatchScore = 0.95m }
                }
            };
            _mockFastApiClient
                .Setup(x => x.GetRecommendationsAsync(1, "beginner", "math"))
                .ReturnsAsync(mockResponse);

            var result = await _aiService.GetRecommendationsAsync(1, 5, "beginner", "math");

            Assert.NotNull(result);
            Assert.Equal(1, result.UserId);
            Assert.Single(result.Recommendations);
            _mockFastApiClient.Verify(x => x.GetRecommendationsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task GetRecommendationsAsync_WithInvalidUserId_ThrowsArgumentException(int userId)
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _aiService.GetRecommendationsAsync(userId, 5, "beginner", "math"));
        }

        [Fact]
        public async Task AnalyzeProgressAsync_WithValidInput_ReturnsAnalysis()
        {
            var mockResponse = new ProgressAnalysisResponse { UserId = 1, SubjectId = 1, CompletionPercentage = 75 };
            _mockFastApiClient
                .Setup(x => x.AnalyzeProgressAsync(1, 1, "detailed"))
                .ReturnsAsync(mockResponse);

            var result = await _aiService.AnalyzeProgressAsync(1, 1, "detailed");

            Assert.NotNull(result);
            Assert.Equal(75, result.CompletionPercentage);
            _mockFastApiClient.Verify(x => x.AnalyzeProgressAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Once);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task AnalyzeProgressAsync_WithInvalidUserId_ThrowsArgumentException(int userId)
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _aiService.AnalyzeProgressAsync(userId, 1, "detailed"));
        }

        [Fact]
        public async Task GetPerformanceMetricsAsync_WithValidUserId_UsesDefaultPeriod()
        {
            var mockResponse = new PerformanceMetricsResponse
            {
                UserId = 1,
                CompareToAverage = new ClassComparison { Percentile = 75 }
            };
            _mockFastApiClient
                .Setup(x => x.GetPerformanceAsync(1, "7days"))
                .ReturnsAsync(mockResponse);

            var result = await _aiService.GetPerformanceMetricsAsync(1);

            Assert.NotNull(result);
            Assert.Equal(75, result.CompareToAverage.Percentile);
            _mockFastApiClient.Verify(x => x.GetPerformanceAsync(1, "7days"), Times.Once);
        }

        [Fact]
        public async Task GetPerformanceMetricsAsync_WithCustomTimePeriod_ForwardsPeriod()
        {
            var mockResponse = new PerformanceMetricsResponse
            {
                UserId = 1,
                CompareToAverage = new ClassComparison { Percentile = 80 }
            };
            _mockFastApiClient
                .Setup(x => x.GetPerformanceAsync(1, "30days"))
                .ReturnsAsync(mockResponse);

            var result = await _aiService.GetPerformanceMetricsAsync(1, "30days");

            Assert.Equal(80, result.CompareToAverage.Percentile);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task GetPerformanceMetricsAsync_WithInvalidUserId_ThrowsArgumentException(int userId)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _aiService.GetPerformanceMetricsAsync(userId));
        }

        [Fact]
        public async Task GeneratePersonalizedPathAsync_WithValidInput_ReturnsPath()
        {
            var mockResponse = new LearningPathResponse
            {
                UserId = 1,
                Weeks = new List<LearningPathWeek>
                {
                    new LearningPathWeek { WeekNumber = 1, Topics = new List<string> { "Basics" } }
                }
            };
            _mockFastApiClient
                .Setup(x => x.GenerateLearningPathAsync(1, "Math", 4, 10))
                .ReturnsAsync(mockResponse);

            var result = await _aiService.GeneratePersonalizedPathAsync(1, "Math", 4, 10);

            Assert.NotNull(result);
            Assert.Single(result.Weeks);
            _mockFastApiClient.Verify(x => x.GenerateLearningPathAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        }

        [Theory]
        [InlineData(0, 10)]
        [InlineData(53, 10)]
        [InlineData(-1, 10)]
        [InlineData(4, 0)]
        [InlineData(4, 169)]
        [InlineData(4, -5)]
        public async Task GeneratePersonalizedPathAsync_WithOutOfRangeInput_ThrowsArgumentException(int weeks, int hours)
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _aiService.GeneratePersonalizedPathAsync(1, "Math", weeks, hours));
        }

        [Fact]
        public async Task MultipleServiceCalls_EachCallsClientOnce()
        {
            _mockFastApiClient
                .Setup(x => x.GetRecommendationsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new RecommendationResponse { UserId = 1 });
            _mockFastApiClient
                .Setup(x => x.AnalyzeProgressAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync(new ProgressAnalysisResponse { UserId = 1 });

            var rec = await _aiService.GetRecommendationsAsync(1, 5, "beginner", "math");
            var prog = await _aiService.AnalyzeProgressAsync(1, 1, "detailed");

            Assert.NotNull(rec);
            Assert.NotNull(prog);
            _mockFastApiClient.Verify(x => x.GetRecommendationsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _mockFastApiClient.Verify(x => x.AnalyzeProgressAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Once);
        }
    }

    /// <summary>
    /// Tests de AIController (vrai contrôleur de production, service simulé).
    /// </summary>
    public class AIControllerTests
    {
        private readonly Mock<IAIService> _mockAIService = new();
        private readonly AIController _controller;

        public AIControllerTests()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"aicontroller-{Guid.NewGuid()}")
                .Options);
            _controller = new AIController(
                _mockAIService.Object,
                new Mock<IHttpClientFactory>().Object,
                new Mock<ILogger<AIController>>().Object,
                db,
                new Mock<IFastApiClient>().Object);
        }

        [Fact]
        public async Task GetRecommendations_WithValidRequest_ReturnsOk()
        {
            var request = new RecommendationRequest { UserId = 1, NumberOfRecommendations = 5, PreferenceLevel = "beginner", SubjectCategory = "math" };
            _mockAIService
                .Setup(x => x.GetRecommendationsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new RecommendationResponse { UserId = 1 });

            var result = await _controller.GetRecommendations(request);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task AnalyzeProgress_WithValidRequest_ReturnsOk()
        {
            var request = new ProgressAnalysisRequest { UserId = 1, SubjectId = 1, AnalysisDepth = "detailed" };
            _mockAIService
                .Setup(x => x.AnalyzeProgressAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync(new ProgressAnalysisResponse { UserId = 1 });

            var result = await _controller.AnalyzeProgress(request);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task GetPerformance_WithValidUserId_ReturnsOk()
        {
            _mockAIService
                .Setup(x => x.GetPerformanceMetricsAsync(It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync(new PerformanceMetricsResponse { UserId = 1 });

            var result = await _controller.GetPerformance(1, "7days");

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task GetPerformance_WithInvalidUserId_ReturnsBadRequest()
        {
            var result = await _controller.GetPerformance(0, "7days");

            Assert.IsType<BadRequestObjectResult>(result);
        }
    }
}
