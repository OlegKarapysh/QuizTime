using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using QuizBank.Model;
using QuizBank.Validation;

namespace QuizBank.Links;

public sealed record LinkReport(IReadOnlyList<Diagnostic> Errors, int Links, int Questions)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Opens every source link of the given questions and checks that the page loads and, when the link has an
/// anchor, that the page has an element with that id. Each page is fetched once. Timeouts, 429 and 5xx are
/// retried with backoff; any other failure, a 404 included, fails at once.
/// </summary>
public sealed partial class LinkChecker
{
    public const int MaxAttempts = 3;
    public const string UserAgent = "QuizTime-check-links/1.0 (+https://github.com/OlegKarapysh/QuizTime)";

    private const int Parallelism = 4;
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);

    private readonly HttpMessageInvoker _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _requestTimeout;

    /// <param name="delay">Waits between retries; tests pass one that doesn't sleep.</param>
    /// <param name="requestTimeout">How long one request may take; 30 seconds by default.</param>
    public LinkChecker(HttpMessageHandler handler, Func<TimeSpan, CancellationToken, Task>? delay = null, TimeSpan? requestTimeout = null)
    {
        _http = new HttpMessageInvoker(handler, disposeHandler: false);
        _delay = delay ?? Task.Delay;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>A handler for real runs: follows redirects and decompresses.</summary>
    public static HttpMessageHandler CreateDefaultHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
    };

    public async Task<LinkReport> CheckAsync(IReadOnlyList<BankFile<Question>> questions, CancellationToken cancellationToken)
    {
        var links = questions
            .SelectMany(file => file.Value.Sources.Select((source, i) => (File: file, Pointer: $"/sources/{i}/url", source.Url)))
            .ToList();

        var pageUrls = links.Select(link => PageUrl(link.Url)).Distinct().ToList();
        var pages = new PageResult[pageUrls.Count];
        var options = new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(Enumerable.Range(0, pageUrls.Count), options,
            async (i, token) => pages[i] = await FetchAsync(pageUrls[i], token));
        var pageByUrl = pageUrls.Zip(pages).ToDictionary(p => p.First, p => p.Second);

        var errors = new List<Diagnostic>();
        foreach (var (file, pointer, url) in links)
        {
            var page = pageByUrl[PageUrl(url)];
            var problem = page.Error ?? MissingAnchor(url, page.Html!);
            if (problem is null) continue;

            var position = file.Document.PositionOf(pointer);
            errors.Add(new Diagnostic(file.Path, position.Line, position.Column, problem));
        }

        return new LinkReport(errors, links.Count, questions.Count);
    }

    private sealed record PageResult(string? Html, string? Error);

    private async Task<PageResult> FetchAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            string transient;
            TimeSpan? retryAfter = null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(UserAgent);
                request.Headers.Accept.ParseAdd("text/html");
                using var response = await _http.SendAsync(request, timeout.Token);

                if (response.IsSuccessStatusCode) return new PageResult(await response.Content.ReadAsStringAsync(timeout.Token), null);

                var status = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();
                if (!IsTransient(response.StatusCode)) return new PageResult(null, $"'{url}' returned {status}.");
                transient = $"got {status}";
                retryAfter = RetryAfter(response);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                transient = string.Create(CultureInfo.InvariantCulture, $"timed out after {_requestTimeout.TotalSeconds:0.#} seconds");
            }
            catch (HttpRequestException e)
            {
                transient = $"failed: {e.Message}";
            }

            if (attempt == MaxAttempts)
                return new PageResult(null, $"'{url}' failed {MaxAttempts} times; the last attempt {transient}.");

            // 2 s, then 4 s, unless the server says how long to wait.
            await _delay(retryAfter ?? FirstRetryDelay * Math.Pow(2, attempt - 1), cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        var wait = header?.Delta ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        return wait is { } w ? TimeSpan.FromTicks(Math.Clamp(w.Ticks, 0, MaxRetryAfter.Ticks)) : null;
    }

    private static string PageUrl(string url) => url.Split('#', 2)[0];

    private static string? MissingAnchor(string url, string html)
    {
        var parts = url.Split('#', 2);
        if (parts.Length < 2 || parts[1].Length == 0) return null;

        var anchor = Uri.UnescapeDataString(parts[1]);
        if (!IsCheckable(parts[0], anchor)) return null;

        // A fragment targets an element's id or an <a name>; data-id, <meta name> and the like don't count.
        var escaped = Regex.Escape(anchor);
        var pattern = $"""(?i:\sid|<a\b[^>]*\sname)\s*=\s*(?:"{escaped}"|'{escaped}'|{escaped}(?=[\s/>]))""";
        return Regex.IsMatch(html, pattern)
            ? null
            : $"Anchor '#{anchor}' is not on '{parts[0]}'; link to a section id that exists on the page.";
    }

    // Text fragments and GitHub's line anchors (#L10-L20) are resolved by the browser, not found in the HTML.
    private static bool IsCheckable(string pageUrl, string anchor) =>
        !anchor.StartsWith(":~:", StringComparison.Ordinal) &&
        !(pageUrl.StartsWith("https://github.com/", StringComparison.Ordinal) && GitHubLineAnchor().IsMatch(anchor));

    [GeneratedRegex(@"^L\d+(?:C\d+)?(?:-L\d+(?:C\d+)?)?$")]
    private static partial Regex GitHubLineAnchor();
}
