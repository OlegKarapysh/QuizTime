using System.Net;
using QuizBank.Links;

namespace QuizBank.Tests;

/// <summary>The link checker against a fake HTTP handler: no network, no waiting.</summary>
public sealed class LinkCheckerTests : IDisposable
{
    private const string SampleUrl = "https://learn.microsoft.com/dotnet/api/system.string.intern?view=net-10.0";
    private const string Page = """<html><body><h2 id="remarks">Remarks</h2><a name='legacy'></a><h3 id=bare>x</h3></body></html>""";

    private readonly TestRepo _repo = new();
    private readonly List<TimeSpan> _delays = [];

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task Working_links_pass()
    {
        var handler = new FakeHandler(_ => Html(Page));

        var report = await CheckAsync(handler);

        Assert.Empty(report.Errors);
        Assert.Equal((1, 1), (report.Links, report.Questions));
        Assert.Equal([SampleUrl], handler.Requests);
    }

    [Fact]
    public async Task Missing_page_fails_at_once()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var report = await CheckAsync(handler);

        Assert.Equal(
            $"{TestRepo.QuestionPath}:{_repo.LineOf(TestRepo.QuestionPath, "url:")}:10: '{SampleUrl}' returned HTTP 404 Not Found.",
            Assert.Single(report.Errors).ToString());
        Assert.Single(handler.Requests);
        Assert.Empty(_delays);
    }

    [Theory]
    [InlineData("remarks")]
    [InlineData("legacy")]
    [InlineData("bare")]
    public Task Anchors_match_ids_and_names(string anchor) => AssertAnchorAsync(anchor, found: true);

    [Fact]
    public async Task Missing_anchor_is_reported()
    {
        _repo.Replace(TestRepo.QuestionPath, SampleUrl, SampleUrl + "#examples");

        var report = await CheckAsync(new FakeHandler(_ => Html(Page)));

        Assert.Equal(
            $"Anchor '#examples' is not on '{SampleUrl}'; link to a section id that exists on the page.",
            Assert.Single(report.Errors).Message);
    }

    [Fact]
    public Task Anchors_are_case_sensitive() => AssertAnchorAsync("Remarks", found: false);

    [Fact]
    public async Task Transient_errors_are_retried_with_backoff()
    {
        var responses = new Queue<HttpResponseMessage>(
            [new(HttpStatusCode.ServiceUnavailable), new(HttpStatusCode.TooManyRequests), Html(Page)]);
        var handler = new FakeHandler(_ => responses.Dequeue());

        var report = await CheckAsync(handler);

        Assert.Empty(report.Errors);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], _delays);
    }

    [Fact]
    public async Task Retry_after_is_honoured_up_to_a_minute()
    {
        var first = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        first.Headers.RetryAfter = new(TimeSpan.FromSeconds(7));
        var second = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        second.Headers.RetryAfter = new(TimeSpan.FromMinutes(10));
        var responses = new Queue<HttpResponseMessage>([first, second, Html(Page)]);

        var report = await CheckAsync(new FakeHandler(_ => responses.Dequeue()));

        Assert.Empty(report.Errors);
        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromMinutes(1)], _delays);
    }

    [Fact]
    public async Task Persistent_server_errors_fail_after_three_attempts()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var report = await CheckAsync(handler);

        Assert.Equal($"'{SampleUrl}' failed 3 times; the last attempt got HTTP 500 Internal Server Error.", Assert.Single(report.Errors).Message);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Network_errors_are_retried()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("Connection refused"));

        var report = await CheckAsync(handler);

        Assert.Equal($"'{SampleUrl}' failed 3 times; the last attempt failed: Connection refused.", Assert.Single(report.Errors).Message);
    }

    [Fact]
    public async Task Timeouts_are_retried()
    {
        var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Html(Page);
        });

        var report = await CheckAsync(handler, requestTimeout: TimeSpan.FromMilliseconds(100));

        Assert.Equal($"'{SampleUrl}' failed 3 times; the last attempt timed out after 0.1 seconds.", Assert.Single(report.Errors).Message);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Each_page_is_fetched_once()
    {
        AddSource(SampleUrl + "#remarks");
        AddSource(SampleUrl + "#legacy");
        var handler = new FakeHandler(_ => Html(Page));

        var report = await CheckAsync(handler);

        Assert.Empty(report.Errors);
        Assert.Equal(3, report.Links);
        Assert.Equal([SampleUrl], handler.Requests);
    }

    [Theory]
    [InlineData("https://github.com/dotnet/runtime/blob/v10.0.0/src/Example.cs#L10-L20")]
    [InlineData("https://www.postgresql.org/docs/16/sql-select.html#:~:text=ORDER%20BY")]
    public async Task Anchors_the_browser_resolves_are_not_looked_up(string url)
    {
        _repo.Replace(TestRepo.QuestionPath, SampleUrl, url);

        var report = await CheckAsync(new FakeHandler(_ => Html("<html></html>")));

        Assert.Empty(report.Errors);
    }

    private async Task AssertAnchorAsync(string anchor, bool found)
    {
        _repo.Replace(TestRepo.QuestionPath, SampleUrl, $"{SampleUrl}#{anchor}");

        var report = await CheckAsync(new FakeHandler(_ => Html(Page)));

        Assert.Equal(found, report.Errors.Count == 0);
    }

    private void AddSource(string url) =>
        _repo.Replace(TestRepo.QuestionPath, "verification:", $"""
              - kind: further
                title: More
                url: {url}
                look_for: The same page again
                checked: 2026-10-29
            verification:
            """);

    private Task<LinkReport> CheckAsync(HttpMessageHandler handler, TimeSpan? requestTimeout = null)
    {
        var checker = new LinkChecker(handler, (delay, _) =>
        {
            _delays.Add(delay);
            return Task.CompletedTask;
        }, requestTimeout);
        return checker.CheckAsync(_repo.Questions(), TestContext.Current.CancellationToken);
    }

    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html) };
}

/// <summary>Answers every request with a callback and records the URLs asked for.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests) Requests.Add(request.RequestUri!.ToString());
        return respond(request, cancellationToken);
    }
}
