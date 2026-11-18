import { useEffect, useState, type ReactNode } from 'react';
import { Alert, Button, Card } from 'antd';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { InlineError } from '../../components/common';
import { getApiErrorMessage } from '../../utils/apiError';
import styles from './BookAppointment.module.css';

interface Eligibility { isBlocked: boolean; message?: string }

export function OnlineBookingNotice({ children }: { children?: ReactNode }) {
    const [eligibility, setEligibility] = useState<Eligibility>();
    const [error, setError] = useState('');
    const [busy, setBusy] = useState(true);
    const [attempt, setAttempt] = useState(0);
    useEffect(() => {
        let active = true;
        void Promise.resolve().then(() => axiosClient.get<unknown, ApiResponse<Eligibility>>('/appointments/online-eligibility'))
            .then(response => { if (active) setEligibility(response.data); })
            .catch(err => { if (active) setError(getApiErrorMessage(err, 'Không tải được điều kiện đặt lịch online.')); })
            .finally(() => { if (active) setBusy(false); });
        return () => { active = false; };
    }, [attempt]);
    if (busy) return <span aria-live="polite" aria-busy="true">Đang kiểm tra điều kiện đặt lịch...</span>;
    if (error) return <><InlineError message={error} onRetry={() => { setBusy(true); setError(''); setAttempt(value => value + 1); }} />{children}</>;
    if (eligibility?.isBlocked) return <div className={styles.pageWrapper}><Card>
        <h1 className={styles.pageTitle}>Đặt lịch khám</h1>
        <Alert type="warning" showIcon title={eligibility.message || 'Tài khoản đang tạm khóa đặt lịch online. Vui lòng liên hệ lễ tân.'}
            action={<Button href="/locations">Liên hệ lễ tân</Button>} />
    </Card></div>;
    return children ?? null;
}
