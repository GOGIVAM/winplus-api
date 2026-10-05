using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>Lot 2, Module 4 : supervision des retraits.</summary>
    public class AdminWithdrawalsTests
    {
        private static readonly InMemoryDatabaseRoot Root = new();
        private readonly string _dbName = $"adminwd-{Guid.NewGuid()}";
        private const int ProfA = 9401, ProfDeleted = 9402, Admin = 9499;

        public AdminWithdrawalsTests()
        {
            using var db = Fresh();
            db.Users.AddRange(
                new User { Id = ProfA, Email = "awa.mbarga@test.local", FirstName = "Awa", LastName = "Mbarga", Role = "teacher" },
                new User { Id = ProfDeleted, Email = "parti@test.local", FirstName = "Paul", Role = "teacher", IsDeleted = true },
                new User { Id = Admin, Email = "adm4@test.local", Role = "admin" });
            db.Withdrawals.AddRange(
                new Withdrawal { Id = 1, UserId = ProfA, Phone = "237677123456", AmountXaf = 1000, Status = "completed", TransferReference = "WDR-1", RequestedAt = DateTime.UtcNow.AddDays(-3) },
                new Withdrawal { Id = 2, UserId = ProfA, Phone = "237677123456", AmountXaf = 2000, Status = "failed", TransferReference = "WDR-2", FailureReason = "Numéro invalide", RequestedAt = DateTime.UtcNow.AddDays(-2) },
                new Withdrawal { Id = 3, UserId = ProfA, Phone = "237677123456", AmountXaf = 3000, Status = "processing", TransferReference = "WDR-3", RequestedAt = DateTime.UtcNow.AddDays(-1) },
                new Withdrawal { Id = 4, UserId = ProfDeleted, Phone = "237699000000", AmountXaf = 4000, Status = "completed", RequestedAt = DateTime.UtcNow.AddDays(-10) });
            db.SaveChanges();
        }

        private TestApplicationDbContext Fresh() => TestApplicationDbContext.Create(_dbName, Root);

        private AdminWithdrawalsController Controller(ApplicationDbContext db)
        {
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            var service = new WithdrawalService(db, wallet, new Mock<INotchPayService>().Object, new Mock<INtfyService>().Object, NullLogger<WithdrawalService>.Instance);
            return new AdminWithdrawalsController(db, service, NullLogger<AdminWithdrawalsController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", Admin.ToString()) }, "test")) },
                },
            };
        }

        private static JsonElement Data(IActionResult result)
        {
            var ok = Assert.IsType<OkObjectResult>(result);
            return JsonDocument.Parse(JsonSerializer.Serialize(ok.Value)).RootElement.GetProperty("data");
        }

        [Fact]
        public void ListAndRescueEndpoints_AreRestrictedToAdminsServerSide()
        {
            var policy = typeof(AdminWithdrawalsController).GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(policy);
            Assert.Equal("AdminOnly", policy!.Policy);
            Assert.DoesNotContain(typeof(AdminWithdrawalsController).GetMethods(), m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null);
            Assert.Equal("AdminOnly", typeof(AdminWalletController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        }

        [Fact]
        public async Task List_ShowsAllStatuses_IncludingDeletedTeachers_WithMaskedPhones()
        {
            using var db = Fresh();
            var data = Data(await Controller(db).List());

            Assert.Equal(4, data.GetProperty("total").GetInt32());
            var items = data.GetProperty("items").EnumerateArray().ToList();
            Assert.Contains(items, i => i.GetProperty("teacher").GetProperty("isDeleted").GetBoolean());
            foreach (var item in items)
            {
                var phone = item.GetProperty("withdrawal").GetProperty("phoneMasked").GetString()!;
                Assert.DoesNotContain("677123", phone);
                Assert.DoesNotContain("699000", phone);
            }
            // Plus récente d'abord par défaut.
            Assert.Equal(3, items[0].GetProperty("withdrawal").GetProperty("id").GetInt32());
        }

        [Fact]
        public async Task FailedFilter_IsolatesFailures_AndOnlyThemAreRescuable()
        {
            using var db = Fresh();
            var data = Data(await Controller(db).List(status: "failed"));
            var items = data.GetProperty("items").EnumerateArray().ToList();

            Assert.Single(items);
            Assert.Equal(2, items[0].GetProperty("withdrawal").GetProperty("id").GetInt32());
            Assert.True(items[0].GetProperty("canRescue").GetBoolean());

            var all = Data(await Controller(db).List()).GetProperty("items").EnumerateArray();
            Assert.All(all.Where(i => i.GetProperty("withdrawal").GetProperty("status").GetString() != "failed"),
                i => Assert.False(i.GetProperty("canRescue").GetBoolean()));
        }

        [Fact]
        public async Task SearchByTeacherName_AndAscendingSort()
        {
            using var db = Fresh();
            var data = Data(await Controller(db).List(search: "mbarga", sort: "asc"));
            var ids = data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("withdrawal").GetProperty("id").GetInt32()).ToList();
            Assert.Equal(new[] { 1, 2, 3 }, ids);
        }

        [Fact]
        public async Task RescueOnInFlightWithdrawal_IsRefusedWith409()
        {
            using var db = Fresh();
            var result = await Controller(db).Close(3, new AdminWithdrawalsController.RescueRequest("Test"));
            Assert.Equal(409, Assert.IsType<ObjectResult>(result).StatusCode);
        }
    }
}
