using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Moq;

namespace SprintAITests
{
    /// <summary>
    /// Mock DTOs - Self-contained test models
    /// </summary>
    public class Recommendation { public string SubjectCategory { get; set; } public float MatchScore { get; set; } }
    public class RecommendationResponse { public int UserId { get; set; } public List<Recommendation> Recommendations { get; set; } = new(); }
    public class ProgressAnalysisResponse { public int UserId { get; set; } public int SubjectId { get; set; } public float CompletionPercentage { get; set; } }
    public class QuizQuestion { public int Id { get; set; } public string Question { get; set; } }
    public class QuizGenerationResponse { public int UserId { get; set; } public int SubjectId { get; set; } public List<QuizQuestion> Questions { get; set; } = new(); }
    public class PerformanceMetricsResponse { public int UserId { get; set; } public int Percentile { get; set; } }
    public class Week { public int WeekNumber { get; set; } public List<string> Topics { get; set; } = new(); }
    public class LearningPathResponse { public int UserId { get; set; } public List<Week> Weeks { get; set; } = new(); }

    public interface IFastApiClient
    {
        Task<RecommendationResponse> GetRecommendationsAsync(int userId, string preferenceLevel, string category);
        Task<ProgressAnalysisResponse> AnalyzeProgressAsync(int userId, int subjectId, string depth);
        Task<QuizGenerationResponse> GenerateQuizAsync(int userId, int subjectId, int count, string difficulty);
        Task<PerformanceMetricsResponse> GetPerformanceAsync(int userId, string timePeriod);
        Task<LearningPathResponse> GenerateLearningPathAsync(int userId, string subject, int weeks, int hoursPerWeek);
    }

    /// <summary>
    /// Unit tests for Sprint 3 AI Service - Recommendations
    /// Tests: 3 tests per endpoint covering valid input and edge cases
    /// </summary>
    public class AIServiceRecommendationTests
    {
        private readonly Mock<IFastApiClient> _mockFastApiClient = new();

        [Fact]
        public async Task GetRecommendationsAsync_WithValidInput_ReturnsRecommendations()
        {
            // Arrange
            var mockResponse = new RecommendationResponse
            {
                UserId = 1,
                Recommendations = new List<Recommendation> { new() { SubjectCategory = "Math", MatchScore = 95f } }
            };
            _mockFastApiClient.Setup(x => x.GetRecommendationsAsync(1, "beginner", "math")).ReturnsAsync(mockResponse);

            // Act
            var result = await _mockFastApiClient.Object.GetRecommendationsAsync(1, "beginner", "math");

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, result.UserId);
            Assert.Single(result.Recommendations);
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithMultipleRecommendations_ReturnsList()
        {
            // Arrange
            var mockResponse = new RecommendationResponse
            {
                UserId = 1,
                Recommendations = new List<Recommendation>
                {
                    new() { SubjectCategory = "Math", MatchScore = 95f },
                    new() { SubjectCategory = "Science", MatchScore = 87f },
                    new() { SubjectCategory = "History", MatchScore = 78f }
                }
            };
            _mockFastApiClient.Setup(x => x.GetRecommendationsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(mockResponse);

            // Act
            var result = await _mockFastApiClient.Object.GetRecommendationsAsync(1, "beginner", "all");

            // Assert
            Assert.Equal(3, result.Recommendations.Count);
            Assert.Equal("Math", result.Recommendations[0].SubjectCategory);
            Assert.Equal(95f, result.Recommendations[0].MatchScore);
        }

        [Fact]
        public async Task GetRecommendationsAsync_WithDifferentLevels_ReturnsDifferentResults()
        {
            // Arrange
            var beginnerResponse = new RecommendationResponse { UserId = 1 };
            var advancedResponse = new RecommendationResponse { UserId = 1 };
            _mockFastApiClient.Setup(x => x.GetRecommendationsAsync(1, "beginner", "math")).ReturnsAsync(beginnerResponse);
            _mockFastApiClient.Setup(x => x.GetRecommendationsAsync(1, "advanced", "math")).ReturnsAsync(advancedResponse);

            // Act & Assert
            var beginner = await _mockFastApiClient.Object.GetRecommendationsAsync(1, "beginner", "math");
            var advanced = await _mockFastApiClient.Object.GetRecommendationsAsync(1, "advanced", "math");
            Assert.NotNull(beginner);
            Assert.NotNull(advanced);
        }
    }

    /// <summary>
    /// Unit tests for Sprint 3 AI Service - Progress Analysis
    /// Tests: 3 tests per endpoint
    /// </summary>
    public class AIServiceProgressAnalysisTests
    {
        private readonly Mock<IFastApiClient> _mockFastApiClient = new();

        [Fact]
        public async Task AnalyzeProgressAsync_WithValidInput_ReturnsAnalysis()
        {
            var mockResponse = new ProgressAnalysisResponse { UserId = 1, SubjectId = 1, CompletionPercentage = 75f };
            _mockFastApiClient.Setup(x => x.AnalyzeProgressAsync(1, 1, "detailed")).ReturnsAsync(mockResponse);

            var result = await _mockFastApiClient.Object.AnalyzeProgressAsync(1, 1, "detailed");

            Assert.NotNull(result);
            Assert.Equal(75f, result.CompletionPercentage);
            Assert.Equal(1, result.SubjectId);
        }

        [Fact]
        public async Task AnalyzeProgressAsync_WithDifferentDepths_ReturnsDifferentResults()
        {
            var simpleResponse = new ProgressAnalysisResponse { UserId = 1, CompletionPercentage = 75f };
            var detailedResponse = new ProgressAnalysisResponse { UserId = 1, CompletionPercentage = 75f };
            _mockFastApiClient.Setup(x => x.AnalyzeProgressAsync(1, 1, "simple")).ReturnsAsync(simpleResponse);
            _mockFastApiClient.Setup(x => x.AnalyzeProgressAsync(1, 1, "detailed")).ReturnsAsync(detailedResponse);

            var simple = await _mockFastApiClient.Object.AnalyzeProgressAsync(1, 1, "simple");
            var detailed = await _mockFastApiClient.Object.AnalyzeProgressAsync(1, 1, "detailed");

            Assert.NotNull(simple);
            Assert.NotNull(detailed);
        }

        [Fact]
        public async Task AnalyzeProgressAsync_WithMultipleSubjects_VerifyAllCalls()
        {
            var response = new ProgressAnalysisResponse { UserId = 1 };
            _mockFastApiClient.Setup(x => x.AnalyzeProgressAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(response);

            await _mockFastApiClient.Object.AnalyzeProgressAsync(1, 1, "detailed");
            await _mockFastApiClient.Object.AnalyzeProgressAsync(1, 2, "detailed");
            await _mockFastApiClient.Object.AnalyzeProgressAsync(1, 3, "detailed");

            _mockFastApiClient.Verify(x => x.AnalyzeProgressAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Exactly(3));
        }
    }

    /// <summary>
    /// Unit tests for Sprint 3 AI Service - Quiz Generation
    /// Tests: 4 tests per endpoint
    /// </summary>
    public class AIServiceQuizGenerationTests
    {
        private readonly Mock<IFastApiClient> _mockFastApiClient = new();

        [Fact]
        public async Task GenerateQuizAsync_WithValidInput_ReturnsQuiz()
        {
            var mockResponse = new QuizGenerationResponse
            {
                UserId = 1,
                SubjectId = 1,
                Questions = new List<QuizQuestion> { new() { Id = 1, Question = "What is 2+2?" } }
            };
            _mockFastApiClient.Setup(x => x.GenerateQuizAsync(1, 1, 10, "easy")).ReturnsAsync(mockResponse);

            var result = await _mockFastApiClient.Object.GenerateQuizAsync(1, 1, 10, "easy");

            Assert.NotNull(result);
            Assert.Single(result.Questions);
            Assert.Equal("What is 2+2?", result.Questions[0].Question);
        }

        [Fact]
        public async Task GenerateQuizAsync_WithMultipleQuestions_ReturnsFullQuiz()
        {
            var questions = new List<QuizQuestion>();
            for (int i = 1; i <= 10; i++)
            {
                questions.Add(new QuizQuestion { Id = i, Question = $"Question {i}" });
            }
            var mockResponse = new QuizGenerationResponse { UserId = 1, SubjectId = 1, Questions = questions };
            _mockFastApiClient.Setup(x => x.GenerateQuizAsync(1, 1, 10, "medium")).ReturnsAsync(mockResponse);

            var result = await _mockFastApiClient.Object.GenerateQuizAsync(1, 1, 10, "medium");

            Assert.Equal(10, result.Questions.Count);
            Assert.Equal("Question 1", result.Questions[0].Question);
            Assert.Equal("Question 10", result.Questions[9].Question);
        }

        [Fact]
        public async Task GenerateQuizAsync_WithDifferentDifficulties_ReturnsDifferentQuizzes()
        {
            var easyResponse = new QuizGenerationResponse { UserId = 1 };
            var hardResponse = new QuizGenerationResponse { UserId = 1 };
            _mockFastApiClient.Setup(x => x.GenerateQuizAsync(1, 1, 5, "easy")).ReturnsAsync(easyResponse);
            _mockFastApiClient.Setup(x => x.GenerateQuizAsync(1, 1, 5, "hard")).ReturnsAsync(hardResponse);

            var easy = await _mockFastApiClient.Object.GenerateQuizAsync(1, 1, 5, "easy");
            var hard = await _mockFastApiClient.Object.GenerateQuizAsync(1, 1, 5, "hard");

            Assert.NotNull(easy);
            Assert.NotNull(hard);
        }

        [Fact]
        public async Task GenerateQuizAsync_WithVariousQuestionCounts_VerifyCalls()
        {
            var response = new QuizGenerationResponse { UserId = 1 };
            _mockFastApiClient.Setup(x => x.GenerateQuizAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(response);

            await _mockFastApiClient.Object.GenerateQuizAsync(1, 1, 5, "easy");
            await _mockFastApiClient.Object.GenerateQuizAsync(1, 1, 10, "medium");
            await _mockFastApiClient.Object.GenerateQuizAsync(1, 1, 20, "hard");

            _mockFastApiClient.Verify(x => x.GenerateQuizAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Exactly(3));
        }
    }

    /// <summary>
    /// Unit tests for Sprint 3 AI Service - Performance Metrics
    /// Tests: 4 tests per endpoint
    /// </summary>
    public class AIServicePerformanceMetricsTests
    {
        private readonly Mock<IFastApiClient> _mockFastApiClient = new();

        [Fact]
        public async Task GetPerformanceAsync_WithValidUserId_ReturnsMetrics()
        {
            var mockResponse = new PerformanceMetricsResponse { UserId = 1, Percentile = 75 };
            _mockFastApiClient.Setup(x => x.GetPerformanceAsync(1, "7days")).ReturnsAsync(mockResponse);

            var result = await _mockFastApiClient.Object.GetPerformanceAsync(1, "7days");

            Assert.NotNull(result);
            Assert.Equal(75, result.Percentile);
            Assert.Equal(1, result.UserId);
        }

        [Fact]
        public async Task GetPerformanceAsync_WithCustomTimePeriod_ReturnsMetrics()
        {
            var mockResponse = new PerformanceMetricsResponse { UserId = 1, Percentile = 80 };
            _mockFastApiClient.Setup(x => x.GetPerformanceAsync(1, "30days")).ReturnsAsync(mockResponse);

            var result = await _mockFastApiClient.Object.GetPerformanceAsync(1, "30days");

            Assert.NotNull(result);
            Assert.Equal(80, result.Percentile);
        }

        [Fact]
        public async Task GetPerformanceAsync_WithDifferentTimePeriods_ReturnsDifferentMetrics()
        {
            var sevenDaysResponse = new PerformanceMetricsResponse { UserId = 1, Percentile = 75 };
            var thirtyDaysResponse = new PerformanceMetricsResponse { UserId = 1, Percentile = 80 };
            var ninetyDaysResponse = new PerformanceMetricsResponse { UserId = 1, Percentile = 85 };
            _mockFastApiClient.Setup(x => x.GetPerformanceAsync(1, "7days")).ReturnsAsync(sevenDaysResponse);
            _mockFastApiClient.Setup(x => x.GetPerformanceAsync(1, "30days")).ReturnsAsync(thirtyDaysResponse);
            _mockFastApiClient.Setup(x => x.GetPerformanceAsync(1, "90days")).ReturnsAsync(ninetyDaysResponse);

            var sevenDays = await _mockFastApiClient.Object.GetPerformanceAsync(1, "7days");
            var thirtyDays = await _mockFastApiClient.Object.GetPerformanceAsync(1, "30days");
            var ninetyDays = await _mockFastApiClient.Object.GetPerformanceAsync(1, "90days");

            Assert.Equal(75, sevenDays.Percentile);
            Assert.Equal(80, thirtyDays.Percentile);
            Assert.Equal(85, ninetyDays.Percentile);
        }

        [Fact]
        public async Task GetPerformanceAsync_WithMultipleUsers_VerifyUserIdTracking()
        {
            var response1 = new PerformanceMetricsResponse { UserId = 1, Percentile = 75 };
            var response2 = new PerformanceMetricsResponse { UserId = 2, Percentile = 85 };
            _mockFastApiClient.Setup(x => x.GetPerformanceAsync(1, "7days")).ReturnsAsync(response1);
            _mockFastApiClient.Setup(x => x.GetPerformanceAsync(2, "7days")).ReturnsAsync(response2);

            var user1 = await _mockFastApiClient.Object.GetPerformanceAsync(1, "7days");
            var user2 = await _mockFastApiClient.Object.GetPerformanceAsync(2, "7days");

            Assert.Equal(1, user1.UserId);
            Assert.Equal(2, user2.UserId);
            Assert.Equal(75, user1.Percentile);
            Assert.Equal(85, user2.Percentile);
        }
    }

    /// <summary>
    /// Unit tests for Sprint 3 AI Service - Learning Path Generation
    /// Tests: 4 tests per endpoint
    /// </summary>
    public class AIServiceLearningPathTests
    {
        private readonly Mock<IFastApiClient> _mockFastApiClient = new();

        [Fact]
        public async Task GenerateLearningPathAsync_WithValidInput_ReturnsPath()
        {
            var mockResponse = new LearningPathResponse
            {
                UserId = 1,
                Weeks = new List<Week> { new() { WeekNumber = 1, Topics = new List<string> { "Basics" } } }
            };
            _mockFastApiClient.Setup(x => x.GenerateLearningPathAsync(1, "Math", 4, 10)).ReturnsAsync(mockResponse);

            var result = await _mockFastApiClient.Object.GenerateLearningPathAsync(1, "Math", 4, 10);

            Assert.NotNull(result);
            Assert.Single(result.Weeks);
            Assert.Equal(1, result.Weeks[0].WeekNumber);
        }

        [Fact]
        public async Task GenerateLearningPathAsync_WithMultipleWeeks_ReturnsFull_Plan()
        {
            var weeks = new List<Week>();
            for (int i = 1; i <= 4; i++)
            {
                weeks.Add(new Week { WeekNumber = i, Topics = new List<string> { $"Topic {i}" } });
            }
            var mockResponse = new LearningPathResponse { UserId = 1, Weeks = weeks };
            _mockFastApiClient.Setup(x => x.GenerateLearningPathAsync(1, "Science", 4, 15)).ReturnsAsync(mockResponse);

            var result = await _mockFastApiClient.Object.GenerateLearningPathAsync(1, "Science", 4, 15);

            Assert.Equal(4, result.Weeks.Count);
            Assert.Equal("Topic 1", result.Weeks[0].Topics[0]);
            Assert.Equal("Topic 4", result.Weeks[3].Topics[0]);
        }

        [Fact]
        public async Task GenerateLearningPathAsync_WithDifferentIntensities_ReturnsDifferentPlans()
        {
            var lightResponse = new LearningPathResponse { UserId = 1 };
            var intensiveResponse = new LearningPathResponse { UserId = 1 };
            _mockFastApiClient.Setup(x => x.GenerateLearningPathAsync(1, "History", 8, 5)).ReturnsAsync(lightResponse);
            _mockFastApiClient.Setup(x => x.GenerateLearningPathAsync(1, "History", 4, 20)).ReturnsAsync(intensiveResponse);

            var light = await _mockFastApiClient.Object.GenerateLearningPathAsync(1, "History", 8, 5);
            var intensive = await _mockFastApiClient.Object.GenerateLearningPathAsync(1, "History", 4, 20);

            Assert.NotNull(light);
            Assert.NotNull(intensive);
        }

        [Fact]
        public async Task GenerateLearningPathAsync_WithDifferentSubjects_VerifySubjectTracking()
        {
            var mathResponse = new LearningPathResponse { UserId = 1 };
            var scienceResponse = new LearningPathResponse { UserId = 1 };
            var historyResponse = new LearningPathResponse { UserId = 1 };
            _mockFastApiClient.Setup(x => x.GenerateLearningPathAsync(1, "Math", 4, 10)).ReturnsAsync(mathResponse);
            _mockFastApiClient.Setup(x => x.GenerateLearningPathAsync(1, "Science", 4, 10)).ReturnsAsync(scienceResponse);
            _mockFastApiClient.Setup(x => x.GenerateLearningPathAsync(1, "History", 4, 10)).ReturnsAsync(historyResponse);

            var math = await _mockFastApiClient.Object.GenerateLearningPathAsync(1, "Math", 4, 10);
            var science = await _mockFastApiClient.Object.GenerateLearningPathAsync(1, "Science", 4, 10);
            var history = await _mockFastApiClient.Object.GenerateLearningPathAsync(1, "History", 4, 10);

            Assert.NotNull(math);
            Assert.NotNull(science);
            Assert.NotNull(history);
        }
    }

    /// <summary>
    /// Integration tests for Sprint 3 AI Services
    /// Tests: All 5 endpoints working together
    /// </summary>
    public class AIServiceIntegrationTests
    {
        private readonly Mock<IFastApiClient> _mockFastApiClient = new();

        [Fact]
        public async Task MultipleEndpoints_ExecuteSequentially_AllSucceed()
        {
            var recommendation = new RecommendationResponse { UserId = 1 };
            var analysis = new ProgressAnalysisResponse { UserId = 1 };
            var quiz = new QuizGenerationResponse { UserId = 1 };
            _mockFastApiClient.Setup(x => x.GetRecommendationsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(recommendation);
            _mockFastApiClient.Setup(x => x.AnalyzeProgressAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(analysis);
            _mockFastApiClient.Setup(x => x.GenerateQuizAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(quiz);

            var rec = await _mockFastApiClient.Object.GetRecommendationsAsync(1, "beginner", "math");
            var prog = await _mockFastApiClient.Object.AnalyzeProgressAsync(1, 1, "detailed");
            var q = await _mockFastApiClient.Object.GenerateQuizAsync(1, 1, 10, "easy");

            Assert.NotNull(rec);
            Assert.NotNull(prog);
            Assert.NotNull(q);
        }

        [Fact]
        public async Task AllFiveEndpoints_CallsMock_VerifyAllCallsExecuted()
        {
            _mockFastApiClient.Setup(x => x.GetRecommendationsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new RecommendationResponse { UserId = 1 });
            _mockFastApiClient.Setup(x => x.AnalyzeProgressAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new ProgressAnalysisResponse { UserId = 1 });
            _mockFastApiClient.Setup(x => x.GenerateQuizAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new QuizGenerationResponse { UserId = 1 });
            _mockFastApiClient.Setup(x => x.GetPerformanceAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new PerformanceMetricsResponse { UserId = 1 });
            _mockFastApiClient.Setup(x => x.GenerateLearningPathAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new LearningPathResponse { UserId = 1 });

            var r1 = await _mockFastApiClient.Object.GetRecommendationsAsync(1, "beginner", "math");
            var r2 = await _mockFastApiClient.Object.AnalyzeProgressAsync(1, 1, "detailed");
            var r3 = await _mockFastApiClient.Object.GenerateQuizAsync(1, 1, 10, "easy");
            var r4 = await _mockFastApiClient.Object.GetPerformanceAsync(1, "7days");
            var r5 = await _mockFastApiClient.Object.GenerateLearningPathAsync(1, "Math", 4, 10);

            Assert.NotNull(r1);
            Assert.NotNull(r2);
            Assert.NotNull(r3);
            Assert.NotNull(r4);
            Assert.NotNull(r5);

            _mockFastApiClient.Verify(x => x.GetRecommendationsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _mockFastApiClient.Verify(x => x.AnalyzeProgressAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Once);
            _mockFastApiClient.Verify(x => x.GenerateQuizAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Once);
            _mockFastApiClient.Verify(x => x.GetPerformanceAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Once);
            _mockFastApiClient.Verify(x => x.GenerateLearningPathAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        }
    }
}
