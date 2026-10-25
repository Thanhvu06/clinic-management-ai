import { Tag } from 'antd';

export function ExpiryBadge({ overdue, completion = false }: { overdue?: boolean; completion?: boolean }) {
    return overdue ? <Tag color="warning">{completion ? 'Quá hạn hoàn tất' : 'Quá hạn'}</Tag> : null;
}
