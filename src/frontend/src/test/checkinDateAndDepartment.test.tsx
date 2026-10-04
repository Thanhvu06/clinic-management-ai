import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { DialogProvider } from '../contexts/DialogContext';
import { CopilotResourceProvider, useCopilotResource } from '../components/copilot/copilotResourceContext';
import { ReceptionAppointments } from '../pages/reception/ReceptionAppointments';
import { ReceptionWorkspace } from '../pages/reception/ReceptionWorkspace';
import { DoctorQueue } from '../pages/doctor/DoctorQueue';
import { DoctorDashboard } from '../pages/doctor/DoctorDashboard';
import axiosClient from '../api/axiosClient';
import { doctorApi } from '../api/doctorApi';
import { organizationApi } from '../api/organizationApi';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../api/doctorApi', () => ({ doctorApi: { getQueue: vi.fn(), getDashboard: vi.fn(), checkInAppointment: vi.fn(), markNoShow: vi.fn() } }));
vi.mock('../api/organizationApi', () => ({ organizationApi: { getMyFacilities: vi.fn(), getDepartments: vi.fn(), getRooms: vi.fn(), getFacilities: vi.fn() } }));
vi.mock('../api/patientVisitApi', () => ({ patientVisitApi: { receptionCheckInAppointment: vi.fn(), getDepartmentQueue: vi.fn() } }));
vi.mock('../api/mpiApi', () => ({ mpiApi: { searchPatients: vi.fn() } }));

const appointments = ['2026-10-05', '2026-10-03', '2026-10-04'].map((appointmentDate, index) => ({
    id: 100 + index, appointmentId: 100 + index, appointmentCode: `K-APT-${index}`, patientId: 1,
    patientName: `Người bệnh ${index}`, patientPhone: '0900000000', doctorId: 2, doctorName: 'Bác sĩ',
    specialtyId: 3, specialtyName: 'Nội khoa', facilityId: 9, facilityName: 'Cơ sở K',
    appointmentDate, startTime: '08:00:00', endTime: '08:30:00', reason: 'Khám', status: 'Confirmed', queueOrder: index + 1
}));
const SelectionProbe = () => {
    const { selection } = useCopilotResource();
    return <output data-testid="department-selection">{JSON.stringify(selection?.context)}</output>;
};
const mount = (Page: React.ComponentType) => render(<MemoryRouter><DialogProvider><CopilotResourceProvider><SelectionProbe /><Page /></CopilotResourceProvider></DialogProvider></MemoryRouter>);

describe('K1 check-in date and K4 department preselection', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date(2026, 9, 4, 0, 30));
        vi.mocked(organizationApi.getMyFacilities).mockResolvedValue({ success: true, data: [{ id: 9, name: 'Cơ sở K', isActive: true }] } as never);
        vi.mocked(organizationApi.getDepartments).mockResolvedValue({ success: true, data: [] } as never);
        vi.mocked(organizationApi.getRooms).mockResolvedValue({ success: true, data: [] } as never);
        vi.mocked(axiosClient.get).mockImplementation(async (url: string) => {
            if (url.includes('/history')) return { success: true, data: [] } as never;
            if (url.includes('/reception/appointments')) return { success: true, data: { items: appointments, totalItems: 3 } } as never;
            if (url.includes('/reception/stats')) return { success: true, data: {} } as never;
            return { success: true, data: [] } as never;
        });
        vi.mocked(doctorApi.getQueue).mockResolvedValue({ success: true, data: appointments } as never);
        vi.mocked(doctorApi.getDashboard).mockResolvedValue({ success: true, data: { currentShift: null, kpis: {}, todayQueue: appointments } } as never);
    });
    afterEach(() => { cleanup(); vi.useRealTimers(); });

    it.each([
        ['ReceptionAppointments', ReceptionAppointments], ['ReceptionWorkspace', ReceptionWorkspace],
        ['DoctorQueue', DoctorQueue], ['DoctorDashboard', DoctorDashboard]
    ] as const)('K1 %s disables other dates, keeps today enabled and preserves button labels/40px size', async (_, Page) => {
        mount(Page);
        await screen.findByText('K-APT-0');
        if (Page === ReceptionWorkspace) await waitFor(() => expect(axiosClient.get).toHaveBeenCalledWith(expect.stringContaining('facilityId=9')));
        for (const [index, date] of ['05/10/2026', '03/10/2026', '04/10/2026'].entries()) {
            const row = screen.getByText(`K-APT-${index}`).closest('tr')!;
            const button = within(row).getByRole('button', { name: 'Tiếp nhận' });
            if (index < 2) {
                expect(button).toBeDisabled();
                expect(button).toHaveAttribute('title', `Chỉ tiếp nhận vào ngày khám ${date}`);
            } else expect(button).toBeEnabled();
            expect(Number.parseFloat(button.style.minHeight || button.style.height)).toBeGreaterThanOrEqual(40);
            if (Page === ReceptionAppointments) {
                fireEvent.click(within(row).getByRole('button', { name: 'Chi tiết' }));
                const modalButton = screen.getByRole('button', { name: 'Tiếp nhận & Cấp phiếu STT' });
                if (index < 2) {
                    expect(modalButton).toBeDisabled();
                    expect(modalButton).toHaveAttribute('title', `Chỉ tiếp nhận vào ngày khám ${date}`);
                } else expect(modalButton).toBeEnabled();
                expect(Number.parseFloat(modalButton.style.minHeight || modalButton.style.height)).toBeGreaterThanOrEqual(40);
                fireEvent.click(screen.getByRole('button', { name: 'Đóng chi tiết lịch hẹn' }));
            }
        }
    });

    it('K4 preselects only a unique active same-facility specialty match and retains manual changes and empty ambiguous choices', async () => {
        const matching = { id: 19, facilityId: 9, specialtyId: 3, name: 'Khoa Nội', isActive: true };
        const other = { id: 20, facilityId: 9, specialtyId: 4, name: 'Khoa Ngoại', isActive: true };
        vi.mocked(organizationApi.getDepartments).mockResolvedValue({ success: true, data: [matching, other,
            { ...matching, id: 21, isActive: false }, { ...matching, id: 22, facilityId: 10 }] } as never);
        mount(ReceptionAppointments);
        await screen.findByText('K-APT-0');
        const open = async () => {
            fireEvent.click(within(screen.getByText('K-APT-0').closest('tr')!).getByRole('button', { name: 'Chi tiết' }));
            await waitFor(() => expect(screen.getAllByRole('option', { name: /Khoa Nội/ }).length).toBeGreaterThan(0));
            return screen.getByRole('combobox', { name: /Khoa tiếp nhận cho Copilot/ });
        };
        let select = await open();
        await waitFor(() => expect(select).toHaveValue('19'));
        await waitFor(() => expect(screen.getByTestId('department-selection')).toHaveTextContent('"departmentId":19'));
        expect(screen.getByText(/Đã chọn sẵn khoa theo chuyên khoa của lịch hẹn; bạn có thể đổi/)).toBeInTheDocument();
        fireEvent.change(select, { target: { value: '20' } });
        await waitFor(() => expect(screen.getByTestId('department-selection')).toHaveTextContent('"departmentId":20'));
        for (const departments of [[{ ...matching, specialtyId: 7 }, other], [matching, { ...matching, id: 23 }]]) {
            fireEvent.click(screen.getByRole('button', { name: 'Đóng chi tiết lịch hẹn' }));
            vi.mocked(organizationApi.getDepartments).mockResolvedValue({ success: true, data: departments } as never);
            select = await open();
            await waitFor(() => expect(select).toHaveValue(''));
            expect(screen.getByTestId('department-selection')).not.toHaveTextContent('departmentId');
        }
    });
});
