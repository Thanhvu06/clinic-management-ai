import { describe, it, expect } from 'vitest';
import { getRoleDashboardPath, sanitizeReturnUrl, isPathAllowedForRole, getRedirectAfterLogin } from '../utils/roleRoutes';

describe('Role Routes and Security Sanitization', () => {
    describe('getRoleDashboardPath', () => {
        it('should map Doctor to /doctor', () => {
            expect(getRoleDashboardPath('Doctor')).toBe('/doctor');
        });

        it('should map Receptionist to /reception', () => {
            expect(getRoleDashboardPath('Receptionist')).toBe('/reception');
        });

        it('should map Pharmacist to /pharmacy', () => {
            expect(getRoleDashboardPath('Pharmacist')).toBe('/pharmacy');
        });

        it('should map DiagnosticTechnician to /diagnostics', () => {
            expect(getRoleDashboardPath('DiagnosticTechnician')).toBe('/diagnostics');
        });

        it('should map Patient to /patient', () => {
            expect(getRoleDashboardPath('Patient')).toBe('/patient');
        });

        it('should map Admin to /admin', () => {
            expect(getRoleDashboardPath('Admin')).toBe('/admin');
        });

        it('should default unknown role to /login', () => {
            expect(getRoleDashboardPath('Unknown')).toBe('/login');
            expect(getRoleDashboardPath(undefined)).toBe('/login');
        });
    });

    describe('sanitizeReturnUrl', () => {
        it('should allow valid relative paths', () => {
            expect(sanitizeReturnUrl('/doctor/queue')).toBe('/doctor/queue');
            expect(sanitizeReturnUrl('/doctor/schedule?week=2026-W36')).toBe('/doctor/schedule?week=2026-W36');
            expect(sanitizeReturnUrl('/doctor/examination/42')).toBe('/doctor/examination/42');
        });

        it('should block open redirect attempts to external protocols or domains', () => {
            expect(sanitizeReturnUrl('https://evil.com')).toBeNull();
            expect(sanitizeReturnUrl('http://evil.com/phishing')).toBeNull();
            expect(sanitizeReturnUrl('//evil.com')).toBeNull();
            expect(sanitizeReturnUrl('javascript:alert(1)')).toBeNull();
            expect(sanitizeReturnUrl('data:text/html,<script>alert(1)</script>')).toBeNull();
        });

        it('should block paths with backslashes', () => {
            expect(sanitizeReturnUrl('/\\evil.com')).toBeNull();
            expect(sanitizeReturnUrl('\\doctor')).toBeNull();
        });

        it('should return null for empty or null inputs', () => {
            expect(sanitizeReturnUrl(null)).toBeNull();
            expect(sanitizeReturnUrl(undefined)).toBeNull();
            expect(sanitizeReturnUrl('')).toBeNull();
            expect(sanitizeReturnUrl('   ')).toBeNull();
        });
    });

    describe('isPathAllowedForRole', () => {
        it('should allow Doctor to access doctor paths', () => {
            expect(isPathAllowedForRole('Doctor', '/doctor')).toBe(true);
            expect(isPathAllowedForRole('Doctor', '/doctor/appointments/5')).toBe(true);
        });

        it('should allow DiagnosticTechnician to access diagnostics paths', () => {
            expect(isPathAllowedForRole('DiagnosticTechnician', '/diagnostics')).toBe(true);
            expect(isPathAllowedForRole('DiagnosticTechnician', '/diagnostics/orders/12')).toBe(true);
            expect(isPathAllowedForRole('DiagnosticTechnician', '/doctor/appointments/5')).toBe(false);
        });

        it('should prevent Doctor from accessing patient or admin paths', () => {
            expect(isPathAllowedForRole('Doctor', '/admin/users')).toBe(false);
            expect(isPathAllowedForRole('Doctor', '/patient/portal')).toBe(false);
        });

        it('should prevent Patient from accessing doctor paths', () => {
            expect(isPathAllowedForRole('Patient', '/doctor/examination/10')).toBe(false);
        });

        it('should disallow auth/error loop pages', () => {
            expect(isPathAllowedForRole('Doctor', '/login')).toBe(false);
            expect(isPathAllowedForRole('Doctor', '/register')).toBe(false);
            expect(isPathAllowedForRole('Doctor', '/forbidden')).toBe(false);
        });
    });

    describe('getRedirectAfterLogin', () => {
        it('should redirect Doctor to /doctor when no returnUrl is provided', () => {
            expect(getRedirectAfterLogin('Doctor', null)).toBe('/doctor');
            expect(getRedirectAfterLogin('Doctor', '')).toBe('/doctor');
        });

        it('should redirect Doctor to valid doctor subroute when specified', () => {
            expect(getRedirectAfterLogin('Doctor', '/doctor/queue')).toBe('/doctor/queue');
            expect(getRedirectAfterLogin('Doctor', '/doctor/examination/12')).toBe('/doctor/examination/12');
        });

        it('should fallback to /doctor when returnUrl is unauthorized for Doctor', () => {
            expect(getRedirectAfterLogin('Doctor', '/admin/dashboard')).toBe('/doctor');
            expect(getRedirectAfterLogin('Doctor', 'https://malicious.com')).toBe('/doctor');
        });
    });
});
