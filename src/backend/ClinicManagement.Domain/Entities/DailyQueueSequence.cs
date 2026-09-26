using System;

namespace ClinicManagement.Domain.Entities;

public class DailyQueueSequence
{
    public long Id { get; set; }
    public long FacilityId { get; set; }
    public long DepartmentId { get; set; }
    public DateOnly Date { get; set; }
    public int LastNumber { get; set; }
}
