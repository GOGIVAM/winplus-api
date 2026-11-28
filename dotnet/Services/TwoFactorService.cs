using System.Security.Cryptography;
using System.Text;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QRCoder;

namespace Backend.Services;

/// <summary>Refus d'une règle de la double authentification, avec un code stable pour les clients.</summary>
public sealed class TwoFactorException : InvalidOperationException
{
    public string Code { get; }
    public TwoFactorException(string code, string message) : base(message) { Code = code; }
}

public interface ITwoFactorService
{
    Task<TwoFactorStatusDto> GetTwoFactorStatusAsync(int userId);
    /// <summary>Démarre l'activation : nouveau secret enregistré en attente, QR code et clé à saisir.</summary>
    Task<TwoFactorSetupResponse> StartSetupAsync(int userId, string accountLabel);
    /// <summary>Confirme l'activation avec un code de l'application ; renvoie les codes de secours, une seule fois.</summary>
    Task<(TwoFactorStatusDto Status, List<string> BackupCodes)> ConfirmSetupAsync(int userId, string code);
    /// <summary>Désactive après vérification d'un code (application ou secours).</summary>
    Task DisableAsync(int userId, string code);
    /// <summary>Remplace les codes de secours après vérification d'un code de l'application.</summary>
    Task<List<string>> RegenerateBackupCodesAsync(int userId, string code);
    Task<bool> IsEnabledAsync(int userId);
    /// <summary>Code de connexion : code de l'application, ou code de secours (consommé).</summary>
    Task<bool> VerifyLoginCodeAsync(int userId, string code);
    /// <summary>Jeton de connexion en attente du second facteur (5 minutes, signé, sans état).</summary>
    string CreateLoginChallenge(int userId, bool rememberMe, bool useCookie);
    (int UserId, bool RememberMe, bool UseCookie)? ReadLoginChallenge(string token);
    List<string> GenerateBackupCodes(int count = BackupCodeCount);
    public const int BackupCodeCount = 8;
}

/// <summary>
/// Double authentification par application (TOTP, RFC 6238 : HMAC-SHA1, 30 s, 6 chiffres), refaite le 28/11/2026.
/// Avant : secret en base64 (illisible par les applications, qui attendent du base32), jamais enregistré à
/// l'initialisation, codes de secours en clair, QR code par l'API Google Charts abandonnée, et aucun second
/// facteur demandé à la connexion.
///   * Secret : base32, chiffré en base (AES-GCM) avec une clé dérivée de JWT:SecretKey (HKDF), car les clés de
///     protection d'ASP.NET ne sont pas conservées d'un déploiement de conteneur à l'autre.
///     Attention : changer JWT:SecretKey rend illisibles les secrets enregistrés (désactivation par l'administration).
///   * Activation en deux temps : secret en attente (IsEnabled = false), confirmé par un premier code.
///   * Codes de secours : 8, à usage unique, enregistrés en empreinte HMAC-SHA256.
///   * Un code de l'application n'est accepté qu'une fois (pas de rejeu dans sa fenêtre de 30 s).
/// </summary>
public class TwoFactorService : ITwoFactorService
{
    public const string Issuer = "WinPlus";
    public const string Method = "authenticator";
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    private readonly ApplicationDbContext _context;
    private readonly ILogger<TwoFactorService> _logger;
    private readonly IMemoryCache? _cache;
    private readonly byte[] _secretKey;
    private readonly byte[] _backupKey;
    private readonly byte[] _challengeKey;

    public TwoFactorService(ApplicationDbContext context, ILogger<TwoFactorService> logger, IConfiguration configuration, IMemoryCache? cache = null)
    {
        _context = context;
        _logger = logger;
        _cache = cache;
        var master = Encoding.UTF8.GetBytes(configuration["JWT:SecretKey"]
            ?? throw new InvalidOperationException("JWT:SecretKey not configured"));
        _secretKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, info: Encoding.UTF8.GetBytes("winplus-2fa-secret"));
        _backupKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, info: Encoding.UTF8.GetBytes("winplus-2fa-backup"));
        _challengeKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, info: Encoding.UTF8.GetBytes("winplus-2fa-challenge"));
    }

    // ── État ─────────────────────────────────────────────────────────────────

    public async Task<TwoFactorStatusDto> GetTwoFactorStatusAsync(int userId)
    {
        var row = await _context.UserTwoFactorAuthentications.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId);
        return Status(userId, row);
    }

    public Task<bool> IsEnabledAsync(int userId) =>
        _context.UserTwoFactorAuthentications.AsNoTracking()
            .AnyAsync(t => t.UserId == userId && t.IsEnabled && t.TotpSecret != null && t.TotpSecret.StartsWith("v1:"));

    private static TwoFactorStatusDto Status(int userId, UserTwoFactorAuthentication? row) => new()
    {
        UserId = userId,
        IsEnabled = row?.IsEnabled == true,
        Method = row?.IsEnabled == true ? row.Method : null,
        EnabledAt = row?.IsEnabled == true ? row.EnabledAt : null,
        LastVerifiedAt = row?.LastVerifiedAt,
        BackupCodesCount = row?.IsEnabled == true ? BackupHashes(row).Count : 0,
        PendingSetup = row is { IsEnabled: false } && !string.IsNullOrEmpty(row.TotpSecret),
    };

    // ── Activation ───────────────────────────────────────────────────────────

    public async Task<TwoFactorSetupResponse> StartSetupAsync(int userId, string accountLabel)
    {
        var row = await _context.UserTwoFactorAuthentications.FirstOrDefaultAsync(t => t.UserId == userId);
        if (row is { IsEnabled: true })
            throw new TwoFactorException("deja_active", "La double authentification est déjà activée sur ce compte.");

        var secret = Base32Encode(RandomNumberGenerator.GetBytes(20));
        if (row == null)
        {
            row = new UserTwoFactorAuthentication { UserId = userId, CreatedAt = DateTime.UtcNow };
            _context.UserTwoFactorAuthentications.Add(row);
        }
        row.IsEnabled = false;
        row.Method = Method;
        row.TotpSecret = Protect(secret);
        row.BackupCodes = null;
        row.BackupCodesUsed = 0;
        row.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var label = Uri.EscapeDataString($"{Issuer}:{accountLabel}");
        var otpauth = $"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(Issuer)}&algorithm=SHA1&digits=6&period=30";
        return new TwoFactorSetupResponse
        {
            Secret = secret,
            SecretGrouped = string.Join(' ', Enumerable.Range(0, (secret.Length + 3) / 4).Select(i => secret.Substring(i * 4, Math.Min(4, secret.Length - i * 4)))),
            OtpauthUrl = otpauth,
            QrCode = QrPngDataUrl(otpauth),
            Message = "Scannez le QR code avec votre application d'authentification, puis saisissez le code à 6 chiffres affiché.",
        };
    }

    public async Task<(TwoFactorStatusDto Status, List<string> BackupCodes)> ConfirmSetupAsync(int userId, string code)
    {
        var row = await _context.UserTwoFactorAuthentications.FirstOrDefaultAsync(t => t.UserId == userId);
        if (row is { IsEnabled: true })
            throw new TwoFactorException("deja_active", "La double authentification est déjà activée sur ce compte.");
        if (row == null || string.IsNullOrEmpty(row.TotpSecret))
            throw new TwoFactorException("aucune_activation", "Commencez par afficher le QR code, puis saisissez le code de l'application.");
        if (!VerifyTotp(userId, Unprotect(row.TotpSecret), code))
            throw new TwoFactorException("code_invalide", "Code incorrect. Vérifiez l'heure de votre téléphone et saisissez le code affiché maintenant.");

        var codes = GenerateBackupCodes();
        row.IsEnabled = true;
        row.Method = Method;
        row.BackupCodes = string.Join(',', codes.Select(HashBackup));
        row.BackupCodesUsed = 0;
        row.EnabledAt = DateTime.UtcNow;
        row.LastVerifiedAt = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        _logger.LogInformation("Double authentification activée pour l'utilisateur {UserId}", userId);
        return (Status(userId, row), codes);
    }

    public async Task DisableAsync(int userId, string code)
    {
        var row = await _context.UserTwoFactorAuthentications.FirstOrDefaultAsync(t => t.UserId == userId);
        if (row is not { IsEnabled: true })
        {
            // Activation commencée mais jamais confirmée : on l'efface simplement.
            if (row != null) { row.TotpSecret = null; row.Method = null; row.UpdatedAt = DateTime.UtcNow; await _context.SaveChangesAsync(); }
            return;
        }
        if (!await CheckCodeAsync(row, code, consumeBackup: true))
            throw new TwoFactorException("code_invalide", "Code incorrect : saisissez le code de l'application ou un code de secours.");
        row.IsEnabled = false;
        row.TotpSecret = null;
        row.BackupCodes = null;
        row.BackupCodesUsed = 0;
        row.Method = null;
        row.EnabledAt = null;
        row.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        _logger.LogInformation("Double authentification désactivée pour l'utilisateur {UserId}", userId);
    }

    public async Task<List<string>> RegenerateBackupCodesAsync(int userId, string code)
    {
        var row = await _context.UserTwoFactorAuthentications.FirstOrDefaultAsync(t => t.UserId == userId && t.IsEnabled)
            ?? throw new TwoFactorException("non_active", "La double authentification n'est pas activée sur ce compte.");
        if (!VerifyTotp(userId, Unprotect(row.TotpSecret!), code))
            throw new TwoFactorException("code_invalide", "Code incorrect : saisissez le code affiché par l'application.");
        var codes = GenerateBackupCodes();
        row.BackupCodes = string.Join(',', codes.Select(HashBackup));
        row.BackupCodesUsed = 0;
        row.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return codes;
    }

    // ── Connexion ────────────────────────────────────────────────────────────

    public async Task<bool> VerifyLoginCodeAsync(int userId, string code)
    {
        var row = await _context.UserTwoFactorAuthentications.FirstOrDefaultAsync(t => t.UserId == userId && t.IsEnabled);
        if (row == null) return false;
        var ok = await CheckCodeAsync(row, code, consumeBackup: true);
        if (ok)
        {
            row.LastVerifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
        return ok;
    }

    public string CreateLoginChallenge(int userId, bool rememberMe, bool useCookie)
    {
        var exp = DateTimeOffset.UtcNow.Add(ChallengeLifetime).ToUnixTimeSeconds();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var payload = $"{userId}.{(rememberMe ? 1 : 0)}.{(useCookie ? 1 : 0)}.{exp}.{nonce}";
        var p = Base64Url(Encoding.UTF8.GetBytes(payload));
        return $"{p}.{Base64Url(HMACSHA256.HashData(_challengeKey, Encoding.UTF8.GetBytes(p)))}";
    }

    public (int UserId, bool RememberMe, bool UseCookie)? ReadLoginChallenge(string token)
    {
        try
        {
            var parts = (token ?? "").Split('.');
            if (parts.Length != 2) return null;
            var expected = Base64Url(HMACSHA256.HashData(_challengeKey, Encoding.UTF8.GetBytes(parts[0])));
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[1]))) return null;
            var f = Encoding.UTF8.GetString(FromBase64Url(parts[0])).Split('.');
            if (f.Length != 5 || !long.TryParse(f[3], out var exp) || DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return null;
            return (int.Parse(f[0]), f[1] == "1", f[2] == "1");
        }
        catch
        {
            return null;
        }
    }

    // ── Codes ────────────────────────────────────────────────────────────────

    private async Task<bool> CheckCodeAsync(UserTwoFactorAuthentication row, string code, bool consumeBackup)
    {
        var clean = (code ?? "").Replace(" ", "").Replace("-", "").Trim();
        if (clean.Length == 6 && clean.All(char.IsDigit))
            return !string.IsNullOrEmpty(row.TotpSecret) && VerifyTotp(row.UserId, Unprotect(row.TotpSecret), clean);

        // Code de secours (8 caractères), à usage unique.
        var hash = HashBackup(clean);
        var hashes = BackupHashes(row);
        var match = hashes.FirstOrDefault(h => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(h), Encoding.ASCII.GetBytes(hash)));
        if (match == null) return false;
        if (consumeBackup)
        {
            hashes.Remove(match);
            row.BackupCodes = string.Join(',', hashes);
            row.BackupCodesUsed += 1;
            row.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _logger.LogInformation("Code de secours utilisé par l'utilisateur {UserId} ({Remaining} restants)", row.UserId, hashes.Count);
        }
        return true;
    }

    private static List<string> BackupHashes(UserTwoFactorAuthentication row) =>
        string.IsNullOrEmpty(row.BackupCodes) ? new() : row.BackupCodes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    private string HashBackup(string code) =>
        Convert.ToHexString(HMACSHA256.HashData(_backupKey, Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", ""))));

    public List<string> GenerateBackupCodes(int count = ITwoFactorService.BackupCodeCount)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var codes = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            var chars = new char[8];
            for (int j = 0; j < chars.Length; j++)
                chars[j] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            codes.Add(new string(chars));
        }
        return codes;
    }

    /// <summary>TOTP avec une fenêtre d'un pas de part et d'autre (décalage d'horloge) et refus du rejeu.</summary>
    private bool VerifyTotp(int userId, string base32Secret, string code)
    {
        var clean = (code ?? "").Replace(" ", "").Trim();
        if (clean.Length != 6 || !clean.All(char.IsDigit)) return false;
        byte[] key;
        try { key = Base32Decode(base32Secret); } catch { return false; }
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var cacheKey = $"2fa-step-{userId}";
        long lastUsed = _cache != null && _cache.TryGetValue(cacheKey, out long l) ? l : long.MinValue;
        for (var i = -1; i <= 1; i++)
        {
            var s = step + i;
            if (s <= lastUsed) continue;
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Totp(key, s)), Encoding.ASCII.GetBytes(clean)))
            {
                _cache?.Set(cacheKey, s, TimeSpan.FromMinutes(3));
                return true;
            }
        }
        return false;
    }

    internal static string Totp(byte[] key, long step)
    {
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0f;
        var value = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6");
    }

    // ── Chiffrement du secret ────────────────────────────────────────────────

    private string Protect(string secret)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(secret);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_secretKey, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return "v1:" + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    private string Unprotect(string stored)
    {
        if (!stored.StartsWith("v1:"))
            throw new TwoFactorException("secret_illisible", "La double authentification de ce compte doit être réactivée.");
        var all = Convert.FromBase64String(stored[3..]);
        var nonce = all[..12];
        var tag = all[12..28];
        var cipher = all[28..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_secretKey, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    // ── Utilitaires ──────────────────────────────────────────────────────────

    private static string QrPngDataUrl(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    internal static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    internal static byte[] Base32Decode(string text)
    {
        var clean = text.Trim().TrimEnd('=').Replace(" ", "").ToUpperInvariant();
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var v = Base32Alphabet.IndexOf(c);
            if (v < 0) throw new FormatException("base32 invalide");
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }
        return output.ToArray();
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(t.PadRight(t.Length + (4 - t.Length % 4) % 4, '='));
    }
}
