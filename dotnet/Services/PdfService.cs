using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Backend.Models.Entities;

namespace Backend.Services;

public interface IPdfService
{
    byte[] GenerateInvoice(Order order, IEnumerable<OrderItem> items, User? user);
    byte[] GenerateCertificate(Certificate cert, User user, Subject subject);
}

public class PdfService : IPdfService
{
    public byte[] GenerateInvoice(Order order, IEnumerable<OrderItem> items, User? user)
    {
        var itemList = items.ToList();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily(Fonts.Arial).FontSize(11));

                // ── En-tête ──────────────────────────────────────────────────
                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("WinPlus").Bold().FontSize(22).FontColor(Colors.Teal.Medium);
                            c.Item().Text("Plateforme éducative camerounaise")
                                    .FontSize(9).FontColor(Colors.Grey.Medium);
                        });
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().Text("FACTURE").Bold().FontSize(20).FontColor(Colors.Grey.Darken3);
                            c.Item().Text($"N° WP-{order.Id:D6}").FontSize(10).FontColor(Colors.Grey.Darken1);
                        });
                    });
                    col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor(Colors.Teal.Medium);
                });

                // ── Contenu ──────────────────────────────────────────────────
                page.Content().PaddingTop(16).Column(col =>
                {
                    // Bloc info commande / client
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("COMMANDE").Bold().FontSize(9).FontColor(Colors.Grey.Medium);
                            c.Item().Text($"Réf : {order.OrderNumber}").FontSize(10);
                            c.Item().Text($"Date : {order.CreatedAt:dd/MM/yyyy}").FontSize(10);
                            c.Item().Text($"Statut : {order.Status}").FontSize(10);
                            if (!string.IsNullOrEmpty(order.PaymentMethod))
                                c.Item().Text($"Paiement : {order.PaymentMethod}").FontSize(10);
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("CLIENT").Bold().FontSize(9).FontColor(Colors.Grey.Medium);
                            if (user != null)
                            {
                                var name = $"{user.FirstName} {user.LastName}".Trim();
                                if (!string.IsNullOrEmpty(name))
                                    c.Item().Text(name).FontSize(10);
                                c.Item().Text(user.Email).FontSize(10);
                            }
                            else
                            {
                                c.Item().Text(order.GuestName ?? "Client anonyme").FontSize(10);
                                if (!string.IsNullOrEmpty(order.GuestEmail))
                                    c.Item().Text(order.GuestEmail).FontSize(10);
                            }
                        });
                    });

                    col.Item().PaddingVertical(16).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(4); // Article
                            cols.RelativeColumn(1); // Prix
                        });

                        // En-tête tableau
                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Teal.Lighten3).Padding(6)
                                  .Text("Article").Bold().FontSize(10);
                            header.Cell().Background(Colors.Teal.Lighten3).Padding(6).AlignRight()
                                  .Text("Prix (XAF)").Bold().FontSize(10);
                        });

                        // Lignes articles
                        for (int i = 0; i < itemList.Count; i++)
                        {
                            var item = itemList[i];
                            var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten5;
                            table.Cell().Background(bg).Padding(6)
                                 .Text(item.Subject?.Title ?? $"Sujet #{item.SubjectId}").FontSize(10);
                            table.Cell().Background(bg).Padding(6).AlignRight()
                                 .Text($"{item.PriceAtPurchase:N0}").FontSize(10);
                        }
                    });

                    // Bloc totaux (aligné à droite)
                    col.Item().AlignRight().Width(220).Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(1); });

                        if (order.DiscountAmount > 0)
                        {
                            var subtotal = order.TotalAmount + order.DiscountAmount;
                            t.Cell().Padding(4).Text("Sous-total").FontSize(10);
                            t.Cell().Padding(4).AlignRight().Text($"{subtotal:N0} XAF").FontSize(10);
                            t.Cell().Padding(4).Text("Remise").FontSize(10).FontColor(Colors.Green.Darken1);
                            t.Cell().Padding(4).AlignRight()
                             .Text($"-{order.DiscountAmount:N0} XAF").FontSize(10).FontColor(Colors.Green.Darken1);
                        }

                        t.Cell().Background(Colors.Teal.Medium).Padding(7)
                         .Text("TOTAL").Bold().FontSize(12).FontColor(Colors.White);
                        t.Cell().Background(Colors.Teal.Medium).Padding(7).AlignRight()
                         .Text($"{order.TotalAmount:N0} XAF").Bold().FontSize(12).FontColor(Colors.White);
                    });
                });

                // ── Pied de page ─────────────────────────────────────────────
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("WinPlus · Cameroun  —  Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                    text.Span(" / ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();
    }

    public byte[] GenerateCertificate(Certificate cert, User user, Subject subject)
    {
        var studentName = $"{user.FirstName} {user.LastName}".Trim();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily(Fonts.Arial));

                page.Content().Border(4).BorderColor(Colors.Teal.Medium)
                    .Padding(24).Column(col =>
                    {
                        // Logo / plateforme
                        col.Item().AlignCenter().Text("WinPlus")
                           .Bold().FontSize(13).FontColor(Colors.Teal.Medium);

                        col.Item().PaddingTop(4).AlignCenter()
                           .Text("— Plateforme éducative camerounaise —")
                           .Italic().FontSize(9).FontColor(Colors.Grey.Medium);

                        // Titre principal
                        col.Item().PaddingTop(20).AlignCenter()
                           .Text("CERTIFICAT DE RÉUSSITE")
                           .Bold().FontSize(28).FontColor(Colors.Grey.Darken3);

                        col.Item().PaddingTop(4).AlignCenter()
                           .LineHorizontal(1).LineColor(Colors.Teal.Lighten2);

                        // Décerné à
                        col.Item().PaddingTop(20).AlignCenter()
                           .Text("Décerné à").Italic().FontSize(13).FontColor(Colors.Grey.Medium);

                        col.Item().PaddingTop(6).AlignCenter()
                           .Text(studentName).Bold().FontSize(34).FontColor(Colors.Teal.Darken2);

                        col.Item().PaddingTop(14).AlignCenter()
                           .Text("pour avoir complété avec succès le cours")
                           .FontSize(13).FontColor(Colors.Grey.Medium);

                        col.Item().PaddingTop(6).AlignCenter()
                           .Text(subject.Title).Bold().FontSize(20).FontColor(Colors.Grey.Darken3);

                        if (cert.Grade.HasValue)
                        {
                            col.Item().PaddingTop(8).AlignCenter()
                               .Text($"Note finale : {cert.Grade:N1} / 100")
                               .FontSize(13).FontColor(Colors.Grey.Medium);
                        }

                        // Pied du certificat
                        col.Item().PaddingTop(28).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text($"Délivré le {cert.IssuedAt:dd MMMM yyyy}").FontSize(11);
                                c.Item().Text($"N° {cert.CertificateNumber}")
                                        .FontSize(9).FontColor(Colors.Grey.Medium);
                            });
                            row.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().Text("Code de vérification")
                                        .FontSize(9).FontColor(Colors.Grey.Medium);
                                c.Item().Text(cert.VerificationCode)
                                        .Bold().FontSize(13).FontColor(Colors.Teal.Medium);
                            });
                        });
                    });
            });
        }).GeneratePdf();
    }
}
