export interface ClinicLocation {
    id: string;
    name: string;
    address: string;
    hours: string;
    phone: string;
    city: string;
    features: string[];
}

export const CLINIC_LOCATIONS: ClinicLocation[] = [
    {
        id: 'q1',
        name: 'Phòng khám Đa khoa ClinicCare - Cơ sở Quận 1',
        address: '45 Lê Duẩn, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh',
        hours: '07:00 - 19:00 (Thứ 2 - Chủ Nhật)',
        phone: '028 3822 1111',
        city: 'TP. Hồ Chí Minh',
        features: ['Khám chuyên khoa', 'Xét nghiệm tổng quát', 'Chẩn đoán hình ảnh', 'Nhà thuốc GPP']
    },
    {
        id: 'q5',
        name: 'Phòng khám Đa khoa ClinicCare - Cơ sở Quận 5',
        address: '215 Hồng Bàng, Phường 11, Quận 5, TP. Hồ Chí Minh',
        hours: '07:00 - 19:00 (Thứ 2 - Chủ Nhật)',
        phone: '028 3855 2222',
        city: 'TP. Hồ Chí Minh',
        features: ['Nhi khoa', 'Tim mạch', 'Xét nghiệm sinh hóa', 'Tư vấn dinh dưỡng']
    },
    {
        id: 'q7',
        name: 'Phòng khám Đa khoa ClinicCare - Cơ sở Nam Sài Gòn',
        address: '123 Nguyễn Văn Linh, Phường Tân Phong, Quận 7, TP. Hồ Chí Minh',
        hours: '07:00 - 19:00 (Thứ 2 - Chủ Nhật)',
        phone: '028 3776 3333',
        city: 'TP. Hồ Chí Minh',
        features: ['Cơ xương khớp', 'Sản phụ khoa', 'Nội soi tiêu hóa', 'Gói khám doanh nghiệp']
    },
    {
        id: 'thu-duc',
        name: 'Phòng khám Đa khoa ClinicCare - Cơ sở TP. Thủ Đức',
        address: '56 Võ Văn Ngân, Phường Linh Chiểu, TP. Thủ Đức, TP. Hồ Chí Minh',
        hours: '07:00 - 19:00 (Thứ 2 - Chủ Nhật)',
        phone: '028 3722 4444',
        city: 'TP. Hồ Chí Minh',
        features: ['Khám sức khỏe tổng quát', 'Tai mũi họng', 'Nhà thuốc GPP', 'Xét nghiệm nhanh']
    },
    {
        id: 'dong-da',
        name: 'Phòng khám Đa khoa ClinicCare - Cơ sở Hà Nội',
        address: '178 Thái Hà, Phường Trung Liệt, Quận Đống Đa, Hà Nội',
        hours: '07:00 - 19:00 (Thứ 2 - Chủ Nhật)',
        phone: '024 3857 5555',
        city: 'Hà Nội',
        features: ['Nội tiết & Chuyển hóa', 'Khám tổng quát', 'Siêu âm tim màu', 'Hỗ trợ AI phân luồng']
    },
    {
        id: 'da-nang',
        name: 'Phòng khám Đa khoa ClinicCare - Cơ sở Đà Nẵng',
        address: '92 Quang Trung, Phường Thạch Thang, Quận Hải Châu, TP. Đà Nẵng',
        hours: '07:00 - 19:00 (Thứ 2 - Chủ Nhật)',
        phone: '0236 388 6666',
        city: 'Đà Nẵng',
        features: ['Đa khoa', 'Tầm soát ung thư', 'Lấy máu xét nghiệm tại nhà', 'Bác sĩ chuyên khoa']
    }
];
