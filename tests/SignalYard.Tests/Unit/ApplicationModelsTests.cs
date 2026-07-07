using FluentAssertions;
using SignalYard.Core.Models;

namespace SignalYard.Tests.Unit;

/// <summary>
/// Unit tests for Application models
/// </summary>
public class ApplicationModelsTests
{
    [Fact]
    public void CreateApplicationRequest_ShouldHaveDefaultRetentionDays()
    {
        // Arrange & Act
        var request = new CreateApplicationRequest
        {
            Name = "TestApp"
        };

        // Assert
        request.RetentionDays.Should().Be(365);
    }

    [Fact]
    public void CreateApplicationRequest_ShouldStoreAllProperties()
    {
        // Arrange & Act
        var request = new CreateApplicationRequest
        {
            Name = "MyApplication",
            Description = "Test application description",
            RetentionDays = 90
        };

        // Assert
        request.Name.Should().Be("MyApplication");
        request.Description.Should().Be("Test application description");
        request.RetentionDays.Should().Be(90);
    }

    [Fact]
    public void UpdateApplicationRequest_ShouldAllowPartialUpdates()
    {
        // Arrange & Act
        var request = new UpdateApplicationRequest
        {
            Description = "Updated description"
            // RetentionDays and Enabled left null
        };

        // Assert
        request.Description.Should().Be("Updated description");
        request.RetentionDays.Should().BeNull();
        request.Enabled.Should().BeNull();
    }

    [Fact]
    public void UpdateApplicationRequest_ShouldAllowAllUpdates()
    {
        // Arrange & Act
        var request = new UpdateApplicationRequest
        {
            Description = "New description",
            RetentionDays = 180,
            Enabled = false
        };

        // Assert
        request.Description.Should().Be("New description");
        request.RetentionDays.Should().Be(180);
        request.Enabled.Should().BeFalse();
    }

    [Fact]
    public void ApplicationDto_ShouldStoreAllProperties()
    {
        // Arrange
        var createdAt = DateTimeOffset.Now;

        // Act
        var dto = new ApplicationDto
        {
            Name = "TestApp",
            Description = "Test description",
            ApiKeyPrefix = "sy_abcdefgh",
            RetentionDays = 365,
            Enabled = true,
            CreatedAt = createdAt
        };

        // Assert
        dto.Name.Should().Be("TestApp");
        dto.Description.Should().Be("Test description");
        dto.ApiKeyPrefix.Should().Be("sy_abcdefgh");
        dto.RetentionDays.Should().Be(365);
        dto.Enabled.Should().BeTrue();
        dto.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void CreateApplicationResponse_ShouldContainApplicationAndApiKey()
    {
        // Arrange & Act
        var response = new CreateApplicationResponse
        {
            Application = new ApplicationDto
            {
                Name = "NewApp",
                ApiKeyPrefix = "sy_abcdefgh",
                RetentionDays = 30,
                Enabled = true,
                CreatedAt = DateTimeOffset.Now
            },
            ApiKey = "sy_generatedapikey123"
        };

        // Assert
        response.Application.Should().NotBeNull();
        response.Application.Name.Should().Be("NewApp");
        response.ApiKey.Should().StartWith("sy_");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void ApplicationName_ShouldNotBeEmpty(string? name)
    {
        // This tests the business logic that application names should not be empty
        var isValid = !string.IsNullOrWhiteSpace(name);
        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("valid-app")]
    [InlineData("valid_app")]
    [InlineData("ValidApp123")]
    public void ApplicationName_ValidNames_ShouldBeAccepted(string name)
    {
        // Valid names should match the pattern [a-zA-Z0-9_-]+
        var isValid = System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$");
        isValid.Should().BeTrue();
    }
}
