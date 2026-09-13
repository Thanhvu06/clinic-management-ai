using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Organization.DTOs;

namespace ClinicManagement.Application.Organization.Interfaces;

public interface IOrganizationService
{
    // Facilities
    Task<IReadOnlyList<FacilityDto>> GetFacilitiesAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<FacilityDto> GetFacilityByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<FacilityDto> CreateFacilityAsync(CreateFacilityRequest request, CancellationToken cancellationToken = default);
    Task<FacilityDto> UpdateFacilityAsync(long id, UpdateFacilityRequest request, CancellationToken cancellationToken = default);
    Task<FacilityDto> ToggleFacilityStatusAsync(long id, CancellationToken cancellationToken = default);

    // Buildings
    Task<IReadOnlyList<BuildingDto>> GetBuildingsByFacilityAsync(long facilityId, CancellationToken cancellationToken = default);
    Task<BuildingDto> CreateBuildingAsync(CreateBuildingRequest request, CancellationToken cancellationToken = default);

    // Departments
    Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(long? facilityId = null, CancellationToken cancellationToken = default);
    Task<DepartmentDto> GetDepartmentByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<DepartmentDto> CreateDepartmentAsync(CreateDepartmentRequest request, CancellationToken cancellationToken = default);

    // Rooms
    Task<IReadOnlyList<RoomDto>> GetRoomsAsync(long? departmentId = null, long? facilityId = null, CancellationToken cancellationToken = default);
    Task<RoomDto> GetRoomByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<RoomDto> CreateRoomAsync(CreateRoomRequest request, CancellationToken cancellationToken = default);

    // Beds
    Task<IReadOnlyList<BedDto>> GetBedsByRoomAsync(long roomId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BedDto>> GetBedsByDepartmentAsync(long departmentId, CancellationToken cancellationToken = default);
    Task<BedDto> CreateBedAsync(CreateBedRequest request, CancellationToken cancellationToken = default);
    Task<BedDto> UpdateBedStatusAsync(long id, UpdateBedStatusRequest request, CancellationToken cancellationToken = default);
}
