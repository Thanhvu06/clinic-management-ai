import { useEffect, useState } from 'react';
import { Alert } from 'antd';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { InlineError } from '../../components/common';
import { getApiErrorMessage } from '../../utils/apiError';

interface Eligibility { isBlocked: boolean; message?: string }

export function OnlineBookingNotice() {
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
    if (busy) return <span role="status">Đang kiểm tra điều kiện đặt lịch...</span>;
    if (error) return <InlineError message={error} onRetry={() => { setBusy(true); setError(''); setAttempt(value => value + 1); }} />;
    return eligibility?.isBlocked ? <Alert type="warning" title={eligibility.message} /> : null;
}
