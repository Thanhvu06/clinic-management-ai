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
}
