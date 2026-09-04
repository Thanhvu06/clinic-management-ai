using System;

namespace ClinicManagement.Application.Admin.DTOs;

public class SystemAuditLogDto
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AdminStatsDto
{
    public int TotalPatients { get; set; }
    public int TotalDoctors { get; set; }
    public int TotalAppointmentsToday { get; set; }
    public int TotalAppointmentsAll { get; set; }
    public int TotalPrescriptions { get; set; }
    public int TotalHealthPackages { get; set; }
    public int LowStockMedicinesCount { get; set; }
}
