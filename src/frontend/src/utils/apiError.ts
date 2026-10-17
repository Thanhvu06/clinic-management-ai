/** Reads server messages while leaving client/network errors to the caller's fallback. */
export const getApiErrorMessage = (err: unknown, fallback: string): string => {
    if (typeof err !== 'object' || err === null) return fallback;

    const error = err as {
        response?: { data?: { message?: unknown } };
        message?: unknown;
        status?: unknown;
    };
    const legacyMessage = error.response?.data?.message;
    if (typeof legacyMessage === 'string' && legacyMessage.trim().length > 0) return legacyMessage;

    if (!(err instanceof Error) && typeof error.status === 'number'
        && typeof error.message === 'string' && error.message.trim().length > 0) return error.message;

    return fallback;
};
