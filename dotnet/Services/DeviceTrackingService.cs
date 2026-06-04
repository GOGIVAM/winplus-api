using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Backend.Data;
using Backend.Models.Entities;
using System.Linq;

namespace Backend.Services;

/// <summary>
/// Device tracking service for Remember Me functionality
/// </summary>
public interface IDeviceTrackingService
{
    Task<DeviceInfo> TrackDeviceAsync(int userId, HttpRequest request, bool rememberMe = false);
    Task<bool> IsDeviceRecognizedAsync(int userId, string userAgent, string ipAddress);
    Task<DeviceInfo?> GetDeviceAsync(int userId, string deviceFingerprint);
    Task UpdateDeviceAsync(int deviceId);
    Task CleanupExpiredDevicesAsync();
}

public class DeviceTrackingService : IDeviceTrackingService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<DeviceTrackingService> _logger;
    private readonly int _rememberMeDays;

    public DeviceTrackingService(
        ApplicationDbContext dbContext,
        ILogger<DeviceTrackingService> logger,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _dbContext = dbContext;
        _logger = logger;
        _rememberMeDays = int.TryParse(configuration["Auth:RememberMeDays"], out var days) ? days : 30;
    }

    /// <summary>
    /// Track a new device or update existing device
    /// </summary>
    public async Task<DeviceInfo> TrackDeviceAsync(int userId, HttpRequest request, bool rememberMe = false)
    {
        try
        {
            var userAgent = request.Headers["User-Agent"].ToString() ?? "Unknown";
            var ipAddress = GetClientIpAddress(request);
            var deviceFingerprint = GetDeviceFingerprint(request, userAgent);

            // Check if device already exists
            var existingDevice = _dbContext.DeviceInfos
                .FirstOrDefault(d => d.UserId == userId &&
                                    d.UserAgent == userAgent &&
                                    d.IpAddress == ipAddress);

            if (existingDevice != null)
            {
                existingDevice.LastUsedAt = DateTime.UtcNow;
                if (rememberMe)
                {
                    existingDevice.RememberUntil = DateTime.UtcNow.AddDays(_rememberMeDays);
                }
                _dbContext.Update(existingDevice);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Device updated for user {UserId}", userId);
                return existingDevice;
            }

            // Create new device
            var deviceInfo = new DeviceInfo
            {
                UserId = userId,
                UserAgent = userAgent,
                IpAddress = ipAddress,
                DeviceFingerprint = deviceFingerprint,
                DeviceName = ExtractDeviceName(userAgent),
                BrowserName = ExtractBrowserName(userAgent),
                BrowserVersion = ExtractBrowserVersion(userAgent),
                OsName = ExtractOsName(userAgent),
                OsVersion = ExtractOsVersion(userAgent),
                RememberUntil = rememberMe ? DateTime.UtcNow.AddDays(_rememberMeDays) : null,
                LastUsedAt = DateTime.UtcNow
            };

            _dbContext.DeviceInfos.Add(deviceInfo);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("New device tracked for user {UserId}: {DeviceName}", userId, deviceInfo.DeviceName);
            return deviceInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking device for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Check if device is recognized for user
    /// </summary>
    public Task<bool> IsDeviceRecognizedAsync(int userId, string userAgent, string ipAddress)
    {
        try
        {
            var device = _dbContext.DeviceInfos
                .FirstOrDefault(d => d.UserId == userId &&
                                    d.UserAgent == userAgent &&
                                    d.IpAddress == ipAddress &&
                                    d.IsRemembered);

            return Task.FromResult(device != null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking device recognition for user {UserId}", userId);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Get device by fingerprint
    /// </summary>
    public Task<DeviceInfo?> GetDeviceAsync(int userId, string deviceFingerprint)
    {
        try
        {
            var device = _dbContext.DeviceInfos
                .FirstOrDefault(d => d.UserId == userId &&
                                    d.DeviceFingerprint == deviceFingerprint &&
                                    d.IsRemembered);
            return Task.FromResult(device);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting device for user {UserId}", userId);
            return Task.FromResult((DeviceInfo?)null);
        }
    }

    /// <summary>
    /// Update last used time of device
    /// </summary>
    public async Task UpdateDeviceAsync(int deviceId)
    {
        try
        {
            var device = _dbContext.DeviceInfos.Find(deviceId);
            if (device != null)
            {
                device.LastUsedAt = DateTime.UtcNow;
                _dbContext.Update(device);
                await _dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating device {DeviceId}", deviceId);
        }
    }

    /// <summary>
    /// Clean up expired devices (RememberUntil < now)
    /// </summary>
    public async Task CleanupExpiredDevicesAsync()
    {
        try
        {
            var expiredDevices = _dbContext.DeviceInfos
                .Where(d => d.RememberUntil != null && d.RememberUntil < DateTime.UtcNow)
                .ToList();

            if (expiredDevices.Any())
            {
                _dbContext.RemoveRange(expiredDevices);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Cleaned up {Count} expired devices", expiredDevices.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up expired devices");
        }
    }

    // ============ Private Helper Methods ============

    private string GetClientIpAddress(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var ips = forwardedFor.ToString().Split(',');
            return ips[0].Trim();
        }

        return request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    private string GetDeviceFingerprint(HttpRequest request, string userAgent)
    {
        var ipAddress = GetClientIpAddress(request);
        var acceptLanguage = request.Headers["Accept-Language"].ToString() ?? "";
        var acceptEncoding = request.Headers["Accept-Encoding"].ToString() ?? "";

        var fingerprint = $"{userAgent}:{ipAddress}:{acceptLanguage}:{acceptEncoding}";
        return Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(fingerprint)));
    }

    private string ExtractDeviceName(string userAgent)
    {
        if (userAgent.Contains("iPad"))
            return "iPad";
        if (userAgent.Contains("iPhone"))
            return "iPhone";
        if (userAgent.Contains("Android"))
            return "Android Device";
        if (userAgent.Contains("Windows"))
            return "Windows PC";
        if (userAgent.Contains("Macintosh"))
            return "Mac";
        if (userAgent.Contains("Linux"))
            return "Linux";

        return "Unknown Device";
    }

    private string ExtractBrowserName(string userAgent)
    {
        if (userAgent.Contains("Chrome") && !userAgent.Contains("Chromium"))
            return "Chrome";
        if (userAgent.Contains("Safari"))
            return "Safari";
        if (userAgent.Contains("Firefox"))
            return "Firefox";
        if (userAgent.Contains("Edge"))
            return "Edge";
        if (userAgent.Contains("Opera"))
            return "Opera";

        return "Unknown Browser";
    }

    private string ExtractBrowserVersion(string userAgent)
    {
        try
        {
            if (userAgent.Contains("Chrome/"))
            {
                var version = userAgent.Split("Chrome/")[1].Split(" ")[0];
                return version.Split(".")[0];
            }
            if (userAgent.Contains("Firefox/"))
            {
                return userAgent.Split("Firefox/")[1].Split(" ")[0];
            }
            if (userAgent.Contains("Version/"))
            {
                return userAgent.Split("Version/")[1].Split(" ")[0];
            }
        }
        catch { }

        return "Unknown";
    }

    private string ExtractOsName(string userAgent)
    {
        if (userAgent.Contains("Windows NT"))
            return "Windows";
        if (userAgent.Contains("Macintosh"))
            return "macOS";
        if (userAgent.Contains("Android"))
            return "Android";
        if (userAgent.Contains("iPhone") || userAgent.Contains("iPad"))
            return "iOS";
        if (userAgent.Contains("Linux"))
            return "Linux";

        return "Unknown";
    }

    private string ExtractOsVersion(string userAgent)
    {
        try
        {
            if (userAgent.Contains("Windows NT"))
            {
                var version = userAgent.Split("Windows NT")[1].Split(";")[0].Trim();
                return version;
            }
            if (userAgent.Contains("Mac OS X"))
            {
                var version = userAgent.Split("Mac OS X")[1].Split(")")[0].Trim();
                return version;
            }
        }
        catch { }

        return "Unknown";
    }
}
