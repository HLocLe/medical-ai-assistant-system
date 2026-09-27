using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Persistence;
using MedMateAI.Domain.Repository;
using MedMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedMateAI.Infrastructure;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private IMedicalFacilityRepository? _medicalFacilities;
    private IDoctorRepository? _doctors;
    private IDoctorInvitationRepository? _doctorInvitations;
    private IFeedbackReviewRepository? _feedbackReviews;
    private IGenericRepository<SubscriptionPlan>? _subscriptionPlans;
    private IUserSubscriptionRepository? _userSubscriptions;
    private IPaymentRepository? _payments;
    private IPaymentTransactionRepository? _paymentTransactions;
    private ISaleCampaignRepository? _saleCampaigns;
    private ISaleRedemptionRepository? _saleRedemptions;
    private ISymptomAnalysisSessionRepository? _symptomAnalysisSessions;
    private ISessionSymptomRepository? _sessionSymptoms;
    private IDepartmentRecommendationRepository? _departmentRecommendations;
    private IMedicalDepartmentRepository? _medicalDepartments;
    private IFacilityDepartmentRepository? _facilityDepartments;
    private IClinicalQuestionRepository? _clinicalQuestions;
    private ISessionClinicalQuestionAnswerRepository? _sessionClinicalQuestionAnswers;
    private IIcdChapterRepository? _icdChapters;
    private IDiseasePriorProbabilityRepository? _diseasePriorProbabilities;
    private IRecoveryPlanRequestRepository? _recoveryPlanRequests;
    private IRecoveryPlanRepository? _recoveryPlans;
    private IRecoveryPlanTemplateRepository? _recoveryPlanTemplates;
    private IQuotaUsageRepository? _quotaUsages;
    private IFreeQuotaUsageRepository? _freeQuotaUsages;
    private IUserMedicationRepository? _userMedications;
    private IUserPushDeviceRepository? _userPushDevices;
    private ILabIndicatorRepository? _labIndicators;
    private IGenericRepository<LabIndicatorAlias>? _labIndicatorAliases;
    private IGenericRepository<LabIndicatorReferenceRange>? _labIndicatorReferenceRanges;
    private IGenericRepository<LabIndicatorAdviceCache>? _labIndicatorAdviceCaches;
    private IGenericRepository<LabTestSession>? _labTestSessions;
    private ILabTestSessionRepository? _labTestSessionDetails;
    private IGenericRepository<LabTestResultDetail>? _labTestResultDetails;
    private IGenericRepository<LabTestOcrExtract>? _labTestOcrExtracts;
    private IGenericRepository<DepartmentConsultationQuestion>? _departmentConsultationQuestions;
    private IGenericRepository<ChecklistItem>? _checklistItems;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public IMedicalFacilityRepository MedicalFacilities =>
        _medicalFacilities ??= new MedicalFacilityRepository(_context);

    public IDoctorRepository Doctors =>
        _doctors ??= new DoctorRepository(_context);

    public IDoctorInvitationRepository DoctorInvitations =>
        _doctorInvitations ??= new DoctorInvitationRepository(_context);

    public IFeedbackReviewRepository FeedbackReviews =>
        _feedbackReviews ??= new FeedbackReviewRepository(_context);

    public IGenericRepository<SubscriptionPlan> SubscriptionPlans =>
        _subscriptionPlans ??= new GenericRepository<SubscriptionPlan>(_context);

    public IUserSubscriptionRepository UserSubscriptions =>
        _userSubscriptions ??= new UserSubscriptionRepository(_context);

    public IPaymentRepository Payments =>
        _payments ??= new PaymentRepository(_context);

    public IPaymentTransactionRepository PaymentTransactions =>
        _paymentTransactions ??= new PaymentTransactionRepository(_context);

    public ISaleCampaignRepository SaleCampaigns =>
        _saleCampaigns ??= new SaleCampaignRepository(_context);

    public ISaleRedemptionRepository SaleRedemptions =>
        _saleRedemptions ??= new SaleRedemptionRepository(_context);

    public ISymptomAnalysisSessionRepository SymptomAnalysisSessions =>
        _symptomAnalysisSessions ??= new SymptomAnalysisSessionRepository(_context);

    public ISessionSymptomRepository SessionSymptoms =>
        _sessionSymptoms ??= new SessionSymptomRepository(_context);

    public IDepartmentRecommendationRepository DepartmentRecommendations =>
        _departmentRecommendations ??= new DepartmentRecommendationRepository(_context);

    public IMedicalDepartmentRepository MedicalDepartments =>
        _medicalDepartments ??= new MedicalDepartmentRepository(_context);

    public IFacilityDepartmentRepository FacilityDepartments =>
        _facilityDepartments ??= new FacilityDepartmentRepository(_context);

    public IClinicalQuestionRepository ClinicalQuestions =>
        _clinicalQuestions ??= new ClinicalQuestionRepository(_context);

    public ISessionClinicalQuestionAnswerRepository SessionClinicalQuestionAnswers =>
        _sessionClinicalQuestionAnswers ??= new SessionClinicalQuestionAnswerRepository(_context);

    public IIcdChapterRepository IcdChapters =>
        _icdChapters ??= new IcdChapterRepository(_context);

    public IDiseasePriorProbabilityRepository DiseasePriorProbabilities =>
        _diseasePriorProbabilities ??= new DiseasePriorProbabilityRepository(_context);

    public IRecoveryPlanRequestRepository RecoveryPlanRequests =>
        _recoveryPlanRequests ??= new RecoveryPlanRequestRepository(_context);

    public IRecoveryPlanRepository RecoveryPlans =>
        _recoveryPlans ??= new RecoveryPlanRepository(_context);

    public IRecoveryPlanTemplateRepository RecoveryPlanTemplates =>
        _recoveryPlanTemplates ??= new RecoveryPlanTemplateRepository(_context);

    public IQuotaUsageRepository QuotaUsages =>
        _quotaUsages ??= new QuotaUsageRepository(_context);

    public IFreeQuotaUsageRepository FreeQuotaUsages =>
        _freeQuotaUsages ??= new FreeQuotaUsageRepository(_context);

    public IUserMedicationRepository UserMedications =>
        _userMedications ??= new UserMedicationRepository(_context);

    public IUserPushDeviceRepository UserPushDevices =>
        _userPushDevices ??= new UserPushDeviceRepository(_context);

    public ILabIndicatorRepository LabIndicators =>
        _labIndicators ??= new LabIndicatorRepository(_context);

    public IGenericRepository<LabIndicatorAlias> LabIndicatorAliases =>
        _labIndicatorAliases ??= new GenericRepository<LabIndicatorAlias>(_context);

    public IGenericRepository<LabIndicatorReferenceRange> LabIndicatorReferenceRanges =>
        _labIndicatorReferenceRanges ??= new GenericRepository<LabIndicatorReferenceRange>(_context);

    public IGenericRepository<LabIndicatorAdviceCache> LabIndicatorAdviceCaches =>
        _labIndicatorAdviceCaches ??= new GenericRepository<LabIndicatorAdviceCache>(_context);

    public IGenericRepository<LabTestSession> LabTestSessions =>
        _labTestSessions ??= new GenericRepository<LabTestSession>(_context);

    public ILabTestSessionRepository LabTestSessionDetails =>
        _labTestSessionDetails ??= new LabTestSessionRepository(_context);

    public IGenericRepository<LabTestResultDetail> LabTestResultDetails =>
        _labTestResultDetails ??= new GenericRepository<LabTestResultDetail>(_context);

    public IGenericRepository<LabTestOcrExtract> LabTestOcrExtracts =>
        _labTestOcrExtracts ??= new GenericRepository<LabTestOcrExtract>(_context);

    public IGenericRepository<DepartmentConsultationQuestion> DepartmentConsultationQuestions =>
        _departmentConsultationQuestions ??= new GenericRepository<DepartmentConsultationQuestion>(_context);

    public IGenericRepository<ChecklistItem> ChecklistItems =>
        _checklistItems ??= new GenericRepository<ChecklistItem>(_context);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already in progress.");
        }

        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        try
        {
            await _transaction.CommitAsync(cancellationToken);
        }
        
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        try
        {
            await _transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void ClearTrackedChanges()
    {
        _context.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}
