using System.Globalization;
using System.Text.Json;
using NATS.Client;
using Shared;

namespace EventsLogger;

public class Program
{
    private const string RankCalculatedSubject = "events.rank.calculated";
    private const string SimilarityCalculatedSubject = "events.similarity.calculated";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Main(string[] args)
    {
        int pid = Environment.ProcessId;

        using IConnection nats = Messaging.Connect();

        using IAsyncSubscription rankSubscription = nats.SubscribeAsync(RankCalculatedSubject, (sender, e) =>
        {
            try
            {
                var evt = JsonSerializer.Deserialize<RankCalculatedEvent>(e.Message.Data, JsonOptions);
                if (evt is null)
                    return;

                Console.WriteLine(
                    $"[{pid}] Event: {evt.EventType}; Id: {evt.Id}; Rank: {evt.Rank.ToString(CultureInfo.InvariantCulture)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{pid}] Error: {ex.Message}");
            }
        });

        using IAsyncSubscription similaritySubscription = nats.SubscribeAsync(SimilarityCalculatedSubject, (sender, e) =>
        {
            try
            {
                var evt = JsonSerializer.Deserialize<SimilarityCalculatedEvent>(e.Message.Data, JsonOptions);
                if (evt is null)
                    return;

                Console.WriteLine(
                    $"[{pid}] Event: {evt.EventType}; Id: {evt.Id}; Similarity: {evt.Similarity.ToString(CultureInfo.InvariantCulture)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{pid}] Error: {ex.Message}");
            }
        });

        Console.WriteLine($"[{pid}] EventsLogger started, waiting for events (Ctrl+C to exit)");

        var exit = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            exit.Set();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => exit.Set();

        exit.Wait();

        nats.Drain();
    }
}

public record RankCalculatedEvent(string EventType, string Id, double Rank);

public record SimilarityCalculatedEvent(string EventType, string Id, double Similarity);
