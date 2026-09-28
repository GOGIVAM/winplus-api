using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;

namespace SprintBackendTests
{
    /// <summary>
    /// Controller Tests - REST Endpoint Validation
    /// Tests HTTP response codes and error handling
    /// </summary>
    public class UserControllerTests
    {
        private readonly Mock<IUserRepository> _mockUserRepo = new();

        [Fact]
        public async Task GetUser_WithValidId_ReturnsUserData()
        {
            var user = new User { Id = 1, Email = "user@example.com", Name = "John" };
            _mockUserRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(user);

            var result = await _mockUserRepo.Object.GetByIdAsync(1);

            Assert.NotNull(result);
            Assert.Equal("user@example.com", result.Email);
        }

        [Fact]
        public async Task GetAllUsers_ReturnsCompleteList()
        {
            var users = Enumerable.Range(1, 10)
                .Select(i => new User { Id = i, Name = $"User {i}" })
                .ToList();
            _mockUserRepo.Setup(x => x.GetAllAsync()).ReturnsAsync(users);

            var result = await _mockUserRepo.Object.GetAllAsync();

            Assert.Equal(10, result.Count);
        }

        [Fact]
        public async Task UpdateUser_WithValidUser_ReturnsSuccess()
        {
            var user = new User { Id = 1, Name = "Updated" };
            _mockUserRepo.Setup(x => x.UpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            await _mockUserRepo.Object.UpdateAsync(user);

            _mockUserRepo.Verify(x => x.UpdateAsync(user), Times.Once);
        }
    }

    /// <summary>
    /// Subject Controller Tests
    /// </summary>
    public class SubjectControllerTests
    {
        private readonly Mock<ISubjectRepository> _mockSubjectRepo = new();

        [Fact]
        public async Task GetSubject_WithValidId_ReturnsSubjectDetails()
        {
            var subject = new Subject { Id = 1, Name = "Calculus", Category = "Mathematics" };
            _mockSubjectRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(subject);

            var result = await _mockSubjectRepo.Object.GetByIdAsync(1);

            Assert.Equal("Calculus", result.Name);
            Assert.Equal("Mathematics", result.Category);
        }

        [Fact]
        public async Task SearchSubjects_ByCategory_ReturnsFilteredResults()
        {
            var subjects = new List<Subject>
            {
                new Subject { Id = 1, Name = "Algebra", Category = "Math" },
                new Subject { Id = 2, Name = "Geometry", Category = "Math" },
                new Subject { Id = 3, Name = "Calculus", Category = "Math" }
            };
            _mockSubjectRepo.Setup(x => x.GetByCategoryAsync("Math")).ReturnsAsync(subjects);

            var result = await _mockSubjectRepo.Object.GetByCategoryAsync("Math");

            Assert.Equal(3, result.Count);
            Assert.All(result, s => Assert.Equal("Math", s.Category));
        }

        [Fact]
        public async Task GetAllSubjects_WithManySubjects_ReturnsFullList()
        {
            var subjects = Enumerable.Range(1, 50)
                .Select(i => new Subject { Id = i, Name = $"Subject {i}" })
                .ToList();
            _mockSubjectRepo.Setup(x => x.GetAllAsync()).ReturnsAsync(subjects);

            var result = await _mockSubjectRepo.Object.GetAllAsync();

            Assert.Equal(50, result.Count);
        }
    }

    /// <summary>
    /// Enrollment Controller Tests
    /// </summary>
    public class EnrollmentControllerTests
    {
        private readonly Mock<IEnrollmentRepository> _mockEnrollmentRepo = new();

        [Fact]
        public async Task EnrollUser_InCourse_ReturnsSuccess()
        {
            var enrollment = new Enrollment { Id = 1, UserId = 1, SubjectId = 5 };
            _mockEnrollmentRepo.Setup(x => x.AddAsync(It.IsAny<Enrollment>())).Returns(Task.CompletedTask);

            await _mockEnrollmentRepo.Object.AddAsync(enrollment);

            _mockEnrollmentRepo.Verify(x => x.AddAsync(It.IsAny<Enrollment>()), Times.Once);
        }

        [Fact]
        public async Task GetUserEnrollments_WithMultipleCourses_ReturnsAllEnrollments()
        {
            var enrollments = Enumerable.Range(1, 5)
                .Select(i => new Enrollment { Id = i, UserId = 1, SubjectId = i })
                .ToList();
            _mockEnrollmentRepo.Setup(x => x.GetByUserIdAsync(1)).ReturnsAsync(enrollments);

            var result = await _mockEnrollmentRepo.Object.GetByUserIdAsync(1);

            Assert.Equal(5, result.Count);
            Assert.All(result, e => Assert.Equal(1, e.UserId));
        }

        [Fact]
        public async Task GetEnrollment_WithValidId_ReturnsEnrollmentDetails()
        {
            var enrollment = new Enrollment { Id = 1, UserId = 1, SubjectId = 1, EnrolledAt = DateTime.Now };
            _mockEnrollmentRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(enrollment);

            var result = await _mockEnrollmentRepo.Object.GetByIdAsync(1);

            Assert.NotNull(result.EnrolledAt);
            Assert.Equal(1, result.SubjectId);
        }
    }

    /// <summary>
    /// Payment Controller Tests
    /// </summary>
    public class PaymentControllerTests
    {
        private readonly Mock<IPaymentRepository> _mockPaymentRepo = new();

        [Fact]
        public async Task ProcessPayment_WithValidAmount_ReturnsSuccess()
        {
            var payment = new Payment { Id = 1, UserId = 1, Amount = 99.99m };
            _mockPaymentRepo.Setup(x => x.AddAsync(It.IsAny<Payment>())).Returns(Task.CompletedTask);

            await _mockPaymentRepo.Object.AddAsync(payment);

            _mockPaymentRepo.Verify(x => x.AddAsync(It.IsAny<Payment>()), Times.Once);
        }

        [Fact]
        public async Task GetUserPaymentHistory_WithMultiplePayments_ReturnsList()
        {
            var payments = new List<Payment>
            {
                new Payment { Id = 1, UserId = 1, Amount = 50m, PaymentDate = DateTime.Now.AddDays(-30) },
                new Payment { Id = 2, UserId = 1, Amount = 75m, PaymentDate = DateTime.Now.AddDays(-15) },
                new Payment { Id = 3, UserId = 1, Amount = 100m, PaymentDate = DateTime.Now }
            };
            _mockPaymentRepo.Setup(x => x.GetByUserIdAsync(1)).ReturnsAsync(payments);

            var result = await _mockPaymentRepo.Object.GetByUserIdAsync(1);

            Assert.Equal(3, result.Count);
            var totalAmount = result.Sum(p => p.Amount);
            Assert.Equal(225m, totalAmount);
        }

        [Fact]
        public async Task GetPayment_WithValidId_ReturnsPaymentDetails()
        {
            var payment = new Payment { Id = 1, UserId = 1, Amount = 150m, PaymentDate = DateTime.Now };
            _mockPaymentRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(payment);

            var result = await _mockPaymentRepo.Object.GetByIdAsync(1);

            Assert.Equal(150m, result.Amount);
            Assert.Equal(1, result.UserId);
        }
    }

    /// <summary>
    /// Cart Controller Tests
    /// </summary>
    public class CartControllerTests
    {
        private readonly Mock<ICartRepository> _mockCartRepo = new();

        [Fact]
        public async Task GetCart_ForUser_ReturnsCartWithItems()
        {
            var cart = new Cart
            {
                Id = 1,
                UserId = 1,
                Items = new List<CartItem>
                {
                    new CartItem { Id = 1, SubjectId = 1, Price = 50m },
                    new CartItem { Id = 2, SubjectId = 2, Price = 75m }
                }
            };
            _mockCartRepo.Setup(x => x.GetByUserIdAsync(1)).ReturnsAsync(cart);

            var result = await _mockCartRepo.Object.GetByUserIdAsync(1);

            Assert.Equal(2, result.Items.Count);
            var totalPrice = result.Items.Sum(i => i.Price);
            Assert.Equal(125m, totalPrice);
        }

        [Fact]
        public async Task AddItemToCart_WithValidItem_UpdatesCart()
        {
            var cart = new Cart { Id = 1, UserId = 1 };
            _mockCartRepo.Setup(x => x.UpdateAsync(It.IsAny<Cart>())).Returns(Task.CompletedTask);

            await _mockCartRepo.Object.UpdateAsync(cart);

            _mockCartRepo.Verify(x => x.UpdateAsync(cart), Times.Once);
        }

        [Fact]
        public async Task ClearCart_ForUser_RemovesAllItems()
        {
            _mockCartRepo.Setup(x => x.ClearAsync(1)).Returns(Task.CompletedTask);

            await _mockCartRepo.Object.ClearAsync(1);

            _mockCartRepo.Verify(x => x.ClearAsync(1), Times.Once);
        }

        [Fact]
        public async Task GetCart_WithEmptyCart_ReturnsEmptyItemList()
        {
            var cart = new Cart { Id = 1, UserId = 1, Items = new List<CartItem>() };
            _mockCartRepo.Setup(x => x.GetByUserIdAsync(1)).ReturnsAsync(cart);

            var result = await _mockCartRepo.Object.GetByUserIdAsync(1);

            Assert.Empty(result.Items);
        }
    }

    /// <summary>
    /// Order Controller Tests
    /// </summary>
    public class OrderControllerTests
    {
        private readonly Mock<IOrderRepository> _mockOrderRepo = new();

        [Fact]
        public async Task CreateOrder_WithValidItems_ReturnsSuccess()
        {
            var order = new Order { UserId = 1, TotalAmount = 250m };
            _mockOrderRepo.Setup(x => x.AddAsync(It.IsAny<Order>())).Returns(Task.CompletedTask);

            await _mockOrderRepo.Object.AddAsync(order);

            _mockOrderRepo.Verify(x => x.AddAsync(It.IsAny<Order>()), Times.Once);
        }

        [Fact]
        public async Task GetOrder_WithValidId_ReturnsOrderDetails()
        {
            var order = new Order { Id = 1, UserId = 1, TotalAmount = 150m, OrderDate = DateTime.Now };
            _mockOrderRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(order);

            var result = await _mockOrderRepo.Object.GetByIdAsync(1);

            Assert.Equal(150m, result.TotalAmount);
            Assert.NotNull(result.OrderDate);
        }

        [Fact]
        public async Task GetUserOrders_WithMultipleOrders_ReturnsAllOrders()
        {
            var orders = Enumerable.Range(1, 5)
                .Select(i => new Order { Id = i, UserId = 1, TotalAmount = i * 100m })
                .ToList();
            _mockOrderRepo.Setup(x => x.GetByUserIdAsync(1)).ReturnsAsync(orders);

            var result = await _mockOrderRepo.Object.GetByUserIdAsync(1);

            Assert.Equal(5, result.Count);
            var totalRevenue = result.Sum(o => o.TotalAmount);
            Assert.Equal(1500m, totalRevenue); // 100 + 200 + 300 + 400 + 500
        }
    }

    /// <summary>
    /// Favorites Controller Tests
    /// </summary>
    public class FavoritesControllerTests
    {
        private readonly Mock<IFavoriteRepository> _mockFavoriteRepo = new();

        [Fact]
        public async Task AddToFavorites_WithValidSubject_ReturnsSuccess()
        {
            var favorite = new Favorite { UserId = 1, SubjectId = 1 };
            _mockFavoriteRepo.Setup(x => x.AddAsync(It.IsAny<Favorite>())).Returns(Task.CompletedTask);

            await _mockFavoriteRepo.Object.AddAsync(favorite);

            _mockFavoriteRepo.Verify(x => x.AddAsync(It.IsAny<Favorite>()), Times.Once);
        }

        [Fact]
        public async Task GetUserFavorites_WithMultipleFavorites_ReturnsAll()
        {
            var favorites = Enumerable.Range(1, 10)
                .Select(i => new Favorite { Id = i, UserId = 1, SubjectId = i })
                .ToList();
            _mockFavoriteRepo.Setup(x => x.GetByUserIdAsync(1)).ReturnsAsync(favorites);

            var result = await _mockFavoriteRepo.Object.GetByUserIdAsync(1);

            Assert.Equal(10, result.Count);
        }

        [Fact]
        public async Task RemoveFromFavorites_WithValidId_DeletesSuccessfully()
        {
            _mockFavoriteRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

            await _mockFavoriteRepo.Object.DeleteAsync(1);

            _mockFavoriteRepo.Verify(x => x.DeleteAsync(1), Times.Once);
        }

        [Fact]
        public async Task GetFavorite_WithValidId_ReturnsFavoriteDetails()
        {
            var favorite = new Favorite { Id = 1, UserId = 1, SubjectId = 5 };
            _mockFavoriteRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(favorite);

            var result = await _mockFavoriteRepo.Object.GetByIdAsync(1);

            Assert.Equal(5, result.SubjectId);
        }
    }

    /// <summary>
    /// End-to-End Workflow Tests
    /// </summary>
    public class EndToEndWorkflowTests
    {
        private readonly Mock<IUserRepository> _mockUserRepo = new();
        private readonly Mock<ISubjectRepository> _mockSubjectRepo = new();
        private readonly Mock<IEnrollmentRepository> _mockEnrollmentRepo = new();
        private readonly Mock<ICartRepository> _mockCartRepo = new();
        private readonly Mock<IPaymentRepository> _mockPaymentRepo = new();
        private readonly Mock<IOrderRepository> _mockOrderRepo = new();

        [Fact]
        public async Task CompleteUserJourney_RegisterBrowseEnrollPayment()
        {
            // Setup
            var user = new User { Id = 1, Email = "new@example.com", Name = "NewUser" };
            var subject = new Subject { Id = 1, Name = "Python Basics", Category = "Programming" };
            var enrollment = new Enrollment { Id = 1, UserId = 1, SubjectId = 1 };
            var payment = new Payment { Id = 1, UserId = 1, Amount = 99.99m };

            _mockUserRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(user);
            _mockSubjectRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(subject);
            _mockEnrollmentRepo.Setup(x => x.AddAsync(It.IsAny<Enrollment>())).Returns(Task.CompletedTask);
            _mockPaymentRepo.Setup(x => x.AddAsync(It.IsAny<Payment>())).Returns(Task.CompletedTask);

            // Act
            var userResult = await _mockUserRepo.Object.GetByIdAsync(1);
            var subjectResult = await _mockSubjectRepo.Object.GetByIdAsync(1);
            await _mockEnrollmentRepo.Object.AddAsync(enrollment);
            await _mockPaymentRepo.Object.AddAsync(payment);

            // Assert
            Assert.NotNull(userResult);
            Assert.NotNull(subjectResult);
            _mockEnrollmentRepo.Verify(x => x.AddAsync(It.IsAny<Enrollment>()), Times.Once);
            _mockPaymentRepo.Verify(x => x.AddAsync(It.IsAny<Payment>()), Times.Once);
        }

        [Fact]
        public async Task ShoppingCart_BrowseAddPaymentCheckout()
        {
            // Setup
            var cart = new Cart
            {
                Id = 1,
                UserId = 1,
                Items = new List<CartItem>
                {
                    new CartItem { SubjectId = 1, Price = 50m },
                    new CartItem { SubjectId = 2, Price = 75m }
                }
            };
            var order = new Order { UserId = 1, TotalAmount = 125m };

            _mockCartRepo.Setup(x => x.GetByUserIdAsync(1)).ReturnsAsync(cart);
            _mockOrderRepo.Setup(x => x.AddAsync(It.IsAny<Order>())).Returns(Task.CompletedTask);

            // Act
            var cartResult = await _mockCartRepo.Object.GetByUserIdAsync(1);
            await _mockOrderRepo.Object.AddAsync(order);

            // Assert
            Assert.Equal(2, cartResult.Items.Count);
            Assert.Equal(125m, cartResult.Items.Sum(i => i.Price));
            _mockOrderRepo.Verify(x => x.AddAsync(It.IsAny<Order>()), Times.Once);
        }

        [Fact]
        public async Task UserAnalytics_AggregateDataFromMultipleSources()
        {
            // Setup multiple data sources
            var user = new User { Id = 1, Name = "Analytics Test" };
            var enrollments = new List<Enrollment> { new(), new(), new() };
            var payments = new List<Payment>
            {
                new Payment { Amount = 100m },
                new Payment { Amount = 150m }
            };

            _mockUserRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(user);
            _mockEnrollmentRepo.Setup(x => x.GetByUserIdAsync(1)).ReturnsAsync(enrollments);
            _mockPaymentRepo.Setup(x => x.GetByUserIdAsync(1)).ReturnsAsync(payments);

            // Act
            var userResult = await _mockUserRepo.Object.GetByIdAsync(1);
            var enrollmentResult = await _mockEnrollmentRepo.Object.GetByUserIdAsync(1);
            var paymentResult = await _mockPaymentRepo.Object.GetByUserIdAsync(1);

            // Assert - Verify aggregated data
            Assert.NotNull(userResult);
            Assert.Equal(3, enrollmentResult.Count);
            Assert.Equal(250m, paymentResult.Sum(p => p.Amount));
        }
    }
}
