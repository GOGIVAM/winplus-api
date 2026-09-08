using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Proxy vers la mémoire conversationnelle WinAI (Module 6, 6C) — FastAPI
/// n'est jamais exposé directement au front. Simple relais, comme
/// TutorProfileController.ProxyToWinAI.
/// </summary>
[ApiController]
[Route("api/winai/memoire")]
[Authorize]
public class WinAIMemoryController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public WinAIMemoryController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    private async Task<IActionResult> Proxy(HttpMethod method, string pythonPath)
    {
        var client = _httpClientFactory.CreateClient("FastApiClient");
        using var req = new HttpRequestMessage(method, pythonPath);
        var auth = HttpContext.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation("Authorization", auth);
        var res = await client.SendAsync(req);
        return Content(await res.Content.ReadAsStringAsync(), "application/json");
    }

    [HttpGet]
    public Task<IActionResult> GetMemories() => Proxy(HttpMethod.Get, "/api/winai/memoire");

    [HttpDelete("{id:int}")]
    public Task<IActionResult> DeleteMemory(int id) => Proxy(HttpMethod.Delete, $"/api/winai/memoire/{id}");
}
