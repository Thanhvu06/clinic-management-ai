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
 * Only allows relative paths starting with '/' and not '//' or containing backslashes.
 * Returns null if the URL is invalid or empty.
 */
export const sanitizeReturnUrl = (url?: string | null): string | null => {
    if (!url || typeof url !== 'string') return null;
    const trimmed = url.trim();
    if (
        trimmed.startsWith('/') &&
        !trimmed.startsWith('//') &&
        !trimmed.includes('\\') &&
        !trimmed.toLowerCase().includes('javascript:') &&
        !trimmed.toLowerCase().includes('data:')
    ) {
        return trimmed;
    }
    return null;
};

/**
 * Checks whether a given role is authorized to access a relative pathname.
 * Public routes are allowed for all authenticated users.
 */
export const isPathAllowedForRole = (role?: string, path?: string | null): boolean => {
    if (!path || !role) return false;
    const cleanPath = path.split('?')[0].split('#')[0];

    // Prevent redirecting to login, register, or error pages
    if (
        cleanPath === '/login' ||
        cleanPath === '/register' ||
        cleanPath === '/forbidden' ||
        cleanPath === '/403' ||
        cleanPath === '/404'
    ) {
        return false;
    }

    // Role-specific sections
    if (cleanPath.startsWith('/doctor')) return role === 'Doctor';
    if (cleanPath.startsWith('/patient')) return role === 'Patient';
    if (cleanPath.startsWith('/admin')) return role === 'Admin';
    if (cleanPath.startsWith('/reception')) return role === 'Receptionist';
    if (cleanPath.startsWith('/pharmacy')) return role === 'Pharmacist';

    // Public sections are accessible by any authenticated user
    return true;
};

/**
 * Resolves safe redirect URL after successful login.
 * If returnUrl is valid and permitted for the role, returns it; otherwise returns role dashboard path.
 */
export const getRedirectAfterLogin = (role?: string, returnUrl?: string | null): string => {
    const defaultDashboard = getRoleDashboardPath(role);
    const safeUrl = sanitizeReturnUrl(returnUrl);

    if (safeUrl && isPathAllowedForRole(role, safeUrl)) {
        return safeUrl;
    }

    return defaultDashboard;
};
