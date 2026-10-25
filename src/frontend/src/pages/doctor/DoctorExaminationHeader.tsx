import { createContext, useContext, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { Alert, Button } from 'antd';
import { RefreshCw } from 'lucide-react';
import { PageHeader } from '../../components/common';

const SyncSlot = createContext<HTMLDivElement | null>(null);

export function DoctorExaminationFrame({ children }: { children: ReactNode }) {
    const [slot, setSlot] = useState<HTMLDivElement | null>(null);
    return (
        <SyncSlot.Provider value={slot}>
            <PageHeader title="Bàn khám lâm sàng" actions={<div ref={setSlot} />} />
            {children}
        </SyncSlot.Provider>
    );
}

export function DoctorExaminationSyncAction({ onSync, isCompletionOverdue }: { onSync: () => void; isCompletionOverdue?: boolean }) {
    const slot = useContext(SyncSlot);
    const button = <Button size="small" icon={<RefreshCw size={14} />} onClick={onSync}>Đồng bộ dữ liệu</Button>;
    return <>{slot ? createPortal(button, slot) : button}{isCompletionOverdue && <Alert type="warning" title="Quá hạn hoàn tất" description="Lượt khám đã qua ngày. Bác sĩ cần kiểm tra và tự hoàn tất hồ sơ." />}</>;
}
