using AwesomeAssertions;
using Dev4u.CampusHub.Errors;
using Dev4u.CampusHub.Grading.Domain.Certificates;
using Dev4u.CampusHub.Grading.UseCases.Certificates;
using FluentResults;
using Moq;
using Xunit;

namespace Dev4u.CampusHub.Grading.UseCases.Tests;

public class IssueCertificatesTests
{
    private readonly Mock<IParticipationRepository> _repository = new();
    private readonly Mock<ICertificateAnnouncer> _announcer = new();
    private readonly IssueCertificates _sut;

    public IssueCertificatesTests()
    {
        _repository.Setup(r => r.SaveAsync(It.IsAny<CourseParticipation>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
        _repository.Setup(r => r.ListEndedWithoutCertificateAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([]);
        _repository.Setup(r => r.ListUnannouncedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _announcer.Setup(a => a.AnnounceAsync(It.IsAny<CourseParticipation>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
        _sut = new IssueCertificates(_repository.Object, _announcer.Object, TimeProvider.System);
    }

    private CourseParticipation Ended()
    {
        var participation = CourseParticipation.Record(Guid.NewGuid(), Guid.NewGuid(), "Intro", "student-1", DateTimeOffset.UtcNow.AddDays(-1)).Value;
        _repository.Setup(r => r.GetAsync(participation.EnrollmentId, It.IsAny<CancellationToken>())).ReturnsAsync(participation);
        return participation;
    }

    [Fact]
    public async Task A_finished_course_earns_a_certificate_which_is_then_announced()
    {
        var participation = Ended();
        _repository.Setup(r => r.ListEndedWithoutCertificateAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([participation]);
        _repository.Setup(r => r.ListUnannouncedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(() => participation.NeedsAnnouncement ? [participation] : []);

        var report = await _sut.ExecuteAsync(CancellationToken.None);

        report.Should().Be(new CertificatesReport(Issued: 1, Announced: 1, Failed: 0));
        participation.Certificate.Should().NotBeNull();
        participation.NeedsAnnouncement.Should().BeFalse();
        _announcer.Verify(a => a.AnnounceAsync(participation, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_failed_announcement_is_retried_by_the_next_run()
    {
        var participation = Ended();
        participation.IssueCertificate(DateTimeOffset.UtcNow);
        _repository.Setup(r => r.ListUnannouncedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([participation]);
        _announcer.SetupSequence(a => a.AnnounceAsync(participation, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Result.Fail(AppErrors.Unavailable("certificate.announcement_failed", "broker down")))
                  .ReturnsAsync(Result.Ok());

        var first = await _sut.ExecuteAsync(CancellationToken.None);
        participation.NeedsAnnouncement.Should().BeTrue("nothing is recorded when the announcement failed");
        var second = await _sut.ExecuteAsync(CancellationToken.None);

        first.Failed.Should().Be(1);
        second.Announced.Should().Be(1);
        participation.NeedsAnnouncement.Should().BeFalse();
    }
}

public class RecordParticipationTests
{
    [Fact]
    public async Task A_redelivered_EnrollmentConfirmed_is_recorded_once()
    {
        var repository = new Mock<IParticipationRepository>();
        repository.SetupSequence(r => r.AddIfAbsentAsync(It.IsAny<CourseParticipation>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(true)
                  .ReturnsAsync(false);
        var sut = new RecordParticipation(repository.Object);
        var command = new RecordParticipationCommand(Guid.NewGuid(), Guid.NewGuid(), "Intro", "student-1", DateTimeOffset.UtcNow.AddDays(30));

        (await sut.ExecuteAsync(command, CancellationToken.None)).Value.Should().BeTrue();
        (await sut.ExecuteAsync(command, CancellationToken.None)).Value.Should().BeFalse("the second delivery is a duplicate");
    }

    [Fact]
    public async Task An_event_published_before_the_course_dates_existed_is_dropped()
    {
        var sut = new RecordParticipation(Mock.Of<IParticipationRepository>());

        var result = await sut.ExecuteAsync(new RecordParticipationCommand(Guid.NewGuid(), Guid.NewGuid(), "Intro", "student-1", CourseEndsAt: null),
                                            CancellationToken.None);

        result.Errors.AreTransient().Should().BeFalse("retrying would not fix it");
        result.Errors.HasCode("participation.invalid").Should().BeTrue();
    }
}
