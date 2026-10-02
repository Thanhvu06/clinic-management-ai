using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Pharmacy.DTOs;

namespace ClinicManagement.Application.Pharmacy.Interfaces;

public interface IPrescriptionPaymentEligibilityService
{
    Task<PrescriptionPaymentEligibilityDto> EvaluateAsync(long prescriptionId, CancellationToken cancellationToken = default);
}
