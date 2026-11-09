import { StatusBadge } from '../../components/common';

export const categoryMap: Record<string, string> = {
        Laboratory: 'Xét nghiệm',
        Ultrasound: 'Siêu âm',
        Imaging: 'CĐ Hình ảnh',
        Other: 'Khác'
    };

export const statusBadge = (status: string) => {
        switch (status) {
            case 'Ordered':
                return <StatusBadge status="Pending" label="Chờ thực hiện" />;
            case 'InProgress':
                return <StatusBadge status="InConsultation" label="Đang thực hiện" />;
            case 'Completed':
                return <StatusBadge status={status} label="Đã hoàn tất" />;
            case 'Cancelled':
                return <StatusBadge status={status} label="Đã hủy" />;
            default:
                return <StatusBadge status={status} label={status} />;
        }
    };
