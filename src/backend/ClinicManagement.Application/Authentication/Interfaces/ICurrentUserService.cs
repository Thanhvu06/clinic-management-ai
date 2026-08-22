namespace ClinicManagement.Application.Authentication.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
}
