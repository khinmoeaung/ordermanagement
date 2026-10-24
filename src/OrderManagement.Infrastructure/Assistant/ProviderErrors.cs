using System.ClientModel;
using System.Net;
using System.Net.Sockets;
using Anthropic.Exceptions;
using Google.GenAI;
using OllamaSharp.Models.Exceptions;

namespace OrderManagement.Infrastructure.Assistant;

/// <summary>Classifies provider failures: transient ones become a 503, anything else surfaces as a real error.</summary>
public static class ProviderErrors
{
    public static bool IsTransient(Exception ex, CancellationToken ct)
    {
        // A cancelled caller is never "the assistant is unavailable".
        if (ct.IsCancellationRequested) return false;

        // Provider-specific types first: Google's exceptions derive from HttpRequestException,
        // so the generic HTTP case below would otherwise treat every Google error as transient.
        switch (ex)
        {
            case ServerError:
                return true;
            case ClientError clientError:
                return clientError.StatusCode == (int)HttpStatusCode.TooManyRequests;
            case ApiException apiException:
                return IsTransientStatus(apiException.StatusCode);

            case AnthropicRateLimitException or Anthropic5xxException or AnthropicIOException:
                return true;
            case AnthropicApiException:
                return false;

            case ClientResultException result:
                return result.Status == 0 ? Inner(result, ct) : IsTransientStatus(result.Status);

            case OllamaException:
                return false;

            case HttpRequestException http:
                return http.StatusCode is null || IsTransientStatus((int)http.StatusCode.Value);

            case TaskCanceledException or TimeoutException or IOException or SocketException:
                return true;
        }

        return Inner(ex, ct);
    }

    private static bool Inner(Exception ex, CancellationToken ct) =>
        ex.InnerException is not null && IsTransient(ex.InnerException, ct);

    private static bool IsTransientStatus(int status) => status == (int)HttpStatusCode.TooManyRequests || status >= 500;
}
