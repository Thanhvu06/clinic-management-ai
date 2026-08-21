using System;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class DoctorLeaveRequest
{
    public long Id { get; set; }
    public long DoctorId { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DoctorLeaveRequestStatus Status { get; set; } = DoctorLeaveRequestStatus.Pending;
    public string? AdminNote { get; set; }

    public Doctor Doctor { get; set; } = null!;
}
