using System.Net.Sockets;
using MedMateAI.Application.Common;

namespace MedMateAI.Application.Helpers;

public static class BackgroundJobRetry
{
    public static bool IsRetryable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            switch (current)
            {
                case TransientRemoteCallException:
                case TimeoutException:
                case HttpRequestException:
                case IOException:
                case SocketException:
                    return true;
                case TaskCanceledException taskCanceled
                    when !taskCanceled.CancellationToken.IsCancellationRequested:
                    return true;
            }
        }

        return false;
    }

    public static bool IsTransientHttpStatusCode(int statusCode) =>
        statusCode is 408 or 429 or >= 500;

    public static bool IsFinalAttempt(int retryCount, int maxAttempts) =>
        retryCount >= Math.Max(0, maxAttempts);
}
