using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TelegramBotApi.Data;

namespace TelegramBotApi.Services;

public record SearchHit(
    int Id,
    int GroupId,
    string GroupName,
    string Snippet,
    string? MatchedFile,
    DateTime SentAt,
    DateTime? Deadline,
    bool IsCompleted);

/// <summary>
/// Full-text search over all tasks (old ones included): task text, subject name and attached file names.
/// Matching is done in memory, case-insensitive for any alphabet — SQLite's LIKE ignores case only
/// for Latin letters, so "петр" would not find "Петр" there. A class has hundreds of tasks at most.
/// </summary>
public class SearchService
{
    public const int MinQueryLength = 2;
    public const int MaxResults = 30;
    private const int SnippetRadius = 60;

    private readonly TelegramBotContext _db;

    public SearchService(TelegramBotContext db) => _db = db;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string? query, long userId, CancellationToken ct)
    {
        var q = Normalize(query);
        if (q.Length < MinQueryLength) return Array.Empty<SearchHit>();
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var tasks = await _db.Messages.AsNoTracking()
            .OrderByDescending(m => m.SentAt)
            .Select(m => new
            {
                m.Id,
                m.ChatGroupId,
                GroupName = m.Group != null ? m.Group.Name : "",
                m.Text,
                m.SentAt,
                m.Deadline,
                Files = m.Attachments.Select(a => a.FileName).ToList(),
                IsCompleted = _db.UserTaskProgresses.Any(p => p.MessageId == m.Id && p.UserId == userId && p.IsCompleted)
            })
            .ToListAsync(ct);

        var hits = new List<SearchHit>();
        foreach (var t in tasks)
        {
            var text = PlainText(t.Text);
            var haystack = Normalize($"{text} {t.GroupName} {string.Join(' ', t.Files)}");
            // Every word must occur somewhere: "петр реферат" finds "Реферат про Петра I"
            if (!words.All(w => haystack.Contains(w, StringComparison.Ordinal))) continue;

            var matchedFile = t.Files.FirstOrDefault(f => words.Any(w => Normalize(f).Contains(w, StringComparison.Ordinal)));
            hits.Add(new SearchHit(t.Id, t.ChatGroupId, t.GroupName, Snippet(text, words), matchedFile,
                t.SentAt, t.Deadline, t.IsCompleted));
            if (hits.Count == MaxResults) break;
        }
        return hits;
    }

    /// <summary>Lower case, "ё" → "е", single spaces — so "Ёлка" and "елка" match.</summary>
    public static string Normalize(string? value) =>
        Regex.Replace((value ?? "").ToLowerInvariant().Replace('ё', 'е'), @"\s+", " ").Trim();

    public static string PlainText(string? text) =>
        Regex.Replace(Regex.Replace(text ?? "", @"!?\[([^\]]*)\]\([^)]*\)", "$1"), @"[*_`#>~]", "").Trim();

    /// <summary>A piece of the text around the first matching word, with "…" where it was cut.</summary>
    public static string Snippet(string text, IReadOnlyList<string> words)
    {
        var flat = Regex.Replace(text, @"\s+", " ").Trim();
        var normalized = Normalize(flat); // normally the same length: only case and ё change
        var pos = normalized.Length != flat.Length
            ? 0 // rare letters change length when lower-cased — then just show the beginning
            : words.Select(w => normalized.IndexOf(w, StringComparison.Ordinal)).Where(i => i >= 0).DefaultIfEmpty(0).Min();

        var start = Math.Max(0, pos - SnippetRadius);
        var end = Math.Min(flat.Length, pos + SnippetRadius * 2);
        var snippet = flat[start..end].Trim();
        return (start > 0 ? "…" : "") + snippet + (end < flat.Length ? "…" : "");
    }
}
