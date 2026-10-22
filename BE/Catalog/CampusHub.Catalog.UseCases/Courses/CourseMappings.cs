using Dev4u.CampusHub.Catalog.Domain.Courses;

namespace Dev4u.CampusHub.Catalog.UseCases.Courses;

internal static class CourseMappings
{
    public static CourseDto ToDto(this Course course, DateTimeOffset now) =>
        new(course.Id, course.Code, course.Title, course.TeacherId, course.Capacity, course.SeatsLeft(now),
            course.Schedule.StartsAt, course.Schedule.EndsAt, course.Status.ToString(), course.PublishedAt);

    public static SeatReservationDto ToDto(this SeatReservation reservation, Course course) =>
        new(reservation.Id, course.Id, course.Title, course.Schedule.StartsAt, course.Schedule.EndsAt, reservation.StudentId,
            reservation.Status.ToString(), reservation.HeldUntil);
}
