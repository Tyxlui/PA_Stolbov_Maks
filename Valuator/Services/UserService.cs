using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Shared;

namespace Valuator.Services;

public sealed class UserService
{
    private const string UsersKey = "USERS";
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 310_000;

    private static readonly Regex LoginRegex = new("^[A-Za-z0-9_.-]{3,32}$", RegexOptions.Compiled);

    private static readonly string DummyHash = HashPassword("dummy-password-for-timing");

    private readonly ShardedStorage _storage;

    public UserService(ShardedStorage storage)
    {
        _storage = storage;
    }

    public static bool IsValidLogin(string? login) => login is not null && LoginRegex.IsMatch(login.Trim());

    public static string Normalize(string login) => login.Trim().ToLowerInvariant();

    public bool Register(string login, string password)
    {
        return _storage.GetMainDatabase().HashSet(UsersKey, Normalize(login), HashPassword(password), StackExchange.Redis.When.NotExists);
    }

    public bool Verify(string login, string password)
    {
        string? stored = _storage.GetMainDatabase().HashGet(UsersKey, Normalize(login));

        if (stored is null)
        {
            VerifyHash(password, DummyHash);
            return false;
        }

        return VerifyHash(password, stored);
    }

    public static Task SignInAsync(HttpContext context, string login)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, Normalize(login)) },
            CookieAuthenticationDefaults.AuthenticationScheme);

        return context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
    }

    private static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyHash(string password, string stored)
    {
        string[] parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out int iterations))
        {
            return false;
        }

        byte[] salt = Convert.FromBase64String(parts[2]);
        byte[] expected = Convert.FromBase64String(parts[3]);
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
