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
    'Tôi cần chuẩn bị gì khi đến khám theo lịch hẹn đã đặt?',
    'Trợ lý AI của ClinicCare hoạt động như thế nào và có an toàn không?',
    'Tôi có thể đổi hoặc hủy lịch hẹn đã đặt được không?',
    'Khung thời gian cho mỗi ca khám là bao lâu?',
    'Làm thế nào để tôi xem lại đơn thuốc và kết quả ca khám?',
    'Tôi cần chuẩn bị gì khi đến khám gói sức khỏe?',
];
const answers = [
    'Quý khách chỉ cần đến trước giờ hẹn 10-15 phút, xuất trình mã lịch hẹn (APT-...) hoặc số điện thoại tại quầy lễ tân để được tiếp đón ngay mà không cần bốc số chờ đợi.',
    'Trợ lý AI phân tích các mô tả triệu chứng bạn cung cấp để định hướng chuyên khoa phù hợp nhất dựa trên danh mục thực tế của phòng khám. Hệ thống tuyệt đối không chẩn đoán thay bác sĩ, không kê đơn và không thu thập thông tin định danh cá nhân nhạy cảm.',
    "Quý khách hoàn toàn có thể gửi yêu cầu dời lịch sang khung giờ khác hoặc hủy lịch trực tiếp trong mục 'Lịch hẹn của tôi'. Bộ phận lễ tân sẽ xét duyệt và phản hồi nhanh chóng.",
    'Mỗi slot khám bệnh tiêu chuẩn tại ClinicCare kéo dài 30 phút, đảm bảo bác sĩ có đủ thời gian thăm khám kỹ lưỡng, lắng nghe và tư vấn chi tiết cho từng bệnh nhân.',
    "Sau khi bác sĩ hoàn tất ca khám, kết quả tóm tắt và đơn thuốc điện tử sẽ được cập nhật ngay trên tài khoản của bạn tại mục 'Đơn thuốc của tôi'. Bạn có thể xem lại hoặc in ra bất kỳ lúc nào.",
    'Quý khách nên nhịn ăn ít nhất 6-8 tiếng trước khi lấy mẫu máu xét nghiệm, uống đủ nước lọc và mang theo các kết quả khám, đơn thuốc đang dùng nếu có.',
];
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
    it('L1 keeps exactly two primary hero links and three tools with their original roles and destinations', async () => {
        const { container } = await mount();
        const hero = container.querySelector('main > section')!;
        const booking = within(hero as HTMLElement).getByRole('link', { name: 'Đặt lịch khám' });
        const actions = booking.parentElement!;
        expect(within(actions).getAllByRole('link')).toHaveLength(2);
        expect(booking).toHaveAttribute('href', '/patient/book');
        expect(within(actions).getByRole('link', { name: 'Trò chuyện với Trợ lý AI' })).toHaveAttribute('href', '/patient/ai-consultation');
        expect(within(hero as HTMLElement).getByRole('link', { name: 'Tìm chuyên khoa' })).toHaveAttribute('href', '/specialties');
        fireEvent.click(within(hero as HTMLElement).getByRole('button', { name: 'Tra giá thuốc' }));
        expect(screen.getByLabelText('Đường dẫn hiện tại')).toHaveTextContent('/medicines');
        fireEvent.click(within(hero as HTMLElement).getByRole('button', { name: 'Tra cứu lịch hẹn' }));
        expect(screen.getByPlaceholderText(/APT-/)).toBeInTheDocument();
    });

    it('L1 uses the local hero asset and preserves the alt text', async () => {
        await mount();
        const image = screen.getByAltText('Bác sĩ chuyên khoa ClinicCare');
        expect(image.getAttribute('src')).toContain('landing-hero.jpg');
        expect(image.getAttribute('src')).not.toContain('images.unsplash.com');
    });

    it('L2 preserves all six complete headings and supplies an eyebrow and accent', async () => {
        await mount();
        for (const [title, eyebrow] of [
            ['Danh mục Chuyên khoa', 'CHUYÊN KHOA'],
            ['Gói Chăm Sóc Sức Khỏe ClinicCare', 'GÓI KHÁM'],
            ['Đội ngũ Bác sĩ Chuyên khoa', 'ĐỘI NGŨ BÁC SĨ'],
            ['Hệ thống Điểm khám ClinicCare', 'ĐIỂM KHÁM'],
            ['Quy trình đặt lịch 4 bước đơn giản', 'QUY TRÌNH'],
            ['Câu hỏi thường gặp', 'HỎI ĐÁP'],
        ]) {
            const heading = screen.getByRole('heading', { name: title });
            expect(heading.previousElementSibling).toHaveTextContent(eyebrow);
            expect(heading.querySelector('span')).toBeInTheDocument();
        }
    });

    it.each(['Đau tức ngực, hồi hộp', 'Đau đầu, chóng mặt kéo dài', 'Ho sốt, đau rát họng', 'Đau nhức khớp gối, thắt lưng'])('L3 routes the symptom button %s to AI consultation', async symptom => {
        await mount();
        fireEvent.click(screen.getByRole('button', { name: symptom }));
        expect(screen.getByRole('dialog', { name: 'Trợ lý ClinicCare AI' })).toBeInTheDocument();
        expect(screen.getByRole('link', { name: 'Đặt lịch khám ngay' })).toHaveAttribute('href', '/patient/book');
    });

    it('L4 derives two initials from API names, strips ThS. BS. and preserves doctor links', async () => {
        await mount();
        const section = document.getElementById('doctors')!;
        expect(within(section).getByText('VA')).toBeInTheDocument();
        expect(within(section).getByText('TB')).toBeInTheDocument();
        expect(within(section).getByText('HN')).toBeInTheDocument();
        const card = screen.getByRole('heading', { name: 'BS. Nguyễn Văn An' }).parentElement!.parentElement!;
        expect(within(card).getByRole('link', { name: 'Hồ sơ' })).toHaveAttribute('href', '/doctors/7');
        expect(within(card).getByRole('link', { name: 'Đặt khám' })).toHaveAttribute('href', '/patient/book?doctorId=7');
        expect(within(section).getByRole('link', { name: 'Đặt lịch khám với bác sĩ' })).toHaveAttribute('href', '/patient/book');
    });

    it('L5 numbers the unchanged four booking steps 01–04', async () => {
        await mount();
        const section = screen.getByRole('heading', { name: 'Quy trình đặt lịch 4 bước đơn giản' }).closest('section')!;
        for (const number of ['01', '02', '03', '04']) expect(within(section).getByText(number)).toBeInTheDocument();
        expect(within(section).getAllByRole('heading', { level: 3 }).map(node => node.textContent)).toEqual(['Chọn chuyên khoa', 'Chọn bác sĩ & Giờ khám', 'Nhận mã hẹn tức thì', 'Khám & Nhận đơn thuốc']);
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

    it('L7 places the final call to action immediately after FAQ with two correct links', async () => {
        await mount();
        const section = screen.getByRole('region', { name: 'Sẵn sàng đặt lịch khám?' });
        expect(section.previousElementSibling).toContainElement(screen.getByRole('heading', { name: 'Câu hỏi thường gặp' }));
        expect(within(section).getByText('Chọn bác sĩ và giờ khám trực tuyến, nhận mã lịch hẹn ngay.')).toBeInTheDocument();
        expect(within(section).getAllByRole('link')).toHaveLength(2);
        expect(within(section).getByRole('link', { name: 'Đặt lịch khám' })).toHaveAttribute('href', '/patient/book');
        expect(within(section).getByRole('link', { name: 'Trò chuyện với Trợ lý AI' })).toHaveAttribute('href', '/patient/ai-consultation');
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
