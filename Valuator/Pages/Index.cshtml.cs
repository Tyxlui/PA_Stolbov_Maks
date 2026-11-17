using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NATS.Client;
using Shared;
using StackExchange.Redis;

namespace Valuator.Pages;

public class IndexModel : PageModel
{
    private const string RankSubject = "valuator.processing.rank";

    private const string SimilarityCalculatedSubject = "events.similarity.calculated";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static readonly IReadOnlyDictionary<string, string> CountryToRegion =
        new Dictionary<string, string>
        {
            ["Russia"] = "RU",
            ["France"] = "EU",
            ["Germany"] = "EU",
            ["UAE"] = "ASIA",
            ["India"] = "ASIA",
        };

    private readonly ILogger<IndexModel> _logger;
    private readonly ShardedStorage _storage;
    private readonly IConnection _nats;

    public IndexModel(
        ILogger<IndexModel> logger,
        ShardedStorage storage,
        IConnection nats)
    {
        _logger = logger;
        _storage = storage;
        _nats = nats;
    }

    public IEnumerable<string> Countries => CountryToRegion.Keys;

    public void OnGet()
    {
    }

    public IActionResult OnPost(string? text, string? country)
    {
        text ??= string.Empty;

        string? author = User.Identity?.Name;
        if (string.IsNullOrEmpty(author))
        {
            return Challenge();
        }

        if (country is null || !CountryToRegion.TryGetValue(country, out string? shardKey))
        {
            return BadRequest("Unknown country");
        }

        _logger.LogDebug("Processing text of length {Length}, country {Country}", text.Length, country);

        string id = Guid.NewGuid().ToString();

        _storage.SaveShardKey(id, shardKey);
        ShardedStorage.LogLookup(id, shardKey);

        IDatabase db = _storage.GetShardDatabase(shardKey);

        string textKey = "TEXT-" + id;
        db.StringSet(textKey, text);
        db.StringSet("COUNTRY-" + id, country);
        db.StringSet("AUTHOR-" + id, author);

        string similarityKey = "SIMILARITY-" + id;

        double similarity = 0;

        foreach (var key in _storage.GetTextKeys(shardKey))
        {
            if (key == textKey)
                continue;

            string? oldText = db.StringGet(key);

            if (oldText == text)
            {
                similarity = 1;
                break;
            }
        }

        db.StringSet(similarityKey, similarity.ToString());

        var similarityEvent = new { eventType = "SimilarityCalculated", id, similarity };
        _nats.Publish(SimilarityCalculatedSubject, JsonSerializer.SerializeToUtf8Bytes(similarityEvent, JsonOptions));

        _nats.Publish(RankSubject, Encoding.UTF8.GetBytes(id));
        _nats.Flush();

        return Redirect($"summary?id={id}");
    }
}
