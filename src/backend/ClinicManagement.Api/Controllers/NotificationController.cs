using System.Threading.Tasks;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Notifications.DTOs;
using ClinicManagement.Application.Notifications.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public class NotificationController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyNotifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isRead = null)
    {
        var result = await _notificationService.GetCurrentUserNotificationsAsync(page, pageSize, isRead, HttpContext.RequestAborted);
        return Ok(ApiResponse<PagedResult<NotificationDto>>.Ok(result));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var result = await _notificationService.GetCurrentUserUnreadCountAsync(HttpContext.RequestAborted);
        return Ok(ApiResponse<UnreadNotificationCountDto>.Ok(result));
    }

    [HttpPatch("{id}/read")]
    public async Task<IActionResult> MarkAsRead(long id)
    {
        var success = await _notificationService.MarkAsReadAsync(id, HttpContext.RequestAborted);
        if (!success)
        {
            return NotFound(new ApiErrorResponse
            {
                Success = false,
                Message = "Không tìm thấy thông báo hoặc bạn không có quyền truy cập.",
                ErrorCode = "NOTIFICATION_NOT_FOUND"
            });
        }

        return Ok(ApiResponse.Ok("Đã đánh dấu thông báo là đã đọc."));
    }

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var count = await _notificationService.MarkAllAsReadAsync(HttpContext.RequestAborted);
        return Ok(ApiResponse<int>.Ok(count, $"Đã đánh dấu tất cả {count} thông báo là đã đọc."));
    }
}
