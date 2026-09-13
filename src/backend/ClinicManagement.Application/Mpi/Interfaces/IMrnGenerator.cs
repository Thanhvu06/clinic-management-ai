using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.Application.Mpi.Interfaces;

public interface IMrnGenerator
{
    Task<string> GenerateNextMrnAsync(CancellationToken cancellationToken = default);
}
