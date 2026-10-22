using AwesomeAssertions;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using Dev4u.CampusHub.Enrollment.UseCases.Enrollments;
using Dev4u.CampusHub.Errors;
using FluentResults;
using Moq;
using Xunit;

namespace Dev4u.CampusHub.Enrollment.UseCases.Tests;

public class RequestEnrollmentTests
{
    private readonly Mock<IEnrollmentRepository> _repository = new();
    private readonly Mock<IEnrollmentSaga> _saga = new();
    private readonly RequestEnrollment _sut;

    public RequestEnrollmentTests()
    {
        _repository.Setup(r => r.AddAsync(It.IsAny<StudentEnrollment>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
        _saga.Setup(s => s.StartAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
        _sut = new RequestEnrollment(_repository.Object, _saga.Object, TimeProvider.System);
    }

    [Fact]
    public async Task A_new_request_is_stored_then_the_saga_starts()
    {
        var command = new RequestEnrollmentCommand(Guid.NewGuid(), Guid.NewGuid());

        var result = await _sut.ExecuteAsync(command, "student-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(nameof(EnrollmentStatus.Requested));
        _repository.Verify(r => r.AddAsync(It.Is<StudentEnrollment>(e => e.Id == command.RequestId && e.StudentId == "student-1"), It.IsAny<CancellationToken>()), Times.Once);
        _saga.Verify(s => s.StartAsync(command.RequestId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_retried_request_returns_the_existing_enrollment_without_storing_it_again()
    {
        var existing = StudentEnrollment.Request(Guid.NewGuid(), Guid.NewGuid(), "student-1", DateTimeOffset.UtcNow).Value;
        existing.MarkSeatReserved(Guid.NewGuid(), "Intro", DateTimeOffset.UtcNow);
        _repository.Setup(r => r.GetAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await _sut.ExecuteAsync(new RequestEnrollmentCommand(existing.Id, existing.CourseId), "student-1", CancellationToken.None);

        result.Value.Status.Should().Be(nameof(EnrollmentStatus.SeatHeld));
        _repository.Verify(r => r.AddAsync(It.IsAny<StudentEnrollment>(), It.IsAny<CancellationToken>()), Times.Never);
        _saga.Verify(s => s.StartAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never); // saga already past the start
    }

    [Fact]
    public async Task Another_students_request_id_cannot_be_reused()
    {
        var existing = StudentEnrollment.Request(Guid.NewGuid(), Guid.NewGuid(), "student-1", DateTimeOffset.UtcNow).Value;
        _repository.Setup(r => r.GetAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await _sut.ExecuteAsync(new RequestEnrollmentCommand(existing.Id, existing.CourseId), "student-2", CancellationToken.None);

        result.Errors.HasCode("enrollment.request_id_taken").Should().BeTrue();
    }

    [Fact]
    public async Task When_the_workflow_engine_is_down_the_failure_is_transient()
    {
        _saga.Setup(s => s.StartAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Fail(EnrollmentErrors.SagaUnavailable(Guid.Empty)));

        var result = await _sut.ExecuteAsync(new RequestEnrollmentCommand(Guid.NewGuid(), Guid.NewGuid()), "student-1", CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.AreTransient().Should().BeTrue(); // the client retries with the same request id
    }
}
