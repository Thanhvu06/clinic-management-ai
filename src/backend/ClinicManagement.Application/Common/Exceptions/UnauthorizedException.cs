namespace ClinicManagement.Application.Common.Exceptions;

public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message = "Chưa đăng nhập") : base(message)
    {
    }
}
