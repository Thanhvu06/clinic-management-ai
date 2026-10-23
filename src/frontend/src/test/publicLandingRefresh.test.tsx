import { AuthProvider } from '../auth/AuthContext';
import { ChatProvider } from '../contexts/ChatContext';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PublicLanding } from '../pages/public/PublicLanding';
import '../theme/browserCompat';

const mocks = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: mocks.get } }));

const doctors = [
    { id: 7, fullName: 'Nguyễn Văn An', academicTitle: 'BS', specialtyName: 'Nội khoa', experienceYears: 12 },
    { id: 8, fullName: 'ThS. BS. Trần Thị Bình', specialtyName: 'Tim mạch', experienceYears: 0 },
    { id: 9, fullName: 'PGS.TS.BS.CKII Lê Hoàng Nam', specialtyName: 'Nội khoa', experienceYears: 5 },
];
const questions = [
    'Tôi cần chuẩn bị gì khi đến khám theo lịch hẹn?',
    'Trợ lý AI dùng để làm gì?',
    'Tôi có thể đổi hoặc hủy lịch hẹn không?',
    'Mỗi lượt khám kéo dài bao lâu?',
    'Tôi xem đơn thuốc và kết quả khám ở đâu?',
    'Khám gói sức khỏe cần chuẩn bị gì?',
];
const answers = [
    'Bạn đến trước giờ hẹn 10–15 phút và đọc mã lịch hẹn (APT-...) hoặc số điện thoại tại quầy lễ tân để làm thủ tục.',
    'Trợ lý AI đọc mô tả triệu chứng của bạn và gợi ý chuyên khoa có trong danh mục của phòng khám. Trợ lý không chẩn đoán và không kê đơn; bác sĩ là người khám và kết luận.',
    'Có. Vào mục Lịch hẹn của tôi để gửi yêu cầu đổi giờ hoặc hủy lịch. Lễ tân sẽ duyệt yêu cầu và báo lại cho bạn.',
    'Mỗi khung giờ đặt lịch dài 30 phút.',
    'Sau khi khám xong, đơn thuốc nằm ở mục Đơn thuốc của tôi, kết quả xét nghiệm nằm ở mục Kết quả cận lâm sàng. Bạn có thể in đơn thuốc từ trang này.',
    'Nhịn ăn 6–8 tiếng trước khi lấy máu xét nghiệm, chỉ uống nước lọc. Mang theo kết quả khám và đơn thuốc đang dùng nếu có.',
];
const fullData: Record<string, unknown[]> = {
    '/doctors': doctors,
    '/specialties': [{ id: 1, specialtyName: 'Nội khoa' }],
    '/health-packages': [{ id: 3, code: 'PKG-1', name: 'Gói tổng quát', price: 1500000, includedServicesJson: '["Khám nội","Xét nghiệm máu"]' }],
    '/locations': [{ id: 4, name: 'Cơ sở Quận 1', address: '12 Lê Lợi', openingHours: '07:00–17:00', phone: '028 1234 5678', city: 'TP. Hồ Chí Minh' }],
};
const RouteProbe = () => <output aria-label="Đường dẫn hiện tại">{useLocation().pathname}</output>;
const mount = async () => {
    const result = render(<ConfigProvider theme={{ token: { motion: false } }}><AuthProvider><ChatProvider><MemoryRouter><PublicLanding /><RouteProbe /><MedicalChatWidget /></MemoryRouter></ChatProvider></AuthProvider></ConfigProvider>);
    await screen.findByRole('heading', { name: 'BS. Nguyễn Văn An' });
    return result;
};
beforeEach(() => {
    mocks.get.mockReset();
    mocks.get.mockImplementation(async (url: string) => ({ success: true, data: url === '/doctors' ? doctors : [] }));
});
afterEach(cleanup);

describe('Public landing refresh L1–L7', () => {
    it('L1 keeps exactly three hero text links and one lookup button with their destinations', async () => {
        const { container } = await mount();
        const hero = container.querySelector('main > section')!;
        const actions = within(hero as HTMLElement).getByRole('navigation', { name: 'Lối tắt' });
        expect(within(actions).getAllByRole('link')).toHaveLength(3);
        expect(within(actions).getByRole('link', { name: 'Đặt lịch khám' })).toHaveAttribute('href', '/patient/book');
        expect(within(actions).getByRole('link', { name: 'Hỏi trợ lý AI' })).toHaveAttribute('href', '/patient/ai-consultation');
        expect(within(actions).getByRole('link', { name: 'Bảng giá thuốc' })).toHaveAttribute('href', '/medicines');
        fireEvent.click(within(actions).getByRole('button', { name: 'Tra cứu lịch hẹn' }));
        expect(screen.getByPlaceholderText(/APT-/)).toBeInTheDocument();
        fireEvent.click(within(actions).getByRole('link', { name: 'Bảng giá thuốc' }));
        expect(screen.getByLabelText('Đường dẫn hiện tại')).toHaveTextContent('/medicines');
    });

    it('L1 shows no hero image and a short title', async () => {
        const { container } = await mount();
        expect(container.querySelector('img')).toBeNull();
        expect(screen.queryByAltText('Bác sĩ chuyên khoa ClinicCare')).not.toBeInTheDocument();
        expect(screen.getByRole('heading', { level: 1, name: 'Đặt lịch khám tại ClinicCare' })).toBeInTheDocument();
    });

    it('L2 shows five sentence-case headings without an eyebrow or accent', async () => {
        mocks.get.mockImplementation(async (url: string) => ({ success: true, data: fullData[url] ?? [] }));
        await mount();
        for (const title of ['Chuyên khoa', 'Bác sĩ', 'Gói khám', 'Cơ sở khám', 'Câu hỏi thường gặp']) {
            const heading = screen.getByRole('heading', { level: 2, name: title });
            expect(heading.previousElementSibling).toBeNull();
            expect(heading.querySelector('span')).not.toBeInTheDocument();
        }
        for (const eyebrow of ['CHUYÊN KHOA', 'GÓI KHÁM', 'ĐỘI NGŨ BÁC SĨ', 'ĐIỂM KHÁM', 'QUY TRÌNH', 'HỎI ĐÁP']) expect(screen.queryByText(eyebrow)).not.toBeInTheDocument();
    });

    it.each(['Đau tức ngực, hồi hộp', 'Đau đầu, chóng mặt kéo dài', 'Ho sốt, đau rát họng', 'Đau nhức khớp gối, thắt lưng'])('L3 drops the symptom button %s and opens AI consultation from the quick link', async symptom => {
        await mount();
        expect(screen.queryByRole('button', { name: symptom })).not.toBeInTheDocument();
        fireEvent.click(within(screen.getByRole('navigation', { name: 'Lối tắt' })).getByRole('link', { name: 'Hỏi trợ lý AI' }));
        expect(screen.getByRole('dialog', { name: 'Trợ lý ClinicCare AI' })).toBeInTheDocument();
        expect(screen.queryByRole('link', { name: 'Đặt lịch khám ngay' })).not.toBeInTheDocument();
    });

    it('L4 derives two initials from API names, strips ThS. BS. and preserves doctor links', async () => {
        await mount();
        const section = document.getElementById('doctors')!;
        expect(within(section).getByText('VA')).toBeInTheDocument();
        expect(within(section).getByText('TB')).toBeInTheDocument();
        expect(within(section).getByText('HN')).toBeInTheDocument();
        const card = screen.getByRole('heading', { name: 'BS. Nguyễn Văn An' }).closest('article')!;
        expect(within(card).getByRole('link', { name: 'Hồ sơ' })).toHaveAttribute('href', '/doctors/7');
        expect(within(card).getByRole('link', { name: 'Đặt khám' })).toHaveAttribute('href', '/patient/book?doctorId=7');
        expect(within(section).queryByRole('link', { name: 'Đặt lịch khám với bác sĩ' })).not.toBeInTheDocument();
    });

    it('L5 drops the four numbered booking steps 01–04', async () => {
        await mount();
        expect(screen.queryByRole('heading', { name: 'Quy trình đặt lịch 4 bước đơn giản' })).not.toBeInTheDocument();
        for (const number of ['01', '02', '03', '04']) expect(screen.queryByText(number)).not.toBeInTheDocument();
        for (const step of ['Chọn chuyên khoa', 'Chọn bác sĩ & Giờ khám', 'Nhận mã hẹn tức thì', 'Khám & Nhận đơn thuốc']) expect(screen.queryByRole('heading', { name: step })).not.toBeInTheDocument();
    });

    it('L6 opens the first question by default and keeps all six questions and answers in an accordion', async () => {
        await mount();
        const section = screen.getByRole('heading', { name: 'Câu hỏi thường gặp' }).closest('section')!;
        const buttons = within(section).getAllByRole('button');
        expect(buttons.map(button => button.textContent)).toEqual(questions);
        expect(buttons[0]).toHaveAttribute('aria-expanded', 'true');
        expect(screen.getByText(answers[0])).toBeVisible();
        for (let index = 1; index < questions.length; index++) {
            fireEvent.click(buttons[index]);
            expect(buttons[index]).toHaveAttribute('aria-expanded', 'true');
            expect(buttons[index - 1]).toHaveAttribute('aria-expanded', 'false');
            await waitFor(() => expect(screen.getByText(answers[index])).toBeVisible());
        }
    });

    it('L7 ends with FAQ and no final call to action', async () => {
        await mount();
        expect(screen.queryByRole('region', { name: 'Sẵn sàng đặt lịch khám?' })).not.toBeInTheDocument();
        expect(screen.queryByText('Chọn bác sĩ và giờ khám trực tuyến, nhận mã lịch hẹn ngay.')).not.toBeInTheDocument();
        const faq = screen.getByRole('region', { name: 'Câu hỏi thường gặp' });
        expect(faq.nextElementSibling).toBeNull();
        expect(screen.queryByRole('link', { name: 'Trò chuyện với Trợ lý AI' })).not.toBeInTheDocument();
    });
});

describe('Public landing SPA links', () => {
    it('navigates from hero booking to /patient/book without reloading the page', async () => {
        const { container } = await mount();
        const hero = container.querySelector('main > section')!;
        const routeProbe = screen.getByLabelText('Đường dẫn hiện tại');
        fireEvent.click(within(hero as HTMLElement).getByRole('link', { name: 'Đặt lịch khám' }));
        expect(routeProbe).toHaveTextContent('/patient/book');
        expect(screen.getByLabelText('Đường dẫn hiện tại')).toBe(routeProbe);
        expect(mocks.get).toHaveBeenCalledTimes(4);
    });
});
