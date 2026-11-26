import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { AdminAuditLogs } from '../pages/admin/AdminAuditLogs';
import { AdminDashboard } from '../pages/admin/AdminDashboard';
import { getAuditActionLabel } from '../utils/auditLogLabels';
import { getAuditActorRoleLabel } from '../utils/auditActor';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));

const userActions = [
    { action: 'CreateAppointment', label: 'Đặt lịch hẹn', userFullName: 'Bùi Thị Tám', userRole: 'Patient', entityName: 'Appointment', referenceCode: 'APT-261126-7D95807B' },
    { action: 'ConfirmAppointment', label: 'Xác nhận lịch hẹn', userFullName: 'Lễ tân Hà', userRole: 'Receptionist', entityName: 'Appointment', referenceCode: 'APT-261126-52ECED5F' },
    { action: 'CheckInPatient', label: 'Tiếp nhận bệnh nhân', userFullName: 'Lễ tân Hà', userRole: 'Receptionist', entityName: 'Appointment', referenceCode: 'APT-261126-52ECED5F' },
    { action: 'RequestAppointmentCancellation', label: 'Yêu cầu hủy lịch hẹn', userFullName: 'Bùi Thị Tám', userRole: 'Patient', entityName: 'Appointment', referenceCode: 'APT-1' },
    { action: 'CancelAppointment', label: 'Hủy lịch hẹn', userFullName: 'Lễ tân Hà', userRole: 'Receptionist', entityName: 'Appointment', referenceCode: 'APT-1' },
    { action: 'RequestAppointmentReschedule', label: 'Yêu cầu đổi lịch hẹn', userFullName: 'Bùi Thị Tám', userRole: 'Patient', entityName: 'Appointment', referenceCode: 'APT-2' },
    { action: 'RescheduleAppointment', label: 'Đổi lịch hẹn', userFullName: 'Lễ tân Hà', userRole: 'Receptionist', entityName: 'Appointment', referenceCode: 'APT-2' },
    { action: 'StartConsultation', label: 'Bắt đầu khám', userFullName: 'BS Lý Kim Dung', userRole: 'Doctor', entityName: 'PatientVisit', referenceCode: 'VIS-20261126-1000' },
    { action: 'COMPLETE_CONSULTATION', label: 'Hoàn tất khám', userFullName: 'BS Lý Kim Dung', userRole: 'Doctor', entityName: 'PatientVisit', referenceCode: 'VIS-20261126-1000' },
    { action: 'CreateLeaveRequest', label: 'Tạo đơn nghỉ phép', userFullName: 'BS Lý Kim Dung', userRole: 'Doctor', entityName: 'DoctorLeaveRequest', referenceCode: 'NP1' },
    { action: 'WithdrawLeaveRequest', label: 'Rút đơn nghỉ phép', userFullName: 'BS Lý Kim Dung', userRole: 'Doctor', entityName: 'DoctorLeaveRequest', referenceCode: 'NP1' },
    { action: 'ApproveLeaveRequest', label: 'Duyệt đơn nghỉ phép', userFullName: 'Trần Quản Trị', userRole: 'Admin', entityName: 'DoctorLeaveRequest', referenceCode: 'NP2' },
    { action: 'RejectLeaveRequest', label: 'Từ chối đơn nghỉ phép', userFullName: 'Trần Quản Trị', userRole: 'Admin', entityName: 'DoctorLeaveRequest', referenceCode: 'NP3' },
];
const logs = userActions.map((item, index) => ({
    id: index + 1, userId: `u-${index}`, userFullName: item.userFullName, userRole: item.userRole, action: item.action,
    entityName: item.entityName, entityId: String(index + 1), referenceCode: item.referenceCode,
    description: `Mô tả ${item.action}`, createdAt: '2026-11-26T08:39:00Z',
}));

describe('user action audit labels (H7)', () => {
    it('names every audited user action and actor role in Vietnamese', () => {
        for (const item of userActions) expect(getAuditActionLabel(item.action)).toBe(item.label);
        expect(getAuditActorRoleLabel('Patient')).toBe('Bệnh nhân');
        expect(getAuditActorRoleLabel('Receptionist')).toBe('Lễ tân');
        expect(getAuditActorRoleLabel('Doctor')).toBe('Bác sĩ');
        expect(getAuditActorRoleLabel('Admin')).toBe('Quản trị viên');
        expect(getAuditActorRoleLabel('')).toBe('');
    });
});

describe('admin pages show user actions with actor, role, time and reference (H7)', () => {
    beforeEach(() => {
        vi.mocked(axiosClient.get).mockImplementation(async (url: string) => {
            if (url.startsWith('/admin/audit-logs')) return { success: true, data: { items: logs, totalItems: logs.length } } as never;
            if (url.startsWith('/admin/stats')) return { success: true, data: { totalAppointmentsToday: 1, totalPatients: 1, totalDoctors: 1, lowStockMedicinesCount: 0 } } as never;
            return { success: true, data: { items: [], totalItems: 0 } } as never;
        });
    });

    it('lists each action on the audit log page with its role and reference code', async () => {
        render(<MemoryRouter><AdminAuditLogs /></MemoryRouter>);
        await screen.findByText('APT-261126-7D95807B');
        for (const item of logs) {
            const row = screen.getByText(`Mô tả ${item.action}`).closest('tr')!;
            expect(within(row).getByText(item.userFullName)).toBeInTheDocument();
            expect(within(row).getByText(getAuditActorRoleLabel(item.userRole))).toBeInTheDocument();
            expect(within(row).getByText(item.referenceCode)).toBeInTheDocument();
            expect(within(row).getByText(getAuditActionLabel(item.action))).toBeInTheDocument();
        }
    });

    it('shows user actions with the actor role in recent activity', async () => {
        render(<MemoryRouter><AdminDashboard /></MemoryRouter>);
        const panel = await screen.findByRole('region', { name: 'Hoạt động gần đây' });
        const row = (await within(panel).findByText('Mô tả ConfirmAppointment')).closest('tr')!;
        expect(within(row).getByText('Lễ tân Hà')).toBeInTheDocument();
        expect(within(row).getByText('Lễ tân')).toBeInTheDocument();
        expect(within(row).getByText('Xác nhận lịch hẹn')).toBeInTheDocument();
    });
});
