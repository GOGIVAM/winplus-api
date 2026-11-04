using System;
using System.Reflection;
using Backend.Controllers;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Logique privée du contrôleur des moyennes scolaires, appelée par réflexion
    /// (le code de production n'est pas modifié) : une moyenne valide est
    /// comprise entre 0 et 20 inclus, et la forme publique d'un enregistrement
    /// reprend ses champs sans en inventer.
    /// </summary>
    public class AcademicRecordsControllerLogicTests
    {
        private static T Call<T>(string name, params object[] args) =>
            (T)typeof(AcademicRecordsController).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;

        [Theory]
        [InlineData("0", true)]
        [InlineData("20", true)]
        [InlineData("12.5", true)]
        [InlineData("-0.01", false)]
        [InlineData("20.01", false)]
        public void La_moyenne_valide_est_comprise_entre_zero_et_vingt(string grade, bool expected)
        {
            Assert.Equal(expected, Call<bool>("IsValidGrade", decimal.Parse(grade, System.Globalization.CultureInfo.InvariantCulture)));
        }

        [Fact]
        public void La_forme_publique_reprend_les_champs_de_l_enregistrement()
        {
            var created = new DateTime(2026, 1, 1);
            var dto = Call<AcademicRecordDto>("ToDto", new AcademicRecord
            {
                Id = 4, StudentId = 9, SchoolYear = "2025-2026", AverageGrade = 14.25m, RecordedByUserId = 3, CreatedAt = created,
            });
            Assert.Equal(4, dto.Id);
            Assert.Equal(9, dto.StudentId);
            Assert.Equal("2025-2026", dto.SchoolYear);
            Assert.Equal(14.25m, dto.AverageGrade);
            Assert.Equal(3, dto.RecordedByUserId);
            Assert.Equal(created, dto.CreatedAt);
            Assert.Null(dto.UpdatedAt);
        }
    }
}
