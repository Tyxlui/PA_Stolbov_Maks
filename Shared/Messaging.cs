using NATS.Client;

namespace Shared;

public static class Messaging
{
    public static IConnection Connect(string? urlOverride = null)
    {
        Options options = ConnectionFactory.GetDefaultOptions();
        options.Url = urlOverride
            ?? Environment.GetEnvironmentVariable("NATS_URL")
            ?? "nats://localhost:4222";
        options.User = Require("NATS_USER");
        options.Password = Require("NATS_PASSWORD");

        return new ConnectionFactory().CreateConnection(options);
    }

    private static string Require(string variable)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"Environment variable {variable} is not set");
        }

        return value;
    }
}
