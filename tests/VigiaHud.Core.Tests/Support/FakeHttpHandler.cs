using System.Net;

namespace VigiaHud.Core.Tests.Support;

/// <summary>
/// Stands in for the network: answers each request with the next prepared response (or throws the prepared
/// exception) and keeps what was asked, body included, so the tests can check both sides.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _answers = new();

    public List<Recorded> Requests { get; } = [];

    public FakeHttpHandler Answer(HttpStatusCode status, string body)
    {
        _answers.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));
        return this;
    }

    public FakeHttpHandler Throw(Exception error)
    {
        _answers.Enqueue((_, _) => Task.FromException<HttpResponseMessage>(error));
        return this;
    }

    /// <summary>Never answers until the request is cancelled: a long poll on a dead link.</summary>
    public FakeHttpHandler Hang()
    {
        _answers.Enqueue(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var contentType = request.Content?.Headers.ContentType?.MediaType;
        Requests.Add(new Recorded(request.Method, request.RequestUri!, contentType, body));
        if (_answers.Count == 0)
        {
            throw new InvalidOperationException($"No answer prepared for {request.RequestUri}");
        }

        return await _answers.Dequeue()(request, cancellationToken);
    }

    public sealed record Recorded(HttpMethod Method, Uri Uri, string? ContentType, string Body);
}
