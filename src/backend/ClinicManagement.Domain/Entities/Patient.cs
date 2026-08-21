using System;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class Patient
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public Gender? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Address { get; set; }
}
