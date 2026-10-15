import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfigProvider } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminFacilities } from '../pages/admin/AdminFacilities';
import { getStaffAssignmentRoleLabel, staffAssignmentRoleLabels } from '../utils/roleLabels';

const mocks = vi.hoisted(() => ({ facilities: vi.fn(), staff: vi.fn(), departments: vi.fn(), rooms: vi.fn(), users: vi.fn() }));
vi.mock('../api/organizationApi', () => ({ organizationApi: { getFacilities: mocks.facilities, getStaffAssignments: mocks.staff, getDepartments: mocks.departments, getRooms: mocks.rooms } }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.users } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn() }) }));
const ok = <T,>(data: T) => ({ success: true, data });
beforeEach(() => {
    vi.resetAllMocks();
    mocks.facilities.mockResolvedValue(ok([{ id: 1, code: 'FAC', name: 'Cơ sở thử nghiệm', isActive: true }]));
    mocks.departments.mockResolvedValue(ok([])); mocks.rooms.mockResolvedValue(ok([]));
    mocks.users.mockResolvedValue(ok({ items: [{ id: 'user', fullName: 'Nhân viên', email: 'user@example.com', roles: ['Doctor'] }] }));
    mocks.staff.mockResolvedValue(ok(Object.keys(staffAssignmentRoleLabels).concat('FutureRole').map((role, id) => ({ id, role, userId: String(id), fullName: `Nhân viên ${id}`, isActive: true }))));
});
afterEach(cleanup);
const mount = () => render(<ConfigProvider theme={{ token: { motion: false } }}><AdminFacilities /></ConfigProvider>);
describe('Facility role localization', () => {
    it('translates every known assignment role and preserves unknown values', () => {
        expect(getStaffAssignmentRoleLabel('DiagnosticTechnician')).toBe('Kỹ thuật viên CLS');
        expect(getStaffAssignmentRoleLabel('LabSupervisor')).toBe('Trưởng khoa xét nghiệm');
        expect(getStaffAssignmentRoleLabel('FutureRole')).toBe('FutureRole');
        expect(getStaffAssignmentRoleLabel('toString')).toBe('toString');
    });
    it('renders Vietnamese role labels in the staff table including unknown fallback', async () => {
        mount();
        fireEvent.click(await screen.findByRole('tab', { name: /Phân công nhân sự/ }));
        const table = await screen.findByRole('table');
        for (const label of Object.values(staffAssignmentRoleLabels).concat('FutureRole')) expect(within(table).getByText(label)).toBeInTheDocument();
        expect(within(table).queryByText('Doctor')).not.toBeInTheDocument();
    });
    it('offers all ten localized assignment roles and preserves department labels without English', async () => {
        mount();
        fireEvent.click(await screen.findByRole('tab', { name: /Phân công nhân sự/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Phân công nhân viên mới' }));
        await waitFor(() => expect(screen.getByRole('combobox', { name: 'Nhân viên *' })).toBeEnabled());
        await userEvent.click(screen.getByRole('combobox', { name: 'Vai trò nhiệm vụ tại cơ sở *' }));
        for (const label of Object.values(staffAssignmentRoleLabels)) {
            fireEvent.keyDown(screen.getByRole('combobox', { name: 'Vai trò nhiệm vụ tại cơ sở *' }), { key: 'ArrowDown' });
            expect((await screen.findAllByText(label)).length).toBeGreaterThan(0);
        }
        fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Hủy' }));
        fireEvent.click(screen.getByRole('tab', { name: /Cấu trúc/ }));
        fireEvent.click(screen.getByRole('button', { name: /Thêm Khoa/ }));
        await userEvent.click(screen.getByRole('combobox', { name: 'Phân loại khoa' }));
        for (const label of ['Lâm sàng', 'Cận lâm sàng', 'Hành chính', 'Dược', 'Cấp cứu', 'Ngoại khoa', 'Điều trị nội trú']) expect((await screen.findAllByText(label)).length).toBeGreaterThan(0);
        expect(screen.queryByText('Lâm sàng (Clinical)')).not.toBeInTheDocument();
    });
});
