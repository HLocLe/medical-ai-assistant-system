using MedMateAI.Application.Helpers;
using MedMateAI.Application.IService;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Persistence;
using Microsoft.Extensions.Logging;

namespace MedMateAI.Application.Service;

public sealed class LabTestOcrProcessor : ILabTestOcrProcessor
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDocumentIntelligenceService _documentIntelligenceService;
    private readonly ILabTestResultAnalyzer _resultAnalyzer;
    private readonly ILogger<LabTestOcrProcessor> _logger;

    public LabTestOcrProcessor(
        IUnitOfWork unitOfWork,
        IDocumentIntelligenceService documentIntelligenceService,
        ILabTestResultAnalyzer resultAnalyzer,
        ILogger<LabTestOcrProcessor> logger)
    {
        _unitOfWork = unitOfWork;
        _documentIntelligenceService = documentIntelligenceService;
        _resultAnalyzer = resultAnalyzer;
        _logger = logger;
    }

    public Task ProcessAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        ProcessAsync(sessionId, isFinalAttempt: true, cancellationToken);

    public async Task ProcessAsync(
        Guid sessionId,
        bool isFinalAttempt,
        CancellationToken cancellationToken = default)
    {
        var session = await _unitOfWork.LabTestSessions.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
        {
            _logger.LogWarning("Lab test OCR job skipped because session {SessionId} was not found.", sessionId);
            return;
        }

        if (session.Status is LabTestSessionStatus.Completed or LabTestSessionStatus.Failed)
        {
            _logger.LogInformation(
                "Lab test OCR job skipped because session {SessionId} is already in status {Status}.",
                sessionId,
                session.Status);
            return;
        }

        if (string.IsNullOrWhiteSpace(session.DocumentUrl))
        {
            session.Status = LabTestSessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.LabTestSessions.Update(session);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(session.RawOcrText))
            {
                var rawOcrText = await _documentIntelligenceService.AnalyzeFromUrlAsync(
                    session.DocumentUrl,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(rawOcrText))
                {
                    _logger.LogWarning(
                        "dịch vụ lab test ocr trả về kết quả rỗng. {SessionId}",
                        sessionId);

                    session.Status = LabTestSessionStatus.Failed;
                    session.UpdatedAt = DateTime.UtcNow;
                    _unitOfWork.LabTestSessions.Update(session);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    return;
                }

                session.RawOcrText = rawOcrText;
                session.ProcessedAt = DateTime.UtcNow;
                session.UpdatedAt = DateTime.UtcNow;

                _unitOfWork.LabTestSessions.Update(session);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await _resultAnalyzer.AnalyzeAndPersistAsync(sessionId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (BackgroundJobRetry.IsRetryable(ex))
        {
            _logger.LogWarning(
                ex,
                "Lab test OCR hit a transient failure for session {SessionId}. FinalAttempt={IsFinalAttempt}.",
                sessionId,
                isFinalAttempt);

            if (!isFinalAttempt)
            {
                throw;
            }

            session.Status = LabTestSessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.LabTestSessions.Update(session);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lab test OCR failed for session {SessionId}.", sessionId);

            session.Status = LabTestSessionStatus.Failed;
            session.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.LabTestSessions.Update(session);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
