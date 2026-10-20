using Backend.Controllers;
using Backend.Services;
using Xunit;

namespace Backend.Tests;

/// <summary>
/// Format actuel des notifications NotchPay ({ id, type, data }) et libellé
/// Mobile Money sans accents (les opérateurs suppriment les lettres accentuées).
/// </summary>
public class NotchPayWebhookFormatTests
{
    [Fact]
    public void DataFormat_IsRead_WithEventIdStatusAndNotchPayReference()
    {
        const string payload = """
        {"id":"evt_1","type":"payment.complete","data":{"id":"pay_1","reference":"trx.ABC","trxref":"WP-54-1792513730","amount":250,"currency":"XAF","status":"complete"}}
        """;

        var parsed = PaymentsController.ReadDataFormatPayload(payload);

        Assert.NotNull(parsed);
        Assert.Equal("evt_1", parsed!.Id);
        Assert.Equal("payment.complete", parsed.Event);
        Assert.Equal("trx.ABC", parsed.Transaction!.Reference);
        Assert.Equal("complete", parsed.Transaction.Status);
        Assert.Equal(250m, parsed.Transaction.Amount);
    }

    [Fact]
    public void DataFormat_PrefersMerchantReference_ForTutorBookings()
    {
        const string payload = """
        {"id":"evt_2","type":"payment.complete","data":{"reference":"trx.XYZ","trxref":"TBK-12-99","amount":5000,"currency":"XAF","status":"complete"}}
        """;

        var parsed = PaymentsController.ReadDataFormatPayload(payload);

        Assert.Equal("TBK-12-99", parsed!.Transaction!.Reference);
    }

    [Fact]
    public void DataFormat_StatusFallsBackToEventType()
    {
        const string payload = """{"id":"evt_3","type":"payment.failed","data":{"reference":"trx.F"}}""";

        Assert.Equal("failed", PaymentsController.ReadDataFormatPayload(payload)!.Transaction!.Status);
    }

    [Fact]
    public void OldFormat_IsNotMistakenForDataFormat()
    {
        const string payload = """{"event":"payment.complete","transaction":{"reference":"trx.OLD","status":"complete"}}""";

        Assert.Null(PaymentsController.ReadDataFormatPayload(payload));
    }

    [Theory]
    [InlineData("WinPlus  Épreuves scolaires", "WinPlus Epreuves scolaires")]
    [InlineData("WinPlus — Réservation répétiteur", "WinPlus - Reservation repetiteur")]
    [InlineData("", "WinPlus")]
    public void UssdText_RemovesAccentsAndExtraSpaces(string input, string expected)
    {
        Assert.Equal(expected, NotchPayService.UssdText(input));
    }

    [Fact]
    public void MaskPhone_KeepsOnlyPrefixAndLastDigits()
    {
        Assert.Equal("+237******741", NotchPayService.MaskPhone("+237656478741"));
    }
}
