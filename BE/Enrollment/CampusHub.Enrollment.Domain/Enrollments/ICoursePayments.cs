using FluentResults;

namespace Dev4u.CampusHub.Enrollment.Domain.Enrollments;

/// <summary>
/// Port to the Billing service (anti-corruption layer). Both operations are idempotent on the enrollment id: a
/// retried charge never charges twice, a retried refund never refunds twice. A declined card is a permanent failure
/// with the code <see cref="DeclinedCode"/>; Billing being down is a transient failure.
/// </summary>
public interface ICoursePayments
{
    public const string DeclinedCode = "payment.declined";

    Task<Result> ChargeAsync(Guid enrollmentId, string studentId, Guid courseId, int amountInCents, CancellationToken cancellationToken);

    /// <summary>Gives the money back. Nothing charged (or already refunded) is a success.</summary>
    Task<Result> RefundAsync(Guid enrollmentId, CancellationToken cancellationToken);
}
