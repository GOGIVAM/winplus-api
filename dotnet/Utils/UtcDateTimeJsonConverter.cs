using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backend.Utils;

/// <summary>
/// Dates reçues sans fuseau (« 2026-12-01T10:00:00 ») lues comme UTC (27/11/2026). Npgsql refuse d'écrire une
/// date de genre Unspecified dans une colonne « timestamp with time zone » ; seuls quelques contrôleurs faisaient
/// SpecifyKind à la main. Les dates avec fuseau sont converties en UTC. L'écriture est inchangée.
/// S'applique aussi aux DateTime? (System.Text.Json réutilise ce convertisseur pour Nullable&lt;DateTime&gt;).
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTime();
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
