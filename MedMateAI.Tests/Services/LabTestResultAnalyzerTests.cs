using MedMateAI.Application.DTOs.LabTests.Ocr;
using MedMateAI.Application.IService;
using MedMateAI.Application.Service;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Persistence;
using MedMateAI.Domain.Repository;
using Microsoft.Extensions.Logging;
using Moq;

namespace MedMateAI.Tests.Services;

[TestFixture]
public class LabTestResultAnalyzerTests
{
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private Mock<ILabTestOcrStructurer> _ocrStructurerMock = null!;
    private Mock<ILogger<LabTestResultAnalyzer>> _loggerMock = null!;
    private Mock<IGenericRepository<LabTestSession>> _labTestSessionRepo = null!;
    private Mock<IGenericRepository<LabTestResultDetail>> _resultDetailRepo = null!;
    private Mock<IGenericRepository<LabTestOcrExtract>> _ocrExtractRepo = null!;
    private Mock<ILabIndicatorRepository> _indicatorRepo = null!;
    private LabTestResultAnalyzer _analyzer = null!;

    [SetUp]
    public void SetUp()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _ocrStructurerMock = new Mock<ILabTestOcrStructurer>();
        _loggerMock = new Mock<ILogger<LabTestResultAnalyzer>>();
        _labTestSessionRepo = new Mock<IGenericRepository<LabTestSession>>();
        _resultDetailRepo = new Mock<IGenericRepository<LabTestResultDetail>>();
        _ocrExtractRepo = new Mock<IGenericRepository<LabTestOcrExtract>>();
        _indicatorRepo = new Mock<ILabIndicatorRepository>();

        _unitOfWorkMock.Setup(u => u.LabTestSessions).Returns(_labTestSessionRepo.Object);
        _unitOfWorkMock.Setup(u => u.LabTestResultDetails).Returns(_resultDetailRepo.Object);
        _unitOfWorkMock.Setup(u => u.LabTestOcrExtracts).Returns(_ocrExtractRepo.Object);
        _unitOfWorkMock.Setup(u => u.LabIndicators).Returns(_indicatorRepo.Object);

        _analyzer = new LabTestResultAnalyzer(
            _unitOfWorkMock.Object,
            _ocrStructurerMock.Object,
            _loggerMock.Object);
    }

    [Test]
    public async Task AnalyzeAndPersistAsync_SessionNotFound_DoesNothing()
    {
        var sessionId = Guid.NewGuid();
        _labTestSessionRepo
            .Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabTestSession?)null);

        await _analyzer.AnalyzeAndPersistAsync(sessionId);

        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task AnalyzeAndPersistAsync_SessionHasNoOcrText_DoesNothing()
    {
        var sessionId = Guid.NewGuid();
        _labTestSessionRepo
            .Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LabTestSession { Id = sessionId, RawOcrText = null });

        await _analyzer.AnalyzeAndPersistAsync(sessionId);

        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task AnalyzeAndPersistAsync_AlreadyAnalyzed_DoesNotPersistAgain()
    {
        var sessionId = Guid.NewGuid();
        _labTestSessionRepo
            .Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LabTestSession { Id = sessionId, RawOcrText = "HGB: 14" });

        _resultDetailRepo
            .Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<LabTestResultDetail, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LabTestResultDetail { Id = Guid.NewGuid() });

        await _analyzer.AnalyzeAndPersistAsync(sessionId);

        _ocrStructurerMock.Verify(
            s => s.StructureAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task AnalyzeAndPersistAsync_OcrReturnsEmptyRows_DoesNotSave()
    {
        var sessionId = Guid.NewGuid();
        var session = new LabTestSession { Id = sessionId, RawOcrText = "some text" };
        _labTestSessionRepo
            .Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        _resultDetailRepo
            .Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<LabTestResultDetail, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabTestResultDetail?)null);

        _ocrStructurerMock
            .Setup(s => s.StructureAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParsedOcrRow>());

        await _analyzer.AnalyzeAndPersistAsync(sessionId);

        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(session.AiSummaryStatus, Is.Null);
    }

    [Test]
    public async Task AnalyzeAndPersistAsync_UnmatchedRow_SavesUnknownStatus()
    {
        var sessionId = Guid.NewGuid();
        var session = new LabTestSession { Id = sessionId, RawOcrText = "UNKN: 5" };
        _labTestSessionRepo
            .Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        _resultDetailRepo
            .Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<LabTestResultDetail, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabTestResultDetail?)null);

        _ocrStructurerMock
            .Setup(s => s.StructureAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParsedOcrRow>
            {
                new("UNKN", null, 5.0)
            });

        _indicatorRepo
            .Setup(r => r.GetAllActiveWithDetailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LabIndicatorMaster>());

        LabTestResultDetail? captured = null;
        _resultDetailRepo
            .Setup(r => r.Add(It.IsAny<LabTestResultDetail>()))
            .Callback<LabTestResultDetail>(d => captured = d);

        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _analyzer.AnalyzeAndPersistAsync(sessionId);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Status, Is.EqualTo(LabResultStatus.Unknown));
        Assert.That(captured.IsMatched, Is.False);
        Assert.That(session.Status, Is.EqualTo(LabTestSessionStatus.Completed));
        Assert.That(session.AiSummaryStatus, Is.EqualTo(LabTestSummaryStatus.Processing));
    }

    [Test]
    public async Task AnalyzeAndPersistAsync_MatchedRowWithReferenceRange_SavesCorrectStatus()
    {
        var sessionId = Guid.NewGuid();
        _labTestSessionRepo
            .Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LabTestSession
            {
                Id = sessionId,
                RawOcrText = "HGB: 14",
                PatientGenderAtTest = Gender.Male,
                PatientAgeAtTest = 30
            });

        _resultDetailRepo
            .Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<LabTestResultDetail, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabTestResultDetail?)null);

        _ocrStructurerMock
            .Setup(s => s.StructureAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ParsedOcrRow>
            {
                new("Hemoglobin", null, 14.0)
            });

        var refRange = new LabIndicatorReferenceRange
        {
            Id = Guid.NewGuid(),
            Gender = Gender.Male,
            MinValue = 13.0,
            MaxValue = 17.0,
            ComparisonType = ReferenceComparisonType.Between
        };

        var indicator = new LabIndicatorMaster
        {
            Id = Guid.NewGuid(),
            FullName = "Hemoglobin",
            Symbol = "HGB",
            LabIndicatorReferenceRanges = new List<LabIndicatorReferenceRange> { refRange },
            LabIndicatorAliases = new List<LabIndicatorAlias>(),
            LabIndicatorAdviceCaches = new List<LabIndicatorAdviceCache>()
        };

        _indicatorRepo
            .Setup(r => r.GetAllActiveWithDetailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LabIndicatorMaster> { indicator });

        LabTestResultDetail? captured = null;
        _resultDetailRepo
            .Setup(r => r.Add(It.IsAny<LabTestResultDetail>()))
            .Callback<LabTestResultDetail>(d => captured = d);

        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _analyzer.AnalyzeAndPersistAsync(sessionId);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Status, Is.EqualTo(LabResultStatus.Normal));
        Assert.That(captured.IsMatched, Is.True);
        Assert.That(captured.IndicatorId, Is.EqualTo(indicator.Id));
    }
}
