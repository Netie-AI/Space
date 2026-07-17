using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace NetieSpace.Services;

/// <summary>
/// Session memory for token savings: marks when a document excerpt was already sent
/// to the model, and caches Look Up / identical chat answers so repeats cost zero tokens.
/// </summary>
public sealed class ConversationMemoryCache
{
    private const int MaxLookupEntries = 256;
    private const int MaxChatEntries = 128;

    private readonly ConcurrentDictionary<string, byte> _primedDocuments = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _lookupAnswers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _chatAnswers = new(StringComparer.Ordinal);

    /// <summary>True if this document fingerprint was already included in a chat system prompt.</summary>
    public bool IsDocumentPrimed(string documentFingerprint) =>
        !string.IsNullOrEmpty(documentFingerprint) &&
        _primedDocuments.ContainsKey(documentFingerprint);

    public void MarkDocumentPrimed(string documentFingerprint)
    {
        if (string.IsNullOrEmpty(documentFingerprint))
            return;

        _primedDocuments[documentFingerprint] = 0;
    }

    public bool TryGetLookup(string word, string? surroundingContext, out string? answer)
    {
        return _lookupAnswers.TryGetValue(LookupKey(word, surroundingContext), out answer);
    }

    public void SetLookup(string word, string? surroundingContext, string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return;

        TrimIfNeeded(_lookupAnswers, MaxLookupEntries);
        _lookupAnswers[LookupKey(word, surroundingContext)] = answer;
    }

    public bool TryGetChat(string documentFingerprint, string userMessage, out string? answer)
    {
        return _chatAnswers.TryGetValue(ChatKey(documentFingerprint, userMessage), out answer);
    }

    public void SetChat(string documentFingerprint, string userMessage, string answer)
    {
        if (string.IsNullOrWhiteSpace(answer) || string.IsNullOrEmpty(documentFingerprint))
            return;

        TrimIfNeeded(_chatAnswers, MaxChatEntries);
        _chatAnswers[ChatKey(documentFingerprint, userMessage)] = answer;
    }

    public void Clear()
    {
        _primedDocuments.Clear();
        _lookupAnswers.Clear();
        _chatAnswers.Clear();
    }

    public static string LookupKey(string word, string? surroundingContext)
    {
        var w = (word ?? string.Empty).Trim().ToLowerInvariant();
        var ctx = string.IsNullOrWhiteSpace(surroundingContext)
            ? string.Empty
            : surroundingContext.Trim();
        if (ctx.Length > 400)
            ctx = ctx[..200] + ctx[^200..];

        return ToHexSha256($"lookup|{w}|{ctx}");
    }

    private static string ChatKey(string documentFingerprint, string userMessage) =>
        ToHexSha256($"chat|{documentFingerprint}|{(userMessage ?? string.Empty).Trim().ToLowerInvariant()}");

    private static void TrimIfNeeded(ConcurrentDictionary<string, string> map, int max)
    {
        if (map.Count < max)
            return;

        // Drop an arbitrary half — session cache, not LRU-critical.
        var i = 0;
        foreach (var key in map.Keys)
        {
            if (i++ % 2 == 0)
                map.TryRemove(key, out _);
            if (map.Count < max / 2)
                break;
        }
    }

    private static string ToHexSha256(string payload)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
}
