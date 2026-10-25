import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import axiosClient from '../api/axiosClient';
import { organizationApi } from '../api/organizationApi';
import { DialogProvider } from '../contexts/DialogContext';
import { ReceptionChangeRequests } from '../pages/reception/ReceptionChangeRequests';
import { AdminFacilities } from '../pages/admin/AdminFacilities';
import { PharmacyInventory } from '../pages/pharmacy/PharmacyInventory';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../api/organizationApi', () => ({ organizationApi: {
    getFacilities: vi.fn(), getDepartments: vi.fn(), getRooms: vi.fn(), getStaffAssignments: vi.fn(), getBedsByRoom: vi.fn(),
} }));
const ok = <T,>(data: T) => ({ success: true, message: '', data });
const mount = (element: React.ReactNode) => render(<MemoryRouter><ConfigProvider theme={{ token: { motion: false } }}><DialogProvider>{element}</DialogProvider></ConfigProvider></MemoryRouter>);

beforeEach(() => vi.resetAllMocks());
afterEach(cleanup);

it('offers processing only for Pending requests and read-only details for all terminal statuses', async () => {
    const statuses = ['Pending', 'Expired', 'Approved', 'Rejected', 'Withdrawn'];
    vi.mocked(axiosClient.get).mockImplementation(async url => ok(String(url).startsWith('/reception/change-requests')
        ? { items: statuses.map((status, id) => ({ id, status, appointmentId: id, patientName: `Người bệnh ${id}`, requestType: 'Cancellation', createdAt: '2026-10-24T02:00:00Z' })), totalItems: 5 }
        : { appointmentCode: 'APT-261024-0123ABCD', slotDate: '2026-10-24', startTime: '09:00:00', endTime: '09:30:00', status: 'Confirmed' }));
    mount(<ReceptionChangeRequests />);
    expect(await screen.findAllByRole('button', { name: 'Xử lý' })).toHaveLength(1);
    expect(screen.getAllByRole('button', { name: 'Xem chi tiết' })).toHaveLength(4);
    for (const button of screen.getAllByRole('button', { name: 'Xem chi tiết' })) {
        fireEvent.click(button);
        const dialog = await screen.findByRole('dialog');
        expect(within(dialog).queryByRole('button', { name: /Duyệt|Từ chối yêu cầu/ })).not.toBeInTheDocument();
        fireEvent.click(within(dialog).getByRole('button', { name: /^Đóng$/ }));
    }
    expect(axiosClient.post).not.toHaveBeenCalled();
}, 15000);

it.each([true, false])('shows consultation rooms and hides bed management only for outpatient facilities: %s', async outpatient => {
    vi.mocked(organizationApi.getFacilities).mockResolvedValue(ok([{ id: 1, code: 'CS01', name: 'Cơ sở khám bệnh', hospitalLevel: outpatient ? 'Phòng khám đa khoa' : 'Hạng 1', address: '', city: '', phone: '', isActive: true, buildingCount: 1, departmentCount: 1 }]));
    vi.mocked(organizationApi.getDepartments).mockResolvedValue(ok([{ id: 1, name: 'Khoa Nội', code: 'KKB' }]) as never);
    vi.mocked(organizationApi.getRooms).mockResolvedValue(ok([{ id: 1, roomNumber: 'P101', name: 'Phòng khám Nội', departmentName: 'Khoa Nội', floorNumber: 1, bedCount: 0 }]) as never);
    vi.mocked(organizationApi.getStaffAssignments).mockResolvedValue(ok([]));
    mount(<AdminFacilities />);
    await screen.findByText('P101 - Phòng khám Nội');
    if (outpatient) {
        expect(screen.getByRole('heading', { name: 'Khoa & phòng khám' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Thêm phòng khám' })).toBeInTheDocument();
        expect(screen.queryByText(/quản lý giường|Khoa phòng & giường bệnh/)).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Thêm phòng bệnh' })).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thêm phòng khám' }));
        expect(await screen.findByRole('dialog', { name: 'Thêm phòng khám' })).toBeInTheDocument();
        expect(screen.queryByText('Sức chứa tối đa (giường)')).not.toBeInTheDocument();
    } else {
        expect(screen.getByRole('heading', { name: 'Khoa phòng & giường bệnh' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Thêm phòng bệnh' })).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Chọn phòng để quản lý giường' })).toBeInTheDocument();
    }
    expect(organizationApi.getBedsByRoom).not.toHaveBeenCalled();
});

it('keeps quantities in the quantity column and removes them from every transaction type label', async () => {
    const types = ['Initial', 'StockIn', 'Dispense', 'Adjustment', 'Reservation'];
    vi.mocked(axiosClient.get).mockImplementation(async url => ok(String(url) === '/medicines/active' ? [] : {
        totalItems: types.length,
        items: types.map((type, id) => ({ id, type, medicineId: 1, medicineCode: 'MED01', medicineName: 'Paracetamol', unit: 'Viên', quantityChange: type === 'Dispense' ? -10 : 20, balanceAfter: 100, actorName: 'Dược sĩ', createdAt: '2026-10-24T02:00:00Z' })),
    }));
    mount(<PharmacyInventory />);
    const table = await screen.findByRole('table');
    expect(within(table).getByText('Xuất đơn')).toBeInTheDocument();
    expect(within(table).getByText('-10 Viên')).toBeInTheDocument();
    for (const row of within(table).getAllByRole('row').slice(1)) {
        const cells = within(row).getAllByRole('cell');
        expect(cells[2].textContent).not.toMatch(/[()\d]/);
    }
});
