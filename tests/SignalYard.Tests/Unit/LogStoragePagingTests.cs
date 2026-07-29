using Azure;
using Azure.Data.Tables;
using FluentAssertions;
using SignalYard.Core.Entities;
using SignalYard.Core.Models;
using SignalYard.Core.Services;
using Moq;

namespace SignalYard.Tests.Unit;

/// <summary>
/// Unit tests for cursor paging in <see cref="LogStorageService.QueryLogsAsync"/>, which backs the
/// log viewer's infinite scroll. A mocked <see cref="TableServiceClient"/> captures the filter the
/// service builds and replays a fixed set of entries.
/// </summary>
public class LogStoragePagingTests
{
    // A single month so the query resolves to exactly one partition, giving deterministic,
    // non-duplicated results from the mock.
    private static readonly DateTimeOffset RangeFrom = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RangeTo = new(2026, 7, 31, 23, 59, 59, TimeSpan.Zero);

    private static List<LogEntry> SampleLogs() =>
    [
        MakeLog(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero), "oldest"),
        MakeLog(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero), "newest"),
        MakeLog(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero), "middle"),
    ];

    [Fact]
    public async Task QueryLogs_ReturnsNewestFirst_RegardlessOfStorageOrder()
    {
        var (service, _) = BuildService(SampleLogs());

        var response = await service.QueryLogsAsync(Request());

        response.Logs.Select(l => l.Message).Should().ContainInOrder("newest", "middle", "oldest");
    }

    [Fact]
    public async Task QueryLogs_WholeResultSetFits_ReportsNoFurtherPage()
    {
        var (service, _) = BuildService(SampleLogs());

        var response = await service.QueryLogsAsync(Request(maxResults: 10));

        response.Logs.Should().HaveCount(3);
        response.IsTruncated.Should().BeFalse();
        response.ContinuationToken.Should().BeNull();
    }

    [Fact]
    public async Task QueryLogs_MoreResultsThanPage_ReturnsPageAndCursorOfLastEntry()
    {
        var (service, _) = BuildService(SampleLogs());

        var response = await service.QueryLogsAsync(Request(maxResults: 2));

        response.Logs.Select(l => l.Message).Should().Equal("newest", "middle");
        response.IsTruncated.Should().BeTrue();
        // The cursor is the row key of the last entry handed back, so the next page resumes there.
        response.ContinuationToken.Should().Be(response.Logs[^1].Id);
    }

    [Fact]
    public async Task QueryLogs_ExactlyFillsPage_ReportsNoFurtherPage()
    {
        var (service, _) = BuildService(SampleLogs());

        var response = await service.QueryLogsAsync(Request(maxResults: 3));

        // Filling the page is not evidence of more: the over-fetch found nothing beyond it.
        response.IsTruncated.Should().BeFalse();
        response.ContinuationToken.Should().BeNull();
    }

    [Fact]
    public async Task QueryLogs_WithoutCursor_DoesNotRestrictRowKeyBeyondTheDateRange()
    {
        var (service, filters) = BuildService(SampleLogs());

        await service.QueryLogsAsync(Request());

        filters.Should().ContainSingle().Which.Should().NotContain("RowKey gt");
    }

    [Fact]
    public async Task QueryLogs_WithCursor_ResumesAfterIt()
    {
        var logs = SampleLogs();
        var (service, filters) = BuildService(logs);
        var cursor = logs[1].RowKey;

        await service.QueryLogsAsync(Request(cursor: cursor, level: "Error"));

        var filter = filters.Should().ContainSingle().Subject;
        filter.Should().Contain($"RowKey gt '{cursor}'");
        // Paging composes with the other filters rather than replacing them.
        filter.Should().Contain("Level eq 'Error'");
        filter.Should().Contain("PartitionKey eq 'App1_202607'");
    }

    [Theory]
    [InlineData("not-a-row-key")]
    [InlineData("123_abc")]
    [InlineData("1234567890123456789_0000000000000000000000000000000")] // guid too long
    [InlineData("1234567890123456789_abc' or RowKey gt '")]             // filter injection attempt
    public async Task QueryLogs_MalformedCursor_IsRejectedWithoutQuerying(string cursor)
    {
        var (service, filters) = BuildService(SampleLogs());

        var act = () => service.QueryLogsAsync(Request(cursor: cursor));

        await act.Should().ThrowAsync<ArgumentException>();
        filters.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryLogs_FetchesOneMoreThanRequested_ToDetectAFurtherPage()
    {
        int? capturedMaxPerPage = null;
        var (service, _) = BuildService(SampleLogs(), maxPerPage => capturedMaxPerPage = maxPerPage);

        await service.QueryLogsAsync(Request(maxResults: 500));

        capturedMaxPerPage.Should().Be(501);
    }

    // --- helpers -----------------------------------------------------------------------------

    private static LogQueryRequest Request(
        int maxResults = 1000, string? cursor = null, string? level = null) => new()
        {
            Application = "App1",
            From = RangeFrom,
            To = RangeTo,
            MaxResults = maxResults,
            ContinuationToken = cursor,
            Level = level
        };

    private static LogEntry MakeLog(DateTimeOffset timestamp, string message) => new()
    {
        PartitionKey = LogEntry.CreatePartitionKey("App1", timestamp),
        RowKey = LogEntry.CreateRowKey(timestamp),
        LogTimestamp = timestamp,
        Application = "App1",
        Level = "Information",
        Message = message
    };

    /// <summary>
    /// Builds the service over a mocked table that returns <paramref name="logs"/> for any query,
    /// along with the list of filters it was asked for.
    /// </summary>
    private static (LogStorageService service, List<string> filters) BuildService(
        List<LogEntry> logs,
        Action<int?>? onQuery = null)
    {
        var filters = new List<string>();
        var tableClient = new Mock<TableClient>();
        var pageable = AsyncPageable<LogEntry>.FromPages(
            [Page<LogEntry>.FromValues(logs, null, Mock.Of<Response>())]);

        tableClient.Setup(x => x.QueryAsync<LogEntry>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, int?, IEnumerable<string>, CancellationToken>((filter, maxPerPage, _, _) =>
            {
                filters.Add(filter);
                onQuery?.Invoke(maxPerPage);
            })
            .Returns(pageable);

        var tableServiceClient = new Mock<TableServiceClient>();
        tableServiceClient.Setup(x => x.GetTableClient("Logs")).Returns(tableClient.Object);

        return (new LogStorageService(tableServiceClient.Object), filters);
    }
}
