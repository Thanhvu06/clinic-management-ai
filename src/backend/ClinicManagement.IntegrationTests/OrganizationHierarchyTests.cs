using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Organization.DTOs;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class OrganizationHierarchyTests : IntegrationTestBase
{
    public OrganizationHierarchyTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task Admin_Can_Create_Full_Hospital_Hierarchy_And_Manage_Beds()
    {
        await AuthenticateAsync("admin@test.com");

        // 1. Create Facility
        var facilityReq = new CreateFacilityRequest
        {
            Code = "FAC-TEST-01",
            Name = "Bệnh viện Đa khoa Quốc tế Test",
            Address = "123 Đường Y Tế, Phường 1",
            City = "TP. Hồ Chí Minh",
            Phone = "02812345678",
            Email = "contact@test-hospital.vn",
            HospitalLevel = "Hạng 1"
        };
        var facRes = await Client.PostAsJsonAsync("/api/v1/facilities", facilityReq);
        Assert.Equal(HttpStatusCode.Created, facRes.StatusCode);
        var createdFac = await facRes.Content.ReadFromJsonAsync<ApiResponse<FacilityDto>>();
        Assert.NotNull(createdFac?.Data);
        var facilityId = createdFac.Data.Id;
        Assert.Equal("FAC-TEST-01", createdFac.Data.Code);

        // 2. Create Building in Facility
        var buildingReq = new CreateBuildingRequest
        {
            FacilityId = facilityId,
            Code = "T01",
            Name = "Tòa nhà Khám bệnh & Cấp cứu A",
            NumberOfFloors = 8
        };
        var bldRes = await Client.PostAsJsonAsync($"/api/v1/facilities/{facilityId}/buildings", buildingReq);
        Assert.Equal(HttpStatusCode.OK, bldRes.StatusCode);
        var createdBld = await bldRes.Content.ReadFromJsonAsync<ApiResponse<BuildingDto>>();
        Assert.NotNull(createdBld?.Data);
        var buildingId = createdBld.Data.Id;

        // 3. Create Department in Facility & Building
        var deptReq = new CreateDepartmentRequest
        {
            FacilityId = facilityId,
            BuildingId = buildingId,
            Code = "K01-NOI",
            Name = "Khoa Nội Tổng Hợp",
            DepartmentType = DepartmentType.Inpatient,
            Description = "Khoa điều trị nội trú các bệnh nội khoa"
        };
        var deptRes = await Client.PostAsJsonAsync("/api/v1/departments", deptReq);
        Assert.Equal(HttpStatusCode.Created, deptRes.StatusCode);
        var createdDept = await deptRes.Content.ReadFromJsonAsync<ApiResponse<DepartmentDto>>();
        Assert.NotNull(createdDept?.Data);
        var departmentId = createdDept.Data.Id;

        // 4. Create Room in Department
        var roomReq = new CreateRoomRequest
        {
            DepartmentId = departmentId,
            BuildingId = buildingId,
            RoomNumber = "P301",
            Name = "Phòng Bệnh Nội Trú 301",
            RoomType = RoomType.InpatientWard,
            FloorNumber = 3,
            MaxCapacity = 4
        };
        var roomRes = await Client.PostAsJsonAsync("/api/v1/hospital-rooms", roomReq);
        Assert.Equal(HttpStatusCode.Created, roomRes.StatusCode);
        var createdRoom = await roomRes.Content.ReadFromJsonAsync<ApiResponse<RoomDto>>();
        Assert.NotNull(createdRoom?.Data);
        var roomId = createdRoom.Data.Id;

        // 5. Create Bed in Room
        var bedReq = new CreateBedRequest
        {
            RoomId = roomId,
            BedNumber = "G01",
            BedType = BedType.Electric,
            DailyRate = 250000m,
            Notes = "Giường điện đa chức năng"
        };
        var bedRes = await Client.PostAsJsonAsync("/api/v1/beds", bedReq);
        Assert.Equal(HttpStatusCode.OK, bedRes.StatusCode);
        var createdBed = await bedRes.Content.ReadFromJsonAsync<ApiResponse<BedDto>>();
        Assert.NotNull(createdBed?.Data);
        var bedId = createdBed.Data.Id;
        Assert.Equal(BedStatus.Available, createdBed.Data.Status);

        // 6. Update Bed Status to Occupied
        var statusReq = new UpdateBedStatusRequest
        {
            Status = BedStatus.Occupied,
            Notes = "Bệnh nhân đã nhập viện"
        };
        var updateBedRes = await Client.PatchAsJsonAsync($"/api/v1/beds/{bedId}/status", statusReq);
        Assert.Equal(HttpStatusCode.OK, updateBedRes.StatusCode);
        var updatedBed = await updateBedRes.Content.ReadFromJsonAsync<ApiResponse<BedDto>>();
        Assert.NotNull(updatedBed?.Data);
        Assert.Equal(BedStatus.Occupied, updatedBed.Data.Status);

        // 7. Get Beds by Room
        var bedsRes = await Client.GetAsync($"/api/v1/beds/by-room/{roomId}");
        Assert.Equal(HttpStatusCode.OK, bedsRes.StatusCode);
        var bedsList = await bedsRes.Content.ReadFromJsonAsync<ApiResponse<IReadOnlyList<BedDto>>>();
        Assert.NotNull(bedsList?.Data);
        Assert.NotEmpty(bedsList.Data);
    }

    [Fact]
    public async Task NonAdmin_Cannot_Create_Facility()
    {
        await AuthenticateAsync("doc@test.com");
        var req = new CreateFacilityRequest
        {
            Code = "FAC-FAIL",
            Name = "Unauthorized Facility",
            Address = "123",
            City = "HCM",
            Phone = "0909"
        };
        var res = await Client.PostAsJsonAsync("/api/v1/facilities", req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Public_Can_List_Facilities()
    {
        var res = await Client.GetAsync("/api/v1/facilities");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var facList = await res.Content.ReadFromJsonAsync<ApiResponse<IReadOnlyList<FacilityDto>>>();
        Assert.NotNull(facList?.Data);
    }

    [Fact]
    public async Task Department_Referencing_Building_In_Different_Facility_ShouldReturn422()
    {
        await AuthenticateAsync("admin@test.com");

        // Create Facility 1
        var f1Res = await Client.PostAsJsonAsync("/api/v1/facilities", new CreateFacilityRequest
        {
            Code = $"FAC-H1-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            Name = "Cơ sở 1",
            Address = "123",
            City = "HCM",
            Phone = "0901"
        });
        var fac1 = (await f1Res.Content.ReadFromJsonAsync<ApiResponse<FacilityDto>>())!.Data!;

        // Create Facility 2 & Building in Facility 2
        var f2Res = await Client.PostAsJsonAsync("/api/v1/facilities", new CreateFacilityRequest
        {
            Code = $"FAC-H2-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            Name = "Cơ sở 2",
            Address = "456",
            City = "HN",
            Phone = "0902"
        });
        var fac2 = (await f2Res.Content.ReadFromJsonAsync<ApiResponse<FacilityDto>>())!.Data!;

        var b2Res = await Client.PostAsJsonAsync($"/api/v1/facilities/{fac2.Id}/buildings", new CreateBuildingRequest
        {
            FacilityId = fac2.Id,
            Code = "B-F2",
            Name = "Tòa nhà thuộc Cơ sở 2",
            NumberOfFloors = 5
        });
        var bld2 = (await b2Res.Content.ReadFromJsonAsync<ApiResponse<BuildingDto>>())!.Data!;

        // Try to create Department in Facility 1 pointing to Building in Facility 2
        var deptRes = await Client.PostAsJsonAsync("/api/v1/departments", new CreateDepartmentRequest
        {
            FacilityId = fac1.Id,
            BuildingId = bld2.Id,
            Code = "DEPT-MISMATCH",
            Name = "Khoa sai cơ sở",
            DepartmentType = DepartmentType.Clinical
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, deptRes.StatusCode);
        var err = await deptRes.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("FACILITY_MISMATCH", err?.ErrorCode);
    }

    [Fact]
    public async Task Room_Referencing_Building_In_Different_Facility_Than_Department_ShouldReturn422()
    {
        await AuthenticateAsync("admin@test.com");

        var f1Res = await Client.PostAsJsonAsync("/api/v1/facilities", new CreateFacilityRequest
        {
            Code = $"FAC-R1-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            Name = "Cơ sở A",
            Address = "123",
            City = "HCM",
            Phone = "0903"
        });
        var fac1 = (await f1Res.Content.ReadFromJsonAsync<ApiResponse<FacilityDto>>())!.Data!;

        var f2Res = await Client.PostAsJsonAsync("/api/v1/facilities", new CreateFacilityRequest
        {
            Code = $"FAC-R2-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            Name = "Cơ sở B",
            Address = "456",
            City = "HN",
            Phone = "0904"
        });
        var fac2 = (await f2Res.Content.ReadFromJsonAsync<ApiResponse<FacilityDto>>())!.Data!;

        var bldRes = await Client.PostAsJsonAsync($"/api/v1/facilities/{fac2.Id}/buildings", new CreateBuildingRequest
        {
            FacilityId = fac2.Id,
            Code = "B-FB",
            Name = "Tòa nhà B",
            NumberOfFloors = 5
        });
        var bldInFac2 = (await bldRes.Content.ReadFromJsonAsync<ApiResponse<BuildingDto>>())!.Data!;

        var deptRes = await Client.PostAsJsonAsync("/api/v1/departments", new CreateDepartmentRequest
        {
            FacilityId = fac1.Id,
            Code = "DEPT-IN-F1",
            Name = "Khoa tại Cơ sở 1",
            DepartmentType = DepartmentType.Clinical
        });
        var deptInFac1 = (await deptRes.Content.ReadFromJsonAsync<ApiResponse<DepartmentDto>>())!.Data!;

        // Try creating Room in Dept of Facility 1 with Building of Facility 2
        var roomRes = await Client.PostAsJsonAsync("/api/v1/hospital-rooms", new CreateRoomRequest
        {
            DepartmentId = deptInFac1.Id,
            BuildingId = bldInFac2.Id,
            RoomNumber = "P-MISMATCH",
            Name = "Phòng sai tòa nhà",
            RoomType = RoomType.Consultation,
            FloorNumber = 1,
            MaxCapacity = 2
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, roomRes.StatusCode);
        var err = await roomRes.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("FACILITY_MISMATCH", err?.ErrorCode);
    }

    [Fact]
    public async Task Room_FloorNumber_Validation_MustNotExceedBuildingFloors_AndNotLessThanOne()
    {
        await AuthenticateAsync("admin@test.com");

        var fRes = await Client.PostAsJsonAsync("/api/v1/facilities", new CreateFacilityRequest
        {
            Code = $"FAC-FL-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            Name = "Cơ sở Floor Test",
            Address = "123",
            City = "HCM",
            Phone = "0905"
        });
        var fac = (await fRes.Content.ReadFromJsonAsync<ApiResponse<FacilityDto>>())!.Data!;

        var bldRes = await Client.PostAsJsonAsync($"/api/v1/facilities/{fac.Id}/buildings", new CreateBuildingRequest
        {
            FacilityId = fac.Id,
            Code = "B-3FL",
            Name = "Tòa nhà 3 tầng",
            NumberOfFloors = 3
        });
        var bld = (await bldRes.Content.ReadFromJsonAsync<ApiResponse<BuildingDto>>())!.Data!;

        var deptRes = await Client.PostAsJsonAsync("/api/v1/departments", new CreateDepartmentRequest
        {
            FacilityId = fac.Id,
            BuildingId = bld.Id,
            Code = "DEPT-FL",
            Name = "Khoa Floor",
            DepartmentType = DepartmentType.Clinical
        });
        var dept = (await deptRes.Content.ReadFromJsonAsync<ApiResponse<DepartmentDto>>())!.Data!;

        // 1. FloorNumber = 5 exceeds building floors 3 -> 422
        var exceedRes = await Client.PostAsJsonAsync("/api/v1/hospital-rooms", new CreateRoomRequest
        {
            DepartmentId = dept.Id,
            BuildingId = bld.Id,
            RoomNumber = "P-501",
            Name = "Phòng Tầng 5",
            RoomType = RoomType.Consultation,
            FloorNumber = 5,
            MaxCapacity = 2
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, exceedRes.StatusCode);
        var errExceed = await exceedRes.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("INVALID_FLOOR", errExceed?.ErrorCode);

        // 2. FloorNumber = 0 is less than 1 -> 400
        var lessRes = await Client.PostAsJsonAsync("/api/v1/hospital-rooms", new CreateRoomRequest
        {
            DepartmentId = dept.Id,
            BuildingId = bld.Id,
            RoomNumber = "P-001",
            Name = "Phòng Tầng 0",
            RoomType = RoomType.Consultation,
            FloorNumber = 0,
            MaxCapacity = 2
        });
        Assert.Equal(HttpStatusCode.BadRequest, lessRes.StatusCode);
        var errLess = await lessRes.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("VALIDATION_ERROR", errLess?.ErrorCode);
    }

    [Fact]
    public async Task Bed_Capacity_MustNotExceedRoomMaxCapacity_OnCreateOrActivate()
    {
        await AuthenticateAsync("admin@test.com");

        var fRes = await Client.PostAsJsonAsync("/api/v1/facilities", new CreateFacilityRequest
        {
            Code = $"FAC-CAP-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            Name = "Cơ sở Sức Chứa",
            Address = "123",
            City = "HCM",
            Phone = "0906"
        });
        var fac = (await fRes.Content.ReadFromJsonAsync<ApiResponse<FacilityDto>>())!.Data!;

        var deptRes = await Client.PostAsJsonAsync("/api/v1/departments", new CreateDepartmentRequest
        {
            FacilityId = fac.Id,
            Code = "DEPT-CAP",
            Name = "Khoa Sức Chứa",
            DepartmentType = DepartmentType.Inpatient
        });
        var dept = (await deptRes.Content.ReadFromJsonAsync<ApiResponse<DepartmentDto>>())!.Data!;

        // Room with MaxCapacity = 2
        var roomRes = await Client.PostAsJsonAsync("/api/v1/hospital-rooms", new CreateRoomRequest
        {
            DepartmentId = dept.Id,
            RoomNumber = "P-CAP-02",
            Name = "Phòng 2 giường",
            RoomType = RoomType.InpatientWard,
            FloorNumber = 1,
            MaxCapacity = 2
        });
        var room = (await roomRes.Content.ReadFromJsonAsync<ApiResponse<RoomDto>>())!.Data!;

        // Add Bed 1 -> OK
        var b1Res = await Client.PostAsJsonAsync("/api/v1/beds", new CreateBedRequest
        {
            RoomId = room.Id,
            BedNumber = "G-01",
            DailyRate = 100000m
        });
        Assert.Equal(HttpStatusCode.OK, b1Res.StatusCode);
        var bed1 = (await b1Res.Content.ReadFromJsonAsync<ApiResponse<BedDto>>())!.Data!;

        // Add Bed 2 -> OK (now at capacity: 2/2)
        var b2Res = await Client.PostAsJsonAsync("/api/v1/beds", new CreateBedRequest
        {
            RoomId = room.Id,
            BedNumber = "G-02",
            DailyRate = 100000m
        });
        Assert.Equal(HttpStatusCode.OK, b2Res.StatusCode);

        // Add Bed 3 -> Exceeds capacity (3 > 2) -> 422
        var b3Res = await Client.PostAsJsonAsync("/api/v1/beds", new CreateBedRequest
        {
            RoomId = room.Id,
            BedNumber = "G-03",
            DailyRate = 100000m
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, b3Res.StatusCode);
        var errCap = await b3Res.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("CAPACITY_EXCEEDED", errCap?.ErrorCode);

        // Deactivate Bed 1 -> capacity becomes 1/2
        var deactRes = await Client.PatchAsJsonAsync($"/api/v1/beds/{bed1.Id}/status", new UpdateBedStatusRequest
        {
            Status = BedStatus.Maintenance,
            IsActive = false
        });
        Assert.Equal(HttpStatusCode.OK, deactRes.StatusCode);

        // Now Bed 3 can be created because active beds = 1/2 -> new active = 2/2
        var b3RetryRes = await Client.PostAsJsonAsync("/api/v1/beds", new CreateBedRequest
        {
            RoomId = room.Id,
            BedNumber = "G-03",
            DailyRate = 100000m
        });
        Assert.Equal(HttpStatusCode.OK, b3RetryRes.StatusCode);

        // Now trying to re-activate Bed 1 would exceed capacity (3 > 2) -> 422
        var reactRes = await Client.PatchAsJsonAsync($"/api/v1/beds/{bed1.Id}/status", new UpdateBedStatusRequest
        {
            Status = BedStatus.Available,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reactRes.StatusCode);
        var errReact = await reactRes.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("CAPACITY_EXCEEDED", errReact?.ErrorCode);
    }
}
