using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Notifications.DTOs;
using ClinicManagement.Application.Notifications.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Notifications;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        ILogger<NotificationService> logger)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<PagedResult<NotificationDto>> GetCurrentUserNotificationsAsync(
        int page = 1,
        int pageSize = 20,
        bool? isRead = null,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            throw new UnauthorizedAccessException("Người dùng chưa đăng nhập.");
        }

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var query = _dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId.Value);

        if (isRead.HasValue)
        {
            query = query.Where(n => n.IsRead == isRead.Value);
        }

        var totalItems = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationDto
            {
                Id = n.Id,
                Type = n.Type,
                Title = n.Title,
                Message = n.Message,
                Route = n.Route,
                RelatedEntityType = n.RelatedEntityType,
                RelatedEntityId = n.RelatedEntityId,
                IsRead = n.IsRead,
                CreatedAtUtc = n.CreatedAtUtc,
                ReadAtUtc = n.ReadAtUtc
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationDto>(items, totalItems, page, pageSize);
    }

    public async Task<UnreadNotificationCountDto> GetCurrentUserUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            return new UnreadNotificationCountDto(0);
        }

        var count = await _dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId.Value && !n.IsRead)
            .CountAsync(cancellationToken);

        return new UnreadNotificationCountDto(count);
    }

    public async Task<bool> MarkAsReadAsync(long id, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            throw new UnauthorizedAccessException("Người dùng chưa đăng nhập.");
        }

        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId.Value, cancellationToken);

        if (notification == null)
        {
            return false;
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<int> MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            throw new UnauthorizedAccessException("Người dùng chưa đăng nhập.");
        }

        var unreadList = await _dbContext.Notifications
            .Where(n => n.UserId == userId.Value && !n.IsRead)
            .ToListAsync(cancellationToken);

        if (unreadList.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        foreach (var notification in unreadList)
        {
            notification.IsRead = true;
            notification.ReadAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return unreadList.Count;
    }

    public async Task<NotificationDto?> CreateNotificationAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(request.DedupeKey))
        {
            var exists = await _dbContext.Notifications
                .AnyAsync(n => n.DedupeKey == request.DedupeKey, cancellationToken);

            if (exists)
            {
                _logger.LogInformation("Notification with DedupeKey {DedupeKey} already exists. Skipping duplicate.", request.DedupeKey);
                return null;
            }
        }

        var notification = new Notification
        {
            UserId = request.UserId,
            Type = request.Type,
            Title = request.Title,
            Message = request.Message,
            Route = request.Route,
            RelatedEntityType = request.RelatedEntityType,
            RelatedEntityId = request.RelatedEntityId,
            DedupeKey = request.DedupeKey,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(notification);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (request.DedupeKey != null)
        {
            _logger.LogWarning(ex, "Concurrency deduplication caught for key: {DedupeKey}", request.DedupeKey);
            return null;
        }

        return new NotificationDto
        {
            Id = notification.Id,
            Type = notification.Type,
            Title = notification.Title,
            Message = notification.Message,
            Route = notification.Route,
            RelatedEntityType = notification.RelatedEntityType,
            RelatedEntityId = notification.RelatedEntityId,
            IsRead = notification.IsRead,
            CreatedAtUtc = notification.CreatedAtUtc,
            ReadAtUtc = notification.ReadAtUtc
        };
    }

    public async Task CreateNotificationsForRolesAsync(
        IEnumerable<string> roles,
        NotificationType type,
        string title,
        string message,
        string? route = null,
        string? relatedEntityType = null,
        string? relatedEntityId = null,
        string? dedupeKeyPrefix = null,
        CancellationToken cancellationToken = default)
    {
        var roleNames = roles.ToList();
        var roleIds = await _dbContext.Roles
            .Where(r => roleNames.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (roleIds.Count == 0) return;

        var userIdsInRoles = await _dbContext.UserRoles
            .Where(ur => roleIds.Contains(ur.RoleId))
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (userIdsInRoles.Count == 0) return;

        var activeUserIds = await _dbContext.Users
            .Where(u => userIdsInRoles.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var userId in activeUserIds)
        {
            string? dedupeKey = null;
            if (!string.IsNullOrEmpty(dedupeKeyPrefix))
            {
                dedupeKey = $"{dedupeKeyPrefix}_{userId}";
                var exists = await _dbContext.Notifications
                    .AnyAsync(n => n.DedupeKey == dedupeKey, cancellationToken);
                if (exists) continue;
            }

            var notification = new Notification
            {
                UserId = userId,
                Type = type,
                Title = title,
                Message = message,
                Route = route,
                RelatedEntityType = relatedEntityType,
                RelatedEntityId = relatedEntityId,
                DedupeKey = dedupeKey,
                IsRead = false,
                CreatedAtUtc = now
            };

            _dbContext.Notifications.Add(notification);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "DbUpdateException occurred during bulk role notification creation.");
        }
    }
}
