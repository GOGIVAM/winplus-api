using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Export RGPD : utilisateur inconnu, drapeaux de minorité, et exclusion des
    /// communications privées dans un export parent dont la minorité n'est pas
    /// vérifiée. Le résultat est un objet anonyme : on le lit par réflexion.
    /// </summary>
    public class UserDataExportServiceTests
    {
        private const int Child = 9201, Other = 9202;

        private static object Prop(object o, string name) => o.GetType().GetProperty(name)!.GetValue(o)!;
        private static object? PropN(object o, string name) => o.GetType().GetProperty(name)!.GetValue(o);

        private static TestApplicationDbContext Seed(DateTime? dob)
        {
            var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = Child, Email = "kid@t.local", Role = "student", DateOfBirth = dob });
            db.Users.Add(new User { Id = Other, Email = "other@t.local", Role = "student" });
            db.DirectMessages.Add(new DirectMessage { FromUserId = Child, ToUserId = Other, Content = "salut" });
            db.SaveChanges();
            return db;
        }

        [Fact]
        public async Task Un_utilisateur_inconnu_leve_une_erreur()
        {
            using var db = TestApplicationDbContext.Create();
            await Assert.ThrowsAsync<KeyNotFoundException>(() => new UserDataExportService(db).BuildExportAsync(1, false));
        }

        [Fact]
        public async Task L_export_personnel_inclut_les_messages_prives_et_le_profil_sans_mot_de_passe()
        {
            using var db = Seed(null);
            var export = await new UserDataExportService(db).BuildExportAsync(Child, false);
            Assert.True((bool)Prop(export, "privateCommunicationsIncluded"));
            Assert.NotNull(PropN(export, "directMessages"));
            Assert.Null(PropN(export, "privateCommunicationsNote"));
            Assert.Null(Prop(export, "profile").GetType().GetProperty("PasswordHash"));
            Assert.False((bool)Prop(export, "minorityVerified"));
            Assert.Null(PropN(export, "isMinor"));
        }

        [Fact]
        public async Task Un_export_parent_sans_date_de_naissance_exclut_les_communications_privees()
        {
            using var db = Seed(null);
            var export = await new UserDataExportService(db).BuildExportAsync(Child, true);
            Assert.False((bool)Prop(export, "privateCommunicationsIncluded"));
            Assert.Null(PropN(export, "directMessages"));
            Assert.Null(PropN(export, "aiConversations"));
            Assert.Null(PropN(export, "aiMemories"));
            Assert.NotNull(PropN(export, "privateCommunicationsNote"));
        }

        [Fact]
        public async Task Un_export_parent_d_un_enfant_mineur_verifie_inclut_les_communications()
        {
            using var db = Seed(DateTime.UtcNow.AddYears(-10));
            var export = await new UserDataExportService(db).BuildExportAsync(Child, true);
            Assert.True((bool)Prop(export, "minorityVerified"));
            Assert.True((bool)Prop(export, "isMinor"));
            Assert.True((bool)Prop(export, "privateCommunicationsIncluded"));
            Assert.NotNull(PropN(export, "directMessages"));
        }

        [Fact]
        public async Task Un_export_parent_d_un_majeur_verifie_exclut_les_communications()
        {
            using var db = Seed(DateTime.UtcNow.AddYears(-30));
            var export = await new UserDataExportService(db).BuildExportAsync(Child, true);
            Assert.True((bool)Prop(export, "minorityVerified"));
            Assert.False((bool)Prop(export, "isMinor"));
            Assert.False((bool)Prop(export, "privateCommunicationsIncluded"));
            Assert.Null(PropN(export, "directMessages"));
        }
    }
}
