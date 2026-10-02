import React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DialogProvider } from '../contexts/DialogContext';
import { CopilotResourceProvider, useCopilotResource } from '../components/copilot/copilotResourceContext';
import { ReceptionAppointments } from '../pages/reception/ReceptionAppointments';
import axiosClient from '../api/axiosClient';
import { organizationApi } from '../api/organizationApi';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../api/patientVisitApi', () => ({ patientVisitApi: { receptionCheckInAppointment: vi.fn() } }));
vi.mock('../api/organizationApi', () => ({ organizationApi: { getDepartments: vi.fn() } }));

const SelectionProbe: React.FC = () => {
    const { selection } = useCopilotResource();
    return <output data-testid="copilot-selection">{selection ? JSON.stringify(selection.context) : ''}</output>;
};

describe('ReceptionAppointments Copilot resource selection', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(axiosClient.get).mockImplementation(async (url: string) => {
            if (url.includes('/history')) return { success: true, data: [] } as never;
            return {
                success: true,
                data: {
                    items: [{ id: 77, appointmentCode: 'AP-77', patientId: 1, patientName: 'Người bệnh', patientPhone: '0900000000', doctorId: 2, doctorName: 'Bác sĩ A', specialtyId: 3, specialtyName: 'Nội khoa', appointmentDate: '2030-01-02', startTime: '08:00:00', endTime: '08:30:00', reason: 'Khám', status: 'Confirmed', facilityId: 9, facilityName: 'Cơ sở X' }],
                    totalItems: 1
                }
            } as never;
        });
        vi.mocked(organizationApi.getDepartments).mockResolvedValue({
            success: true,
            data: [
                { id: 19, facilityId: 9, code: 'KHOA-NOI', name: 'Khoa Nội', isActive: true },
                { id: 20, facilityId: 9, code: 'KHOA-NGOAI', name: 'Khoa Ngoại', isActive: true }
            ]
        } as never);
    });

    it('uses a department explicitly selected from the appointment facility for Copilot check-in', async () => {
        render(<DialogProvider><CopilotResourceProvider><SelectionProbe /><ReceptionAppointments /></CopilotResourceProvider></DialogProvider>);
        await screen.findByText('AP-77');
        fireEvent.click(screen.getByRole('button', { name: /Chi tiết/i }));

        await waitFor(() => expect(organizationApi.getDepartments).toHaveBeenCalledWith(9, expect.any(AbortSignal)));
        const department = await screen.findByRole('option', { name: /Khoa Nội/ });
        fireEvent.change(screen.getByRole('combobox', { name: /Khoa tiếp nhận cho Copilot/ }), { target: { value: '19' } });

        await waitFor(() => expect(screen.getByTestId('copilot-selection')).toHaveTextContent('"appointmentId":77'));
        expect(screen.getByTestId('copilot-selection')).toHaveTextContent('"departmentId":19');
        expect(department).toBeInTheDocument();
    });

    it('clears the department from Copilot when deselected, uses a new selection for another department, and clears on close', async () => {
        render(<DialogProvider><CopilotResourceProvider><SelectionProbe /><ReceptionAppointments /></CopilotResourceProvider></DialogProvider>);
        await screen.findByText('AP-77');
        fireEvent.click(screen.getByRole('button', { name: /Chi tiết/i }));
        const combobox = await screen.findByRole('combobox', { name: /Khoa tiếp nhận cho Copilot/ });
        fireEvent.change(combobox, { target: { value: '19' } });
        await waitFor(() => expect(screen.getByTestId('copilot-selection')).toHaveTextContent('"departmentId":19'));

        fireEvent.change(combobox, { target: { value: '' } });
        await waitFor(() => expect(screen.getByTestId('copilot-selection')).toHaveTextContent('{"appointmentId":77}'));
        expect(screen.getByTestId('copilot-selection')).not.toHaveTextContent('departmentId');

        fireEvent.change(combobox, { target: { value: '20' } });
        await waitFor(() => expect(screen.getByTestId('copilot-selection')).toHaveTextContent('"departmentId":20'));
        fireEvent.click(screen.getByRole('button', { name: 'Đóng chi tiết lịch hẹn' }));
        await waitFor(() => expect(screen.getByTestId('copilot-selection')).toHaveTextContent(''));
    });

    it('ignores an older department response after a newer appointment is opened', async () => {
        const appointments = [
            { id: 77, appointmentCode: 'AP-A', patientId: 1, patientName: 'Bệnh nhân A', patientPhone: '0900000000', doctorId: 2, doctorName: 'Bác sĩ A', specialtyId: 3, specialtyName: 'Nội khoa', appointmentDate: '2030-01-02', startTime: '08:00:00', endTime: '08:30:00', reason: 'Khám', status: 'Confirmed', facilityId: 9, facilityName: 'Cơ sở A' },
            { id: 78, appointmentCode: 'AP-B', patientId: 2, patientName: 'Bệnh nhân B', patientPhone: '0900000001', doctorId: 3, doctorName: 'Bác sĩ B', specialtyId: 4, specialtyName: 'Ngoại khoa', appointmentDate: '2030-01-02', startTime: '09:00:00', endTime: '09:30:00', reason: 'Khám', status: 'Confirmed', facilityId: 10, facilityName: 'Cơ sở B' }
        ];
        vi.mocked(axiosClient.get).mockImplementation(async (url: string) => {
            if (url.includes('/history')) return { success: true, data: [] } as never;
            return { success: true, data: { items: appointments, totalItems: appointments.length } } as never;
        });
        let resolveA!: (value: unknown) => void;
        let resolveB!: (value: unknown) => void;
        vi.mocked(organizationApi.getDepartments).mockImplementation((facilityId?: number) => new Promise(resolve => {
            if (facilityId === 9) resolveA = resolve;
            else resolveB = resolve;
        }) as never);

        render(<DialogProvider><CopilotResourceProvider><SelectionProbe /><ReceptionAppointments /></CopilotResourceProvider></DialogProvider>);
        await screen.findByText('AP-A');
        fireEvent.click(screen.getAllByRole('button', { name: /Chi tiết/i })[0]);
        await waitFor(() => expect(organizationApi.getDepartments).toHaveBeenCalledWith(9, expect.any(AbortSignal)));
        fireEvent.click(screen.getByRole('button', { name: 'Đóng chi tiết lịch hẹn' }));
        fireEvent.click(screen.getAllByRole('button', { name: /Chi tiết/i })[1]);
        await waitFor(() => expect(organizationApi.getDepartments).toHaveBeenCalledWith(10, expect.any(AbortSignal)));

        resolveB({ success: true, data: [{ id: 201, facilityId: 10, code: 'KHOA-B', name: 'Khoa B', isActive: true }] });
        await screen.findByRole('option', { name: /Khoa B/ });
        resolveA({ success: true, data: [{ id: 101, facilityId: 9, code: 'KHOA-A', name: 'Khoa A', isActive: true }] });
        await waitFor(() => expect(screen.queryByRole('option', { name: /Khoa A/ })).not.toBeInTheDocument());
        expect(screen.getByRole('option', { name: /Khoa B/ })).toBeInTheDocument();
        expect(screen.getByTestId('copilot-selection')).toHaveTextContent('"appointmentId":78');
    });

    it('clears department capability and reports a safe error when department loading fails', async () => {
        vi.mocked(organizationApi.getDepartments).mockRejectedValue(new Error('department service unavailable'));
        render(<DialogProvider><CopilotResourceProvider><SelectionProbe /><ReceptionAppointments /></CopilotResourceProvider></DialogProvider>);
        await screen.findByText('AP-77');
        fireEvent.click(screen.getByRole('button', { name: /Chi tiết/i }));
        expect(await screen.findByRole('alert')).toHaveTextContent('department service unavailable');
        expect(screen.getByTestId('copilot-selection')).toHaveTextContent('{"appointmentId":77}');
        expect(screen.queryByRole('option', { name: /Khoa Nội/ })).not.toBeInTheDocument();
    });
});
