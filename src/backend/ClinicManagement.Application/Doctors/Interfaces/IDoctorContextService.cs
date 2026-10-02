using System.Threading.Tasks;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Doctors.Interfaces;

public interface IDoctorContextService
{
    Task<Doctor> GetCurrentActiveDoctorAsync();
}
