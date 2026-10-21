using System;
using System.Linq;
using Backend.Services;
using Backend.Utils;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 10.8 : normalisation téléphone et opérateur réunies dans PhoneNumbers
    /// (même comportement que les copies retirées), pays d'un numéro, TVA par pays.
    /// </summary>
    public class CountryModel108Tests
    {
        [Theory]
        [InlineData("656478741", "237656478741")]
        [InlineData("+237 656 47 87 41", "237656478741")]
        [InlineData("237656478741", "237656478741")]
        [InlineData("+241 06 12 34 56", "24106123456")]
        public void ToE164Digits_AddsCameroonCodeToNineLocalDigitsOnly(string phone, string expected)
        {
            Assert.Equal(expected, PhoneNumbers.ToE164Digits(phone));
        }

        [Theory]
        [InlineData("237656478741", "cm.orange")]
        [InlineData("+237699000000", "cm.orange")]
        [InlineData("690000000", "cm.orange")]
        [InlineData("237670000000", "cm.mtn")]
        [InlineData("650000000", "cm.mtn")]
        [InlineData("68", "cm.mtn")]
        [InlineData("", "cm.mtn")]
        public void CameroonMobileMoneyChannel_OrangePrefixesElseMtn(string phone, string expected)
        {
            Assert.Equal(expected, PhoneNumbers.CameroonMobileMoneyChannel(phone));
        }

        [Theory]
        [InlineData("656478741", "237656478741")]
        [InlineData("00237656478741", "237656478741")]
        [InlineData("+237 656478741", "237656478741")]
        [InlineData("256478741", null)]
        [InlineData("24106123456", null)]
        [InlineData(null, null)]
        public void CameroonMobileOrNull_AcceptsCameroonMobilesOnly(string? phone, string? expected)
        {
            Assert.Equal(expected, PhoneNumbers.CameroonMobileOrNull(phone));
        }

        [Theory]
        [InlineData("656478741", "CM")]
        [InlineData("+237656478741", "CM")]
        [InlineData("00237656478741", "CM")]
        [InlineData("+241 06 12 34 56", "GA")]
        [InlineData("+242 06 123 45 67", "CG")]
        [InlineData("+235 66 12 34 56", "TD")]
        [InlineData("+236 72 12 34 56", "CF")]
        [InlineData("+240 222 123 456", "GQ")]
        [InlineData("+33 6 12 34 56 78", null)]
        [InlineData("+241 1234", null)]
        [InlineData("", null)]
        public void CountryOf_RecognisesCemacDialCodes(string phone, string? expected)
        {
            Assert.Equal(expected, PhoneNumbers.CountryOf(phone));
        }

        [Fact]
        public void Vat_OnlyCameroonIsOpen_DefaultUnchanged()
        {
            Assert.Equal(VatPolicy.CatalogRate, VatPolicy.RateFor("CM"));
            Assert.Equal(VatPolicy.CatalogRate, VatPolicy.RateFor(null));
            Assert.Equal(new[] { "CM" }, VatPolicy.OpenCountries.ToArray());
            Assert.Throws<NotSupportedException>(() => VatPolicy.RateFor("GA"));
            // Sans pays : même calcul qu'avant le lot.
            Assert.Equal(VatPolicy.ExclTaxFromInclTax(5000m, "CM"), VatPolicy.ExclTaxFromInclTax(5000m));
            Assert.Equal(5000m, VatPolicy.ExclTaxFromInclTax(5000m) + VatPolicy.TaxIncludedIn(5000m));
        }
    }
}
