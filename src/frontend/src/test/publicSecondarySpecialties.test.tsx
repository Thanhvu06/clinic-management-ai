import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DoctorsList } from '../pages/public/DoctorsList';
import { DoctorDetail } from '../pages/public/DoctorDetail';

const mocks = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get } }));
const specialties = [{ id: 1, specialtyName: 'Nội tổng quát', specialtyCode: 'SP01' },
    { id: 6, specialtyName: 'Tim mạch', specialtyCode: 'SP06' }];
const doctor = { id: 1, fullName: 'Nguyễn Minh Khải', academicTitle: 'BS.CKI', experienceYears: 12,
    specialtyId: 1, specialtyName: 'Nội tổng quát', specialties };
const ok = (data: unknown) => ({ success: true, data });

beforeEach(() => {
    vi.resetAllMocks();
    mocks.get.mockImplementation(async (url: string) => {
        if (url === '/doctors') return ok([doctor, { id: 2, fullName: 'Bác sĩ chuyên khoa khác', experienceYears: 2,
            specialtyId: 2, specialtyName: 'Nhi khoa', specialties: [{ id: 2, specialtyName: 'Nhi khoa' }] }]);
        if (url === '/specialties') return ok(specialties);
        if (url === '/doctors/1') return ok(doctor);
        if (url === '/doctors/1/specialties') return ok([specialties[0]]);
        if (url === '/doctors/1/availability') return ok({ days: [] });
        throw new Error(`Unexpected API: ${url}`);
    });
});
afterEach(cleanup);

const mount = (detail = false) => render(<ConfigProvider theme={{ token: { motion: false } }}>
    <MemoryRouter initialEntries={[detail ? '/doctors/1' : '/doctors']}><Routes>
        <Route path="/doctors" element={<DoctorsList />} />
        <Route path="/doctors/:id" element={<DoctorDetail />} />
    </Routes></MemoryRouter>
</ConfigProvider>);

describe('Public secondary doctor specialties', () => {
    it('shows both specialties and finds the secondary doctor with the cardiology filter', async () => {
        mount();
        expect(await screen.findByText('Nội tổng quát · Tim mạch')).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: /Bác sĩ chuyên khoa khác/ })).toBeInTheDocument();
        fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Lọc theo chuyên khoa' }));
        await userEvent.click(await screen.findByText('Tim mạch', { selector: '.ant-select-item-option-content' }));
        await waitFor(() => expect(screen.queryByRole('heading', { name: /Bác sĩ chuyên khoa khác/ })).not.toBeInTheDocument());
        expect(screen.getByRole('heading', { name: /Nguyễn Minh Khải/ })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Đặt lịch khám' })).toHaveAttribute('href', '/patient/book?doctorId=1&specialtyId=6');
    });

    it('uses the profile API specialty list and preserves links to both specialties', async () => {
        mount(true);
        await screen.findByRole('heading', { name: /Nguyễn Minh Khải/ });
        const primary = screen.getByRole('link', { name: 'Nội tổng quát' });
        expect(primary.parentElement).toHaveTextContent('Nội tổng quát · Tim mạch');
        expect(screen.getByRole('link', { name: 'Tim mạch' })).toHaveAttribute('href', '/specialties/6');
    });

    it('shows a retryable API error instead of empty doctor results', async () => {
        mocks.get.mockRejectedValueOnce({ status: 503, message: 'Mất kết nối kiểm thử' });
        mount();
        expect(await screen.findByText('Mất kết nối kiểm thử')).toBeInTheDocument();
        expect(screen.queryByText('Không tìm thấy bác sĩ phù hợp')).not.toBeInTheDocument();
        await userEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByText('Nội tổng quát · Tim mạch')).toBeInTheDocument();
    });
});
