import { useEffect, useRef, useState } from 'react';
import { Button, Input, Select, Space } from 'antd';
import axiosClient from '../../api/axiosClient';
import { prepareRoleAction } from '../../api/aiCopilotApi';
import type { ApiResponse } from '../../types';
import type { AiToolExecutionResult } from '../../types/ai';
import { formatDateOnly } from '../../utils/formatters';
import { getApiErrorMessage } from '../../utils/apiError';
import { InlineError } from '../common/InlineError';

interface Slot {
    slotId: number;
    startTime: string;
    endTime: string;
}

export interface PatientAppointmentChangeData {
    operation: 'cancel' | 'reschedule';
    appointmentId: number;
    appointmentCode: string;
    doctorId?: number;
    specialtyId?: number;
    currentSlotId?: number;
    day?: string;
    sessionId: string;
    onPreview: (result: AiToolExecutionResult, message: string) => void;
}

export function PatientAppointmentChangeForm({ data }: { data: PatientAppointmentChangeData }) {
    const reschedule = data.operation === 'reschedule';
    const [day, setDay] = useState(data.day ?? '');
    const [slots, setSlots] = useState<Slot[]>([]);
    const [slotId, setSlotId] = useState<number>();
    const [reason, setReason] = useState('');
    const [loadingSlots, setLoadingSlots] = useState(reschedule && Boolean(data.day));
    const [slotError, setSlotError] = useState('');
    const [retryCount, setRetryCount] = useState(0);
    const [preparing, setPreparing] = useState(false);
    const [prepareError, setPrepareError] = useState('');
    const [submitted, setSubmitted] = useState(false);
    const preparation = useRef<AbortController | null>(null);
    const attempt = useRef<{ args: string; key: string } | null>(null);

    useEffect(() => () => preparation.current?.abort(), []);
    useEffect(() => {
        if (!reschedule || !data.doctorId || !day) return;
        const controller = new AbortController();
        const query = new URLSearchParams({ fromDate: day, toDate: day });
        if (data.specialtyId) query.set('specialtyId', String(data.specialtyId));
        void axiosClient.get<never, ApiResponse<Slot[]>>(
            '/doctors/' + data.doctorId + '/available-slots?' + query.toString(),
            { signal: controller.signal }
        ).then(response => {
            if (!controller.signal.aborted) setSlots((response.data ?? []).filter(slot => slot.slotId !== data.currentSlotId));
        }).catch(error => {
            if (!controller.signal.aborted) setSlotError(getApiErrorMessage(error, 'Không tải được khung giờ.'));
        }).finally(() => {
            if (!controller.signal.aborted) setLoadingSlots(false);
        });
        return () => controller.abort();
    }, [data.currentSlotId, data.doctorId, data.specialtyId, day, reschedule, retryCount]);

    const slotLabel = (slot: Slot) => slot.startTime.slice(0, 5) + ' – ' + slot.endTime.slice(0, 5);
    const prepare = async () => {
        if (preparation.current || reason.trim().length < 5 || (reschedule && !slotId)) return;
        const controller = new AbortController();
        preparation.current = controller;
        setPreparing(true);
        setPrepareError('');
        const args = JSON.stringify({ appointmentId: data.appointmentId, requestedSlotId: slotId, reason: reason.trim() });
        if (attempt.current?.args !== args) attempt.current = { args, key: crypto.randomUUID() };
        try {
            const result = await prepareRoleAction({
                toolName: 'patient.prepare_' + data.operation + '_appointment',
                toolVersion: '1.0',
                sessionId: data.sessionId,
                argumentsJson: args,
                idempotencyKey: attempt.current.key
            }, controller.signal);
            if (controller.signal.aborted) return;
            const preview = result.data as Record<string, unknown> | undefined;
            if (result.status !== 'pending_confirmation' || !result.actionId || typeof preview?.concurrencyToken !== 'string') {
                setPrepareError(result.error?.message ?? 'Không tạo được bản xem trước.');
                return;
            }
            const selected = slots.find(slot => slot.slotId === slotId);
            const change = reschedule && selected
                ? 'đổi lịch hẹn ' + data.appointmentCode + ' sang ngày ' + formatDateOnly(day) + ', ' + slotLabel(selected)
                : 'hủy lịch hẹn ' + data.appointmentCode;
            data.onPreview({ status: result.status, actionId: result.actionId, data: preview },
                'Xem trước yêu cầu ' + change + '. Lý do: ' + reason.trim());
            setSubmitted(true);
        } catch (error) {
            if (!controller.signal.aborted) setPrepareError(getApiErrorMessage(error, 'Không tạo được bản xem trước.'));
        } finally {
            preparation.current = null;
            if (!controller.signal.aborted) setPreparing(false);
        }
    };

    if (submitted) return <p>Đã tạo bản xem trước.</p>;
    return <form onSubmit={event => { event.preventDefault(); void prepare(); }}>
        <Space orientation="vertical" size="small">
            {reschedule && <>
                <Input type="date" aria-label="Ngày mới" value={day} disabled={preparing}
                    onChange={event => {
                        setDay(event.target.value);
                        setSlotId(undefined);
                        setSlots([]);
                        setSlotError('');
                        setLoadingSlots(Boolean(event.target.value));
                    }} />
                {day && (loadingSlots ? <p role="status">Đang tải khung giờ…</p>
                    : slotError ? <InlineError message={slotError} onRetry={() => { setSlotError(''); setLoadingSlots(true); setRetryCount(count => count + 1); }} />
                    : slots.length === 0 ? <p>Không có khung giờ trống vào ngày này.</p>
                    : <Select aria-label="Khung giờ mới" placeholder="Chọn khung giờ" disabled={preparing}
                        value={slotId} onChange={setSlotId} options={slots.map(slot => ({ value: slot.slotId, label: slotLabel(slot) }))} />)}
            </>}
            <Input.TextArea aria-label="Lý do yêu cầu" placeholder="Nhập lý do (ít nhất 5 ký tự)" value={reason}
                disabled={preparing} maxLength={500} onChange={event => setReason(event.target.value)} />
            {prepareError && <InlineError message={prepareError} onRetry={() => void prepare()} />}
            <Button htmlType="submit" type="primary" disabled={preparing || reason.trim().length < 5 || (reschedule && !slotId)}>
                {preparing ? 'Đang chuẩn bị…' : 'Xem trước yêu cầu'}
            </Button>
        </Space>
    </form>;
}
