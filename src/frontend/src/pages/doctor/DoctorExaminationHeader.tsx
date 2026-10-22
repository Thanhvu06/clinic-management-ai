import { createContext, useContext, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { Button } from 'antd';
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

export function DoctorExaminationSyncAction({ onSync }: { onSync: () => void }) {
    const slot = useContext(SyncSlot);
    const button = <Button size="small" icon={<RefreshCw size={14} />} onClick={onSync}>Đồng bộ dữ liệu</Button>;
    return slot ? createPortal(button, slot) : button;
}
