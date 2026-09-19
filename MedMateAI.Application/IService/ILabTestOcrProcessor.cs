namespace MedMateAI.Application.IService;

public interface ILabTestOcrProcessor
{
    Task ProcessAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task ProcessAsync(Guid sessionId, bool isFinalAttempt, CancellationToken cancellationToken = default);
}
