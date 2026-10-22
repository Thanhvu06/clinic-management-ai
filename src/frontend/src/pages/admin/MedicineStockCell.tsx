import React from 'react';
import { Typography } from 'antd';
import { AlertTriangle } from 'lucide-react';
import styles from './AdminMedicines.module.css';

interface MedicineStockCellProps {
    stockQuantity: number;
    reorderLevel: number;
}

export const MedicineStockCell: React.FC<MedicineStockCellProps> = ({ stockQuantity, reorderLevel }) => {
    // "<=" khớp với cách backend tính tồn kho thấp (MedicineProjections, AdminController).
    const low = stockQuantity <= reorderLevel;
    return (
        <Typography.Text type={low ? 'warning' : undefined} className={styles.medicineStock}>
            <strong>{stockQuantity}</strong>{' '}
            <span className={low ? undefined : styles.medicineStockMin}>· tối thiểu {reorderLevel}</span>
            {low && <span className={styles.stockWarning}><AlertTriangle size={14}><title>Tồn kho dưới ngưỡng cảnh báo!</title></AlertTriangle></span>}
        </Typography.Text>
    );
};
