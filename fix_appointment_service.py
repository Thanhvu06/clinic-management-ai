import re

with open('src/backend/ClinicManagement.Infrastructure/Appointments/AppointmentService.cs', 'r', encoding='utf-8') as f:
    content = f.read()

old_query = '''        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AppointmentDto
            {
                Id = a.Id,
                AppointmentCode = a.AppointmentCode,
                PatientId = a.PatientId,
                DoctorId = a.DoctorId,
                SpecialtyId = a.SpecialtyId,
                AppointmentSlotId = a.AppointmentSlotId,
                AppointmentDate = a.AppointmentDate,
                StartTime = a.StartTime,
                EndTime = a.EndTime,
                Reason = a.Reason,
                Status = a.Status.ToString()
            })
            .ToListAsync();'''

new_query = '''        var items = await (from a in query
                           join d in _dbContext.Doctors on a.DoctorId equals d.Id
                           join u in _dbContext.Users on d.UserId equals u.Id
                           join s in _dbContext.Specialties on a.SpecialtyId equals s.Id
                           select new AppointmentDto
                           {
                               Id = a.Id,
                               AppointmentCode = a.AppointmentCode,
                               PatientId = a.PatientId,
                               DoctorId = a.DoctorId,
                               DoctorName = u.FullName,
                               SpecialtyId = a.SpecialtyId,
                               SpecialtyName = s.Name,
                               AppointmentSlotId = a.AppointmentSlotId,
                               AppointmentDate = a.AppointmentDate,
                               StartTime = a.StartTime,
                               EndTime = a.EndTime,
                               Reason = a.Reason,
                               Status = a.Status.ToString()
                           })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();'''

content = content.replace(old_query, new_query)

pattern = r"var appointment = await _dbContext\.Appointments\s*\.AsNoTracking\(\)\s*\.FirstOrDefaultAsync\(a => a\.Id == appointmentId && a\.PatientId == patient\.Id\);\s*if \(appointment == null\)\s*throw new NotFoundException\([^\)]+\);\s*return new AppointmentDto\s*{[^}]+};"

new_query2 = '''var appointment = await (from a in _dbContext.Appointments.AsNoTracking()
                                 join d in _dbContext.Doctors on a.DoctorId equals d.Id
                                 join u in _dbContext.Users on d.UserId equals u.Id
                                 join s in _dbContext.Specialties on a.SpecialtyId equals s.Id
                                 where a.Id == appointmentId && a.PatientId == patient.Id
                                 select new AppointmentDto
                                 {
                                     Id = a.Id,
                                     AppointmentCode = a.AppointmentCode,
                                     PatientId = a.PatientId,
                                     DoctorId = a.DoctorId,
                                     DoctorName = u.FullName,
                                     SpecialtyId = a.SpecialtyId,
                                     SpecialtyName = s.Name,
                                     AppointmentSlotId = a.AppointmentSlotId,
                                     AppointmentDate = a.AppointmentDate,
                                     StartTime = a.StartTime,
                                     EndTime = a.EndTime,
                                     Reason = a.Reason,
                                     Status = a.Status.ToString()
                                 }).FirstOrDefaultAsync();

        if (appointment == null)
            throw new NotFoundException("L?ch h?n không t?n t?i ho?c b?n không có quy?n xem.");

        return appointment;'''

content = re.sub(pattern, new_query2, content)

# One more to fix: CreateAppointmentAsync returns AppointmentDto too!
# Let's just fix that too.
create_pattern = r"return new AppointmentDto\s*{\s*Id = appointment\.Id,[^}]+};"
create_new = '''return new AppointmentDto
            {
                Id = appointment.Id,
                AppointmentCode = appointment.AppointmentCode,
                PatientId = appointment.PatientId,
                DoctorId = appointment.DoctorId,
                DoctorName = "", // Will be reloaded by frontend if needed or we could load it
                SpecialtyId = appointment.SpecialtyId,
                SpecialtyName = "",
                AppointmentSlotId = appointment.AppointmentSlotId,
                AppointmentDate = appointment.AppointmentDate,
                StartTime = appointment.StartTime,
                EndTime = appointment.EndTime,
                Reason = appointment.Reason,
                Status = appointment.Status.ToString()
            };'''
content = re.sub(create_pattern, create_new, content)

with open('src/backend/ClinicManagement.Infrastructure/Appointments/AppointmentService.cs', 'w', encoding='utf-8') as f:
    f.write(content)
