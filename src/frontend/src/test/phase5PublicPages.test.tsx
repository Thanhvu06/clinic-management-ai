import { readFileSync } from 'node:fs';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatProvider } from '../contexts/ChatContext';
import { DialogProvider } from '../contexts/DialogContext';
import { PublicLayout } from '../layouts/PublicLayout';
import { DoctorDetail } from '../pages/public/DoctorDetail';
import { DoctorsList } from '../pages/public/DoctorsList';
import { HealthPackageDetail } from '../pages/public/HealthPackageDetail';
import { PublicLanding } from '../pages/public/PublicLanding';
import { formatFreeSlotCount, formatScheduleDayShort, formatScheduleWeekday } from '../pages/public/doctorScheduleLabels';
import { Login } from '../pages/Login';
import { Register } from '../pages/Register';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ get: vi.fn(), data: {} as Record<string, unknown> }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get, post: vi.fn() } }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({
    user: null, isAuthenticated: false, identityVersion: 1, login: vi.fn(), logout: vi.fn(),
}) }));

const RouteProbe = () => <output aria-label="Đường dẫn hiện tại">{useLocation().pathname}{useLocation().search}</output>;
const mount = (route: string, path: string, element: React.ReactNode) => render(
    <ConfigProvider theme={{ token: { motion: false } }}><DialogProvider><ChatProvider>
        <MemoryRouter initialEntries={[route]}>
            <Routes><Route element={<PublicLayout />}><Route path={path} element={element} /></Route></Routes>
            <RouteProbe />
        </MemoryRouter>
    </ChatProvider></DialogProvider></ConfigProvider>
);

const availability = {
    doctorId: 5, doctorName: 'Nguyễn Văn An', specialtyName: 'Nội khoa',
    days: [
        { date: '2026-10-22', dayOfWeek: 'Thursday', scheduleBlocks: [{ startTime: '07:00:00', endTime: '11:30:00' }], availableSlots: [{ slotId: 11, startTime: '08:00:00', endTime: '08:30:00' }] },
        { date: '2026-10-23', dayOfWeek: 'Friday', scheduleBlocks: [], availableSlots: [{ slotId: 12, startTime: '09:00:00', endTime: '09:30:00' }, { slotId: 13, startTime: '09:30:00', endTime: '10:00:00' }] },
        { date: '2026-10-25', dayOfWeek: 'Sunday', scheduleBlocks: [], availableSlots: [] },
    ],
};
const baseData: Record<string, unknown> = {
    '/doctors/5': { id: 5, fullName: 'Nguyễn Văn An', academicTitle: 'BS', experienceYears: 10, description: '' },
    '/doctors/5/specialties': [{ id: 2, specialtyName: 'Nội khoa', specialtyCode: 'NOI' }],
    '/doctors/5/availability': availability,
    '/health-packages/3': { id: 3, code: 'PKG-1', name: 'Gói tổng quát', targetAudience: 'Người lớn', description: 'Khám định kỳ', price: 1500000, includedServices: ['Khám nội', 'Xét nghiệm máu'], isActive: true },
    '/doctors': [{ id: 5, fullName: 'Nguyễn Văn An', academicTitle: 'BS', experienceYears: 10, specialtyId: 2, specialtyName: 'Nội khoa' }],
    '/specialties': [{ id: 2, specialtyName: 'Nội khoa' }],
    '/health-packages': [{ id: 3, code: 'PKG-1', name: 'Gói tổng quát', price: 1500000, includedServicesJson: '["Khám nội"]' }],
    '/locations': [{ id: 4, name: 'Cơ sở Quận 1', address: '12 Lê Lợi', openingHours: '07:00–17:00', phone: '028 1234 5678', city: 'TP. Hồ Chí Minh' }],
};

beforeEach(() => {
    mocks.data = { ...baseData };
    mocks.get.mockReset();
    mocks.get.mockImplementation(async (url: string) => {
        if (url.startsWith('/notifications')) return { success: true, data: { unreadCount: 0 } };
        return { success: true, data: url in mocks.data ? mocks.data[url] : [] };
    });
});
afterEach(cleanup);

describe('GĐ5 mục 1: dải ngày ở trang bác sĩ', () => {
    it('formats weekdays in Vietnamese from the calendar date, not the API English name', () => {
        expect(formatScheduleDayShort('2026-10-22')).toBe('T5 22/10');
        expect(formatScheduleDayShort('2026-10-25')).toBe('CN 25/10');
        expect(formatScheduleWeekday('2026-10-26')).toBe('Thứ Hai');
        expect(formatFreeSlotCount(1)).toBe('1 giờ trống');
        expect(formatFreeSlotCount(0)).toBe('Hết chỗ');
    });

    it('renders the strip inside its own scrollable group with Vietnamese labels and no English words', async () => {
        mount('/doctors/5', '/doctors/:id', <DoctorDetail />);
        const strip = await screen.findByRole('group', { name: 'Chọn ngày khám' });
        const days = within(strip).getAllByRole('button');
        expect(days.map(day => day.textContent)).toEqual(['T5 22/10' + '1 giờ trống', 'T6 23/10' + '2 giờ trống', 'CN 25/10' + 'Hết chỗ']);
        expect(strip.className).toMatch(/dateStrip/);
        expect(strip.closest('section')).toContainElement(screen.getByRole('heading', { name: 'Lịch còn trống 14 ngày tới' }));
        const main = screen.getByRole('main');
        expect(main.textContent).not.toMatch(/Thursday|Friday|Sunday|slot/i);
        expect(screen.getByText('Thứ Năm, 22/10/2026')).toBeInTheDocument();
    });

    it('keeps the date strip column shrinkable so the page never widens', () => {
        const css = readFileSync('src/pages/public/DoctorDetail.module.css', 'utf8');
        expect(css).toMatch(/\.layout\s*\{[^}]*grid-template-columns:\s*minmax\(0,\s*1fr\)/);
        expect(css).toMatch(/\.main\s*\{[^}]*min-width:\s*0/);
        expect(css).toMatch(/\.dateStrip\s*\{[^}]*overflow-x:\s*auto/);
    });

    it('selects another day and books a slot from it', async () => {
        mount('/doctors/5', '/doctors/:id', <DoctorDetail />);
        fireEvent.click(await screen.findByRole('button', { name: /T6 23\/10/ }));
        expect(screen.getByRole('button', { name: /T6 23\/10/ })).toHaveAttribute('aria-pressed', 'true');
        fireEvent.click(screen.getByRole('button', { name: '09:30–10:00' }));
        expect(screen.getByLabelText('Đường dẫn hiện tại')).toHaveTextContent('/patient/book?doctorId=5&specialtyId=2&date=2026-10-23&slotId=13');
    });
});

describe('GĐ5 mục 2 và 4: khung bên phải và nội dung trang bác sĩ', () => {
    it('stacks one full-width primary button above one secondary link on the doctor page', async () => {
        mount('/doctors/5', '/doctors/:id', <DoctorDetail />);
        const aside = await screen.findByRole('complementary', { name: 'Đặt lịch khám' });
        const links = within(aside).getAllByRole('link');
        expect(links.map(link => link.textContent)).toEqual(['Đặt lịch khám', 'Xem chuyên khoa Nội khoa']);
        expect(links[0]).toHaveClass('ant-btn', 'ant-btn-primary', 'ant-btn-block');
        expect(links[0]).toHaveAttribute('href', '/patient/book?doctorId=5&specialtyId=2');
        expect(links[1]).not.toHaveClass('ant-btn');
        expect(links[1].className).not.toMatch(/btn/);
        expect(within(aside).getByText('Giờ trống gần nhất: T5 22/10, 08:00')).toBeInTheDocument();
    });

    it('stacks one full-width primary button above one secondary link on the package page', async () => {
        mount('/health-packages/3', '/health-packages/:id', <HealthPackageDetail />);
        const aside = await screen.findByRole('complementary', { name: 'Giá gói' });
        const links = within(aside).getAllByRole('link');
        expect(links.map(link => link.textContent)).toEqual(['Đăng ký gói khám', 'Xem các gói khám khác']);
        expect(links[0]).toHaveClass('ant-btn-primary', 'ant-btn-block');
        expect(links[1]).not.toHaveClass('ant-btn');
        expect(screen.queryByText(/Cam kết chất lượng/)).not.toBeInTheDocument();
        expect(screen.queryByText(/không phát sinh chi phí phụ/)).not.toBeInTheDocument();
    });

    it('drops the hard-coded expertise list and 30-minute block, and hides an empty introduction', async () => {
        mount('/doctors/5', '/doctors/:id', <DoctorDetail />);
        await screen.findByRole('heading', { level: 1, name: 'BS. Nguyễn Văn An' });
        expect(screen.queryByText('Lĩnh vực chuyên môn')).not.toBeInTheDocument();
        expect(screen.queryByText(/Quy chuẩn ca khám 30 phút/)).not.toBeInTheDocument();
        expect(screen.queryByText('Thăm khám lâm sàng và chẩn đoán bệnh lý chuyên khoa')).not.toBeInTheDocument();
        expect(screen.queryByRole('heading', { name: 'Giới thiệu' })).not.toBeInTheDocument();
    });

    it('shows the introduction only when the doctor has one', async () => {
        mocks.data['/doctors/5'] = { ...(baseData['/doctors/5'] as object), description: 'Bác sĩ nội tổng quát tại cơ sở Quận 1.' };
        mount('/doctors/5', '/doctors/:id', <DoctorDetail />);
        const heading = await screen.findByRole('heading', { name: 'Giới thiệu' });
        expect(heading.closest('section')).toHaveTextContent('Bác sĩ nội tổng quát tại cơ sở Quận 1.');
    });

    it('separates a load error with retry from the not-found state', async () => {
        mocks.get.mockRejectedValueOnce({ status: 500, message: 'Máy chủ bận' });
        mount('/doctors/5', '/doctors/:id', <DoctorDetail />);
        expect(await screen.findByText('Máy chủ bận')).toBeInTheDocument();
        expect(screen.queryByText('Không tìm thấy bác sĩ')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByRole('heading', { level: 1, name: 'BS. Nguyễn Văn An' })).toBeInTheDocument();
    });
});

describe('GĐ5 mục 3: nút trong danh sách bác sĩ', () => {
    it('uses a standard antd button link for booking and drops the marketing subtitle', async () => {
        mount('/doctors', '/doctors', <DoctorsList />);
        expect(await screen.findByRole('heading', { level: 1, name: 'Đội ngũ bác sĩ' })).toBeInTheDocument();
        const card = (await screen.findByRole('heading', { level: 2, name: 'BS. Nguyễn Văn An' })).closest('[class*="doctorCard"]') as HTMLElement;
        const cardBooking = within(card).getByRole('link', { name: 'Đặt lịch khám' });
        expect(cardBooking).toHaveClass('ant-btn', 'ant-btn-primary');
        expect(cardBooking).toHaveAttribute('href', '/patient/book?doctorId=5&specialtyId=2');
        expect(cardBooking.className).not.toMatch(/btn-sm/);
        expect(screen.queryByText(/giàu y đức/)).not.toBeInTheDocument();
    });
});

describe('GĐ5 mục 5 và 6: header và footer', () => {
    it('has one primary booking button, text links for sign in and sign up, and no top bar', async () => {
        const { container } = mount('/doctors', '/doctors', <DoctorsList />);
        await screen.findByRole('heading', { level: 1, name: 'Đội ngũ bác sĩ' });
        const header = container.querySelector('header')!;
        const primary = header.querySelectorAll('.ant-btn-primary');
        expect([...primary].map(node => node.textContent)).toEqual(['Đặt lịch khám', 'Đặt lịch khám']);
        expect(primary[0].closest('[class*="mobileMenu"]')).toBeNull();
        expect(primary[1].closest('[class*="mobileMenu"]')).not.toBeNull();
        const authArea = primary[0].parentElement!;
        for (const name of ['Đăng nhập', 'Đăng ký']) {
            const link = within(authArea).getByRole('link', { name });
            expect(link).not.toHaveClass('ant-btn');
            expect(link.className).toMatch(/textLink/);
        }
        expect(within(authArea).getByRole('button', { name: 'Tra cứu lịch hẹn' })).toBeInTheDocument();
        expect(container.querySelector('[class*="topBar"]')).toBeNull();
        expect(screen.queryByText('Hệ thống cơ sở phòng khám')).not.toBeInTheDocument();
        expect(screen.queryByText('Đăng ký ngay')).not.toBeInTheDocument();
    });

    it('puts the AI note beside the copyright line and drops the tagline', async () => {
        const { container } = mount('/doctors', '/doctors', <DoctorsList />);
        await screen.findByRole('heading', { level: 1, name: 'Đội ngũ bác sĩ' });
        const footer = container.querySelector('footer')!;
        expect(footer).not.toHaveTextContent(/thông minh|trí tuệ nhân tạo|trải nghiệm/);
        const note = within(footer).getByText('Trợ lý AI chỉ gợi ý chuyên khoa để tham khảo, không thay thế chẩn đoán của bác sĩ.');
        expect(note.parentElement).toHaveTextContent(/© 2026 ClinicCare AI/);
        const policy = within(footer).getByRole('heading', { name: 'Chính sách' }).parentElement!;
        expect(policy).not.toContainElement(note);
        expect(within(footer).getAllByRole('heading', { level: 3 }).map(node => node.textContent)).toEqual(['Khám bệnh', 'Tra cứu', 'Chính sách']);
    });

    it('uses only neutral colors and the single accent in public layout styles', () => {
        for (const path of ['src/layouts/PublicLayout.module.css', 'src/pages/public/PublicLanding.module.css', 'src/pages/public/PublicPages.module.css',
            'src/pages/public/DoctorDetail.module.css', 'src/pages/public/HealthPackageDetail.module.css', 'src/pages/public/SpecialtyDetail.module.css']) {
            const css = readFileSync(path, 'utf8');
            expect(css, path).not.toMatch(/--cc-color-(?:secondary|teal|navy)|linear-gradient|text-transform:\s*uppercase|translateY\(-\d+px/);
            expect(css, path).not.toMatch(/#[0-9a-f]{3,8}\b/i);
        }
        expect(readFileSync('src/layouts/PublicLayout.module.css', 'utf8')).toMatch(/\.footerCol h3\s*\{[^}]*color:\s*var\(--cc-color-text-dark\)/);
    });
});

describe('GĐ5 mục 7 và 8: trang chủ', () => {
    it('drops the hero image, slogan, feature row, steps, AI banner, eyebrows and two-tone titles', async () => {
        const { container } = mount('/', '/', <PublicLanding />);
        await screen.findByRole('heading', { level: 2, name: 'Chuyên khoa' });
        expect(container.querySelector('main img')).toBeNull();
        for (const removed of ['Chăm sóc sức khỏe thông minh với Trợ lý AI', 'Đặt lịch trực tuyến 24/7', 'Định tuyến AI an toàn',
            'Quy trình đặt lịch', 'Bạn chưa rõ nên khám chuyên khoa nào?', 'CHUYÊN KHOA', 'GÓI KHÁM', 'ĐỘI NGŨ BÁC SĨ', 'Sẵn sàng đặt lịch khám?']) {
            expect(document.body.textContent).not.toContain(removed);
        }
        expect(container.querySelectorAll('h2 span')).toHaveLength(0);
    });

    it('shows search first, four quick links, then specialties, doctors, packages, locations and FAQ in order', async () => {
        mount('/', '/', <PublicLanding />);
        await screen.findByRole('heading', { level: 2, name: 'Chuyên khoa' });
        const hero = screen.getByRole('heading', { level: 1, name: 'Đặt lịch khám tại ClinicCare AI' }).closest('section')!;
        expect(within(hero).getByRole('textbox', { name: 'Tìm bác sĩ, chuyên khoa, gói khám' })).toBeInTheDocument();
        const quick = within(hero).getByRole('navigation', { name: 'Lối tắt' });
        expect([...quick.children].map(node => node.textContent)).toEqual(['Đặt lịch khám', 'Tra cứu lịch hẹn', 'Bảng giá thuốc', 'Hỏi trợ lý AI']);
        const headings = [...document.querySelectorAll('main main h2')].map(node => node.textContent);
        expect(headings.filter(text => ['Chuyên khoa', 'Bác sĩ', 'Gói khám', 'Cơ sở khám', 'Câu hỏi thường gặp'].includes(text ?? ''))).toEqual(['Chuyên khoa', 'Bác sĩ', 'Gói khám', 'Cơ sở khám', 'Câu hỏi thường gặp']);
        const locations = screen.getByRole('region', { name: 'Cơ sở khám' });
        expect(within(locations).getByText('12 Lê Lợi')).toBeInTheDocument();
        expect(within(locations).getByText('Giờ làm việc: 07:00–17:00')).toBeInTheDocument();
        expect(within(locations).getByRole('link', { name: '028 1234 5678' })).toHaveAttribute('href', 'tel:02812345678');
        const specialties = screen.getByRole('region', { name: 'Chuyên khoa' });
        expect(within(specialties).getByRole('link', { name: 'Hỏi trợ lý AI' })).toHaveAttribute('href', '/patient/ai-consultation');
    });

    it('hides every data section that has no data and keeps FAQ', async () => {
        mocks.data = {};
        mount('/', '/', <PublicLanding />);
        await waitFor(() => expect(mocks.get).toHaveBeenCalledTimes(4));
        await screen.findByRole('heading', { level: 2, name: 'Câu hỏi thường gặp' });
        for (const name of ['Chuyên khoa', 'Bác sĩ', 'Gói khám', 'Cơ sở khám']) expect(screen.queryByRole('heading', { level: 2, name })).not.toBeInTheDocument();
    });

    it('hides only the packages section when packages fail to load', async () => {
        mocks.get.mockImplementation(async (url: string) => {
            if (url === '/health-packages') throw new Error('down');
            return { success: true, data: url in mocks.data ? mocks.data[url] : [] };
        });
        mount('/', '/', <PublicLanding />);
        await screen.findByRole('heading', { level: 2, name: 'Bác sĩ' });
        expect(screen.queryByRole('heading', { level: 2, name: 'Gói khám' })).not.toBeInTheDocument();
        expect(screen.getByRole('heading', { level: 2, name: 'Cơ sở khám' })).toBeInTheDocument();
    });

    it('shows a retryable error when the core data fails', async () => {
        mocks.get.mockImplementation(async (url: string) => {
            if (url === '/specialties') throw { status: 503, message: 'Mất kết nối' };
            return { success: true, data: [] };
        });
        mount('/', '/', <PublicLanding />);
        expect(await screen.findByText('Mất kết nối')).toBeInTheDocument();
        mocks.get.mockImplementation(async (url: string) => ({ success: true, data: url in mocks.data ? mocks.data[url] : [] }));
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByRole('heading', { level: 2, name: 'Chuyên khoa' })).toBeInTheDocument();
    });

    it('suggests doctors, specialties and packages while typing', async () => {
        mount('/', '/', <PublicLanding />);
        await screen.findByRole('heading', { level: 2, name: 'Chuyên khoa' });
        fireEvent.change(screen.getByRole('textbox', { name: 'Tìm bác sĩ, chuyên khoa, gói khám' }), { target: { value: 'gói' } });
        const suggestions = await screen.findByRole('region', { name: 'Gợi ý tìm kiếm' });
        expect(within(suggestions).getByRole('link', { name: 'Gói tổng quát' })).toHaveAttribute('href', '/health-packages/3');
    });
});

describe('GĐ5 mục 9: đăng nhập và đăng ký', () => {
    it('renders a plain centered login form without external images and keeps the E2E labels', async () => {
        const { container } = mount('/login', '/login', <Login />);
        expect(await screen.findByRole('heading', { level: 1, name: 'Đăng nhập' })).toBeInTheDocument();
        expect(container.querySelector('img')).toBeNull();
        expect(screen.queryByText('Chăm sóc sức khỏe thông minh và tin cậy')).not.toBeInTheDocument();
        expect(screen.getByLabelText(/^Email hoặc Số điện thoại/)).toHaveAttribute('id', 'emailOrPhone');
        expect(screen.getByLabelText(/Mật khẩu/)).toBeInTheDocument();
        const submit = screen.getByRole('button', { name: /Đăng nhập vào hệ thống/ });
        expect(submit).toHaveTextContent(/^Đăng nhập$/);
        expect(submit.querySelector('svg')).toBeNull();
    });

    it('renders the register form in the same shell without images', async () => {
        const { container } = mount('/register', '/register', <Register />);
        expect(await screen.findByRole('heading', { level: 1, name: 'Tạo tài khoản' })).toBeInTheDocument();
        expect(container.querySelector('img')).toBeNull();
        expect(screen.getByRole('button', { name: 'Đăng ký' })).toBeInTheDocument();
    });

    it('has no external image sources in the auth shell or pages', () => {
        for (const path of ['src/components/forms/AuthShell.tsx', 'src/pages/Login.tsx', 'src/pages/Register.tsx']) {
            const source = readFileSync(path, 'utf8');
            expect(source, path).not.toMatch(/unsplash|https?:\/\/[^'"\s]+\.(?:jpg|png|webp)|heroImageUrl/);
        }
        expect(readFileSync('src/components/forms/forms.module.css', 'utf8')).toMatch(/\.authBox\s*\{[^}]*max-width:\s*420px/);
    });
});
