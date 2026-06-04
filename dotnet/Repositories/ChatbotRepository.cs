using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

/// <summary>
/// Interface pour le repository Chatbot
/// </summary>
public interface IChatbotRepository
{
    // Conversations
    Task<Conversation> CreateConversationAsync(Conversation conversation);
    Task<Conversation?> GetConversationByIdAsync(int id, bool includeMessages = false);
    Task<Conversation?> GetConversationByIdForUserAsync(int id, int userId, bool includeMessages = false);
    Task<List<Conversation>> GetConversationsForUserAsync(int userId, int page, int pageSize, bool includeDeleted = false);
    Task<int> GetConversationCountForUserAsync(int userId, bool includeDeleted = false);
    Task<Conversation> UpdateConversationAsync(Conversation conversation);
    Task<bool> DeleteConversationAsync(int id, int userId, bool hardDelete = false);
    
    // Messages
    Task<Message> CreateMessageAsync(Message message);
    Task<Message?> GetMessageByIdAsync(int id);
    Task<List<Message>> GetMessagesForConversationAsync(int conversationId, int? limit = null);
    Task<Message> UpdateMessageAsync(Message message);
    Task<bool> DeleteMessageAsync(int id);
    
    // Context
    Task<ChatbotContext?> GetContextForUserAsync(int userId);
    Task<ChatbotContext> CreateOrUpdateContextAsync(ChatbotContext context);
}

/// <summary>
/// Repository pour gérer les opérations Chatbot en base de données
/// </summary>
public class ChatbotRepository : IChatbotRepository
{
    private readonly ApplicationDbContext _context;

    public ChatbotRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    #region Conversations

    public async Task<Conversation> CreateConversationAsync(Conversation conversation)
    {
        conversation.CreatedAt = DateTime.UtcNow;
        conversation.UpdatedAt = DateTime.UtcNow;
        
        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync();
        
        return conversation;
    }

    public async Task<Conversation?> GetConversationByIdAsync(int id, bool includeMessages = false)
    {
        var query = _context.Conversations
            .Where(c => c.Id == id && !c.IsDeleted);

        if (includeMessages)
        {
            query = query.Include(c => c.Messages.Where(m => !m.IsDeleted).OrderBy(m => m.CreatedAt));
        }

        return await query.FirstOrDefaultAsync();
    }

    public async Task<Conversation?> GetConversationByIdForUserAsync(int id, int userId, bool includeMessages = false)
    {
        var query = _context.Conversations
            .Where(c => c.Id == id && c.UserId == userId && !c.IsDeleted);

        if (includeMessages)
        {
            query = query.Include(c => c.Messages.Where(m => !m.IsDeleted).OrderBy(m => m.CreatedAt));
        }

        return await query.FirstOrDefaultAsync();
    }

    public async Task<List<Conversation>> GetConversationsForUserAsync(int userId, int page, int pageSize, bool includeDeleted = false)
    {
        var query = _context.Conversations
            .Where(c => c.UserId == userId);

        if (!includeDeleted)
        {
            query = query.Where(c => !c.IsDeleted);
        }

        return await query
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> GetConversationCountForUserAsync(int userId, bool includeDeleted = false)
    {
        var query = _context.Conversations
            .Where(c => c.UserId == userId);

        if (!includeDeleted)
        {
            query = query.Where(c => !c.IsDeleted);
        }

        return await query.CountAsync();
    }

    public async Task<Conversation> UpdateConversationAsync(Conversation conversation)
    {
        conversation.UpdatedAt = DateTime.UtcNow;
        _context.Conversations.Update(conversation);
        await _context.SaveChangesAsync();
        
        return conversation;
    }

    public async Task<bool> DeleteConversationAsync(int id, int userId, bool hardDelete = false)
    {
        var conversation = await _context.Conversations
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

        if (conversation == null)
        {
            return false;
        }

        if (hardDelete)
        {
            _context.Conversations.Remove(conversation);
        }
        else
        {
            conversation.IsDeleted = true;
            conversation.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return true;
    }

    #endregion

    #region Messages

    public async Task<Message> CreateMessageAsync(Message message)
    {
        message.CreatedAt = DateTime.UtcNow;
        
        _context.Messages.Add(message);
        await _context.SaveChangesAsync();
        
        // Mettre à jour les stats de la conversation
        var conversation = await _context.Conversations.FindAsync(message.ConversationId);
        if (conversation != null)
        {
            conversation.MessageCount++;
            conversation.LastMessageAt = message.CreatedAt;
            conversation.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
        
        return message;
    }

    public async Task<Message?> GetMessageByIdAsync(int id)
    {
        return await _context.Messages
            .Where(m => m.Id == id && !m.IsDeleted)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Message>> GetMessagesForConversationAsync(int conversationId, int? limit = null)
    {
        var query = _context.Messages
            .Where(m => m.ConversationId == conversationId && !m.IsDeleted)
            .OrderBy(m => m.CreatedAt);

        if (limit.HasValue)
        {
            return await query.Take(limit.Value).ToListAsync();
        }

        return await query.ToListAsync();
    }

    public async Task<Message> UpdateMessageAsync(Message message)
    {
        _context.Messages.Update(message);
        await _context.SaveChangesAsync();
        
        return message;
    }

    public async Task<bool> DeleteMessageAsync(int id)
    {
        var message = await _context.Messages.FindAsync(id);
        if (message == null)
        {
            return false;
        }

        message.IsDeleted = true;
        await _context.SaveChangesAsync();
        
        return true;
    }

    #endregion

    #region Context

    public async Task<ChatbotContext?> GetContextForUserAsync(int userId)
    {
        return await _context.ChatbotContexts
            .FirstOrDefaultAsync(c => c.UserId == userId);
    }

    public async Task<ChatbotContext> CreateOrUpdateContextAsync(ChatbotContext context)
    {
        var existing = await _context.ChatbotContexts
            .FirstOrDefaultAsync(c => c.UserId == context.UserId);

        if (existing == null)
        {
            context.CreatedAt = DateTime.UtcNow;
            context.UpdatedAt = DateTime.UtcNow;
            _context.ChatbotContexts.Add(context);
        }
        else
        {
            existing.EducationLevel = context.EducationLevel ?? existing.EducationLevel;
            existing.Grade = context.Grade ?? existing.Grade;
            existing.UserObjectives = context.UserObjectives ?? existing.UserObjectives;
            existing.EnrolledSubjects = context.EnrolledSubjects ?? existing.EnrolledSubjects;
            existing.RecentActivity = context.RecentActivity ?? existing.RecentActivity;
            existing.NavigationHistory = context.NavigationHistory ?? existing.NavigationHistory;
            existing.Preferences = context.Preferences ?? existing.Preferences;
            existing.Strengths = context.Strengths ?? existing.Strengths;
            existing.Weaknesses = context.Weaknesses ?? existing.Weaknesses;
            existing.LearningStyle = context.LearningStyle ?? existing.LearningStyle;
            existing.UpdatedAt = DateTime.UtcNow;
            
            context = existing;
        }

        await _context.SaveChangesAsync();
        return context;
    }

    #endregion
}
