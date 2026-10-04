import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { AuthProvider, useAuth } from '../auth/AuthContext';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
afterEach(() => { cleanup(); localStorage.clear(); vi.resetAllMocks(); });
const Probe = () => {
    const { loading, user, isAuthenticated } = useAuth();
    return <output data-testid="auth">{JSON.stringify({ loading, user, isAuthenticated })}</output>;
};

describe('R3-5 authentication initialization', () => {
    it.each([
        [{ status: 401 }, false],
        [{ message: 'Network Error' }, true],
        [{ status: 500 }, true],
        [{ status: 429 }, true],
    ])('keeps tokens only for transient errors %j', async (error, keep) => {
        localStorage.setItem('token', 'saved-token');
        vi.mocked(axiosClient.get).mockRejectedValue(error);
        render(<AuthProvider><Probe /></AuthProvider>);
        await waitFor(() => expect(screen.getByTestId('auth')).toHaveTextContent('"loading":false'));
        expect(localStorage.getItem('token')).toBe(keep ? 'saved-token' : null);
        expect(screen.getByTestId('auth')).toHaveTextContent('"user":null');
        expect(screen.getByTestId('auth')).toHaveTextContent('"isAuthenticated":false');
    });
    it('sets the user for a successful auth/me response', async () => {
        localStorage.setItem('token', 'saved-token');
        const user = { userId: 'patient', fullName: 'Bệnh nhân thử nghiệm', role: 'Patient' };
        vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: user });
        render(<AuthProvider><Probe /></AuthProvider>);
        await waitFor(() => expect(screen.getByTestId('auth')).toHaveTextContent('"isAuthenticated":true'));
        expect(screen.getByTestId('auth')).toHaveTextContent(user.fullName);
        expect(localStorage.getItem('token')).toBe('saved-token');
        expect(axiosClient.get).toHaveBeenCalledWith('/auth/me');
    });
    it('does not delete the token for a response without a user and without a 401', async () => {
        localStorage.setItem('token', 'saved-token');
        vi.mocked(axiosClient.get).mockResolvedValue({ success: false });
        render(<AuthProvider><Probe /></AuthProvider>);
        await waitFor(() => expect(screen.getByTestId('auth')).toHaveTextContent('"loading":false'));
        expect(localStorage.getItem('token')).toBe('saved-token');
        expect(screen.getByTestId('auth')).toHaveTextContent('"user":null');
    });
});
