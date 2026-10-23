import { readFileSync } from 'node:fs';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatProvider } from '../contexts/ChatContext';
import { PublicLayout } from '../layouts/PublicLayout';
import { PublicLanding } from '../pages/public/PublicLanding';
import { AuthShell } from '../components/forms/AuthShell';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ get: vi.fn(), patient: false }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get } }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({
    isAuthenticated: mocks.patient, user: mocks.patient ? { role: 'Patient', fullName: 'Người bệnh' } : null,
    logout: vi.fn(), identityVersion: 1,
}) }));

const location = {
    id: 20, name: 'Cơ sở trả về từ API', address: 'Địa chỉ từ API',
    openingHours: '08:00–16:00 từ API', phone: '0901 234 567',
};
const mount = (child = <div>Nội dung trang</div>) => render(<ChatProvider><MemoryRouter>
    <Routes><Route element={<PublicLayout />}><Route path="/" element={child} /></Route></Routes>
</MemoryRouter></ChatProvider>);

beforeEach(() => {
    mocks.patient = false;
    mocks.get.mockReset();
    mocks.get.mockImplementation(async (url: string) => ({ success: true, data: url === '/locations' ? [location] : [] }));
});
afterEach(cleanup);

describe('Public brand and API clinic footer', () => {
    it.each([false, true])('uses ClinicCare AI in public and patient logos and copyright (patient=%s)', async patient => {
        mocks.patient = patient;
        mount(<PublicLanding />);
        expect(within(screen.getByRole('banner')).getByRole('link', { name: 'ClinicCare AI' })).toBeInTheDocument();
        const footer = screen.getByRole('contentinfo');
        expect(within(footer).getByText('ClinicCare AI', { exact: true })).toBeInTheDocument();
        expect(within(footer).getByText('© 2026 ClinicCare AI', { exact: true })).toBeInTheDocument();
        expect(screen.getByRole('heading', { level: 1, name: 'Đặt lịch khám tại ClinicCare AI' })).toBeInTheDocument();
        await screen.findByRole('region', { name: 'Thông tin cơ sở khám' });
        expect(mocks.get.mock.calls.filter(([url]) => url === '/locations')).toHaveLength(1);
    });

    it('uses ClinicCare AI in the auth logo and HTML title', () => {
        render(<MemoryRouter><AuthShell title="Đăng nhập">Biểu mẫu</AuthShell></MemoryRouter>);
        expect(screen.getByRole('link', { name: 'ClinicCare AI' })).toHaveAttribute('href', '/');
        expect(readFileSync('index.html', 'utf8')).toContain('<title>ClinicCare AI</title>');
    });

    it('renders all clinic contact fields from the API in the footer', async () => {
        mount();
        const clinic = await screen.findByRole('region', { name: 'Thông tin cơ sở khám' });
        expect(screen.getByRole('contentinfo')).toContainElement(clinic);
        for (const value of [location.name, location.address, location.openingHours, location.phone]) {
            expect(within(clinic).getByText(value)).toBeInTheDocument();
        }
        expect(mocks.get).toHaveBeenCalledWith('/locations');
        expect(screen.queryByText('Địa chỉ, giờ làm việc và số điện thoại các cơ sở')).not.toBeInTheDocument();
    });

    it('hides clinic information after an empty API result', async () => {
        mocks.get.mockResolvedValue({ success: true, data: [] });
        mount();
        await vi.waitFor(() => expect(screen.queryByRole('status')).not.toBeInTheDocument());
        expect(screen.queryByRole('region', { name: 'Thông tin cơ sở khám' })).not.toBeInTheDocument();
    });

    it('shows a loading state, then an error with a retry that recovers clinic information', async () => {
        let reject!: (reason: Error) => void;
        mocks.get.mockImplementationOnce(() => new Promise((_, fail) => { reject = fail; }));
        mount();
        expect(screen.getByRole('status')).toHaveTextContent('Đang tải thông tin cơ sở khám');
        reject(new Error('Mất kết nối'));
        fireEvent.click(await screen.findByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByRole('region', { name: 'Thông tin cơ sở khám' })).toHaveTextContent(location.address);
    });
});
