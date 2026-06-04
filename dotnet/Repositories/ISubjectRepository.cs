using Backend.Models.Entities;

namespace Backend.Repositories;

public interface ISubjectRepository
{
    Task<Subject?> GetByIdAsync(int id);
    Task<IEnumerable<Subject>> GetAllAsync();
    Task<IEnumerable<Subject>> GetAllAsync(int page, int limit);
    Task<IEnumerable<Subject>> GetPublishedAsync();
    Task<IEnumerable<Subject>> GetByCategoryAsync(string category);
    Task<IEnumerable<Subject>> SearchAsync(string searchTerm);
    Task<Subject> CreateAsync(Subject subject);
    Task<Subject> UpdateAsync(Subject subject);
    Task<bool> DeleteAsync(int id);
    Task<int> CountAsync();
    Task<int> GetCountAsync();
    Task<IEnumerable<Subject>> GetPopularAsync(int limit = 10);
}
