using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.Application.Common.Interfaces;

public interface IFacilityAuthorizationService
{
    Task ValidateUserFacilityAccessAsync(Guid userId, long facilityId, CancellationToken cancellationToken = default);
    Task ValidateVisitAccessAsync(Guid userId, long visitId, CancellationToken cancellationToken = default);
    Task ValidateAppointmentAccessAsync(Guid userId, long appointmentId, CancellationToken cancellationToken = default);
    Task ValidateInvoiceAccessAsync(Guid userId, long invoiceId, CancellationToken cancellationToken = default);
    Task<List<long>> GetUserAccessibleFacilityIdsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> HasFullFacilityAccessAsync(Guid userId, CancellationToken cancellationToken = default);
}
