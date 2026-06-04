using Backend.Models.Entities;
using Backend.Models.DTOs;
using Backend.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Backend.Services;

public interface ITwoFactorService
{
    Task<TwoFactorStatusDto> GetTwoFactorStatusAsync(int userId);
    Task<TwoFactorSetupResponse> InitializeTwoFactorAsync(int userId, string method);
    Task<TwoFactorStatusDto> EnableTwoFactorAsync(int userId, string code, string secret);
    Task<TwoFactorStatusDto> VerifyTwoFactorAsync(int userId, string code);
    Task DisableTwoFactorAsync(int userId);
    Task<bool> ValidateTwoFactorCodeAsync(int userId, string code);
    string GenerateTotpSecret();
    List<string> GenerateBackupCodes(int count = 5);
}

public class TwoFactorService : ITwoFactorService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TwoFactorService> _logger;

    public TwoFactorService(ApplicationDbContext context, ILogger<TwoFactorService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<TwoFactorStatusDto> GetTwoFactorStatusAsync(int userId)
    {
        try
        {
            var twoFa = await _context.UserTwoFactorAuthentications
                .FirstOrDefaultAsync(t => t.UserId == userId);

            if (twoFa == null)
            {
                return new TwoFactorStatusDto
                {
                    UserId = userId,
                    IsEnabled = false,
                    Method = null,
                    EnabledAt = null,
                    LastVerifiedAt = null,
                    BackupCodesCount = 0
                };
            }

            var backupCodesCount = string.IsNullOrEmpty(twoFa.BackupCodes) 
                ? 0 
                : twoFa.BackupCodes.Split(',').Length - twoFa.BackupCodesUsed;

            return new TwoFactorStatusDto
            {
                UserId = twoFa.UserId,
                IsEnabled = twoFa.IsEnabled,
                Method = twoFa.Method,
                EnabledAt = twoFa.EnabledAt,
                LastVerifiedAt = twoFa.LastVerifiedAt,
                BackupCodesCount = backupCodesCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting 2FA status for user {UserId}", userId);
            throw;
        }
    }

    public async Task<TwoFactorSetupResponse> InitializeTwoFactorAsync(int userId, string method)
    {
        try
        {
            var secret = GenerateTotpSecret();
            var backupCodes = GenerateBackupCodes();

            // Generate QR code using Google Charts API
            var qrCodeUrl = GenerateQrCode(userId, secret);

            return new TwoFactorSetupResponse
            {
                QrCode = qrCodeUrl,
                BackupCodes = backupCodes,
                Secret = secret,
                Message = "2FA setup initiated. Please scan the QR code with your authenticator app."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing 2FA for user {UserId}", userId);
            throw;
        }
    }

    public async Task<TwoFactorStatusDto> EnableTwoFactorAsync(int userId, string code, string secret)
    {
        try
        {
            // Verify the code with the secret
            if (!VerifyTotpCode(secret, code))
            {
                throw new InvalidOperationException("Invalid verification code");
            }

            var existing = await _context.UserTwoFactorAuthentications
                .FirstOrDefaultAsync(t => t.UserId == userId);

            var backupCodes = GenerateBackupCodes();
            var backupCodesStr = string.Join(",", backupCodes);

            if (existing == null)
            {
                var twoFa = new UserTwoFactorAuthentication
                {
                    UserId = userId,
                    IsEnabled = true,
                    Method = "authenticator",
                    TotpSecret = secret,
                    BackupCodes = backupCodesStr,
                    BackupCodesUsed = 0,
                    EnabledAt = DateTime.UtcNow,
                    LastVerifiedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.UserTwoFactorAuthentications.Add(twoFa);
            }
            else
            {
                existing.IsEnabled = true;
                existing.Method = "authenticator";
                existing.TotpSecret = secret;
                existing.BackupCodes = backupCodesStr;
                existing.BackupCodesUsed = 0;
                existing.EnabledAt = DateTime.UtcNow;
                existing.LastVerifiedAt = DateTime.UtcNow;
                existing.UpdatedAt = DateTime.UtcNow;

                _context.UserTwoFactorAuthentications.Update(existing);
            }

            await _context.SaveChangesAsync();

            return new TwoFactorStatusDto
            {
                UserId = userId,
                IsEnabled = true,
                Method = "authenticator",
                EnabledAt = DateTime.UtcNow,
                LastVerifiedAt = DateTime.UtcNow,
                BackupCodesCount = backupCodes.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enabling 2FA for user {UserId}", userId);
            throw;
        }
    }

    public async Task<TwoFactorStatusDto> VerifyTwoFactorAsync(int userId, string code)
    {
        try
        {
            var twoFa = await _context.UserTwoFactorAuthentications
                .FirstOrDefaultAsync(t => t.UserId == userId && t.IsEnabled);

            if (twoFa == null)
            {
                throw new InvalidOperationException("2FA not enabled for this user");
            }

            if (!VerifyTotpCode(twoFa.TotpSecret, code))
            {
                throw new InvalidOperationException("Invalid verification code");
            }

            twoFa.LastVerifiedAt = DateTime.UtcNow;
            _context.UserTwoFactorAuthentications.Update(twoFa);
            await _context.SaveChangesAsync();

            return new TwoFactorStatusDto
            {
                UserId = twoFa.UserId,
                IsEnabled = true,
                Method = twoFa.Method,
                EnabledAt = twoFa.EnabledAt,
                LastVerifiedAt = twoFa.LastVerifiedAt,
                BackupCodesCount = twoFa.BackupCodesUsed > 0 ? 5 - twoFa.BackupCodesUsed : 5
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying 2FA for user {UserId}", userId);
            throw;
        }
    }

    public async Task DisableTwoFactorAsync(int userId)
    {
        try
        {
            var twoFa = await _context.UserTwoFactorAuthentications
                .FirstOrDefaultAsync(t => t.UserId == userId);

            if (twoFa != null)
            {
                twoFa.IsEnabled = false;
                twoFa.TotpSecret = null;
                twoFa.BackupCodes = null;
                twoFa.Method = null;
                twoFa.UpdatedAt = DateTime.UtcNow;

                _context.UserTwoFactorAuthentications.Update(twoFa);
                await _context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling 2FA for user {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> ValidateTwoFactorCodeAsync(int userId, string code)
    {
        try
        {
            var twoFa = await _context.UserTwoFactorAuthentications
                .FirstOrDefaultAsync(t => t.UserId == userId && t.IsEnabled);

            if (twoFa == null || string.IsNullOrEmpty(twoFa.TotpSecret))
            {
                return false;
            }

            return VerifyTotpCode(twoFa.TotpSecret, code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating 2FA code for user {UserId}", userId);
            return false;
        }
    }

    public string GenerateTotpSecret()
    {
        using (var rng = new RNGCryptoServiceProvider())
        {
            byte[] secret = new byte[20];
            rng.GetBytes(secret);
            return Convert.ToBase64String(secret);
        }
    }

    public List<string> GenerateBackupCodes(int count = 5)
    {
        var codes = new List<string>();
        using (var rng = new RNGCryptoServiceProvider())
        {
            for (int i = 0; i < count; i++)
            {
                byte[] code = new byte[4];
                rng.GetBytes(code);
                codes.Add(Convert.ToBase64String(code).Replace("+", "").Replace("/", "").Substring(0, 8).ToUpper());
            }
        }
        return codes;
    }

    private string GenerateQrCode(int userId, string secret)
    {
        // Use Google Charts API to generate QR code
        string encodedSecret = Uri.EscapeDataString(secret);
        string label = $"Win+:user{userId}@winplus.app";
        string encodedLabel = Uri.EscapeDataString(label);
        
        string otpauthUrl = $"otpauth://totp/{encodedLabel}?secret={encodedSecret}&issuer=Win%2B";
        string qrCodeUrl = $"https://chart.googleapis.com/chart?chs=300x300&chld=M|0&cht=qr&chl={Uri.EscapeDataString(otpauthUrl)}";
        
        return qrCodeUrl;
    }

    private bool VerifyTotpCode(string secret, string code)
    {
        try
        {
            if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(code) || code.Length != 6)
            {
                return false;
            }

            // Simple TOTP verification implementation
            // In production, use OtpNet library for robust TOTP handling
            var secretBytes = Convert.FromBase64String(secret);
            var otpProvider = new ToTPProvider(secretBytes);
            
            // Check current code and adjacent time windows
            return otpProvider.VerifyCode(code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying TOTP code");
            return false;
        }
    }
}

/// <summary>
/// Simple TOTP implementation for 2FA
/// </summary>
internal class ToTPProvider
{
    private readonly byte[] _secret;

    public ToTPProvider(byte[] secret)
    {
        _secret = secret;
    }

    public bool VerifyCode(string code)
    {
        try
        {
            if (!long.TryParse(code, out long userCode))
                return false;

            // Get current time window
            var timeStep = GetTimeStep();

            // Check current and adjacent time windows for clock skew tolerance
            for (int i = -1; i <= 1; i++)
            {
                var generatedCode = GenerateCode(timeStep + i);
                if (generatedCode == code)
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private long GetTimeStep()
    {
        return (long)Math.Floor((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds / 30.0);
    }

    private string GenerateCode(long timeStep)
    {
        using (var hmac = new System.Security.Cryptography.HMACSHA1(_secret))
        {
            byte[] timeBytes = BitConverter.GetBytes(timeStep);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(timeBytes);

            byte[] hash = hmac.ComputeHash(timeBytes);
            int offset = hash[hash.Length - 1] & 0xf;

            int value = ((hash[offset] & 0x7f) << 24)
                      | ((hash[offset + 1] & 0xff) << 16)
                      | ((hash[offset + 2] & 0xff) << 8)
                      | (hash[offset + 3] & 0xff);

            return (value % 1000000).ToString("D6");
        }
    }
}
