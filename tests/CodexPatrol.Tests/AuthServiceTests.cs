using System.Text.Json;
using CodexPatrol.Models;
using CodexPatrol.Services;
using Xunit;

namespace CodexPatrol.Tests;

public sealed class AuthServiceTests
{
    [Fact]
    public void TrySetPassword_ShouldPersistHashAndVerifyPassword()
    {
        var baseDirectory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(baseDirectory, "appsettings.json"), "{\"PatrolSettings\":{}}\n");
            var settings = new PatrolSettings();
            var auth = new AuthService(settings, baseDirectory);

            var saved = auth.TrySetPassword("Password@123", out var error);

            Assert.True(saved);
            Assert.Equal(string.Empty, error);
            Assert.True(auth.HasPasswordConfigured());
            Assert.True(auth.VerifyPassword("Password@123"));
            Assert.False(auth.VerifyPassword("Password@456"));
            Assert.StartsWith("PBKDF2-SHA256$", settings.LoginPasswordHash, StringComparison.Ordinal);

            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(baseDirectory, "appsettings.json")));
            var savedHash = document.RootElement
                .GetProperty("PatrolSettings")
                .GetProperty("LoginPasswordHash")
                .GetString();

            Assert.Equal(settings.LoginPasswordHash, savedHash);
            Assert.DoesNotContain("Password@123", savedHash ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(baseDirectory);
        }
    }

    [Fact]
    public void TrySetPassword_WithShortPassword_ShouldReject()
    {
        var auth = new AuthService(new PatrolSettings());

        var saved = auth.TrySetPassword("1234567", out var error);

        Assert.False(saved);
        Assert.Equal("密码长度不能少于 8 位", error);
        Assert.False(auth.HasPasswordConfigured());
    }

    [Fact]
    public void SessionToken_ShouldAuthenticateAcrossServiceInstances()
    {
        var settings = new PatrolSettings { LoginPasswordHash = "PBKDF2-SHA256$1$AQ==$AQ==" };
        var first = new AuthService(settings);
        var second = new AuthService(settings);

        var token = first.CreateSessionToken();

        Assert.True(first.IsAuthenticated(token));
        Assert.True(second.IsAuthenticated(token));
    }

    [Fact]
    public void SessionToken_ShouldRejectTamperedToken()
    {
        var auth = new AuthService(new PatrolSettings { LoginPasswordHash = "PBKDF2-SHA256$1$AQ==$AQ==" });
        var token = auth.CreateSessionToken();
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        Assert.False(auth.IsAuthenticated(tampered));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexPatrolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
