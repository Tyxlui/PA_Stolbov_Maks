using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shared;
using StackExchange.Redis;

namespace Valuator.Pages;

public class SummaryModel : PageModel
{
    private readonly ILogger<SummaryModel> _logger;
    private readonly ShardedStorage _storage;

    public SummaryModel(
        ILogger<SummaryModel> logger,
        ShardedStorage storage)
    {
        _logger = logger;
        _storage = storage;
    }
    public double? Rank { get; set; }
    public double Similarity { get; set; }

    public IActionResult OnGet(string id)
    {
        _logger.LogDebug("Summary for {Id}", id);

        if (!_storage.TryResolveShard(id, out string shardKey, out IDatabase db))
        {
            return NotFound();
        }

        string? author = db.StringGet("AUTHOR-" + id);
        string? currentUser = User.Identity?.Name;

        if (author is null || !string.Equals(author, currentUser, StringComparison.Ordinal))
        {
            _logger.LogWarning("User {User} tried to open summary {Id} of another user", currentUser, id);
            return new ContentResult
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentType = "text/plain; charset=utf-8",
                Content = "Доступ запрещён: результаты проверки доступны только автору текста."
            };
        }

        string? rankValue = db.StringGet("RANK-" + id);
        string? similarityValue = db.StringGet("SIMILARITY-" + id);

        if (double.TryParse(rankValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double rank))
        {
            Rank = rank;
        }

        if (double.TryParse(similarityValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double similarity))
        {
            Similarity = similarity;
        }

        return Page();
    }
}
