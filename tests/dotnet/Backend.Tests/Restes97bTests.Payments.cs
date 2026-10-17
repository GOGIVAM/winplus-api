using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Services.Entitlements;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace Backend.Tests
{
    /// <summary>Restes 9.7b, point 5. historique réel des paiements du parent.</summary>
    public partial class Restes97bTests
    {
        // ── 5. Historique réel des paiements du parent ───────────────────────

        [Fact]
        public async Task ParentPayments_MergesOrdersAndWallet_WithoutDuplicates()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var subject = new Subject { Title = "BAC C Physique 2024", Price = 3000 };
            db.Subjects.Add(subject);
            db.SaveChanges();
            var paid = new Order { OrderNumber = "ORD-1", UserId = Parent, TotalAmount = 3000, Status = "Completed", PaymentMethod = "mobile_money", CreatedAt = DateTime.UtcNow.AddMinutes(-3), CompletedDate = DateTime.UtcNow.AddMinutes(-3) };
            paid.Items.Add(new OrderItem { SubjectId = subject.Id, PriceAtPurchase = 3000 });
            var balance = new Order { OrderNumber = "ORD-2", UserId = Parent, TotalAmount = 1500, Status = "Completed", PaymentMethod = "balance", CreatedAt = DateTime.UtcNow.AddMinutes(-2), CompletedDate = DateTime.UtcNow.AddMinutes(-2) };
            var other = new Order { OrderNumber = "ORD-3", UserId = Student, TotalAmount = 9999, Status = "Completed" };
            db.Orders.AddRange(paid, balance, other);
            db.SaveChanges();
            db.Payments.Add(new Payment { OrderId = paid.Id, UserId = Parent, Amount = 3000, Status = "completed", Operator = "mtn" });
            db.SaveChanges();

            var wallet = new Mock<IWalletService>();
            wallet.Setup(w => w.GetHistoryAsync(Parent, "all", 1, It.IsAny<int>())).ReturnsAsync((new List<WalletHistoryItem>
            {
                new(1, DateTime.UtcNow.AddMinutes(-2), WalletEntryTypes.BalancePurchase, WalletSources.Purchase, "debit", "Achat panier ORD-2", -1500, 1500, 0, WalletEntryStatus.Confirmed, "Order", balance.Id),
                new(2, DateTime.UtcNow.AddMinutes(-1), WalletEntryTypes.ParentWalletPurchase, WalletSources.Purchase, "debit", "Achat pour Ana", -2000, 2000, 0, WalletEntryStatus.Confirmed, null, null),
                new(3, DateTime.UtcNow.AddMinutes(-4), WalletEntryTypes.Recharge, WalletSources.Recharge, "credit", "Recharge Mobile Money", 5000, 5000, 0, WalletEntryStatus.Confirmed, null, null),
                new(4, DateTime.UtcNow.AddMinutes(-5), WalletEntryTypes.MonthlyAllocation, WalletSources.MonthlyAllocation, "credit", "Dotation", 1000, 1000, 0, WalletEntryStatus.Confirmed, null, null),
            }, 4));
            var ctl = new ParentCreditsController(db, NullLogger<ParentCreditsController>.Instance, new Mock<INtfyService>().Object, wallet.Object, Ent(db));
            Bind(ctl, Parent, "parent");

            var json = JsonSerializer.Serialize(((OkObjectResult)await ctl.GetPayments()).Value);
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");
            var items = data.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(4, items.Count); // 2 commandes du parent + achat hors commande + recharge (ni doublon, ni dotation, ni commande d'un autre)
            Assert.Equal("Achat pour Ana", items[0].GetProperty("Label").GetString());
            Assert.Contains(items, i => i.GetProperty("Label").GetString() == "BAC C Physique 2024" && i.GetProperty("Method").GetString() == "MTN Mobile Money");
            Assert.Contains(items, i => i.GetProperty("Reference").GetString() == "ORD-2" && i.GetProperty("Method").GetString() == "Solde");
            // Dépensé ce mois : 3 000 + 1 500 (commandes) + 2 000 (achat par le solde) ; la recharge n'est pas une dépense.
            Assert.Equal(6500m, data.GetProperty("monthSpentXaf").GetDecimal());
        }
    }
}
