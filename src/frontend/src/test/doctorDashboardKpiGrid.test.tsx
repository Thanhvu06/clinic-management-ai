import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { DoctorDashboard } from '../pages/doctor/DoctorDashboard';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({
    useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn(), showToast: vi.fn() })
}));

it('places the four doctor metrics in one MetricStrip without ant columns', async () => {
    vi.mocked(axiosClient.get).mockResolvedValue({ success: true, message: '', data: {
        kpis: { totalAppointmentsToday: 12, waitingCount: 5, inConsultationCount: 1, completedCount: 6, noShowCount: 0 },
        currentShift: null, nextPatient: null, todayQueue: [], upcomingAppointments: []
    } });
    const { container } = render(<MemoryRouter><DoctorDashboard /></MemoryRouter>);

    await screen.findByText('Hôm nay không có ca trực');
    const grid = screen.getByRole('group', { name: 'Số liệu hôm nay' });
    expect(grid.tagName).toBe('DIV');
    expect(grid.children).toHaveLength(4);
    for (const title of ['Đang chờ khám', 'Đang khám', 'Đã khám xong', 'Vắng mặt']) {
        const cell = within(grid).getByText(title).parentElement;
        expect(cell?.parentElement).toBe(grid);
    }
    expect(container.querySelector('.ant-col')).not.toBeInTheDocument();
});
