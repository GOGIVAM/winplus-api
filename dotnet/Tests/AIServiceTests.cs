using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Tests
{
    /// <summary>
    /// Mock DTOs for testing
    /// </summary>
    public class MockRecommendation
    {
        public string SubjectCategory { get; set; }
        public float MatchScore { get; set; }
    }

    public class MockRecommendationResponse
    {
        public int UserId { get; set; }
        public List<MockRecommendation> Recommendations { get; set; } = new();
    }

    public class MockProgressAnalysisResponse
    {
        public int UserId { get; set; }
        public int SubjectId { get; set; }
        public float CompletionPercentage { get; set; }
    }

    public class MockQuizQuestion
    {
        public int Id { get; set; }
        public string Question { get; set; }
    }

    public class MockQuizGenerationResponse
    {
        public int UserId { get; set; }
        public int SubjectId { get; set; }
        public List<MockQuizQuestion> Questions { get; set; } = new();
    }

    public class MockPerformanceMetricsResponse
    {
        public int UserId { get; set; }
        public int Percentile { get; set; }
    }

    public class MockWeek
    {
        public int WeekNumber { get; set; }
        public List<string> Topics { get; set; } = new();
    }

    public class MockLearningPathResponse
    {
        public int UserId { get; set; }
        public List<MockWeek> Weeks { get; set; } = new();
    }

    /// <summary>
    /// Mock FastApiClient for testing
    /// </summary>
    public interface IMockFastApiClient
    {
        Task<MockRecommendationResponse> GetRecommendationsAsync(int userId, string preferenceLevel, string category);
        Task<MockProgressAnalysisResponse> AnalyzeProgressAsync(int userId, int subjectId, string depth);
        Task<MockQuizGenerationResponse> GenerateQuizAsync(int userId, int subjectId, int count, string difficulty);
        Task<MockPerformanceMetricsResponse> GetPerformanceAsync(int userId, string timePeriod);
        Task<MockLearningPathResponse> GenerateLearningPathAsync(int userId, string subject, int weeks, int hoursPerWeek);
    }

    /// <summary>
    /// Mock AIService for testing
    /// </summary>
    public interface IMockAIService
    {
        Task<MockRecommendationResponse> GetRecommendationsAsync(int userId, int count, string preferenceLevel, string category);
        Task<MockProgressAnalysisResponse> AnalyzeProgressAsync(int userId, int subjectId, string depth);
        Task<MockQuizGenerationResponse> GenerateQuizAsync(int userId, int subjectId, int questionCount, string difficulty);
        Task<MockPerformanceMetricsResponse> GetPerformanceMetricsAsync(int userId, string timePeriod = "7days");
        Task<MockLearningPathResponse> GeneratePersonalizedPathAsync(int userId, string goalSubject, int weeks, int hoursPerWeek);
    }

    /// <summary>
    /// Unit tests for Sprint 3 AI Service - Recommendations
    /// </summary>
    public class AIServiceRecommendationTests
    {
        private readonly Mock<IFastApiClient> _mockFastApiClient;
        private readonly Mock<ILogger<AIService>> _mockLogger;
        private readonly AIService _aiService;

        public AIServiceRecommendationTests()
        {
            _mockFastApiClient = new Mock<IFastApiClient>();
            _mockLogger = new Mock<ILogger<AIService>>();
            _aiService = new AIService(_mockFastApiClient.Object, _mockLogger.Object);
        }

        #region GetRecommendationsAsync Tests

        [Fact]
        public async Task GetRecommendationsAsync_WithValidInput_ReturnsRecommendations()
        {
            // Arrange
            var mockResponse = new RecommendationResponse
            {
                UserId = 1,
                Recommendations = new List<Recommendation>
                {
                    new Recommendation { SubjectCategory = "Math", MatchScore = 95f }
                }
            };
            _mockFastApiClient
                .Setup(x => x.GetRecommendationsAsync(1, "beginner", "math"))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _aiService.GetRecommendationsAsync(1, 5, "beginner", "math");

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, result.UserId);
            Assert.Single(result.Recommendations);
            _mockFastApiClient.Verify(x => x.GetRecommendationsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithZeroUserId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _aiService.GetRecommendationsAsync(0, 5, "beginner", "math"));
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithNegativeUserId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _aiService.GetRecommendationsAsync(-1, 5, "beginner", "math"));
        }

        #endregion

        #region AnalyzeProgressAsync Tests

        [Fact]
        public async Task AnalyzeProgressAsync_WithValidInput_ReturnsAnalysis()
        {
            // Arrange
            var mockResponse = new ProgressAnalysisResponse
            {
                UserId = 1,
                SubjectId = 1,
                CompletionPercentage = 75f
            };
            _mockFastApiClient
                .Setup(x => x.AnalyzeProgressAsync(1, 1, "detailed"))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _aiService.AnalyzeProgressAsync(1, 1, "detailed");

            // Assert
            Assert.NotNull(result);
            Assert.Equal(75f, result.CompletionPercentage);
            _mockFastApiClient.Verify(x => x.AnalyzeProgressAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task AnalyzeProgressAsync_WithZeroUserId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _aiService.AnalyzeProgressAsync(0, 1, "detailed"));
        }

        [Fact]
        public async Task AnalyzeProgressAsync_WithNegativeUserId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _aiService.AnalyzeProgressAsync(-1, 1, "detailed"));
        }

        #endregion

        #region GenerateQuizAsync Tests

        [Fact]
        public async Task GenerateQuizAsync_WithValidInput_ReturnsQuiz()
        {
            // Arrange
            var mockResponse = new QuizGenerationResponse
            {
                UserId = 1,
                SubjectId = 1,
                Questions = new List<QuizQuestion>
                {
                    new QuizQuestion { Id = 1, Question = "What is 2+2?" }
                }
            };
            _mockFastApiClient
                .Setup(x => x.GenerateQuizAsync(1, 1, 10, "easy"))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _aiService.GenerateQuizAsync(1, 1, 10, "easy");

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Questions);
            _mockFastApiClient.Verify(x => x.GenerateQuizAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GenerateQuizAsync_WithZeroQuestions_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _aiService.GenerateQuizAsync(1, 1, 0, "easy"));
        }

        [Fact]
        public async Task GenerateQuizAsync_WithTooManyQuestions_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _aiService.GenerateQuizAsync(1, 1, 101, "easy"));
        }

        [Fact]
        public async Task GenerateQuizAsync_WithNegativeQuestions_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _aiService.GenerateQuizAsync(1, 1, -5, "easy"));
        }

        #endregion

        #region GetPerformanceMetricsAsync Tests

        [Fact]
        public async Task GetPerformanceMetricsAsync_WithValidUserId_ReturnsMetrics()
        {
            // Arrange
            var mockResponse = new PerformanceMetricsResponse
            {
                UserId = 1,
                Percentile = 75
            };
            _mockFastApiClient
                .Setup(x => x.GetPerformanceAsync(1, "7days"))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _aiService.GetPerformanceMetricsAsync(1);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(75, result.Percentile);
            _mockFastApiClient.Verify(x => x.GetPerformanceAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetPerformanceMetricsAsync_WithCustomTimePeriod_ReturnsMetrics()
        {
            // Arrange
            var mockResponse = new PerformanceMetricsResponse
            {
                UserId = 1,
                Percentile = 80
            };
            _mockFastApiClient
                .Setup(x => x.GetPerformanceAsync(1, "30days"))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _aiService.GetPerformanceMetricsAsync(1, "30days");

            // Assert
            Assert.NotNull(result);
            Assert.Equal(80, result.Percentile);
        }

        [Fact]
        public async Task GetPerformanceMetricsAsync_WithZeroUserId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _aiService.GetPerformanceMetricsAsync(0));
        }

        [Fact]
        public async Task GetPerformanceMetricsAsync_WithNegativeUserId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _aiService.GetPerformanceMetricsAsync(-1));
        }

        #endregion

        #region GeneratePersonalizedPathAsync Tests

        [Fact]
        public async Task GeneratePersonalizedPathAsync_WithValidInput_ReturnsPath()
        {
            // Arrange
            var mockResponse = new LearningPathResponse
            {
                UserId = 1,
                Weeks = new List<Week>
                {
                    new Week { WeekNumber = 1, Topics = new List<string> { "Basics" } }
                }
            };
            _mockFastApiClient
                .Setup(x => x.GenerateLearningPathAsync(1, "Math", 4, 10))
                .ReturnsAsync(mockResponse);

            // Act
            var result = await _aiService.GeneratePersonalizedPathAsync(1, "Math", 4, 10);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Weeks);
            _mockFastApiClient.Verify(x => x.GenerateLearningPathAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        }

        [Fact]
        public async Task GeneratePersonalizedPathAsync_WithZeroWeeks_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _aiService.GeneratePersonalizedPathAsync(1, "Math", 0, 10));
        }

        [Fact]
        public async Task GeneratePersonalizedPathAsync_WithTooManyWeeks_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _aiService.GeneratePersonalizedPathAsync(1, "Math", 53, 10));
        }

        [Fact]
        public async Task GeneratePersonalizedPathAsync_WithZeroHoursPerWeek_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _aiService.GeneratePersonalizedPathAsync(1, "Math", 4, 0));
        }

        [Fact]
        public async Task GeneratePersonalizedPathAsync_WithTooManyHoursPerWeek_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _aiService.GeneratePersonalizedPathAsync(1, "Math", 4, 169));
        }

        [Fact]
        public async Task GeneratePersonalizedPathAsync_WithNegativeWeeks_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _aiService.GeneratePersonalizedPathAsync(1, "Math", -1, 10));
        }

        [Fact]
        public async Task GeneratePersonalizedPathAsync_WithNegativeHours_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _aiService.GeneratePersonalizedPathAsync(1, "Math", 4, -5));
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task MultipleServiceCalls_VerifyCallOrder()
        {
            // Arrange
            var recommendation = new RecommendationResponse
            {
                UserId = 1,
                Recommendations = new List<Recommendation>()
            };
            var analysis = new ProgressAnalysisResponse { UserId = 1 };
            
            _mockFastApiClient
                .Setup(x => x.GetRecommendationsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(recommendation);
            _mockFastApiClient
                .Setup(x => x.AnalyzeProgressAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync(analysis);

            // Act
            var rec = await _aiService.GetRecommendationsAsync(1, 5, "beginner", "math");
            var prog = await _aiService.AnalyzeProgressAsync(1, 1, "detailed");

            // Assert
            Assert.NotNull(rec);
            Assert.NotNull(prog);
            _mockFastApiClient.Verify(
                x => x.GetRecommendationsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), 
                Times.Once);
            _mockFastApiClient.Verify(
                x => x.AnalyzeProgressAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), 
                Times.Once);
        }

        #endregion
    }

    /// <summary>
    /// Unit tests for the FastApiClient class
    /// Tests HTTP communication with FastApi AI backend
    /// </summary>
    public class FastApiClientTests
    {
        [Fact]
        public void FastApiClient_Constructor_InitializesSuccessfully()
        {
            // Arrange
            var mockHttpClient = new Mock<System.Net.Http.HttpClient>();
            var mockLogger = new Mock<ILogger<FastApiClient>>();
            var mockConfig = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();

            // Act
            var client = new FastApiClient(mockHttpClient.Object, mockLogger.Object, mockConfig.Object);

            // Assert
            Assert.NotNull(client);
        }
    }

    /// <summary>
    /// Unit tests for AIController
    /// Tests REST endpoint handling and HTTP response
    /// </summary>
    public class AIControllerTests
    {
        private readonly Mock<IAIService> _mockAIService;
        private readonly Mock<ILogger<Controllers.AIController>> _mockLogger;
        private readonly Controllers.AIController _controller;

        public AIControllerTests()
        {
            _mockAIService = new Mock<IAIService>();
            _mockLogger = new Mock<ILogger<Controllers.AIController>>();
            _controller = new Controllers.AIController(_mockAIService.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task GetRecommendations_WithValidRequest_ReturnsOkResult()
        {
            // Arrange
            var request = new RecommendationRequest
            {
                UserId = 1,
                NumberOfRecommendations = 5,
                PreferenceLevel = "beginner",
                SubjectCategory = "math"
            };
            var response = new RecommendationResponse
            {
                UserId = 1,
                Recommendations = new List<Recommendation>()
            };
            _mockAIService
                .Setup(x => x.GetRecommendationsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(response);

            // Act
            var result = await _controller.GetRecommendations(request);

            // Assert
            Assert.NotNull(result);
            var okResult = result as Microsoft.AspNetCore.Mvc.OkObjectResult;
            Assert.NotNull(okResult);
        }

        [Fact]
        public async Task AnalyzeProgress_WithValidRequest_ReturnsOkResult()
        {
            // Arrange
            var request = new ProgressAnalysisRequest
            {
                UserId = 1,
                SubjectId = 1,
                AnalysisDepth = "detailed"
            };
            var response = new ProgressAnalysisResponse { UserId = 1 };
            _mockAIService
                .Setup(x => x.AnalyzeProgressAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync(response);

            // Act
            var result = await _controller.AnalyzeProgress(request);

            // Assert
            Assert.NotNull(result);
            var okResult = result as Microsoft.AspNetCore.Mvc.OkObjectResult;
            Assert.NotNull(okResult);
        }

        [Fact]
        public async Task GetPerformance_WithValidUserId_ReturnsOkResult()
        {
            // Arrange
            var response = new PerformanceMetricsResponse { UserId = 1 };
            _mockAIService
                .Setup(x => x.GetPerformanceMetricsAsync(It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync(response);

            // Act
            var result = await _controller.GetPerformance(1, "7days");

            // Assert
            Assert.NotNull(result);
            var okResult = result as Microsoft.AspNetCore.Mvc.OkObjectResult;
            Assert.NotNull(okResult);
        }

        [Fact]
        public async Task GetPerformance_WithInvalidUserId_ReturnsBadRequest()
        {
            // Act
            var result = await _controller.GetPerformance(0, "7days");

            // Assert
            Assert.NotNull(result);
            var badResult = result as Microsoft.AspNetCore.Mvc.BadRequestObjectResult;
            Assert.NotNull(badResult);
        }
    }
}
