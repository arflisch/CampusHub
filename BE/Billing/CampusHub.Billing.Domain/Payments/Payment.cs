using FluentResults;

namespace Dev4u.CampusHub.Billing.Domain.Payments;

public enum PaymentStatus
{
    /// <summary>The provider took the money.</summary>
    Captured,

    /// <summary>The provider refused the charge: nothing was taken.</summary>
    Declined,

    /// <summary>The money was given back (saga compensation).</summary>
    Refunded,
}

/// <summary>
/// What a student paid for an enrollment.
/// <para>
/// The id is the enrollment id: one enrollment is charged at most once, whatever the number of retries of the saga,
/// and a declined charge stays declined (retrying the same enrollment returns the same decision instead of charging
/// again). Both the charge and the refund are sent to the provider with the enrollment id as idempotency key.
/// </para>
/// </summary>
public sealed class Payment
{
    public const string Currency = "EUR";

    private Payment(Guid enrollmentId, string studentId, Guid courseId, int amountInCents, PaymentStatus status,
                    string? providerReference, string? declineReason, DateTimeOffset createdAt, DateTimeOffset? refundedAt, long version)
    {
        EnrollmentId = enrollmentId;
        StudentId = studentId;
        CourseId = courseId;
        AmountInCents = amountInCents;
        Status = status;
        ProviderReference = providerReference;
        DeclineReason = declineReason;
        CreatedAt = createdAt;
        RefundedAt = refundedAt;
        Version = version;
    }

    public Guid EnrollmentId { get; }

    /// <summary>Identity provider subject of the student who pays.</summary>
    public string StudentId { get; }

    public Guid CourseId { get; }

    public int AmountInCents { get; }

    public PaymentStatus Status { get; private set; }

    /// <summary>Id of the charge at the provider, needed to refund it.</summary>
    public string? ProviderReference { get; }

    /// <summary>Code given by the provider when it declined the charge.</summary>
    public string? DeclineReason { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? RefundedAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented by the repository on every successful save.</summary>
    public long Version { get; private set; }

    public static Result<Payment> Captured(Guid enrollmentId, string studentId, Guid courseId, int amountInCents,
                                           string providerReference, DateTimeOffset now) =>
        Validate(enrollmentId, studentId, courseId, amountInCents)
            .Bind(() => Result.Ok(new Payment(enrollmentId, studentId, courseId, amountInCents, PaymentStatus.Captured,
                                              providerReference, declineReason: null, now, refundedAt: null, version: 0)));

    public static Result<Payment> Declined(Guid enrollmentId, string studentId, Guid courseId, int amountInCents,
                                           string declineReason, DateTimeOffset now) =>
        Validate(enrollmentId, studentId, courseId, amountInCents)
            .Bind(() => Result.Ok(new Payment(enrollmentId, studentId, courseId, amountInCents, PaymentStatus.Declined,
                                              providerReference: null, declineReason, now, refundedAt: null, version: 0)));

    /// <summary>Checks a charge request before anything is sent to the provider.</summary>
    public static Result Validate(Guid enrollmentId, string? studentId, Guid courseId, int amountInCents)
    {
        if (enrollmentId == Guid.Empty || courseId == Guid.Empty || string.IsNullOrWhiteSpace(studentId))
        {
            return Result.Fail(PaymentErrors.InvalidRequest("The enrollment, the course and the student are required."));
        }

        return amountInCents is > 0 and <= PaymentErrors.MaxAmountInCents
            ? Result.Ok()
            : Result.Fail(PaymentErrors.InvalidRequest($"The amount must be between 1 and {PaymentErrors.MaxAmountInCents} cents."));
    }

    /// <summary>Rebuilds a payment from storage. Persistence only: no rule is checked.</summary>
    public static Payment Rehydrate(Guid enrollmentId, string studentId, Guid courseId, int amountInCents, PaymentStatus status,
                                    string? providerReference, string? declineReason, DateTimeOffset createdAt,
                                    DateTimeOffset? refundedAt, long version) =>
        new(enrollmentId, studentId, courseId, amountInCents, status, providerReference, declineReason, createdAt, refundedAt, version);

    /// <summary>
    /// Records the refund of a captured payment. Refunding twice, or refunding a declined payment (nothing was
    /// taken), succeeds without change: a compensation must be safe to replay.
    /// </summary>
    public void MarkRefunded(DateTimeOffset now)
    {
        if (Status == PaymentStatus.Captured)
        {
            Status = PaymentStatus.Refunded;
            RefundedAt = now;
        }
    }

    /// <summary>Called by the repository once the new version is stored. Persistence only.</summary>
    public void MarkPersisted() => Version++;
}
