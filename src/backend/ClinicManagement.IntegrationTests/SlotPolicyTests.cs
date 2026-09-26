using System;
using System.Linq;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class SlotPolicyTests
{
    [Theory]
    [InlineData(AppointmentStatus.Pending, true)]
    [InlineData(AppointmentStatus.Confirmed, true)]
    [InlineData(AppointmentStatus.PendingReschedule, true)]
    [InlineData(AppointmentStatus.PendingCancellation, true)]
    [InlineData(AppointmentStatus.CheckedIn, true)]
    [InlineData(AppointmentStatus.InConsultation, true)]
    [InlineData(AppointmentStatus.Completed, false)]
    [InlineData(AppointmentStatus.Cancelled, false)]
    [InlineData(AppointmentStatus.NoShow, false)]
    public void HoldsSlot_ShouldReturnExpectedResult_ForAllNineAppointmentStatuses(AppointmentStatus status, bool expectedHoldsSlot)
    {
        // Act
        var holdsSlot = status.HoldsSlot();

        // Assert
        Assert.Equal(expectedHoldsSlot, holdsSlot);
    }

    [Fact]
    public void SlotPolicySets_ShouldBeExhaustiveAndMutuallyExclusive()
    {
        // Arrange
        var allStatuses = Enum.GetValues<AppointmentStatus>();
        Assert.Equal(9, allStatuses.Length);

        // Act & Assert
        Assert.Equal(6, AppointmentStatusExtensions.HoldingSlotStatuses.Length);
        Assert.Equal(3, AppointmentStatusExtensions.ReleasedSlotStatuses.Length);

        // Disjoint sets check
        var intersection = AppointmentStatusExtensions.HoldingSlotStatuses
            .Intersect(AppointmentStatusExtensions.ReleasedSlotStatuses)
            .ToList();
        Assert.Empty(intersection);

        // Union equals all 9 statuses
        var union = AppointmentStatusExtensions.HoldingSlotStatuses
            .Union(AppointmentStatusExtensions.ReleasedSlotStatuses)
            .OrderBy(s => s)
            .ToList();
        Assert.Equal(allStatuses.OrderBy(s => s), union);
    }
}
