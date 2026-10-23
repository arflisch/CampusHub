using AwesomeAssertions;
using Dev4u.CampusHub.Errors;
using Dev4u.CampusHub.Grading.Domain.Certificates;
using Xunit;

namespace Dev4u.CampusHub.Grading.Domain.Tests;

public class CourseParticipationTests
{
    private static readonly DateTimeOffset EndsAt = new(2026, 12, 18, 17, 0, 0, TimeSpan.Zero);

    private static CourseParticipation Participation() =>
        CourseParticipation.Record(Guid.NewGuid(), Guid.NewGuid(), "Intro to .NET", "student-1", EndsAt).Value;

    [Fact]
    public void An_event_without_the_end_of_the_course_is_refused()
    {
        var result = CourseParticipation.Record(Guid.NewGuid(), Guid.NewGuid(), "Intro", "student-1", courseEndsAt: null);

        result.Errors.HasCode("participation.invalid").Should().BeTrue();
    }

    [Fact]
    public void No_certificate_before_the_course_ends()
    {
        var participation = Participation();

        participation.IssueCertificate(EndsAt.AddMinutes(-1)).Errors.HasCode("certificate.course_not_ended").Should().BeTrue();
        participation.Certificate.Should().BeNull();
    }

    [Fact]
    public void Issuing_twice_keeps_the_first_certificate_and_its_number()
    {
        var participation = Participation();

        participation.IssueCertificate(EndsAt).IsSuccess.Should().BeTrue();
        participation.IssueCertificate(EndsAt.AddDays(1)).IsSuccess.Should().BeTrue();

        participation.Certificate.Should().Be(new Certificate(CourseParticipation.NumberFor(participation.EnrollmentId, EndsAt), EndsAt));
        participation.Certificate.Number.Should().StartWith("CH-2026-");
    }

    [Fact]
    public void A_certificate_needs_announcing_until_it_is_announced()
    {
        var participation = Participation();
        participation.NeedsAnnouncement.Should().BeFalse("there is no certificate yet");

        participation.IssueCertificate(EndsAt);
        participation.NeedsAnnouncement.Should().BeTrue();

        participation.MarkCertificateAnnounced(EndsAt.AddMinutes(1));
        participation.NeedsAnnouncement.Should().BeFalse();
        participation.CertificateAnnouncedAt.Should().Be(EndsAt.AddMinutes(1));
    }
}
