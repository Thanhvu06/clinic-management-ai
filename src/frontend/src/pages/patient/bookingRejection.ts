export function isBookingRejected(error: unknown): boolean {
    const failure = error as { status?: number; errorCode?: string; response?: { status?: number; data?: { errorCode?: string } } };
    const status = failure?.response?.status ?? failure?.status;
    const code = failure?.response?.data?.errorCode ?? failure?.errorCode;
    return (status !== undefined && status >= 400 && status < 500)
        || ['SLOT_ALREADY_BOOKED', 'PATIENT_TIME_CONFLICT', 'DOCTOR_NOT_AVAILABLE'].includes(code ?? '');
}
