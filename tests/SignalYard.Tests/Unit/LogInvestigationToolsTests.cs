using Azure;
using Azure.Data.Tables;
using FluentAssertions;
using SignalYard.Core.Entities;
using SignalYard.Core.Services;
using SignalYard.Web.Mcp;
using Moq;

namespace SignalYard.Tests.Unit;

/// <summary>
/// Unit tests for the MCP log-investigation tools. Real storage services are constructed over a
/// mocked <see cref="TableServiceClient"/> so the tool logic (defaulting, clamping, the search
/// post-filter, and the "query all applications" fan-out) is exercised end-to-end.
/// </summary>
public class LogInvestigationToolsTests
{
    // A single month so a specific-application query resolves to exactly one partition,
    // giving deterministic, non-duplicated results from the mock.
    private static readonly DateTimeOffset RangeFrom = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RangeTo = new(2026, 7, 31, 23, 59, 59, TimeSpan.Zero);

    private static List<LogEntry> SampleLogs() =>
    [
        MakeLog("App1", new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            "Information", "User alice logged in", properties: "{\"User\":\"alice\"}"),
        MakeLog("App1", new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            "Error", "Payment failed", exception: "System.Exception: boom", properties: "{\"OrderId\":42}"),
        MakeLog("App1", new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero),
            "Warning", "Slow response", properties: null),
    ];

    [Fact]
    public async Task ListApplications_ReturnsAllApplications()
    {
        var (logStore, appStore) = BuildServices(SampleLogs());

        var apps = await LogInvestigationTools.ListApplications(appStore);

        apps.Select(a => a.Name).Should().BeEquivalentTo("App1", "App2");
    }

    [Fact]
    public async Task QueryLogs_ReturnsProjectedEntries_NewestFirst()
    {
        var (logStore, appStore) = BuildServices(SampleLogs());

        var result = await LogInvestigationTools.QueryLogs(
            logStore, appStore, application: "App1", from: RangeFrom, to: RangeTo);

        result.TotalReturned.Should().Be(3);
        result.Entries.Select(e => e.Message).Should()
            .ContainInOrder("User alice logged in", "Payment failed", "Slow response");
        // Properties are omitted by default to keep the payload small.
        result.Entries.Should().OnlyContain(e => e.Properties == null);
    }

    [Fact]
    public async Task QueryLogs_IncludeProperties_PopulatesProperties()
    {
        var (logStore, appStore) = BuildServices(SampleLogs());

        var result = await LogInvestigationTools.QueryLogs(
            logStore, appStore, application: "App1", from: RangeFrom, to: RangeTo, includeProperties: true);

        result.Entries.First(e => e.Message == "User alice logged in").Properties.Should().ContainKey("User");
    }

    [Theory]
    [InlineData("alice", "User alice logged in")]  // matches message + property value
    [InlineData("boom", "Payment failed")]         // matches exception
    [InlineData("OrderId", "Payment failed")]      // matches property key
    public async Task QueryLogs_Search_FiltersReturnedPage(string search, string expectedMessage)
    {
        var (logStore, appStore) = BuildServices(SampleLogs());

        var result = await LogInvestigationTools.QueryLogs(
            logStore, appStore, application: "App1", from: RangeFrom, to: RangeTo, search: search);

        result.Entries.Should().ContainSingle().Which.Message.Should().Be(expectedMessage);
    }

    [Fact]
    public async Task QueryLogs_Search_NoMatch_ReturnsEmpty()
    {
        var (logStore, appStore) = BuildServices(SampleLogs());

        var result = await LogInvestigationTools.QueryLogs(
            logStore, appStore, application: "App1", from: RangeFrom, to: RangeTo, search: "no-such-text");

        result.Entries.Should().BeEmpty();
        result.TotalReturned.Should().Be(0);
    }

    [Fact]
    public async Task QueryLogs_ClampsMaxResultsToOneThousand()
    {
        int? capturedMaxPerPage = null;
        var (logStore, appStore) = BuildServices(SampleLogs(), maxPerPage => capturedMaxPerPage = maxPerPage);

        await LogInvestigationTools.QueryLogs(
            logStore, appStore, application: "App1", from: RangeFrom, to: RangeTo, maxResults: 5000);

        capturedMaxPerPage.Should().Be(1000);
    }

    [Fact]
    public async Task QueryLogs_NoApplication_QueriesAllApplications()
    {
        var (logStore, appStore) = BuildServices(SampleLogs());

        // With no application specified, the tool must fetch all app names and fan out;
        // otherwise the storage layer returns nothing.
        var result = await LogInvestigationTools.QueryLogs(
            logStore, appStore, application: null, from: RangeFrom, to: RangeTo);

        result.Entries.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetLogStats_NoApplication_AggregatesAcrossApplications()
    {
        var (logStore, appStore) = BuildServices(SampleLogs());

        var stats = await LogInvestigationTools.GetLogStats(
            logStore, appStore, application: null, from: RangeFrom, to: RangeTo);

        stats.TotalCount.Should().BeGreaterThan(0);
        stats.ErrorCount.Should().BeGreaterThan(0); // the sample contains an Error entry
    }

    // --- helpers -------------------------------------------------------------------------------

    private static LogEntry MakeLog(
        string application,
        DateTimeOffset timestamp,
        string level,
        string message,
        string? exception = null,
        string? properties = null) => new()
    {
        PartitionKey = LogEntry.CreatePartitionKey(application, timestamp),
        RowKey = LogEntry.CreateRowKey(timestamp),
        LogTimestamp = timestamp,
        Application = application,
        Level = level,
        Message = message,
        Exception = exception,
        Properties = properties
    };

    private static (LogStorageService logStore, ApplicationStorageService appStore) BuildServices(
        List<LogEntry> logs,
        Action<int?>? onLogQuery = null)
    {
        var tableServiceClient = new Mock<TableServiceClient>();

        tableServiceClient.Setup(x => x.GetTableClient("Logs")).Returns(BuildLogsTable(logs, onLogQuery));
        tableServiceClient.Setup(x => x.GetTableClient("Applications")).Returns(BuildApplicationsTable());
        tableServiceClient.Setup(x => x.GetTableClient("ApiKeys")).Returns(Mock.Of<TableClient>());

        var apiKeyService = new ApiKeyService(tableServiceClient.Object);
        var appStore = new ApplicationStorageService(tableServiceClient.Object, apiKeyService);
        var logStore = new LogStorageService(tableServiceClient.Object);
        return (logStore, appStore);
    }

    private static TableClient BuildLogsTable(List<LogEntry> logs, Action<int?>? onLogQuery)
    {
        var mock = new Mock<TableClient>();
        var pageable = AsyncPageable<LogEntry>.FromPages(
            [Page<LogEntry>.FromValues(logs, null, Mock.Of<Response>())]);

        mock.Setup(x => x.QueryAsync<LogEntry>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, int?, IEnumerable<string>, CancellationToken>((_, maxPerPage, _, _) => onLogQuery?.Invoke(maxPerPage))
            .Returns(pageable);

        return mock.Object;
    }

    private static TableClient BuildApplicationsTable()
    {
        var mock = new Mock<TableClient>();
        var apps = new List<Application>
        {
            new() { Name = "App1", Description = "First app", ApiKeyPrefix = "sy_aaa", Enabled = true, RetentionDays = 30, CreatedAt = RangeFrom },
            new() { Name = "App2", Description = "Second app", ApiKeyPrefix = "sy_bbb", Enabled = true, RetentionDays = 90, CreatedAt = RangeFrom },
        };
        var pageable = AsyncPageable<Application>.FromPages(
            [Page<Application>.FromValues(apps, null, Mock.Of<Response>())]);

        mock.Setup(x => x.QueryAsync<Application>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(pageable);

        return mock.Object;
    }
}
