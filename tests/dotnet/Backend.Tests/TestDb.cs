using System;
using System.Collections;
using System.Linq;
using System.Text.Json;
using Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Backend.Tests
{
    /// <summary>
    /// Contexte de test (Module 26) : le vrai modèle ApplicationDbContext, sur le
    /// fournisseur EF InMemory. Certaines propriétés sont des types propres à
    /// PostgreSQL (Dictionary&lt;string, object&gt; en jsonb, etc.) que InMemory
    /// refuse de mapper : elles sont converties en JSON texte, uniquement ici.
    ///
    /// Limites connues d'InMemory, à garder en tête : pas de transaction réelle,
    /// pas de contrainte relationnelle ni de sensibilité à la casse SQL. Il sert
    /// aux tests de logique applicative, pas aux tests de schéma.
    /// Aucune base PostgreSQL de test n'est configurée : ses identifiants ne sont
    /// pas lus par les tests (voir le rapport du Module 26).
    /// </summary>
    public class TestApplicationDbContext : ApplicationDbContext
    {
        public TestApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public static TestApplicationDbContext Create(string? name = null) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(name ?? $"test-{Guid.NewGuid()}")
                .Options);

        /// <summary>
        /// Contexte adossé à une racine InMemory explicite : plusieurs contextes
        /// créés avec le même nom et la même racine voient les mêmes données,
        /// comme plusieurs scopes DI sur une même base (Module 22).
        /// </summary>
        public static TestApplicationDbContext Create(string name, Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot root) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(name, root)
                .Options);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entity in modelBuilder.Model.GetEntityTypes().ToList())
            {
                foreach (var property in entity.GetProperties().ToList())
                {
                    var type = property.ClrType;
                    if (!NeedsJsonConversion(type)) continue;

                    var converterType = typeof(JsonValueConverter<>).MakeGenericType(type);
                    property.SetValueConverter((ValueConverter)Activator.CreateInstance(converterType)!);
                }
            }
        }

        private static bool NeedsJsonConversion(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)
                || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(byte[])
                || t == typeof(DateOnly) || t == typeof(TimeOnly))
                return false;
            return typeof(IDictionary).IsAssignableFrom(t)
                || t == typeof(JsonDocument) || t == typeof(JsonElement)
                || (t.IsGenericType && t.GetGenericTypeDefinition().Name.StartsWith("Dictionary"));
        }

        private sealed class JsonValueConverter<T> : ValueConverter<T, string>
        {
            public JsonValueConverter() : base(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                s => JsonSerializer.Deserialize<T>(s, (JsonSerializerOptions?)null)!)
            { }
        }
    }
}
