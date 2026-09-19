namespace MedMateAI.Application.Common;

/// <summary>
/// Transient failure talking to an HTTP / 3rd-party dependency (timeout, 5xx, 429, network).
/// Safe to retry via Hangfire AutomaticRetry while the session stays Processing.
/// </summary>
public sealed class TransientRemoteCallException : Exception
{
    public TransientRemoteCallException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
