import React from 'react';

interface StatusBadgeProps {
    status: string;
    label?: string;
    size?: 'sm' | 'md';
}

export const StatusBadge: React.FC<StatusBadgeProps> = ({
    status,
    label,
    size = 'md'
}) => {
    const normalize = (status || '').toLowerCase().trim();

    let badgeClass = 'badge-default';
    let text = label || status;

    switch (normalize) {
        case 'confirmed':
        case 'daxacnhan':
        case 'đã xác nhận':
            badgeClass = 'badge-info';
            text = label || 'Đã xác nhận';
            break;
        case 'checkedin':
        case 'datiépnhan':
        case 'đã tiếp nhận':
            badgeClass = 'badge-warning';
            text = label || 'Đã tiếp nhận';
            break;
        case 'inconsultation':
        case 'dangkham':
        case 'đang khám':
            badgeClass = 'badge-info';
            text = label || 'Đang khám';
            break;
        case 'completed':
        case 'hoanthanh':
        case 'hoàn tất':
        case 'hoàn thành':
        case 'approved':
        case 'đã duyệt':
            badgeClass = 'badge-success';
            text = label || (normalize.includes('approved') ? 'Đã duyệt' : 'Hoàn thành');
            break;
        case 'cancelled':
        case 'dahuy':
        case 'đã hủy':
        case 'rejected':
        case 'từ chối':
            badgeClass = 'badge-danger';
            text = label || (normalize.includes('rejected') ? 'Từ chối' : 'Đã hủy');
            break;
        case 'noshow':
        case 'vangmat':
        case 'vắng mặt':
            badgeClass = 'badge-danger';
            text = label || 'Vắng mặt';
            break;
        case 'pending':
        case 'choxuly':
        case 'chờ xử lý':
        case 'chờ duyệt':
        case 'chờ tiếp nhận':
            badgeClass = 'badge-warning';
            text = label || 'Chờ xử lý';
            break;
        default:
            badgeClass = 'badge-default';
            break;
    }

    const padding = size === 'sm' ? '2px 8px' : '4px 10px';
    const fontSize = size === 'sm' ? '0.74rem' : '0.8rem';

    return (
        <span className={`badge ${badgeClass}`} style={{ padding, fontSize }}>
            <span style={{
                width: '6px',
                height: '6px',
                borderRadius: '50%',
                backgroundColor: 'currentColor',
                display: 'inline-block'
            }} />
            {text}
        </span>
    );
};
