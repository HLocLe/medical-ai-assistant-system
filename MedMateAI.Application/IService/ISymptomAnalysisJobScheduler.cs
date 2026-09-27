namespace MedMateAI.Application.IService;

public interface ISymptomAnalysisJobScheduler
{
    void EnqueueAnalyze(Guid sessionId);
}
