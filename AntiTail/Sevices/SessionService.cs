using AntiTail.Models;
using Microsoft.Extensions.Caching.Memory;

namespace AntiTail.Services;

/// <summary>
/// Зберігає FSM-стан реєстрації в пам'яті.
/// Ключ — Telegram ChatId. TTL — 30 хвилин (якщо користувач не завершив реєстрацію).
/// </summary>
public class SessionService
{
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(30);

    public SessionService(IMemoryCache cache)
    {
        _cache = cache;
    }

    private static string Key(long chatId) => $"session:{chatId}";

    public UserSession GetOrCreate(long chatId)
    {
        return _cache.GetOrCreate(Key(chatId), entry =>
        {
            entry.SlidingExpiration = SessionTtl;
            return new UserSession();
        })!;
    }

    public void Save(long chatId, UserSession session)
    {
        _cache.Set(Key(chatId), session, new MemoryCacheEntryOptions
        {
            SlidingExpiration = SessionTtl
        });
    }

    public void Remove(long chatId) => _cache.Remove(Key(chatId));
}