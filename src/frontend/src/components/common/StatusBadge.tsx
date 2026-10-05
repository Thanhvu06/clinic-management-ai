import React from 'react';
import { Tag } from 'antd';
import styles from './common.module.css';

interface StatusBadgeProps {
    status: string;
    label?: string;
    size?: 'sm' | 'md';
}

type TagColor = 'processing' | 'warning' | 'success' | 'error' | 'default';

export const StatusBadge: React.FC<StatusBadgeProps> = ({
    status,
    label,
    size = 'md'
}) => {
    const normalize = (status || '').toLowerCase().trim();

    let color: TagColor = 'default';
    let text = label || status;

    switch (normalize) {
        case 'confirmed':
        case 'daxacnhan':
        case 'đã xác nhận':
            color = 'processing';
            text = label || 'Đã xác nhận';
            break;
        case 'checkedin':
        case 'datiépnhan':
        case 'đã tiếp nhận':
            color = 'warning';
            text = label || 'Đã tiếp nhận';
            break;
        case 'inconsultation':
        case 'dangkham':
        case 'đang khám':
            color = 'processing';
            text = label || 'Đang khám';
            break;
        case 'completed':
        case 'hoanthanh':
        case 'hoàn tất':
        case 'hoàn thành':
        case 'approved':
        case 'đã duyệt':
            color = 'success';
            text = label || (normalize.includes('approved') ? 'Đã duyệt' : 'Hoàn thành');
            break;
        case 'cancelled':
        case 'dahuy':
        case 'đã hủy':
        case 'rejected':
        case 'từ chối':
            color = 'error';
            text = label || (normalize.includes('rejected') ? 'Từ chối' : 'Đã hủy');
            break;
        case 'noshow':
        case 'vangmat':
        case 'vắng mặt':
            color = 'error';
            text = label || 'Vắng mặt';
            break;
        case 'pending':
        case 'choxuly':
        case 'chờ xử lý':
        case 'chờ duyệt':
        case 'chờ tiếp nhận':
            color = 'warning';
            text = label || 'Chờ xử lý';
            break;
        default:
            color = 'default';
            break;
    }

    return (
        <Tag color={color} className={`${styles.statusBadge} ${size === 'sm' ? styles.statusBadgeSm : ''}`}>
            <span className={styles.statusDot} />
            {text}
        </Tag>
    );
};
