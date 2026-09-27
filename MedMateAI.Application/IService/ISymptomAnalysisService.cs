using MedMateAI.Application.DTOs.Common;
using MedMateAI.Application.DTOs.SymptomAnalysis.Requests;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.Session;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.ClinicalQuestions;
using MedMateAI.Application.DTOs.SymptomAnalysis.Responses.Quota;
using MedMateAI.Domain.Enums;

namespace MedMateAI.Application.IService;

public interface ISymptomAnalysisService
{
    Task<SymptomAnalysisResponse?> GetSessionByIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<SymptomAnalysisSessionSummaryResponse>> GetSessionsByUserIdAsync(
        Guid userId,
        SymptomAnalysisSessionType? sessionType,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<SymptomAnalysisSessionSummaryResponse>> GetAllSessionsAsync(
        SymptomAnalysisSessionType? sessionType,
        SymptomAnalysisSessionStatus? status,
        Guid? userId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<SuggestClinicalQuestionsResponse> SuggestClinicalQuestionAsync(
        SuggestClinicalQuestionRequest request,
        CancellationToken cancellationToken = default);

    Task<ClinicalQuestionAnswersResponse> SubmitClinicalQuestionAnswersAsync(
        SubmitClinicalQuestionAnswersRequest request,
        CancellationToken cancellationToken = default);

    Task ProcessAnalyzeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task ProcessAnalyzeAsync(
        Guid sessionId,
        bool isFinalAttempt,
        CancellationToken cancellationToken = default);

    Task<SymptomAnalysisQuotaResponse?> GetQuotaAsync(
        CancellationToken cancellationToken = default);

    Task<int> ExpireAbandonedSessionsAsync(CancellationToken cancellationToken = default);
}
