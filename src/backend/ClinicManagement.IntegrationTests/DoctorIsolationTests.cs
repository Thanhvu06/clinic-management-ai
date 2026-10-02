using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.Prescriptions.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class DoctorIsolationTests : IntegrationTestBase
{
    public DoctorIsolationTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Given_Doctor1Appointment_When_Doctor2AttemptsToManageOrPrescribe_Then_ReturnsNotFound()
    {
        // 1. Create a dedicated slot and appointment assigned to Doctor 1
        var date = GetFutureWorkingDate(21);
        var slot = await CreateAvailableSlotAsync(DoctorEntityId, date, new TimeOnly(11, 0, 0), new TimeOnly(11, 30, 0));
        var slotId = slot.Id;

        await AuthenticateAsync("pat1@test.com");
        var createResponse = await Client.PostAsJsonAsync("/api/v1/appointments", new
        {
            DoctorId = DoctorEntityId,
            SpecialtyId = SpecialtyEntityId,
            AppointmentSlotId = slotId,
            Reason = "Khám tim mạch cùng Bác sĩ 1"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var doc1AppointmentId = createDoc.RootElement.GetProperty("data").GetProperty("id").GetInt64();

        // 2. Doctor 2 attempts to access/manage Doctor 1's appointment
        await AuthenticateAsync("doc2@test.com");

        // Attempt 1: View history
        var doc2HistoryResponse = await Client.GetAsync($"/api/v1/doctor/appointments/{doc1AppointmentId}/history");
        Assert.Equal(HttpStatusCode.NotFound, doc2HistoryResponse.StatusCode);

        // Attempt 2: Complete appointment
        var doc2CompleteResponse = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{doc1AppointmentId}/complete", new ClinicManagement.Application.Appointments.DTOs.Doctor.CompleteAppointmentDto
        {
            Summary = "Chẩn đoán trái phép",
            FollowUpInstruction = "Kế hoạch điều trị trái phép"
        });
        Assert.Equal(HttpStatusCode.NotFound, doc2CompleteResponse.StatusCode);

        // Attempt 3: Create prescription
        var doc2PrescriptionResponse = await Client.PostAsJsonAsync($"/api/v1/doctor/appointments/{doc1AppointmentId}/prescription", new CreatePrescriptionDto
        {
            Notes = "Kê đơn trái phép",
            Items = new List<CreatePrescriptionItemDto>
            {
                new()
                {
                    MedicineId = MedicineEntityId,
                    Quantity = 2,
                    Dosage = "1 viên",
                    Frequency = "Ngày 2 lần",
                    DurationDays = 3,
                    Instructions = "Sau ăn"
                }
            }
        });
        Assert.Equal(HttpStatusCode.NotFound, doc2PrescriptionResponse.StatusCode);

        // 3. Doctor 1 authenticates and confirms access is permitted
        await AuthenticateAsync("doc@test.com");
        var doc1HistoryResponse = await Client.GetAsync($"/api/v1/doctor/appointments/{doc1AppointmentId}/history");
        Assert.Equal(HttpStatusCode.OK, doc1HistoryResponse.StatusCode);
    }
}
