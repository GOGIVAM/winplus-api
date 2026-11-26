using Backend.Utils;
using Xunit;

namespace Backend.Tests;

public class SchoolLabelsTests
{
    [Theory]
    [InlineData("Troisieme", "3ème")]
    [InlineData("troisième", "3ème")]
    [InlineData("3eme", "3ème")]
    [InlineData("Premiere D", "Première D")]
    [InlineData("Baccalaureat C", "Baccalauréat C")]
    [InlineData("BEPC / entree ENSET", "BEPC / entrée ENSET")]
    [InlineData("Ecole normale", "École normale")]
    [InlineData("Terminale C", "Terminale C")]
    [InlineData("Lower Sixth", "Lower Sixth")]
    [InlineData("Premier cycle", "Premier cycle")]
    [InlineData(null, null)]
    public void Libelles_accentues(string? saisi, string? attendu) => Assert.Equal(attendu, SchoolLabels.Normalize(saisi));
}
