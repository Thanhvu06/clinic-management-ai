using System;

namespace ClinicManagement.Application.Common.Exceptions;

public class ForbiddenException : Exception
{
    public string ErrorCode { get; }

    public ForbiddenException(string message) : base(message)
    {
        ErrorCode = "FORBIDDEN";
    }

    public ForbiddenException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
