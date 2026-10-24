import { useState } from 'react';
import { Button, Collapse } from 'antd';
import shared from './LandingSections.module.css';
import styles from './LandingFaq.module.css';

export function LandingFaq() {
    const [openFaq, setOpenFaq] = useState<string | undefined>('0');
    return <section className={shared.section} aria-labelledby="landing-faq-title">
        <div className={shared.container}>
            <div className={shared.sectionHeader}><h2 id="landing-faq-title">Câu hỏi thường gặp</h2></div>
            <div className={styles.faqContainer}>
                <Collapse accordion collapsible="icon" activeKey={openFaq ? [openFaq] : []}
                    onChange={keys => setOpenFaq(Array.isArray(keys) ? keys[0] : keys)}
                    items={faqItems.map((item, idx) => ({
                        key: String(idx),
                        label: <Button type="text" className={styles.faqQuestion}
                            aria-expanded={openFaq === String(idx)}
                            onClick={() => setOpenFaq(openFaq === String(idx) ? undefined : String(idx))}>
                            {item.question}
                        </Button>,
                        children: <p className={styles.faqAnswer}>{item.answer}</p>
                    }))} />
            </div>
            <p className={styles.credit}>Ảnh minh họa: Unsplash</p>
        </div>
    </section>;
}

const faqItems = [
    {
        question: 'Tôi cần chuẩn bị gì khi đến khám theo lịch hẹn?',
        answer: 'Bạn đến trước giờ hẹn 10–15 phút và đọc mã lịch hẹn (APT-...) hoặc số điện thoại tại quầy lễ tân để làm thủ tục.'
    },
    {
        question: 'Trợ lý AI dùng để làm gì?',
        answer: 'Trợ lý AI đọc mô tả triệu chứng của bạn và gợi ý chuyên khoa có trong danh mục của phòng khám. Trợ lý không chẩn đoán và không kê đơn; bác sĩ là người khám và kết luận.'
    },
    {
        question: 'Tôi có thể đổi hoặc hủy lịch hẹn không?',
        answer: 'Có. Vào mục Lịch hẹn của tôi để gửi yêu cầu đổi giờ hoặc hủy lịch. Lễ tân sẽ duyệt yêu cầu và báo lại cho bạn.'
    },
    {
        question: 'Mỗi lượt khám kéo dài bao lâu?',
        answer: 'Mỗi khung giờ đặt lịch dài 30 phút.'
    },
    {
        question: 'Tôi xem đơn thuốc và kết quả khám ở đâu?',
        answer: 'Sau khi khám xong, đơn thuốc nằm ở mục Đơn thuốc của tôi, kết quả xét nghiệm nằm ở mục Kết quả cận lâm sàng. Bạn có thể in đơn thuốc từ trang này.'
    },
    {
        question: 'Khám gói sức khỏe cần chuẩn bị gì?',
        answer: 'Nhịn ăn 6–8 tiếng trước khi lấy máu xét nghiệm, chỉ uống nước lọc. Mang theo kết quả khám và đơn thuốc đang dùng nếu có.'
    }
];
