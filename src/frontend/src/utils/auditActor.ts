import { getStaffAssignmentRoleLabel } from './roleLabels';

export const getAuditActorRoleLabel = (role: string | null | undefined): string => {
    if (!role) return '';
    return role === 'Patient' ? 'Bệnh nhân' : getStaffAssignmentRoleLabel(role);
};
