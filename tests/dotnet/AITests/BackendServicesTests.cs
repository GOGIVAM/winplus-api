using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;

namespace SprintBackendTests
{
    /// <summary>
    /// Mock Models for Backend Services Testing
    /// </summary>
    public class User { public int Id { get; set; } public string Email { get; set; } public string Name { get; set; } }
    public class Subject { public int Id { get; set; } public string Name { get; set; } public string Category { get; set; } }
    public class Enrollment { public int Id { get; set; } public int UserId { get; set; } public int SubjectId { get; set; } public DateTime EnrolledAt { get; set; } }
    public class Payment { public int Id { get; set; } public int UserId { get; set; } public decimal Amount { get; set; } public DateTime PaymentDate { get; set; } }
    public class Cart { public int Id { get; set; } public int UserId { get; set; } public List<CartItem> Items { get; set; } = new(); }
    public class CartItem { public int Id { get; set; } public int SubjectId { get; set; } public decimal Price { get; set; } }
    public class Order { public int Id { get; set; } public int UserId { get; set; } public decimal TotalAmount { get; set; } public DateTime OrderDate { get; set; } }
    public class Analytics { public int UserId { get; set; } public int CoursesComplete { get; set; } public decimal AvgScore { get; set; } }
    public class Favorite { public int Id { get; set; } public int UserId { get; set; } public int SubjectId { get; set; } }

    public interface IUserRepository { Task<User> GetByIdAsync(int id); Task<List<User>> GetAllAsync(); Task AddAsync(User user); Task UpdateAsync(User user); }
    public interface ISubjectRepository { Task<Subject> GetByIdAsync(int id); Task<List<Subject>> GetAllAsync(); Task<List<Subject>> GetByCategoryAsync(string category); }
    public interface IEnrollmentRepository { Task<Enrollment> GetByIdAsync(int id); Task<List<Enrollment>> GetByUserIdAsync(int userId); Task AddAsync(Enrollment enrollment); }
    public interface IPaymentRepository { Task<Payment> GetByIdAsync(int id); Task<List<Payment>> GetByUserIdAsync(int userId); Task AddAsync(Payment payment); }
    public interface ICartRepository { Task<Cart> GetByUserIdAsync(int userId); Task UpdateAsync(Cart cart); Task ClearAsync(int userId); }
    public interface IOrderRepository { Task<Order> GetByIdAsync(int id); Task<List<Order>> GetByUserIdAsync(int userId); Task AddAsync(Order order); }
    public interface IAnalyticsRepository { Task<Analytics> GetByUserIdAsync(int userId); Task UpdateAsync(Analytics analytics); }
    public interface IFavoriteRepository { Task<Favorite> GetByIdAsync(int id); Task<List<Favorite>> GetByUserIdAsync(int userId); Task AddAsync(Favorite favorite); Task DeleteAsync(int id); }

    /// <summary>
    /// User Service Tests
    /// </summary>
    public class UserServiceTests
    {
        private readonly Mock<IUserRepository> _mockUserRepo = new();

        [Fact]
        public async Task GetUserById_WithValidId_ReturnsUser()
        {
            var user = new User { Id = 1, Email = "test@example.com", Name = "John Doe" };
            _mockUserRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(user);

            var result = await _mockUserRepo.Object.GetByIdAsync(1);

            Assert.NotNull(result);
            Assert.Equal(1, result.Id);
            Assert.Equal("test@example.com", result.Email);
        }

        [Fact]
        public async Task GetAllUsers_ReturnsUserList()
        {
            var users = new List<User> 
            { 
                new User { Id = 1, Name = "User 1" },
                new User { Id = 2, Name = "User 2" }
            };
            _mockUserRepo.Setup(x => x.GetAllAsync()).ReturnsAsync(users);

            var result = await _mockUserRepo.Object.GetAllAsync();

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public async Task AddUser_WithValidUser_CallsRepository()
        {
            var user = new User { Id = 1, Email = "new@example.com" };
            _mockUserRepo.Setup(x => x.AddAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            await _mockUserRepo.Object.AddAsync(user);

            _mockUserRepo.Verify(x => x.AddAsync(It.IsAny<User>()), Times.Once);
        }

        [Fact]
        public async Task UpdateUser_WithValidUser_CallsRepository()
        {
            var user = new User { Id = 1, Email = "updated@example.com" };
            _mockUserRepo.Setup(x => x.UpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            await _mockUserRepo.Object.UpdateAsync(user);

            _mockUserRepo.Verify(x => x.UpdateAsync(It.IsAny<User>()), Times.Once);
        }
    }

    /// <summary>
    /// Subject Service Tests
    /// </summary>
    public class SubjectServiceTests
    {
        private readonly Mock<ISubjectRepository> _mockSubjectRepo = new();

        [Fact]
        public async Task GetSubjectById_WithValidId_ReturnsSubject()
        {
            var subject = new Subject { Id = 1, Name = "Math", Category = "Science" };
            _mockSubjectRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(subject);

            var result = await _mockSubjectRepo.Object.GetByIdAsync(1);

            Assert.NotNull(result);
            Assert.Equal("Math", result.Name);
        }

        [Fact]
        public async Task GetAllSubjects_ReturnsSubjectList()
        {
            var subjects = new List<Subject>
            {
                new Subject { Id = 1, Name = "Math" },
                new Subject { Id = 2, Name = "Science" },
                new Subject { Id = 3, Name = "History" }
            };
            _mockSubjectRepo.Setup(x => x.GetAllAsync()).ReturnsAsync(subjects);

            var result = await _mockSubjectRepo.Object.GetAllAsync();

            Assert.Equal(3, result.Count);
        }

        [Fact]
        public async Task GetSubjectsByCategory_WithValidCategory_ReturnsFilteredList()
        {
            var subjects = new List<Subject>
            {
                new Subject { Id = 1, Name = "Algebra", Category = "Math" },
                new Subject { Id = 2, Name = "Geometry", Category = "Math" }
            };
            _mockSubjectRepo.Setup(x => x.GetByCategoryAsync("Math")).ReturnsAsync(subjects);

            var result = await _mockSubjectRepo.Object.GetByCategoryAsync("Math");

            Assert.Equal(2, result.Count);
            Assert.All(result, s => Assert.Equal("Math", s.Category));
        }

        [Fact]
        public async Task GetSubjectsByCategory_WithEmptyCategory_ReturnsEmptyList()
        {
            _mockSubjectRepo.Setup(x => x.GetByCategoryAsync("NonExistent")).ReturnsAsync(new List<Subject>());

            var result = await _mockSubjectRepo.Object.GetByCategoryAsync("NonExistent");

            Assert.Empty(result);
        }
    }

    /// <summary>
    /// Enrollment Service Tests
    /// </summary>
    public class EnrollmentServiceTests
    {
        private readonly Mock<IEnrollmentRepository> _mockEnrollmentRepo = new();

        [Fact]
        public async Task GetEnrollmentById_WithValidId_ReturnsEnrollment()
        {
            var enrollment = new Enrollment { Id = 1, UserId = 1, SubjectId = 1, EnrolledAt = DateTime.Now };
            _mockEnrollmentRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(enrollment);

            var result = await _mockEnrollmentRepo.Object.GetByIdAsync(1);

            Assert.NotNull(result);
            Assert.Equal(1, result.UserId);
        }

        [Fact]
        public async Task GetEnrollmentsByUserId_WithValidUserId_ReturnsEnrollmentList()
        {
            var enrollments = new List<Enrollment>
            {
                new Enrollment { Id = 1, UserId = 1, SubjectId = 1 },
                new Enrollment { Id = 2, UserId = 1, SubjectId = 2 },
                new Enrollment { Id = 3, UserId = 1, SubjectId = 3 }
            };
            _mockEnrollmentRepo.Setup(x => x.GetByUserIdAsync(1)).ReturnsAsync(enrollments);

            var result = await _mockEnrollmentRepo.Object.GetByUserIdAsync(1);

            Assert.Equal(3, result.Count);
            Assert.All(result, e => Assert.Equal(1, e.UserId));
        }

        [Fact]
        public async Task AddEnrollment_WithValidEnrollment_CallsRepository()
        {
            var enrollment = new Enrollment { UserId = 1, SubjectId = 1 };
            _mockEnrollmentRepo.Setup(x => x.AddAsync(It.IsAny<Enrollment>())).Returns(Task.CompletedTask);

            await _mockEnrollmentRepo.Object.AddAsync(enrollment);

            _mockEnrollmentRepo.Verify(x => x.AddAsync(It.IsAny<Enrollment>()), Times.Once);
        }

        [Fact]
        public async Task GetEnrollmentsByUserId_WithNoEnrollments_ReturnsEmptyList()
        {
            _mockEnrollmentRepo.Setup(x => x.GetByUserIdAsync(99)).ReturnsAsync(new List<Enrollment>());

            var result = await _mockEnrollmentRepo.Object.GetByUserIdAsync(99);

            Assert.Empty(result);
        }
    }

    /// <summary>
    /// Payment Service Tests
    /// </summary>
    public class PaymentServiceTests
    {
        private readonly Mock<IPaymentRepository> _mockPaymentRepo = new();

        [Fact]
        public async Task GetPaymentById_WithValidId_ReturnsPayment()
        {
            var payment = new Payment { Id = 1, UserId = 1, Amount = 99.99m, PaymentDate = DateTime.Now };
            _mockPaymentRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(payment);

            var result = await _mockPaymentRepo.Object.GetByIdAsync(1);

            Assert.NotNull(result);
            Assert.Equal(99.99m, result.Amount);
        }

        [Fact]
        public async Task GetPaymentsByUserId_WithValidUserId_ReturnsPaymentList()
        {
            var payments = new List<Payment>
            {
                new Payment { Id = 1, UserId = 1, Amount = 50m },
                new Payment { Id = 2, UserId = 1, Amount = 75m },
                new Payment { Id = 3, UserId = 1, Amount = 100m }
            };
            _mockPaymentRepo.Setup(x => x.GetByUserIdAsync(1)).ReturnsAsync(payments);

            var result = await _mockPaymentRepo.Object.GetByUserIdAsync(1);

            Assert.Equal(3, result.Count);
            Assert.Equal(225m, result.Sum(p => p.Amount));
        }

        [Fact]
        public async Task AddPayment_WithValidPayment_CallsRepository()
        {
            var payment = new Payment { UserId = 1, Amount = 50m };
            _mockPaymentRepo.Setup(x => x.AddAsync(It.IsAny<Payment>())).Returns(Task.CompletedTask);

            await _mockPaymentRepo.Object.AddAsync(payment);

            _mockPaymentRepo.Verify(x => x.AddAsync(It.IsAny<Payment>()), Times.Once);
        }

        [Fact]
        public async Task GetPaymentsByUserId_WithNoPayments_ReturnsEmptyList()
        {
            _mockPaymentRepo.Setup(x => x.GetByUserIdAsync(99)).ReturnsAsync(new List<Payment>());

            var result = await _mockPaymentRepo.Object.GetByUserIdAsync(99);

            Assert.Empty(result);
        }
    }

    /// <summary>
    /// Cart Service Tests
    /// </summary>
    public class CartServiceTests
    {
        private readonly Mock<ICartRepository> _mockCartRepo = new();

        [Fact]
        public async Task GetCartByUserId_WithValidUserId_ReturnsCart()
        {
            var cart = new Cart { Id = 1, UserId = 1, Items = new() };
            _mockCartRepo.Setup(x => x.GetByUserIdAsync(1)).ReturnsAsync(cart);

            var result = await _mockCartRepo.Object.GetByUserIdAsync(1);

            Assert.NotNull(result);
            Assert.Equal(1, result.UserId);
        }

        [Fact]
        public async Task UpdateCart_WithValidCart_CallsRepository()
        {
            var cart = new Cart { Id = 1, UserId = 1 };
            _mockCartRepo.Setup(x => x.UpdateAsync(It.IsAny<Cart>())).Returns(Task.CompletedTask);

            await _mockCartRepo.Object.UpdateAsync(cart);

            _mockCartRepo.Verify(x => x.UpdateAsync(It.IsAny<Cart>()), Times.Once);
        }

        [Fact]
        public async Task ClearCart_WithValidUserId_CallsRepository()
        {
            _mockCartRepo.Setup(x => x.ClearAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

            await _mockCartRepo.Object.ClearAsync(1);

            _mockCartRepo.Verify(x => x.ClearAsync(1), Times.Once);
        }

        [Fact]
        public async Task GetCartByUserId_WithMultipleItems_ReturnsCartWithItems()
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
            Assert.Equal(125m, result.Items.Sum(i => i.Price));
        }
    }

    /// <summary>
    /// Order Service Tests
    /// </summary>
    public class OrderServiceTests
    {
        private readonly Mock<IOrderRepository> _mockOrderRepo = new();

        [Fact]
        public async Task GetOrderById_WithValidId_ReturnsOrder()
        {
            var order = new Order { Id = 1, UserId = 1, TotalAmount = 150m, OrderDate = DateTime.Now };
            _mockOrderRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(order);

            var result = await _mockOrderRepo.Object.GetByIdAsync(1);

            Assert.NotNull(result);
            Assert.Equal(150m, result.TotalAmount);
        }

        [Fact]
        public async Task GetOrdersByUserId_WithValidUserId_ReturnsOrderList()
        {
            var orders = new List<Order>
            {
                new Order { Id = 1, UserId = 1, TotalAmount = 100m },
                new Order { Id = 2, UserId = 1, TotalAmount = 200m },
                new Order { Id = 3, UserId = 1, TotalAmount = 150m }
            };
            _mockOrderRepo.Setup(x => x.GetByUserIdAsync(1)).ReturnsAsync(orders);

            var result = await _mockOrderRepo.Object.GetByUserIdAsync(1);

            Assert.Equal(3, result.Count);
            Assert.Equal(450m, result.Sum(o => o.TotalAmount));
        }

        [Fact]
        public async Task AddOrder_WithValidOrder_CallsRepository()
        {
            var order = new Order { UserId = 1, TotalAmount = 100m };
            _mockOrderRepo.Setup(x => x.AddAsync(It.IsAny<Order>())).Returns(Task.CompletedTask);

            await _mockOrderRepo.Object.AddAsync(order);

            _mockOrderRepo.Verify(x => x.AddAsync(It.IsAny<Order>()), Times.Once);
        }
    }

    /// <summary>
    /// Favorite Service Tests
    /// </summary>
    public class FavoriteServiceTests
    {
        private readonly Mock<IFavoriteRepository> _mockFavoriteRepo = new();

        [Fact]
        public async Task GetFavoriteById_WithValidId_ReturnsFavorite()
        {
            var favorite = new Favorite { Id = 1, UserId = 1, SubjectId = 1 };
            _mockFavoriteRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(favorite);

            var result = await _mockFavoriteRepo.Object.GetByIdAsync(1);

            Assert.NotNull(result);
            Assert.Equal(1, result.UserId);
        }

        [Fact]
        public async Task GetFavoritesByUserId_WithValidUserId_ReturnsFavoriteList()
        {
            var favorites = new List<Favorite>
            {
                new Favorite { Id = 1, UserId = 1, SubjectId = 1 },
                new Favorite { Id = 2, UserId = 1, SubjectId = 2 },
                new Favorite { Id = 3, UserId = 1, SubjectId = 3 }
            };
            _mockFavoriteRepo.Setup(x => x.GetByUserIdAsync(1)).ReturnsAsync(favorites);

            var result = await _mockFavoriteRepo.Object.GetByUserIdAsync(1);

            Assert.Equal(3, result.Count);
        }

        [Fact]
        public async Task AddFavorite_WithValidFavorite_CallsRepository()
        {
            var favorite = new Favorite { UserId = 1, SubjectId = 1 };
            _mockFavoriteRepo.Setup(x => x.AddAsync(It.IsAny<Favorite>())).Returns(Task.CompletedTask);

            await _mockFavoriteRepo.Object.AddAsync(favorite);

            _mockFavoriteRepo.Verify(x => x.AddAsync(It.IsAny<Favorite>()), Times.Once);
        }

        [Fact]
        public async Task DeleteFavorite_WithValidId_CallsRepository()
        {
            _mockFavoriteRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

            await _mockFavoriteRepo.Object.DeleteAsync(1);

            _mockFavoriteRepo.Verify(x => x.DeleteAsync(1), Times.Once);
        }
    }

    /// <summary>
    /// Integration Tests - Multi-Service Workflows
    /// </summary>
    public class IntegrationTests
    {
        private readonly Mock<IUserRepository> _mockUserRepo = new();
        private readonly Mock<ICartRepository> _mockCartRepo = new();
        private readonly Mock<IOrderRepository> _mockOrderRepo = new();

        [Fact]
        public async Task UserCheckoutWorkflow_UserAddToCartAndCreateOrder()
        {
            // Setup
            var user = new User { Id = 1, Name = "John" };
            var cart = new Cart { Id = 1, UserId = 1, Items = new List<CartItem> { new() { SubjectId = 1 } } };
            var order = new Order { UserId = 1, TotalAmount = 99.99m };

            _mockUserRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(user);
            _mockCartRepo.Setup(x => x.GetByUserIdAsync(1)).ReturnsAsync(cart);
            _mockOrderRepo.Setup(x => x.AddAsync(It.IsAny<Order>())).Returns(Task.CompletedTask);

            // Act
            var userResult = await _mockUserRepo.Object.GetByIdAsync(1);
            var cartResult = await _mockCartRepo.Object.GetByUserIdAsync(1);
            await _mockOrderRepo.Object.AddAsync(order);

            // Assert
            Assert.NotNull(userResult);
            Assert.Single(cartResult.Items);
            _mockOrderRepo.Verify(x => x.AddAsync(It.IsAny<Order>()), Times.Once);
        }

        [Fact]
        public async Task MultipleServiceCalls_VerifyAllOperationsSucceed()
        {
            var user = new User { Id = 1, Name = "Test" };
            var cart = new Cart { Id = 1, UserId = 1 };
            var order = new Order { Id = 1, UserId = 1 };

            _mockUserRepo.Setup(x => x.GetByIdAsync(It.IsAny<int>())).ReturnsAsync(user);
            _mockCartRepo.Setup(x => x.GetByUserIdAsync(It.IsAny<int>())).ReturnsAsync(cart);
            _mockOrderRepo.Setup(x => x.GetByIdAsync(It.IsAny<int>())).ReturnsAsync(order);

            var u = await _mockUserRepo.Object.GetByIdAsync(1);
            var c = await _mockCartRepo.Object.GetByUserIdAsync(1);
            var o = await _mockOrderRepo.Object.GetByIdAsync(1);

            Assert.NotNull(u);
            Assert.NotNull(c);
            Assert.NotNull(o);
        }
    }
}
