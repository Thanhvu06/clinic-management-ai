import { useEffect, useMemo, useState } from 'react';
import axiosClient from '../../api/axiosClient';
import type { ApiResponse } from '../../types';
import { getApiErrorMessage } from '../../utils/apiError';

interface PaymentStatus {
    prescriptionId: number;
    isFullyPaid: boolean;
}

export const usePrescriptionPayment = (prescriptionId: number | null) => {
    const [revision, setRevision] = useState(0);
    const request = useMemo(() => ({ prescriptionId, revision }), [prescriptionId, revision]);
    const [result, setResult] = useState<{
        request: typeof request;
        data: PaymentStatus | null;
        error: string | null;
    } | null>(null);

    useEffect(() => {
        let active = true;
        if (request.prescriptionId === null) return;
        const load = async () => {
            try {
                const response = await axiosClient.get<unknown, ApiResponse<PaymentStatus>>(
                    `/pharmacy/prescriptions/${request.prescriptionId}/payment-status`
                );
                if (!response.success || response.data?.prescriptionId !== request.prescriptionId
                    || typeof response.data.isFullyPaid !== 'boolean') {
                    throw new Error('Không thể xác minh thanh toán đơn thuốc.');
                }
                if (active) setResult({ request, data: response.data, error: null });
            } catch (cause) {
                if (active) setResult({ request, data: null, error: getApiErrorMessage(cause, 'Không thể tải trạng thái thanh toán.') });
            }
        };
        void load();
        return () => { active = false; };
    }, [request]);

    const current = result?.request === request ? result : null;
    return {
        loading: prescriptionId !== null && current === null,
        error: current?.error ?? null,
        isFullyPaid: current?.data?.isFullyPaid === true,
        status: current?.data ?? null,
        retry: () => setRevision(value => value + 1)
    };
};
