import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { renderCopilotCardData } from '../components/copilot/copilotDataRenderers';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import { DialogProvider } from '../contexts/DialogContext';
import { DoctorSchedule } from '../pages/doctor/DoctorSchedule';
import { ReceptionAppointments } from '../pages/reception/ReceptionAppointments';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));

afterEach(() => vi.useRealTimers());

it('shows the prescription code and patient name, with a fallback for incomplete rows', () => {
    render(<>{renderCopilotCardData({
        type: 'pharmacist_prescription_queue',
        title: 'Đơn chờ cấp',
        data: [
            { prescriptionCode: 'RX-261103-0042', patientName: 'Nguyễn Văn An' },
            { prescriptionCode: 'RX-261103-0043' }
        ]
    })}</>);

    expect(screen.getByText('RX-261103-0042 — Nguyễn Văn An')).toBeInTheDocument();
    expect(screen.getByText('Đơn thuốc trong hàng đợi')).toBeInTheDocument();
});

it('pads day and month in the receptionist appointment table', async () => {
    vi.mocked(axiosClient.get).mockResolvedValue({
        success: true,
        data: { totalItems: 1, items: [{
            id: 77, appointmentCode: 'APT-77', patientName: 'Người bệnh', doctorName: 'Bác sĩ A',
            appointmentDate: '2030-01-02', startTime: '08:00:00', endTime: '08:30:00', status: 'Confirmed'
        }] }
    });
    render(<DialogProvider><CopilotResourceProvider><ReceptionAppointments /></CopilotResourceProvider></DialogProvider>);

    expect(await screen.findByText(/02\/01\/2030 08:00/)).toBeInTheDocument();
});

it('pads both dates in the weekly schedule heading', async () => {
    vi.useFakeTimers({ toFake: ['Date'], shouldAdvanceTime: true });
    vi.setSystemTime(new Date(2026, 10, 3, 12));
    vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: [] });
    render(<MemoryRouter><DialogProvider><DoctorSchedule /></DialogProvider></MemoryRouter>);

    expect(await screen.findByText('Tuần từ 02/11/2026 đến 08/11/2026')).toBeInTheDocument();
});
