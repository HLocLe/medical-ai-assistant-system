using MedMateAI.Application.Common;
using MedMateAI.Application.Helpers;
using NUnit.Framework;

namespace MedMateAI.Tests.Helpers;

[TestFixture]
public class BackgroundJobRetryTests
{
    [Test]
    public void IsRetryable_TransientRemoteCall_ReturnsTrue()
    {
        Assert.That(
            BackgroundJobRetry.IsRetryable(new TransientRemoteCallException("x", 503)),
            Is.True);
    }

    [Test]
    public void IsRetryable_Timeout_ReturnsTrue()
    {
        Assert.That(BackgroundJobRetry.IsRetryable(new TimeoutException()), Is.True);
    }

    [Test]
    public void IsRetryable_InvalidOperation_ReturnsFalse()
    {
        Assert.That(BackgroundJobRetry.IsRetryable(new InvalidOperationException("bad")), Is.False);
    }

    [TestCase(408, true)]
    [TestCase(429, true)]
    [TestCase(500, true)]
    [TestCase(503, true)]
    [TestCase(400, false)]
    [TestCase(404, false)]
    public void IsTransientHttpStatusCode_MatchesExpected(int statusCode, bool expected)
    {
        Assert.That(BackgroundJobRetry.IsTransientHttpStatusCode(statusCode), Is.EqualTo(expected));
    }

    [TestCase(0, 5, false)]
    [TestCase(4, 5, false)]
    [TestCase(5, 5, true)]
    [TestCase(6, 5, true)]
    public void IsFinalAttempt_MatchesExpected(int retryCount, int maxAttempts, bool expected)
    {
        Assert.That(BackgroundJobRetry.IsFinalAttempt(retryCount, maxAttempts), Is.EqualTo(expected));
    }
}
