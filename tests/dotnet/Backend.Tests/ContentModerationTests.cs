using Backend.Utils;
using Xunit;

namespace Backend.Tests;

/// <summary>Filtre des messageries : coordonnées et gros mots refusés, contenu scolaire accepté.</summary>
public class ContentModerationTests
{
    [Theory]
    [InlineData("appelle-moi au 699123456")]
    [InlineData("mon num c'est 6 99 12 34 56")]
    [InlineData("+237 699-12-34-56")]
    [InlineData("00237699123456")]
    [InlineData("le fixe : 222 23 45 67")]
    [InlineData("call +33 6 12 34 56 78")]
    [InlineData("écris à jean.dupont@gmail.com")]
    [InlineData("jean point dupont arobase gmail point com")]
    [InlineData("jeandupont (at) yahoo (dot) fr")]
    [InlineData("rejoins https://chat.whatsapp.com/AbCdEf")]
    [InlineData("wa.me/237699123456")]
    [InlineData("mon telegram t.me/jean")]
    [InlineData("écris-moi sur WhatsApp ce soir")]
    [InlineData("ajoute-moi sur insta")]
    [InlineData("contacte moi sur whatsap")]
    public void Coordonnees_refusees(string text)
    {
        var r = ContentModeration.Check(text);
        Assert.NotNull(r);
        Assert.Equal(ContentModeration.CodeContact, r!.Code);
    }

    [Theory]
    [InlineData("t'es un connard")]
    [InlineData("MERDEEEE alors")]
    [InlineData("putain ce devoir")]
    [InlineData("sal0pe")]
    [InlineData("ferme ta gueule")]
    [InlineData("what the fuck")]
    [InlineData("espèce d'enculé")]
    [InlineData("fils de pute")]
    public void Gros_mots_refuses(string text)
    {
        var r = ContentModeration.Check(text);
        Assert.NotNull(r);
        Assert.Equal(ContentModeration.CodeLanguage, r!.Code);
    }

    [Theory]
    [InlineData("Le volume du cône est V = πr²h/3.")]
    [InlineData("Je suis en retard pour le devoir, désolé.")]
    [InlineData("La chienne allaite ses chiots (SVT, mammifères).")]
    [InlineData("tg(x) = sin(x)/cos(x)")]
    [InlineData("Le budget est de 650 000 000 FCFA.")]
    [InlineData("Le Cameroun compte 28 000 000 habitants environ.")]
    [InlineData("Calcule 12 345 678 × 3, puis convertis 612 345 679 € en FCFA.")]
    [InlineData("Le pays compte 26 500 000 habitants et 2 300 000 élèves.")]
    [InlineData("La réponse est unique, et la conjugaison est correcte.")]
    [InlineData("Rendez-vous sur WinPlus demain à 18 h pour réviser la page 234.")]
    [InlineData("On peut parler de WhatsApp en cours d'informatique.")]
    [InlineData("Mon numéro de table au BEPC est le 12.")]
    [InlineData("")]
    [InlineData(null)]
    public void Contenu_scolaire_accepte(string? text)
    {
        Assert.Null(ContentModeration.Check(text));
    }

    [Fact]
    public void Plusieurs_champs_premier_refus()
    {
        var r = ContentModeration.Check("Titre correct", "mon mail : a.b@gmail.com");
        Assert.Equal(ContentModeration.CodeContact, r!.Code);
    }
}
