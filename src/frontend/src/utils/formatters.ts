/**
 * Parses included services safely from either a JSON string, an existing string array, or undefined.
 */
export function parseIncludedServices(input: unknown): string[] {
    if (!input) return [];
    if (Array.isArray(input)) {
        return input.filter((item): item is string => typeof item === 'string' && item.trim().length > 0);
    }
    if (typeof input === 'string') {
        const trimmed = input.trim();
        if (!trimmed) return [];
        try {
            const parsed = JSON.parse(trimmed);
            if (Array.isArray(parsed)) {
                return parsed.filter((item): item is string => typeof item === 'string' && item.trim().length > 0);
            }
        } catch {
            // Fallback: if comma or semicolon separated
            if (trimmed.includes(';') || trimmed.includes(',')) {
                const separator = trimmed.includes(';') ? ';' : ',';
                return trimmed.split(separator).map(s => s.trim()).filter(Boolean);
            }
            return [trimmed];
        }
    }
    return [];
}

/**
 * Formats a number to Vietnamese Dong currency display (e.g., 1.500.000 ₫).
 */
export function formatVndCurrency(amount: number | null | undefined): string {
    if (amount === null || amount === undefined || isNaN(amount)) {
        return '0 ₫';
    }
    return new Intl.NumberFormat('vi-VN', {
        style: 'currency',
        currency: 'VND'
    }).format(amount);
}

/**
 * Formats a Date or ISO date string to DD/MM/YYYY.
 */
export function formatDisplayDate(dateInput: string | Date | null | undefined): string {
    if (!dateInput) return '';
    try {
        const d = typeof dateInput === 'string' ? new Date(dateInput) : dateInput;
        if (isNaN(d.getTime())) return String(dateInput);
        const day = String(d.getDate()).padStart(2, '0');
        const month = String(d.getMonth() + 1).padStart(2, '0');
        const year = d.getFullYear();
        return `${day}/${month}/${year}`;
    } catch {
        return String(dateInput);
    }
}
