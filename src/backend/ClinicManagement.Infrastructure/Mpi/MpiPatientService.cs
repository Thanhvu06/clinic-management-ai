using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Mpi.DTOs;
using ClinicManagement.Application.Mpi.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Mpi;

public class MpiPatientService : IMpiPatientService
{
    private readonly AppDbContext _dbContext;
    private readonly IMrnGenerator _mrnGenerator;

    public MpiPatientService(AppDbContext dbContext, IMrnGenerator mrnGenerator)
    {
        _dbContext = dbContext;
        _mrnGenerator = mrnGenerator;
    }

    public async Task<PagedResult<MpiPatientDto>> SearchPatientsAsync(PatientSearchQuery query, CancellationToken cancellationToken = default)
    {
        var dbQuery = _dbContext.Patients
            .AsNoTracking()
            .Include(p => p.PrimaryFacility)
            .Include(p => p.Allergies)
            .Include(p => p.EmergencyContacts)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.MedicalRecordNumber))
        {
            var mrn = query.MedicalRecordNumber.Trim();
            dbQuery = dbQuery.Where(p => p.MedicalRecordNumber.Contains(mrn));
        }

        if (!string.IsNullOrWhiteSpace(query.NationalId))
        {
            var nid = query.NationalId.Trim();
            dbQuery = dbQuery.Where(p => p.NationalId != null && p.NationalId.Contains(nid));
        }

        if (!string.IsNullOrWhiteSpace(query.BhytNumber))
        {
            var bhyt = query.BhytNumber.Trim();
            dbQuery = dbQuery.Where(p => p.BhytNumber != null && p.BhytNumber.Contains(bhyt));
        }

        if (!string.IsNullOrWhiteSpace(query.PhoneNumber))
        {
            var phone = query.PhoneNumber.Trim();
            dbQuery = dbQuery.Where(p => p.PhoneNumber != null && p.PhoneNumber.Contains(phone));
        }

        if (query.FacilityId.HasValue)
        {
            dbQuery = dbQuery.Where(p => p.PrimaryFacilityId == query.FacilityId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim().ToLower();
            dbQuery = dbQuery.Where(p =>
                p.FullName.ToLower().Contains(term) ||
                p.MedicalRecordNumber.ToLower().Contains(term) ||
                (p.PhoneNumber != null && p.PhoneNumber.Contains(term)) ||
                (p.NationalId != null && p.NationalId.Contains(term)) ||
                (p.BhytNumber != null && p.BhytNumber.Contains(term)));
        }

        var totalItems = await dbQuery.CountAsync(cancellationToken);
        var page = query.Page > 0 ? query.Page : 1;
        var pageSize = query.PageSize > 0 ? query.PageSize : 20;

        var items = await dbQuery
            .OrderByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => MapToMpiDto(p))
            .ToListAsync(cancellationToken);

        return new PagedResult<MpiPatientDto>(items, totalItems, page, pageSize);
    }

    public async Task<MpiPatientDto> GetPatientByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var patient = await _dbContext.Patients
            .AsNoTracking()
            .Include(p => p.PrimaryFacility)
            .Include(p => p.Allergies)
            .Include(p => p.EmergencyContacts)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (patient == null)
            throw new NotFoundException($"Không tìm thấy hồ sơ bệnh nhân với ID: {id}");

        return MapToMpiDto(patient);
    }

    public async Task<MpiPatientDto> GetPatientByMrnAsync(string mrn, CancellationToken cancellationToken = default)
    {
        var cleanMrn = mrn.Trim();
        var patient = await _dbContext.Patients
            .AsNoTracking()
            .Include(p => p.PrimaryFacility)
            .Include(p => p.Allergies)
            .Include(p => p.EmergencyContacts)
            .FirstOrDefaultAsync(p => p.MedicalRecordNumber == cleanMrn, cancellationToken);

        if (patient == null)
            throw new NotFoundException($"Không tìm thấy hồ sơ bệnh nhân với mã MRN: {cleanMrn}");

        return MapToMpiDto(patient);
    }

    public async Task<MpiPatientDto> RegisterWalkInPatientAsync(RegisterWalkInPatientRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Check duplicate NationalId if provided
        var cleanNid = string.IsNullOrWhiteSpace(request.NationalId) ? null : request.NationalId.Trim();
        if (cleanNid != null)
        {
            var existingByNid = await _dbContext.Patients
                .FirstOrDefaultAsync(p => p.NationalId == cleanNid, cancellationToken);
            if (existingByNid != null)
                throw new ConflictException($"Bệnh nhân với số CCCD/Định danh '{cleanNid}' đã tồn tại trong hệ thống (Mã MRN: {existingByNid.MedicalRecordNumber}).");
        }

        // 2. Generate new MRN and persist Patient inside transaction
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var mrn = await _mrnGenerator.GenerateNextMrnAsync(cancellationToken);

            var patient = new Patient
            {
                UserId = null,
                MedicalRecordNumber = mrn,
                FullName = request.FullName.Trim(),
                PhoneNumber = request.PhoneNumber?.Trim(),
                Email = request.Email?.Trim(),
                Gender = request.Gender,
                DateOfBirth = request.DateOfBirth,
                Address = request.Address?.Trim(),
                NationalId = cleanNid,
                BhytNumber = request.BhytNumber?.Trim(),
                BloodType = request.BloodType?.Trim(),
                RhFactor = request.RhFactor?.Trim(),
                PrimaryFacilityId = request.PrimaryFacilityId
            };

            if (request.Allergies != null && request.Allergies.Count > 0)
            {
                foreach (var a in request.Allergies)
                {
                    patient.Allergies.Add(new PatientAllergy
                    {
                        AllergenType = a.AllergenType,
                        AllergenName = a.AllergenName.Trim(),
                        Severity = a.Severity,
                        ReactionDescription = a.ReactionDescription?.Trim(),
                        RecordedAtUtc = DateTime.UtcNow
                    });
                }
            }

            if (request.EmergencyContact != null && !string.IsNullOrWhiteSpace(request.EmergencyContact.FullName))
            {
                patient.EmergencyContacts.Add(new EmergencyContact
                {
                    FullName = request.EmergencyContact.FullName.Trim(),
                    Relationship = request.EmergencyContact.Relationship.Trim(),
                    PhoneNumber = request.EmergencyContact.PhoneNumber.Trim(),
                    Address = request.EmergencyContact.Address?.Trim(),
                    IsPrimary = true
                });
            }

            _dbContext.Patients.Add(patient);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return await GetPatientByIdAsync(patient.Id, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<MpiPatientDto> UpdatePatientMpiAsync(long patientId, UpdateMpiPatientRequest request, CancellationToken cancellationToken = default)
    {
        var patient = await _dbContext.Patients
            .Include(p => p.PrimaryFacility)
            .Include(p => p.Allergies)
            .Include(p => p.EmergencyContacts)
            .FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken);

        if (patient == null)
            throw new NotFoundException($"Không tìm thấy hồ sơ bệnh nhân với ID: {patientId}");

        if (!string.IsNullOrWhiteSpace(request.NationalId) && request.NationalId.Trim() != patient.NationalId)
        {
            var cleanNid = request.NationalId.Trim();
            var nidConflict = await _dbContext.Patients
                .AnyAsync(p => p.Id != patientId && p.NationalId == cleanNid, cancellationToken);
            if (nidConflict)
                throw new ConflictException($"Số CCCD/Định danh '{cleanNid}' đã được sử dụng bởi một hồ sơ bệnh nhân khác.");
            patient.NationalId = cleanNid;
        }

        patient.FullName = request.FullName.Trim();
        patient.PhoneNumber = request.PhoneNumber?.Trim();
        patient.Email = request.Email?.Trim();
        patient.Gender = request.Gender;
        patient.DateOfBirth = request.DateOfBirth;
        patient.Address = request.Address?.Trim();
        patient.BhytNumber = request.BhytNumber?.Trim();
        patient.BloodType = request.BloodType?.Trim();
        patient.RhFactor = request.RhFactor?.Trim();
        patient.PrimaryFacilityId = request.PrimaryFacilityId;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapToMpiDto(patient);
    }

    public async Task<PatientAllergyDto> AddAllergyAsync(long patientId, CreatePatientAllergyRequest request, CancellationToken cancellationToken = default)
    {
        var patientExists = await _dbContext.Patients.AnyAsync(p => p.Id == patientId, cancellationToken);
        if (!patientExists)
            throw new NotFoundException($"Không tìm thấy bệnh nhân với ID: {patientId}");

        var allergy = new PatientAllergy
        {
            PatientId = patientId,
            AllergenType = request.AllergenType,
            AllergenName = request.AllergenName.Trim(),
            Severity = request.Severity,
            ReactionDescription = request.ReactionDescription?.Trim(),
            RecordedAtUtc = DateTime.UtcNow
        };

        _dbContext.PatientAllergies.Add(allergy);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new PatientAllergyDto
        {
            Id = allergy.Id,
            PatientId = allergy.PatientId,
            AllergenType = allergy.AllergenType,
            AllergenTypeName = allergy.AllergenType.ToString(),
            AllergenName = allergy.AllergenName,
            Severity = allergy.Severity,
            SeverityName = allergy.Severity.ToString(),
            ReactionDescription = allergy.ReactionDescription,
            RecordedAtUtc = allergy.RecordedAtUtc
        };
    }

    public async Task RemoveAllergyAsync(long patientId, long allergyId, CancellationToken cancellationToken = default)
    {
        var allergy = await _dbContext.PatientAllergies
            .FirstOrDefaultAsync(a => a.Id == allergyId && a.PatientId == patientId, cancellationToken);

        if (allergy == null)
            throw new NotFoundException($"Không tìm thấy thông tin dị ứng với ID: {allergyId}");

        _dbContext.PatientAllergies.Remove(allergy);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static MpiPatientDto MapToMpiDto(Patient p)
    {
        int? age = null;
        if (p.DateOfBirth.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            age = today.Year - p.DateOfBirth.Value.Year;
            if (p.DateOfBirth.Value > today.AddYears(-age.Value)) age--;
        }

        return new MpiPatientDto
        {
            Id = p.Id,
            UserId = p.UserId,
            MedicalRecordNumber = p.MedicalRecordNumber,
            FullName = p.FullName,
            PhoneNumber = p.PhoneNumber,
            Email = p.Email,
            Gender = p.Gender,
            DateOfBirth = p.DateOfBirth,
            Age = age,
            Address = p.Address,
            NationalId = p.NationalId,
            BhytNumber = p.BhytNumber,
            BloodType = p.BloodType,
            RhFactor = p.RhFactor,
            PrimaryFacilityId = p.PrimaryFacilityId,
            PrimaryFacilityName = p.PrimaryFacility?.Name,
            Allergies = p.Allergies.Select(a => new PatientAllergyDto
            {
                Id = a.Id,
                PatientId = a.PatientId,
                AllergenType = a.AllergenType,
                AllergenTypeName = a.AllergenType.ToString(),
                AllergenName = a.AllergenName,
                Severity = a.Severity,
                SeverityName = a.Severity.ToString(),
                ReactionDescription = a.ReactionDescription,
                RecordedAtUtc = a.RecordedAtUtc
            }).ToList(),
            EmergencyContacts = p.EmergencyContacts.Select(c => new EmergencyContactDto
            {
                Id = c.Id,
                PatientId = c.PatientId,
                FullName = c.FullName,
                Relationship = c.Relationship,
                PhoneNumber = c.PhoneNumber,
                Address = c.Address,
                IsPrimary = c.IsPrimary
            }).ToList()
        };
    }
}
