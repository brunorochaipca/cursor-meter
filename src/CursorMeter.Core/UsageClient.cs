using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CursorMeter;

public sealed class UsageClient : IDisposable
{
    public const string UsageUrl = "https://api2.cursor.sh/aiserver.v1.DashboardService/GetCurrentPeriodUsage";
    public const string WebUsageUrl = "https://cursor.com/api/dashboard/get-current-period-usage";
    public const string TokenUrl = "https://api2.cursor.sh/oauth/token";
    public const string ClientId = "KbZUR41cY7W6zRSdpSUJ7I7mLYBKOCmB";

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    /// <summary>ExtraSpend. Sees the usage JSON before it is parsed. Null until the minute graph subscribes.</summary>
    public static Action<string>? UsageObserved;

    public UsageClient()
        : this(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
    {
    }

    public UsageClient(HttpMessageHandler handler)
        : this(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) }, ownsClient: true)
    {
    }

    public UsageClient(HttpClient http, bool ownsClient = false)
    {
        _http = http;
        _ownsClient = ownsClient;
    }

    public async Task<UsageQuotas> FetchAsync(CursorSession session, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(session.AccessToken))
            throw new CursorAuthException("Sem sessão do Cursor.");

        var token = session.AccessToken.Trim();
        using var first = await SendUsageAsync(token, cancellationToken).ConfigureAwait(false);
        if (first.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            if (string.IsNullOrWhiteSpace(session.RefreshToken))
                throw new CursorAuthException("A sessão do Cursor expirou. Volta a entrar no Cursor.");
            token = await RefreshAsync(session.RefreshToken, cancellationToken).ConfigureAwait(false);
            using var second = await SendUsageAsync(token, cancellationToken).ConfigureAwait(false);
            return await ReadUsageAsync(second, cancellationToken).ConfigureAwait(false);
        }

        return await ReadUsageAsync(first, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UsageQuotas> FetchWebAsync(string sessionCookie, CancellationToken cancellationToken = default)
    {
        if (!IsUsableCookie(sessionCookie))
            throw new CursorAuthException("A sessão do Firefox em cursor.com não é válida.");

        using var request = new HttpRequestMessage(HttpMethod.Post, WebUsageUrl)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Cookie", FirefoxSession.CookieName + "=" + sessionCookie.Trim());
        request.Headers.TryAddWithoutValidation("Origin", "https://cursor.com");
        request.Headers.TryAddWithoutValidation("Referer", "https://cursor.com/dashboard");
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new CursorAuthException("A sessão do Firefox em cursor.com expirou. Volta a entrar.");
        return await ReadUsageAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>ExtraSpend. One page of the usage list, the same request as cursor.com/dashboard/usage.</summary>
    public async Task<string> FetchUsageEventsAsync(
        string sessionCookie, long startMs, long endMs, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!IsUsableCookie(sessionCookie))
            throw new CursorAuthException("A sessão em cursor.com não é válida.");

        var body = JsonSerializer.Serialize(new
        {
            teamId = 0,
            startDate = startMs,
            endDate = endMs,
            page,
            pageSize,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://cursor.com/api/dashboard/get-filtered-usage-events")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Cookie", FirefoxSession.CookieName + "=" + sessionCookie.Trim());
        request.Headers.TryAddWithoutValidation("Origin", "https://cursor.com");
        request.Headers.TryAddWithoutValidation("Referer", "https://cursor.com/dashboard/usage");
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new CursorAuthException("A sessão em cursor.com expirou.");
        if (!response.IsSuccessStatusCode)
            throw new CursorUsageException($"O Cursor não devolveu a utilização do dia ({(int)response.StatusCode}).");
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsUsableCookie(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && !value.Contains(';')
            && !value.Contains('\r')
            && !value.Contains('\n');

    private async Task<HttpResponseMessage> SendUsageAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, UsageUrl)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("Connect-Protocol-Version", "1");
        request.Headers.Accept.ParseAdd("application/json");
        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            grant_type = "refresh_token",
            client_id = ClientId,
            refresh_token = refreshToken,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshToken);
        request.Headers.TryAddWithoutValidation("Connect-Protocol-Version", "1");
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new CursorAuthException("A sessão do Cursor expirou. Volta a entrar no Cursor.");
        if (!response.IsSuccessStatusCode)
            throw new CursorUsageException($"O Cursor recusou a renovação da sessão ({(int)response.StatusCode}).");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("access_token", out var access)
            || access.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(access.GetString()))
        {
            throw new CursorAuthException("A sessão do Cursor expirou. Volta a entrar no Cursor.");
        }

        return access.GetString()!.Trim();
    }

    private static async Task<UsageQuotas> ReadUsageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new CursorAuthException("A sessão do Cursor expirou. Volta a entrar no Cursor.");
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new CursorUsageException("O Cursor redirecionou o pedido de utilização.");
        if (!response.IsSuccessStatusCode)
            throw new CursorUsageException($"O Cursor não devolveu a utilização ({(int)response.StatusCode}).");

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            UsageObserved?.Invoke(json); // ExtraSpend
        }
        catch (Exception)
        {
            // ExtraSpend must not break the quota read.
        }

        try
        {
            return UsageParser.Parse(json);
        }
        catch (JsonException)
        {
            throw new CursorUsageException("A resposta de utilização não é JSON.");
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
            _http.Dispose();
    }
}
