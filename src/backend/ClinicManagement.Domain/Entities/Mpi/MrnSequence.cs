using System;

namespace ClinicManagement.Domain.Entities;

public class MrnSequence
{
    public int Id { get; set; }
    public int Year { get; set; }
    public long LastSequenceNumber { get; set; }
    public byte[]? RowVersion { get; set; } = Guid.NewGuid().ToByteArray();
}
