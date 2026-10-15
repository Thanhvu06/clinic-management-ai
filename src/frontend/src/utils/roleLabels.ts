export const staffAssignmentRoleLabels: Record<string, string> = {
    Receptionist: 'Lễ tân',
    Doctor: 'Bác sĩ',
    Nurse: 'Điều dưỡng',
    Cashier: 'Thu ngân',
    Pharmacist: 'Dược sĩ',
    Radiologist: 'Bác sĩ chẩn đoán hình ảnh',
    LabTechnician: 'Kỹ thuật viên xét nghiệm',
    DiagnosticTechnician: 'Kỹ thuật viên CLS',
    LabSupervisor: 'Trưởng khoa xét nghiệm',
    Admin: 'Quản trị viên',
};

export function getStaffAssignmentRoleLabel(role: string): string {
    return Object.hasOwn(staffAssignmentRoleLabels, role) ? staffAssignmentRoleLabels[role] : role;
}
