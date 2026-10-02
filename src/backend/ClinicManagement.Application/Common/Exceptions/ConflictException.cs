using System;

namespace ClinicManagement.Application.Common.Exceptions;

public class ConflictException : Exception
{
    public string ErrorCode { get; }

    public ConflictException(string message) : base(message)
    {
        ErrorCode = "STATE_CONFLICT";
    }

    public ConflictException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
