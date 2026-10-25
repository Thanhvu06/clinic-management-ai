export const formatCopilotDateTime = (value: string): string => {
    const date = new Date(/(?:z|[+-]\d{2}:?\d{2})$/i.test(value) ? value : `${value}Z`);
    return Number.isNaN(date.valueOf()) ? value : date.toLocaleString('vi-VN', {
        timeZone: 'Asia/Ho_Chi_Minh', day: '2-digit', month: '2-digit', year: 'numeric',
        hour: '2-digit', minute: '2-digit', hour12: false
    });
};

export const formatCopilotClock = (value: unknown): string => {
    const match = typeof value === 'string' ? /^(\d{1,2}):(\d{2})/.exec(value) : null;
    return match ? `${match[1].padStart(2, '0')}:${match[2]}` : '';
};
