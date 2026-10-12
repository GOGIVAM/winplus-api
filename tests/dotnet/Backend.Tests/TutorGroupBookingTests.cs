using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 9.3 (suivi 17.3) : prix de groupe calculé par TutorPricing.Compute,
    /// au-dessus de la grille du 9.1 (créneaux, dégressivité, prorata 30 min).
    /// </summary>
    public class TutorGroupPricingTests
    {
        private static readonly DateOnly Monday = new(2026, 10, 19);

        private static TutorPricing.Grid Grid(TutorPricing.GroupPricing group, params TutorDurationDiscount[] durationDiscounts) =>
            new(6000m, new List<TutorRate>(), new List<TutorRateModifier>(), durationDiscounts.ToList()) { Group = group };

        private static TutorPricing.GroupPricing PerStudent(int max, params (int Size, int Percent)[] tiers) =>
            new(TutorPricing.GroupPerStudent, max, null,
                tiers.Select(t => new TutorGroupDiscount { MinGroupSize = t.Size, PercentDiscount = t.Percent }).ToList());

        [Fact]
        public void SingleStudent_PriceUnchanged_WhateverTheGroupMethod()
        {
            var solo = TutorPricing.Compute(Grid(TutorPricing.GroupPricing.None), Monday, new TimeSpan(14, 0, 0), 90, "online", null);
            var withGroups = TutorPricing.Compute(Grid(PerStudent(6, (2, 20))), Monday, new TimeSpan(14, 0, 0), 90, "online", null, 1);
            Assert.Equal(9000, solo.TotalXaf);
            Assert.Equal(solo.TotalXaf, withGroups.TotalXaf);
            Assert.Equal(1, withGroups.ParticipantCount);
            Assert.Null(withGroups.GroupPricingMethod);
            Assert.Equal(9000, withGroups.PerStudentXaf);
        }

        [Fact]
        public void PerStudent_HighestTierReached_TotalIsExactMultiple()
        {
            // 6 000 XAF/h, 2 h, dégressivité de durée 10 % dès 2 h : prix individuel 10 800.
            // Paliers : 2 élèves -10 %, 4 élèves -25 %. Groupe de 4 : 8 100 par élève, 32 400 au total.
            var grid = Grid(PerStudent(6, (2, 10), (4, 25)), new TutorDurationDiscount { MinDurationMinutes = 120, PercentDiscount = 10 });
            var q = TutorPricing.Compute(grid, Monday, new TimeSpan(14, 0, 0), 120, "online", null, 4);
            Assert.Equal(10800, q.SoloTotalXaf);
            Assert.Equal(25, q.GroupDiscountPercent);
            Assert.Equal(8100, q.PerStudentXaf);
            Assert.Equal(32400, q.TotalXaf);
            Assert.Equal(TutorPricing.GroupPerStudent, q.GroupPricingMethod);

            // Groupe de 3 : palier « 2 élèves » seulement.
            var three = TutorPricing.Compute(grid, Monday, new TimeSpan(14, 0, 0), 120, "online", null, 3);
            Assert.Equal(9720, three.PerStudentXaf);
            Assert.Equal(29160, three.TotalXaf);
        }

        [Fact]
        public void PerStudent_NoTier_EachStudentPaysSoloPrice()
        {
            var q = TutorPricing.Compute(Grid(PerStudent(5)), Monday, new TimeSpan(10, 0, 0), 60, "online", null, 2);
            Assert.Equal(6000, q.PerStudentXaf);
            Assert.Equal(12000, q.TotalXaf);
        }

        [Fact]
        public void Flat_GroupPaysFixedPercentOfSoloPrice_IndependentOfSize()
        {
            // 6 000 XAF/h, 1 h 30 : 9 000 individuel ; prix fixe 150 % = 13 500 pour le groupe.
            var grid = Grid(new TutorPricing.GroupPricing(TutorPricing.GroupFlat, 8, 150, Array.Empty<TutorGroupDiscount>()));
            var three = TutorPricing.Compute(grid, Monday, new TimeSpan(9, 0, 0), 90, "online", null, 3);
            var eight = TutorPricing.Compute(grid, Monday, new TimeSpan(9, 0, 0), 90, "online", null, 8);
            Assert.Equal(13500, three.TotalXaf);
            Assert.Equal(13500, eight.TotalXaf);
            Assert.Equal(4500, three.PerStudentXaf);
            Assert.Equal(150, three.GroupFlatPercent);
        }

        [Fact]
        public void Group_RefusedWhenDisabledOrAboveMaxSize()
        {
            Assert.Throws<InvalidOperationException>(() =>
                TutorPricing.Compute(Grid(TutorPricing.GroupPricing.None), Monday, new TimeSpan(9, 0, 0), 60, "online", null, 2));
            Assert.Throws<InvalidOperationException>(() =>
                TutorPricing.Compute(Grid(PerStudent(3)), Monday, new TimeSpan(9, 0, 0), 60, "online", null, 4));
            Assert.Throws<InvalidOperationException>(() =>
                TutorPricing.Compute(Grid(PerStudent(3)), Monday, new TimeSpan(9, 0, 0), 60, "online", null, 0));
        }

        [Fact]
        public void ProfileWithoutMethod_IsIndividualOnly()
        {
            var p = new TutorProfile { GroupPricingMethod = null, MaxGroupSize = 10 };
            Assert.False(TutorPricing.GroupPricing.FromProfile(p).Enabled);
            var flat = new TutorProfile { GroupPricingMethod = TutorPricing.GroupFlat, MaxGroupSize = 1 };
            Assert.False(TutorPricing.GroupPricing.FromProfile(flat).Enabled);
        }

        [Theory]
        [InlineData("per_student", 1, null)]
        [InlineData("per_student", 21, null)]
        [InlineData("flat", 5, 99)]
        [InlineData("flat", 5, 1001)]
        [InlineData("flat", 5, null)]
        [InlineData("other", 5, null)]
        public void ValidateGroupPricing_RejectsOutOfBounds(string method, int max, int? flat) =>
            Assert.Throws<InvalidOperationException>(() => TutorPricing.ValidateGroupPricing(method, max, flat, null, 1));

        [Fact]
        public void ValidateGroupPricing_TiersBoundedBySizeAndPercent()
        {
            Assert.Throws<InvalidOperationException>(() => TutorPricing.ValidateGroupPricing("per_student", 4, null,
                new[] { new TutorGroupDiscountDto { MinGroupSize = 5, PercentDiscount = 10 } }, 1));
            Assert.Throws<InvalidOperationException>(() => TutorPricing.ValidateGroupPricing("per_student", 4, null,
                new[] { new TutorGroupDiscountDto { MinGroupSize = 2, PercentDiscount = 71 } }, 1));
            Assert.Throws<InvalidOperationException>(() => TutorPricing.ValidateGroupPricing("per_student", 4, null,
                new[] { new TutorGroupDiscountDto { MinGroupSize = 2, PercentDiscount = 10 }, new TutorGroupDiscountDto { MinGroupSize = 2, PercentDiscount = 20 } }, 1));

            var ok = TutorPricing.ValidateGroupPricing("per_student", 6, null,
                new[] { new TutorGroupDiscountDto { MinGroupSize = 4, PercentDiscount = 25 }, new TutorGroupDiscountDto { MinGroupSize = 2, PercentDiscount = 10 } }, 7);
            Assert.Equal("per_student", ok.Method);
            Assert.Equal(new[] { 2, 4 }, ok.Discounts.Select(d => d.MinGroupSize));

            var off = TutorPricing.ValidateGroupPricing("none", 8, null, null, 7);
            Assert.Null(off.Method);
            Assert.Equal(1, off.MaxGroupSize);
        }
    }

    /// <summary>
    /// Lot 9.3 (suivi 17.4) : contrôles d'autorisation de la réservation pour
    /// autrui et remboursement au bon portefeuille selon le type de payeur.
    /// </summary>
    public class TutorBookingPartiesTests
    {
        private static readonly InMemoryDatabaseRoot _root = new();
        private readonly string _dbName = $"tutor-parties-{Guid.NewGuid()}";

        // Identifiants propres à cette classe (verrou de repli statique par utilisateur).
        private const int Tutor = 9301, TutorProfileId = 9302;
        private const int Parent = 9303, Child = 9304, PendingChild = 9305, Stranger = 9306;
        private const int InstitutionAccount = 9307, InstitutionAccount2 = 9308, InstitutionId = 9309, OtherInstitutionId = 9310;
        private const int Pupil = 9311, InactivePupil = 9312, OtherPupil = 9313;
        private const int GroupMember = 9314, GroupMember2 = 9315, GroupId = 9316;
        private const int PendingPupil = 9317, RefusedPupil = 9318;

        public TutorBookingPartiesTests()
        {
            using var db = Fresh();
            User U(int id, string role, int? institutionId = null) => new() { Id = id, Email = $"u{id}@test.local", Role = role, IsActive = true, InstitutionId = institutionId };
            db.Users.AddRange(
                U(Tutor, "teacher"), U(Parent, "parent"), U(Child, "student"), U(PendingChild, "student"), U(Stranger, "student"),
                U(InstitutionAccount, "institution", InstitutionId), U(InstitutionAccount2, "institution", InstitutionId),
                U(Pupil, "student"), U(InactivePupil, "student"), U(OtherPupil, "student"),
                U(GroupMember, "student"), U(GroupMember2, "student"), U(PendingPupil, "student"), U(RefusedPupil, "student"));
            db.TutorProfiles.Add(new TutorProfile { Id = TutorProfileId, UserId = Tutor, IsActive = true });
            db.ParentStudentLinks.AddRange(
                new ParentStudentLink { ParentId = Parent, StudentId = Child, Status = "accepted", InitiatedBy = Parent },
                new ParentStudentLink { ParentId = Parent, StudentId = PendingChild, Status = "pending", InitiatedBy = Parent });
            db.Institutions.AddRange(
                new Institution { Id = InstitutionId, Name = "Lycée Test", Country = "CM" },
                new Institution { Id = OtherInstitutionId, Name = "Autre lycée", Country = "CM" });
            db.InstitutionStudents.AddRange(
                new InstitutionStudent { InstitutionId = InstitutionId, StudentId = Pupil, GroupName = "Tle C", IsActive = true, ConfirmationStatus = InstitutionAttachmentStatuses.Accepted },
                // Suivi 17.14 : rattachements importés non confirmés par l'élève.
                new InstitutionStudent { InstitutionId = InstitutionId, StudentId = PendingPupil, GroupName = "Tle C", IsActive = true, ConfirmationStatus = InstitutionAttachmentStatuses.Pending },
                new InstitutionStudent { InstitutionId = InstitutionId, StudentId = RefusedPupil, GroupName = "Tle C", IsActive = true, ConfirmationStatus = InstitutionAttachmentStatuses.Refused },
                new InstitutionStudent { InstitutionId = InstitutionId, StudentId = InactivePupil, GroupName = "Tle C", IsActive = false, ConfirmationStatus = InstitutionAttachmentStatuses.Accepted },
                new InstitutionStudent { InstitutionId = OtherInstitutionId, StudentId = OtherPupil, IsActive = true, ConfirmationStatus = InstitutionAttachmentStatuses.Accepted });
            db.StudyGroups.Add(new StudyGroup
            {
                Id = GroupId, OwnerId = GroupMember, Name = "Révisions maths", JoinCode = "ABC123", IsActive = true,
                Members = new List<StudyGroupMember>
                {
                    new() { UserId = GroupMember, Role = "owner" },
                    new() { UserId = GroupMember2 },
                },
            });
            db.SaveChanges();
        }

        private TestApplicationDbContext Fresh() => TestApplicationDbContext.Create(_dbName, _root);

        private Task<TutorBookingParties> Resolve(int actor, string? payerType, int[]? participants, int? groupId = null, int? assignmentId = null)
        {
            var db = Fresh();
            return new TutorBookingPartyResolver(db).ResolveAsync(actor, payerType, participants, groupId, assignmentId, TutorProfileId);
        }

        // ── Élève ──

        [Fact]
        public async Task Student_BooksForSelf_PayerIsStudent()
        {
            var p = await Resolve(Child, null, null);
            Assert.Equal(TutorPayerTypes.Student, p.PayerType);
            Assert.Equal(Child, p.PayerUserId);
            Assert.Equal(new[] { Child }, p.ParticipantIds);
        }

        [Fact]
        public async Task Student_CannotBookForAnotherStudent() =>
            await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => Resolve(Child, "student", new[] { Stranger }));

        // ── Parent ──

        [Fact]
        public async Task Parent_BooksForAcceptedChild_PaysFromOwnWallet()
        {
            var p = await Resolve(Parent, "parent", new[] { Child });
            Assert.Equal(TutorPayerTypes.Parent, p.PayerType);
            Assert.Equal(Parent, p.PayerUserId);
            Assert.Equal(new[] { Child }, p.ParticipantIds);
        }

        [Theory]
        [InlineData(PendingChild)] // lien en attente : pas encore accepté par l'élève
        [InlineData(Stranger)]     // aucun lien
        public async Task Parent_WithoutAcceptedLink_Forbidden(int student) =>
            await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => Resolve(Parent, "parent", new[] { Child, student }));

        [Fact]
        public async Task Student_CannotPretendToBeParentOrInstitution()
        {
            await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => Resolve(Stranger, "parent", new[] { Child }));
            await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => Resolve(Stranger, "institution", new[] { Pupil }));
        }

        // ── Institution ──

        [Fact]
        public async Task Institution_BooksForAttachedStudent_SharedWalletHolder()
        {
            var first = await Resolve(InstitutionAccount2, "institution", new[] { Pupil });
            Assert.Equal(TutorPayerTypes.Institution, first.PayerType);
            Assert.Equal(InstitutionId, first.InstitutionId);
            Assert.Equal(InstitutionAccount2, first.BookedByUserId);
            // Titulaire fixé au premier usage, puis partagé par tous les comptes.
            Assert.Equal(InstitutionAccount2, first.PayerUserId);
            var second = await Resolve(InstitutionAccount, "institution", new[] { Pupil });
            Assert.Equal(InstitutionAccount2, second.PayerUserId);
            Assert.Equal(InstitutionAccount, second.BookedByUserId);
            using var db = Fresh();
            Assert.Equal(InstitutionAccount2, await InstitutionWallet.ResolveWalletOwnerAsync(db, InstitutionAccount));
            Assert.Equal(Parent, await InstitutionWallet.ResolveWalletOwnerAsync(db, Parent));
        }

        [Theory]
        [InlineData(OtherPupil)]    // élève d'une autre institution
        [InlineData(InactivePupil)] // rattachement inactif
        [InlineData(Stranger)]      // aucun rattachement
        [InlineData(PendingPupil)]  // 17.14 : rattachement pas encore confirmé par l'élève
        [InlineData(RefusedPupil)]  // 17.14 : rattachement refusé par l'élève
        public async Task Institution_WithoutAttachment_Forbidden(int student) =>
            await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => Resolve(InstitutionAccount, "institution", new[] { student }));

        // ── Groupe d'étude ──

        [Fact]
        public async Task StudyGroupMember_BooksForMembers_PaysHimself()
        {
            var p = await Resolve(GroupMember2, "study_group", new[] { GroupMember, GroupMember2 }, GroupId);
            Assert.Equal(TutorPayerTypes.StudyGroup, p.PayerType);
            Assert.Equal(GroupMember2, p.PayerUserId);
            Assert.Equal(GroupId, p.StudyGroupId);
            Assert.Equal(GroupMember2, p.ParticipantIds[0]);
            Assert.Equal(2, p.ParticipantIds.Count);
        }

        [Fact]
        public async Task StudyGroup_NonMemberBooker_Forbidden() =>
            await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => Resolve(Stranger, "study_group", new[] { GroupMember }, GroupId));

        [Fact]
        public async Task StudyGroup_NonMemberParticipant_Forbidden() =>
            await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => Resolve(GroupMember, "study_group", new[] { GroupMember, Stranger }, GroupId));

        // ── Attribution ──

        [Fact]
        public async Task Assignment_MustBeActive_ForThisTutor_AndCoverParticipants()
        {
            int pendingId, activeId;
            using (var db = Fresh())
            {
                var pending = new TutorAssignment { TutorProfileId = TutorProfileId, AssignedByUserId = Parent, AssignerType = "parent", Status = TutorAssignmentStatuses.Pending,
                    Beneficiaries = new List<TutorAssignmentBeneficiary> { new() { StudentUserId = Child } } };
                var active = new TutorAssignment { TutorProfileId = TutorProfileId, AssignedByUserId = Parent, AssignerType = "parent", Status = TutorAssignmentStatuses.Active,
                    Beneficiaries = new List<TutorAssignmentBeneficiary> { new() { StudentUserId = Child } } };
                db.TutorAssignments.AddRange(pending, active);
                await db.SaveChangesAsync();
                pendingId = pending.Id;
                activeId = active.Id;
            }
            await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => Resolve(Parent, "parent", new[] { Child }, assignmentId: pendingId));
            var ok = await Resolve(Parent, "parent", new[] { Child }, assignmentId: activeId);
            Assert.Equal(activeId, ok.AssignmentId);
            // Le bénéficiaire peut aussi réserver pour lui-même depuis l'attribution.
            Assert.Equal(activeId, (await Resolve(Child, null, null, assignmentId: activeId)).AssignmentId);
            // Un autre parent ne peut pas s'en servir.
            await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => Resolve(Stranger, null, null, assignmentId: activeId));
        }

        [Fact]
        public async Task AssignmentBeneficiaries_ParentOnlyAcceptedChildren_InstitutionWholeClass()
        {
            using var db = Fresh();
            var resolver = new TutorBookingPartyResolver(db);
            await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => resolver.ResolveAssignmentBeneficiariesAsync(Parent, new[] { PendingChild }, null));
            var parent = await resolver.ResolveAssignmentBeneficiariesAsync(Parent, new[] { Child }, null);
            Assert.Equal("parent", parent.AssignerType);

            var cls = await resolver.ResolveAssignmentBeneficiariesAsync(InstitutionAccount, null, "Tle C");
            Assert.Equal(new[] { Pupil }, cls.StudentIds); // l'élève inactif de la classe n'est pas repris
            await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => resolver.ResolveAssignmentBeneficiariesAsync(InstitutionAccount, new[] { OtherPupil }, null));
            await Assert.ThrowsAsync<TutorBookingForbiddenException>(() => resolver.ResolveAssignmentBeneficiariesAsync(Child, new[] { Child }, null));
        }

        // ── Remboursement au bon portefeuille ──

        [Theory]
        [InlineData(TutorPayerTypes.Parent, Parent, WalletOwnerTypes.Parent)]
        [InlineData(TutorPayerTypes.Institution, InstitutionAccount, WalletOwnerTypes.Institution)]
        public async Task Refund_CreditsPayerWallet_NotTheStudent(string payerType, int payer, string ownerType)
        {
            int id;
            using (var db = Fresh())
            {
                var start = DateTime.UtcNow.AddHours(72);
                var booking = new TutorBooking
                {
                    TutorProfileId = TutorProfileId, StudentUserId = payerType == TutorPayerTypes.Parent ? Child : Pupil,
                    PayerUserId = payer, PayerType = payerType, BookedByUserId = payer,
                    PayerInstitutionId = payerType == TutorPayerTypes.Institution ? InstitutionId : null,
                    SessionDate = DateOnly.FromDateTime(start), StartTime = new TimeSpan(start.Hour, 0, 0), EndTime = new TimeSpan(start.Hour, 0, 0) + TimeSpan.FromMinutes(60),
                    PriceXaf = 10000, Status = "pending_tutor_approval", PaymentStatus = "completed",
                    PaymentMethod = TutorPaymentMethods.Wallet, PaymentFeeXaf = 0,
                };
                db.TutorBookings.Add(booking);
                await db.SaveChangesAsync();
                id = booking.Id;
            }

            using (var db = Fresh())
            {
                var booking = await db.TutorBookings.Include(b => b.TutorProfile).FirstAsync(b => b.Id == id);
                booking.Status = "rejected";
                var refunds = new TutorRefundService(db, new WalletService(db, NullLogger<WalletService>.Instance), new Mock<INtfyService>().Object,
                    NullLogger<TutorRefundService>.Instance, new ConfigurationBuilder().Build());
                await refunds.RefundAsync(booking, TutorRefundCause.TutorDeclined);
            }

            using (var check = Fresh())
            {
                var refund = await check.WalletTransactions.AsNoTracking().SingleAsync(t => t.SourceId == id && t.EntryType == WalletEntryTypes.TutoringRefund);
                Assert.Equal(payer, refund.OwnerId);
                Assert.Equal(ownerType, refund.OwnerType);
                Assert.Equal(10000, refund.Amount);
            }
        }
    }
}
