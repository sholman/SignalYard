using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using SignalYard.Web.Models;

namespace SignalYard.Tests.Unit;

/// <summary>
/// Unit tests for view model validation
/// </summary>
public class ViewModelValidationTests
{
    [Fact]
    public void ApplicationFormModel_ValidName_ShouldPassValidation()
    {
        // Arrange
        var model = new ApplicationFormModel
        {
            Name = "ValidAppName",
            RetentionDays = 365
        };

        // Act
        var results = ValidateModel(model);

        // Assert
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("valid-app")]
    [InlineData("valid_app")]
    [InlineData("ValidApp123")]
    [InlineData("app")]
    public void ApplicationFormModel_ValidNames_ShouldPassValidation(string name)
    {
        // Arrange
        var model = new ApplicationFormModel
        {
            Name = name,
            RetentionDays = 30
        };

        // Act
        var results = ValidateModel(model);

        // Assert
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("invalid app")]    // Contains space
    [InlineData("invalid.app")]    // Contains period
    [InlineData("invalid@app")]    // Contains @
    [InlineData("invalid/app")]    // Contains /
    public void ApplicationFormModel_InvalidNames_ShouldFailValidation(string name)
    {
        // Arrange
        var model = new ApplicationFormModel
        {
            Name = name,
            RetentionDays = 30
        };

        // Act
        var results = ValidateModel(model);

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains("Name"));
    }

    [Fact]
    public void ApplicationFormModel_MissingName_ShouldFailValidation()
    {
        // Arrange
        var model = new ApplicationFormModel
        {
            Name = null,
            RetentionDays = 30
        };

        // Act
        var results = ValidateModel(model);

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains("Name"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3651)]
    [InlineData(10000)]
    public void ApplicationFormModel_InvalidRetentionDays_ShouldFailValidation(int days)
    {
        // Arrange
        var model = new ApplicationFormModel
        {
            Name = "ValidApp",
            RetentionDays = days
        };

        // Act
        var results = ValidateModel(model);

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains("RetentionDays"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(365)]
    [InlineData(3650)]
    public void ApplicationFormModel_ValidRetentionDays_ShouldPassValidation(int days)
    {
        // Arrange
        var model = new ApplicationFormModel
        {
            Name = "ValidApp",
            RetentionDays = days
        };

        // Act
        var results = ValidateModel(model);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void LogSearchFormModel_ShouldHaveDefaults()
    {
        // Arrange & Act
        var model = new LogSearchFormModel();

        // Assert
        model.TimeRange.Should().Be("24h");
        model.Application.Should().BeNull();
        model.Level.Should().BeNull();
        model.SearchText.Should().BeNull();
    }

    [Fact]
    public void LogViewerViewModel_ShouldInitializeWithEmptyCollections()
    {
        // Arrange & Act
        var model = new LogViewerViewModel();

        // Assert
        model.Applications.Should().NotBeNull();
        model.Applications.Should().BeEmpty();
        model.Logs.Should().NotBeNull();
        model.Logs.Should().BeEmpty();
        model.SelectedTimeRange.Should().Be("24h");
    }

    [Fact]
    public void ApplicationsViewModel_ShouldInitializeWithEmptyCollections()
    {
        // Arrange & Act
        var model = new ApplicationsViewModel();

        // Assert
        model.Applications.Should().NotBeNull();
        model.Applications.Should().BeEmpty();
        model.ErrorMessage.Should().BeNull();
        model.SuccessMessage.Should().BeNull();
        model.GeneratedApiKey.Should().BeNull();
    }

    private static List<ValidationResult> ValidateModel(object model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        Validator.TryValidateObject(model, context, results, true);
        return results;
    }
}
