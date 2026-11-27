using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Backend.Models.Entities;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    /// <summary>
    /// B1 (achat de formation) : nullable depuis que <see cref="CourseId"/>
    /// existe. Exactement un des deux doit être renseigné, jamais les deux,
    /// jamais aucun  contrainte posée en base (CHECK, voir migration
    /// SQL_AddCourseCheckout.sql) ET revalidée côté service avant toute
    /// écriture (OrderService.CreateOrderAsync).
    /// </summary>
    public int? SubjectId { get; set; }

    public int? CourseId { get; set; }

    public decimal PriceAtPurchase { get; set; }

    /// <summary>Titre de l'épreuve ou de la formation, rempli à la lecture des commandes (non stocké). Sans lui,
    /// les clients affichaient « sujet n° X » : l'entité liée n'était pas chargée, et elle exposerait trop de champs.</summary>
    [NotMapped]
    public string? Title { get; set; }

    [JsonIgnore]
    public Order Order { get; set; } = null!;
    public Subject? Subject { get; set; }
    public Course? Course { get; set; }
}
