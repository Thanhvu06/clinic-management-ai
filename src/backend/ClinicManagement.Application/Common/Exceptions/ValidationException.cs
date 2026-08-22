namespace ClinicManagement.Application.Common.Exceptions;

public class ValidationException : Exception
{
    public Dictionary<string, string[]> Errors { get; }

    public ValidationException() : base("Dữ liệu không hợp lệ")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(string message) : base(message)
    {
        Errors = new Dictionary<string, string[]>();
    }
    
    public ValidationException(string field, string error) : base("Dữ liệu không hợp lệ")
    {
        Errors = new Dictionary<string, string[]> { { field, new[] { error } } };
    }
}
