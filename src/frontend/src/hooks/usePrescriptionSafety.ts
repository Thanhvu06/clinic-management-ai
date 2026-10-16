import { useEffect, useState } from 'react';
import { doctorApi } from '../api/doctorApi';
import type { PrescriptionSafetyResultDto } from '../types/prescriptionSafety';

export function usePrescriptionSafety(id: number, isVisit: boolean, medicineIds: number[], enabled = true) {
    const medicinesKey = JSON.stringify([...new Set(medicineIds)].sort((a, b) => a - b));
    const identity = `${isVisit ? 'visit' : 'appointment'}:${id}:${medicinesKey}`;
    const [attempt, setAttempt] = useState(0);
    const [state, setState] = useState<{ identity: string; result: PrescriptionSafetyResultDto | null; loading: boolean; error: string }>({ identity: '', result: null, loading: true, error: '' });
    useEffect(() => {
        let active = true;
        if (!enabled || !id) return;
        void Promise.resolve().then(() => {
            if (active) setState({ identity, result: null, loading: true, error: '' });
        });
        const timer = setTimeout(() => {
            void (async () => {
                try {
                    const response = await doctorApi.checkPrescriptionSafety(id, JSON.parse(medicinesKey) as number[], isVisit);
                    if (!active) return;
                    if (!response.success || !response.data) throw new Error(response.message || 'Không thể kiểm tra an toàn đơn thuốc.');
                    setState({ identity, result: response.data, loading: false, error: '' });
                } catch (failure: unknown) {
                    if (active) setState({ identity, result: null, loading: false, error: (failure as { message?: string })?.message || 'Không thể kiểm tra an toàn đơn thuốc.' });
                }
            })();
        }, 400);
        return () => { active = false; clearTimeout(timer); };
    }, [id, isVisit, medicinesKey, identity, attempt, enabled]);
    const current = state.identity === identity ? state : { result: null, loading: true, error: '' };
    return { ...current, retry: () => setAttempt(value => value + 1) };
}
