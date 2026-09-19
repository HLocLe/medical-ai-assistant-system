using MedMateAI.Application.Common;
using MedMateAI.Application.IService;
using MedMateAI.Application.Service;
using MedMateAI.Domain.Entities;
using MedMateAI.Domain.Enums;
using MedMateAI.Domain.Persistence;
using MedMateAI.Domain.Repository;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace MedMateAI.Tests.Services;

[TestFixture]
public class LabTestOcrProcessorTests
{
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;
    private Mock<IGenericRepository<LabTestSession>> _labTestSessionsMock = null!;
    private Mock<IDocumentIntelligenceService> _documentIntelligenceServiceMock = null!;
    private Mock<ILabTestResultAnalyzer> _resultAnalyzerMock = null!;
    private Mock<ILogger<LabTestOcrProcessor>> _loggerMock = null!;
    private LabTestOcrProcessor _processor = null!;

    [SetUp]
    public void SetUp()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _labTestSessionsMock = new Mock<IGenericRepository<LabTestSession>>();
        _documentIntelligenceServiceMock = new Mock<IDocumentIntelligenceService>();
        _resultAnalyzerMock = new Mock<ILabTestResultAnalyzer>();
        _loggerMock = new Mock<ILogger<LabTestOcrProcessor>>();

        _unitOfWorkMock.Setup(unitOfWork => unitOfWork.LabTestSessions).Returns(_labTestSessionsMock.Object);

        _processor = new LabTestOcrProcessor(
            _unitOfWorkMock.Object,
            _documentIntelligenceServiceMock.Object,
            _resultAnalyzerMock.Object,
            _loggerMock.Object);
    }

    [Test]
    public async Task ProcessAsync_SessionNotFound_DoesNothing()
    {
        var sessionId = Guid.NewGuid();
        _labTestSessionsMock.Setup(repository => repository.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabTestSession?)null);

        await _processor.ProcessAsync(sessionId, CancellationToken.None);

        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _documentIntelligenceServiceMock.Verify(
            service => service.AnalyzeFromUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase(LabTestSessionStatus.Completed)]
    [TestCase(LabTestSessionStatus.Failed)]
    public async Task ProcessAsync_SessionAlreadyTerminal_SkipsProcessing(LabTestSessionStatus status)
    {
        var session = MakeSession(status: status);
        _labTestSessionsMock.Setup(repository => repository.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        await _processor.ProcessAsync(session.Id, CancellationToken.None);

        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task ProcessAsync_MissingDocumentUrl_MarksSessionFailed(string? documentUrl)
    {
        var session = MakeSession(documentUrl: documentUrl);
        _labTestSessionsMock.Setup(repository => repository.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        await _processor.ProcessAsync(session.Id, CancellationToken.None);

        Assert.That(session.Status, Is.EqualTo(LabTestSessionStatus.Failed));
        _labTestSessionsMock.Verify(repository => repository.Update(session), Times.Once);
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _documentIntelligenceServiceMock.Verify(
            service => service.AnalyzeFromUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ProcessAsync_AnalysisSucceeds_PersistsRawOcrAndAnalyzesResults()
    {
        var session = MakeSession(documentUrl: "https://example.com/doc.pdf");
        _labTestSessionsMock.Setup(repository => repository.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _documentIntelligenceServiceMock.Setup(service => service.AnalyzeFromUrlAsync(session.DocumentUrl!, It.IsAny<CancellationToken>()))
            .ReturnsAsync("raw ocr text");

        await _processor.ProcessAsync(session.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(session.RawOcrText, Is.EqualTo("raw ocr text"));
            Assert.That(session.ProcessedAt, Is.Not.Null);
            Assert.That(session.Status, Is.EqualTo(LabTestSessionStatus.Processing));
        });
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _resultAnalyzerMock.Verify(
            analyzer => analyzer.AnalyzeAndPersistAsync(session.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ProcessAsync_ExistingRawOcrText_SkipsOcrAndAnalyzes()
    {
        var session = MakeSession(documentUrl: "https://example.com/doc.pdf");
        session.RawOcrText = "already extracted";
        _labTestSessionsMock.Setup(repository => repository.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        await _processor.ProcessAsync(session.Id, isFinalAttempt: false, CancellationToken.None);

        _documentIntelligenceServiceMock.Verify(
            service => service.AnalyzeFromUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _resultAnalyzerMock.Verify(
            analyzer => analyzer.AnalyzeAndPersistAsync(session.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ProcessAsync_TerminalException_MarksSessionFailedAndSkipsAnalysis()
    {
        var session = MakeSession(documentUrl: "https://example.com/doc.pdf");
        _labTestSessionsMock.Setup(repository => repository.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _documentIntelligenceServiceMock.Setup(service => service.AnalyzeFromUrlAsync(session.DocumentUrl!, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ocr failure"));

        await _processor.ProcessAsync(session.Id, CancellationToken.None);

        Assert.That(session.Status, Is.EqualTo(LabTestSessionStatus.Failed));
        _resultAnalyzerMock.Verify(
            analyzer => analyzer.AnalyzeAndPersistAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void ProcessAsync_TransientException_NotFinalAttempt_RethrowsAndKeepsProcessing()
    {
        var session = MakeSession(documentUrl: "https://example.com/doc.pdf");
        _labTestSessionsMock.Setup(repository => repository.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _documentIntelligenceServiceMock.Setup(service => service.AnalyzeFromUrlAsync(session.DocumentUrl!, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransientRemoteCallException("timeout", statusCode: 504));

        Assert.ThrowsAsync<TransientRemoteCallException>(
            async () => await _processor.ProcessAsync(session.Id, isFinalAttempt: false, CancellationToken.None));

        Assert.That(session.Status, Is.EqualTo(LabTestSessionStatus.Processing));
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ProcessAsync_TransientException_FinalAttempt_MarksSessionFailed()
    {
        var session = MakeSession(documentUrl: "https://example.com/doc.pdf");
        _labTestSessionsMock.Setup(repository => repository.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _documentIntelligenceServiceMock.Setup(service => service.AnalyzeFromUrlAsync(session.DocumentUrl!, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("timed out"));

        await _processor.ProcessAsync(session.Id, isFinalAttempt: true, CancellationToken.None);

        Assert.That(session.Status, Is.EqualTo(LabTestSessionStatus.Failed));
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ProcessAsync_ResultAnalyzerThrowsTerminal_MarksSessionFailedAfterOcrPersist()
    {
        var session = MakeSession(documentUrl: "https://example.com/doc.pdf");
        _labTestSessionsMock.Setup(repository => repository.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _documentIntelligenceServiceMock.Setup(service => service.AnalyzeFromUrlAsync(session.DocumentUrl!, It.IsAny<CancellationToken>()))
            .ReturnsAsync("raw ocr text");
        _resultAnalyzerMock.Setup(analyzer => analyzer.AnalyzeAndPersistAsync(session.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("analysis failure"));

        await _processor.ProcessAsync(session.Id, CancellationToken.None);

        Assert.That(session.Status, Is.EqualTo(LabTestSessionStatus.Failed));
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static LabTestSession MakeSession(
        string? documentUrl = "https://example.com/doc.pdf",
        LabTestSessionStatus status = LabTestSessionStatus.Processing) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            DocumentUrl = documentUrl,
            Status = status
        };
}
