using Backend.Models.Entities;
using Backend.Models.DTOs;
using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface ISessionService
{
    Task<List<SessionDto>> GetUserSessionsAsync(int userId);
    Task<SessionDto> CreateSessionAsync(int userId, string deviceName, string deviceType, string ipAddress, string userAgent, string location);
    Task UpdateSessionActivityAsync(int sessionId);
    Task DeleteSessionAsync(int sessionId);
    Task DeleteAllSessionsAsync(int userId);
    Task<SessionDto> GetSessionByIdAsync(int sessionId);
}

public class SessionService : ISessionService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SessionService> _logger;

    public SessionService(ApplicationDbContext context, ILogger<SessionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<SessionDto>> GetUserSessionsAsync(int userId)
    {
        try
        {
            var sessions = await _context.UserSessions
                .Where(s => s.UserId == userId && s.IsActive)
                .Select(s => new SessionDto
                {
                    Id = s.Id,
                    UserId = s.UserId,
                    DeviceName = s.DeviceName,
                    DeviceType = s.DeviceType,
                    IpAddress = s.IpAddress,
                    UserAgent = s.UserAgent,
                    Location = s.Location,
                    CreatedAt = s.CreatedAt,
                    LastActivityAt = s.LastActivityAt,
                    ExpiresAt = s.ExpiresAt,
                    IsActive = s.IsActive
                })
                .OrderByDescending(s => s.LastActivityAt)
                .ToListAsync();

            return sessions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sessions for user {UserId}", userId);
            throw;
        }
    }

    public async Task<SessionDto> CreateSessionAsync(int userId, string deviceName, string deviceType, string ipAddress, string userAgent, string location)
    {
        try
        {
            var session = new UserSession
            {
                UserId = userId,
                DeviceName = deviceName,
                DeviceType = deviceType,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Location = location,
                CreatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30),
                IsActive = true
            };

            _context.UserSessions.Add(session);
            await _context.SaveChangesAsync();

            return new SessionDto
            {
                Id = session.Id,
                UserId = session.UserId,
                DeviceName = session.DeviceName,
                DeviceType = session.DeviceType,
                IpAddress = session.IpAddress,
                UserAgent = session.UserAgent,
                Location = session.Location,
                CreatedAt = session.CreatedAt,
                LastActivityAt = session.LastActivityAt,
                ExpiresAt = session.ExpiresAt,
                IsActive = session.IsActive
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating session for user {UserId}", userId);
            throw;
        }
    }

    public async Task UpdateSessionActivityAsync(int sessionId)
    {
        try
        {
            var session = await _context.UserSessions.FindAsync(sessionId);
            if (session != null && session.IsActive)
            {
                session.LastActivityAt = DateTime.UtcNow;
                
                // Extend expiration if session is still active
                if (session.ExpiresAt < DateTime.UtcNow.AddDays(7))
                {
                    session.ExpiresAt = DateTime.UtcNow.AddDays(30);
                }

                _context.UserSessions.Update(session);
                await _context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating session activity for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task DeleteSessionAsync(int sessionId)
    {
        try
        {
            var session = await _context.UserSessions.FindAsync(sessionId);
            if (session != null)
            {
                session.IsActive = false;
                _context.UserSessions.Update(session);
                await _context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task DeleteAllSessionsAsync(int userId)
    {
        try
        {
            var sessions = await _context.UserSessions
                .Where(s => s.UserId == userId && s.IsActive)
                .ToListAsync();

            foreach (var session in sessions)
            {
                session.IsActive = false;
            }

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting all sessions for user {UserId}", userId);
            throw;
        }
    }

    public async Task<SessionDto> GetSessionByIdAsync(int sessionId)
    {
        try
        {
            var session = await _context.UserSessions.FindAsync(sessionId);
            if (session == null)
                return null;

            return new SessionDto
            {
                Id = session.Id,
                UserId = session.UserId,
                DeviceName = session.DeviceName,
                DeviceType = session.DeviceType,
                IpAddress = session.IpAddress,
                UserAgent = session.UserAgent,
                Location = session.Location,
                CreatedAt = session.CreatedAt,
                LastActivityAt = session.LastActivityAt,
                ExpiresAt = session.ExpiresAt,
                IsActive = session.IsActive
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session {SessionId}", sessionId);
            throw;
        }
    }
}
