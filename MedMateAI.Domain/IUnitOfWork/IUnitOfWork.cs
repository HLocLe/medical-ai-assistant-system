using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Repository;

namespace MedMateAI.Domain.Persistence;

public interface IUnitOfWork : IAsyncDisposable
{
    IMedicalFacilityRepository MedicalFacilities { get; }
    IDoctorRepository Doctors { get; }
    IDoctorInvitationRepository DoctorInvitations { get; }
    IFeedbackReviewRepository FeedbackReviews { get; }
    IGenericRepository<SubscriptionPlan> SubscriptionPlans { get; }
    IUserSubscriptionRepository UserSubscriptions { get; }
    IPaymentRepository Payments { get; }
    IPaymentTransactionRepository PaymentTransactions { get; }
    ISaleCampaignRepository SaleCampaigns { get; }
    ISaleRedemptionRepository SaleRedemptions { get; }

    ISymptomAnalysisSessionRepository SymptomAnalysisSessions { get; }

    ISessionSymptomRepository SessionSymptoms { get; }

    IDepartmentRecommendationRepository DepartmentRecommendations { get; }

    IMedicalDepartmentRepository MedicalDepartments { get; }

    IFacilityDepartmentRepository FacilityDepartments { get; }

    IClinicalQuestionRepository ClinicalQuestions { get; }

    ISessionClinicalQuestionAnswerRepository SessionClinicalQuestionAnswers { get; }

    IIcdChapterRepository IcdChapters { get; }
    IDiseasePriorProbabilityRepository DiseasePriorProbabilities { get; }
    IRecoveryPlanRequestRepository RecoveryPlanRequests { get; }
    IRecoveryPlanRepository RecoveryPlans { get; }
    IRecoveryPlanTemplateRepository RecoveryPlanTemplates { get; }
    IQuotaUsageRepository QuotaUsages { get; }
    IFreeQuotaUsageRepository FreeQuotaUsages { get; }
    IUserMedicationRepository UserMedications { get; }
    IUserPushDeviceRepository UserPushDevices { get; }

    ILabIndicatorRepository LabIndicators { get; }

    IGenericRepository<LabIndicatorAlias> LabIndicatorAliases { get; }

    IGenericRepository<LabIndicatorReferenceRange> LabIndicatorReferenceRanges { get; }

    IGenericRepository<LabIndicatorAdviceCache> LabIndicatorAdviceCaches { get; }

    IGenericRepository<LabTestSession> LabTestSessions { get; }

    ILabTestSessionRepository LabTestSessionDetails { get; }

    IGenericRepository<LabTestResultDetail> LabTestResultDetails { get; }

    IGenericRepository<LabTestOcrExtract> LabTestOcrExtracts { get; }

    IGenericRepository<DepartmentConsultationQuestion> DepartmentConsultationQuestions { get; }

    IGenericRepository<ChecklistItem> ChecklistItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
    void ClearTrackedChanges();
}
