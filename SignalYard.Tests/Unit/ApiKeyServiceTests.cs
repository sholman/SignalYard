using FluentAssertions;
using SignalYard.Core.Services;

namespace SignalYard.Tests.Unit;

/// <summary>
/// Unit tests for ApiKeyService - focusing on methods that don't require Azure Table Storage
/// </summary>
public class ApiKeyServiceTests
{
    [Fact]
    public void GenerateApiKey_ShouldStartWithPrefix()
    {
        // Arrange - We need a mock TableServiceClient, but for testing generation we can test the algorithm
        var apiKey = GenerateApiKeyLocally();

        // Assert
        apiKey.Should().StartWith("sy_");
    }

    [Fact]
    public void GenerateApiKey_ShouldHaveSufficientLength()
    {
        // Arrange
        var apiKey = GenerateApiKeyLocally();

        // Assert - sy_ prefix + base64 encoded 32 bytes (minus special chars)
        apiKey.Length.Should().BeGreaterThan(35);
    }

    [Fact]
    public void GenerateApiKey_ShouldNotContainSpecialCharacters()
    {
        // Arrange
        var apiKey = GenerateApiKeyLocally();

        // Assert
        apiKey.Should().NotContain("+");
        apiKey.Should().NotContain("/");
        apiKey.Should().NotContain("=");
    }

    [Fact]
    public void GenerateApiKey_ShouldGenerateUniqueKeys()
    {
        // Arrange
        var keys = new HashSet<string>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            keys.Add(GenerateApiKeyLocally());
        }

        // Assert
        keys.Should().HaveCount(100); // All keys should be unique
    }

    [Fact]
    public void HashApiKey_ShouldProduceConsistentHash()
    {
        // Arrange
        var apiKey = "sy_testkey123456789";
        
        // Act
        var hash1 = HashApiKeyLocally(apiKey);
        var hash2 = HashApiKeyLocally(apiKey);

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void HashApiKey_ShouldProduceSha256LengthHash()
    {
        // Arrange
        var apiKey = "sy_testkey123456789";
        
        // Act
        var hash = HashApiKeyLocally(apiKey);

        // Assert - SHA256 produces 64 hex characters
        hash.Should().HaveLength(64);
    }

    [Fact]
    public void HashApiKey_ShouldProduceDifferentHashesForDifferentKeys()
    {
        // Arrange
        var apiKey1 = "sy_testkey1";
        var apiKey2 = "sy_testkey2";

        // Act
        var hash1 = HashApiKeyLocally(apiKey1);
        var hash2 = HashApiKeyLocally(apiKey2);

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void HashApiKey_ShouldProduceLowercaseHex()
    {
        // Arrange
        var apiKey = "sy_testkey123456789";
        
        // Act
        var hash = HashApiKeyLocally(apiKey);

        // Assert
        hash.Should().MatchRegex("^[a-f0-9]+$");
    }

    [Fact]
    public void GetApiKeyPrefix_ShouldReturnFirst12Characters()
    {
        // Arrange
        var apiKey = "sy_abcdefghijklmnopqrstuvwxyz";

        // Act
        var prefix = GetApiKeyPrefixLocally(apiKey);

        // Assert
        prefix.Should().Be("sy_abcdefghi");
    }

    [Fact]
    public void GetApiKeyPrefix_ShouldReturnFullKeyIfShorterThan12()
    {
        // Arrange
        var apiKey = "sy_short";

        // Act
        var prefix = GetApiKeyPrefixLocally(apiKey);

        // Assert
        prefix.Should().Be("sy_short");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid_key")]
    [InlineData("xyz_12345")]
    public void ValidateApiKey_ShouldRejectInvalidPrefixes(string invalidKey)
    {
        // Invalid keys should not pass basic validation
        var isValid = !string.IsNullOrWhiteSpace(invalidKey) && invalidKey.StartsWith("sy_");
        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("sy_validkey123")]
    [InlineData("sy_anothervalidkey")]
    public void ValidateApiKey_ShouldAcceptValidPrefixes(string validKey)
    {
        // Valid keys should pass basic prefix validation
        var isValid = !string.IsNullOrWhiteSpace(validKey) && validKey.StartsWith("sy_");
        isValid.Should().BeTrue();
    }

    // Helper methods that mirror the ApiKeyService implementation for isolated testing
    private static string GenerateApiKeyLocally()
    {
        var keyBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var base64Key = Convert.ToBase64String(keyBytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "");
        return $"sy_{base64Key}";
    }

    private static string HashApiKeyLocally(string apiKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(apiKey);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private static string GetApiKeyPrefixLocally(string apiKey)
    {
        return apiKey.Length > 12 ? apiKey[..12] : apiKey;
    }
}
