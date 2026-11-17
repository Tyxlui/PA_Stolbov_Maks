using System.Globalization;
using System.Text;
using System.Text.Json;
using NATS.Client;
using Shared;
using StackExchange.Redis;

namespace RankCalculator;

public class Program
{
    private const string RankSubject = "valuator.processing.rank";

    private const string QueueGroup = "rank-calculators";

    private const string RankCalculatedSubject = "events.rank.calculated";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Main(string[] args)
    {
        int pid = Environment.ProcessId;

        using var storage = new ShardedStorage();

        using IConnection nats = Messaging.Connect();

        using IAsyncSubscription subscription = nats.SubscribeAsync(RankSubject, QueueGroup, (sender, e) =>
        {
            try
            {
                string id = Encoding.UTF8.GetString(e.Message.Data);

                if (!storage.TryResolveShard(id, out string shardKey, out IDatabase db))
                {
                    Console.WriteLine($"[{pid}] Shard for id {id} not found, skipping");
                    return;
                }

                string? text = db.StringGet("TEXT-" + id);
                if (text is null)
                {
                    Console.WriteLine($"[{pid}] Text for id {id} not found in shard {shardKey}, skipping");
                    return;
                }

                double rank = CalculateRank(text);

                db.StringSet("RANK-" + id, rank.ToString(CultureInfo.InvariantCulture));

                var rankEvent = new { eventType = "RankCalculated", id, rank };
                nats.Publish(RankCalculatedSubject, JsonSerializer.SerializeToUtf8Bytes(rankEvent, JsonOptions));

                Console.WriteLine($"[{pid}] {id}: rank = {rank.ToString(CultureInfo.InvariantCulture)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{pid}] Error: {ex.Message}");
            }
        });

        Console.WriteLine($"[{pid}] RankCalculator started, waiting for messages (Ctrl+C to exit)");

        var exit = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            exit.Set();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => exit.Set();

        exit.Wait();

        subscription.Drain();
        nats.Drain();
    }

    private static double CalculateRank(string text)
    {
        if (text.Length == 0)
            return 0;

        int nonAlphabetic = text.Count(c =>
            !((c >= 'a' && c <= 'z') ||
              (c >= 'A' && c <= 'Z') ||
              (c >= 'а' && c <= 'я') ||
              (c >= 'А' && c <= 'Я')));

        return (double)nonAlphabetic / text.Length;
    }
}
