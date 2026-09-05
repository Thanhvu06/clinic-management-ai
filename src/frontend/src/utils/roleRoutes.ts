/**
 * Unified role to dashboard path mapping.
 * Ensures consistent routing across Auth guards, Public layout, and Navbars.
 */
export const getRoleDashboardPath = (role?: string): string => {
    switch (role) {
        case 'Admin':
            return '/admin';
        case 'Doctor':
            return '/doctor';
        case 'Receptionist':
            return '/reception';
        case 'Pharmacist':
            return '/pharmacy';
        case 'Patient':
            return '/patient';
        default:
            return '/login';
    }
};

/**
 * Sanitizes returnUrl query parameter to prevent open redirect vulnerabilities.
 * Only allows relative paths starting with '/' and not '//'.
 */
export const sanitizeReturnUrl = (url?: string | null, fallback: string = '/'): string => {
    if (!url || typeof url !== 'string') return fallback;
    const trimmed = url.trim();
    if (trimmed.startsWith('/') && !trimmed.startsWith('//') && !trimmed.includes('\\')) {
        return trimmed;
    }
    return fallback;
};
