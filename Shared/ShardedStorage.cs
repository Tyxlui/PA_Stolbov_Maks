using StackExchange.Redis;

namespace Shared;

public sealed class ShardedStorage : IDisposable
{
    public static readonly string[] Regions = { "RU", "EU", "ASIA" };

    private readonly IConnectionMultiplexer _main;
    private readonly Dictionary<string, IConnectionMultiplexer> _shards = new(StringComparer.OrdinalIgnoreCase);

    public ShardedStorage()
    {
        string password = Environment.GetEnvironmentVariable("REDIS_PASSWORD") ?? string.Empty;
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("Environment variable REDIS_PASSWORD is not set");
        }

        _main = Connect("DB_MAIN", password);
        foreach (string region in Regions)
        {
            _shards[region] = Connect("DB_" + region, password);
        }
    }

    private static IConnectionMultiplexer Connect(string variable, string password)
    {
        string? endpoint = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(
                $"Environment variable {variable} is not set (expected format host:port, e.g. localhost:6000)");
        }

        ConfigurationOptions options = ConfigurationOptions.Parse(endpoint);
        options.Password = password;
        return ConnectionMultiplexer.Connect(options);
    }

    public IDatabase GetMainDatabase()
    {
        return _main.GetDatabase();
    }

    public void SaveShardKey(string id, string shardKey)
    {
        _main.GetDatabase().StringSet("SHARD-" + id, shardKey);
    }

    public bool TryResolveShard(string id, out string shardKey, out IDatabase db)
    {
        string? value = _main.GetDatabase().StringGet("SHARD-" + id);

        if (string.IsNullOrEmpty(value) || !_shards.ContainsKey(value))
        {
            shardKey = string.Empty;
            db = null!;
            return false;
        }

        shardKey = value;
        LogLookup(id, shardKey);
        db = _shards[shardKey].GetDatabase();
        return true;
    }
    public IDatabase GetShardDatabase(string shardKey)
    {
        return _shards[shardKey].GetDatabase();
    }

    public IEnumerable<RedisKey> GetTextKeys(string shardKey)
    {
        IConnectionMultiplexer connection = _shards[shardKey];
        IServer server = connection.GetServer(connection.GetEndPoints()[0]);
        return server.Keys(pattern: "TEXT-*");
    }

    public static void LogLookup(string id, string shardKey)
    {
        Console.WriteLine($"LOOKUP: {id}, {shardKey}");
    }

    public void Dispose()
    {
        _main.Dispose();
        foreach (IConnectionMultiplexer shard in _shards.Values)
        {
            shard.Dispose();
        }
    }
}
