import React, { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import axiosClient from '../../api/axiosClient';
import { InlineError } from '../../components/common';
import type { ApiResponse } from '../../types';
import { getApiErrorMessage } from '../../utils/apiError';

export const APPOINTMENT_DEEP_LINK_PARAM = 'appointmentId';

interface AppointmentDeepLinkProps<T> {
    openedAppointmentId: number | null;
    onOpen: (appointment: T) => void;
}

export function AppointmentDeepLink<T>({ openedAppointmentId, onOpen }: AppointmentDeepLinkProps<T>): React.ReactElement | null {
    const [searchParams, setSearchParams] = useSearchParams();
    const requestedId = Number(searchParams.get(APPOINTMENT_DEEP_LINK_PARAM)) || 0;
    const [error, setError] = useState('');
    const [attempt, setAttempt] = useState(0);
    const handledKeyRef = useRef<string | null>(null);
    const openedFromLinkRef = useRef(false);
    const onOpenRef = useRef(onOpen);

    useEffect(() => {
        onOpenRef.current = onOpen;
    }, [onOpen]);

    useEffect(() => {
        if (requestedId <= 0) return;
        const key = `${requestedId}:${attempt}`;
        if (handledKeyRef.current === key) return;
        handledKeyRef.current = key;
        setError('');
        axiosClient.get<unknown, ApiResponse<T>>(`/reception/appointments/${requestedId}`)
            .then(res => {
                if (res.success && res.data) onOpenRef.current(res.data);
                else setError(getApiErrorMessage(res, 'Không mở được lịch hẹn được chọn.'));
            })
            .catch(err => setError(getApiErrorMessage(err, 'Không mở được lịch hẹn được chọn.')));
    }, [requestedId, attempt]);

    useEffect(() => {
        if (requestedId > 0 && openedAppointmentId === requestedId) {
            openedFromLinkRef.current = true;
            return;
        }
        // Closing the dialog drops the parameter so a refresh or back navigation does not reopen it.
        if (openedFromLinkRef.current && openedAppointmentId === null) {
            openedFromLinkRef.current = false;
            const next = new URLSearchParams(searchParams);
            next.delete(APPOINTMENT_DEEP_LINK_PARAM);
            setSearchParams(next, { replace: true });
        }
    }, [openedAppointmentId, requestedId, searchParams, setSearchParams]);

    if (!error || requestedId <= 0) return null;
    return <InlineError title="Không mở được lịch hẹn" message={error} onRetry={() => setAttempt(value => value + 1)} />;
}
