using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using System.Net;
using System.Text.Json;

namespace ClinicManagement.Api.Middlewares;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await _next(httpContext);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(httpContext, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        
        var response = new ApiErrorResponse
        {
            Success = false,
            Message = exception.Message
        };

        switch (exception)
        {
            case ValidationException e:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.ErrorCode = "VALIDATION_ERROR";
                response.Message = "Dữ liệu không hợp lệ";
                response.Errors = e.Errors;
                break;
            case UnauthorizedException e:
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                response.ErrorCode = "UNAUTHORIZED";
                response.Message = e.Message;
                break;
            case ForbiddenException e:
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                response.ErrorCode = e.ErrorCode;
                response.Message = e.Message;
                break;
            case NotFoundException e:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                response.ErrorCode = "RESOURCE_NOT_FOUND";
                response.Message = e.Message;
                break;
            case ConflictException e:
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                response.ErrorCode = e.ErrorCode;
                response.Message = e.Message;
                break;
            case BusinessException e:
                context.Response.StatusCode = (int)HttpStatusCode.UnprocessableEntity;
                response.ErrorCode = e.ErrorCode;
                response.Message = e.Message;
                break;
            default:
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.ErrorCode = "INTERNAL_SERVER_ERROR";
                response.Message = "Lỗi hệ thống";
                break;
        }

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return context.Response.WriteAsync(json);
    }
}
