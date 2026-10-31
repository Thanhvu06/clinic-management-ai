using ClinicManagement.Application.Common.Constants;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Diagnostics.DTOs;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Diagnostics;

public partial class DiagnosticWorkflowService
{
    public async Task<PagedResult<DiagnosticOrderDto>> GetLabActiveOrdersAsync(int page, int pageSize)
    {
        if (_httpContextAccessor?.HttpContext?.User.IsInRole(RoleNames.LabSupervisor) != true)
            throw new ForbiddenException("Chỉ trưởng khoa xét nghiệm được xem tổng quan xét nghiệm.");
        var facilityIds = await GetVerificationFacilityIdsAsync(RoleNames.LabSupervisor);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = WhereTechnicianFacilityScope(_dbContext.DiagnosticOrders.AsNoTracking(), facilityIds)
            .Where(order => order.Status == DiagnosticOrderStatus.Ordered || order.Status == DiagnosticOrderStatus.InProgress);
        var count = await query.CountAsync();
        var ids = await query.OrderBy(order => order.OrderedAtUtc).ThenBy(order => order.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(order => order.Id).ToListAsync();
        return new PagedResult<DiagnosticOrderDto>(await GetOrderDtosByIdsAsync(ids), count, page, pageSize);
    }
}
