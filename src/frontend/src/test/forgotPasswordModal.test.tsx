import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { ForgotPasswordModal } from '../components/ForgotPasswordModal';
import axiosClient from '../api/axiosClient';

vi.mock('../api/axiosClient', () => ({
    default: {
        post: vi.fn(),
    },
}));

describe('ForgotPasswordModal Component', () => {
    const mockOnClose = vi.fn();

    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('requests token successfully, receives resetToken, and transitions to reset step', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: 'Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được xử lý.',
            data: { resetToken: 'demo-reset-token-12345' }
        });

        render(<ForgotPasswordModal isOpen={true} onClose={mockOnClose} />);

        expect(screen.getByRole('heading', { name: 'Quên mật khẩu' })).toBeInTheDocument();
        const emailInput = screen.getByLabelText(/Địa chỉ Email tài khoản/i);
        fireEvent.change(emailInput, { target: { value: 'patient@cliniccare.vn' } });

        const submitBtn = screen.getByRole('button', { name: /Gửi mã đặt lại mật khẩu/i });
        fireEvent.click(submitBtn);

        await waitFor(() => {
            expect(axiosClient.post).toHaveBeenCalledWith('/auth/forgot-password', {
                email: 'patient@cliniccare.vn'
            });
        });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Đặt lại mật khẩu' })).toBeInTheDocument();
            const tokenInput = screen.getByLabelText(/Mã xác thực \(Token\)/i) as HTMLInputElement;
            expect(tokenInput.value).toBe('demo-reset-token-12345');
        });
    });

    it('submits reset password payload with trimmed email, token, and new password', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: 'Mã xác thực đã được tạo thành công.',
            data: { resetToken: '  test-token-abc  ' }
        });

        render(<ForgotPasswordModal isOpen={true} onClose={mockOnClose} />);

        const emailInput = screen.getByLabelText(/Địa chỉ Email tài khoản/i);
        fireEvent.change(emailInput, { target: { value: '  patient@cliniccare.vn  ' } });
        fireEvent.click(screen.getByRole('button', { name: /Gửi mã đặt lại mật khẩu/i }));

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Đặt lại mật khẩu' })).toBeInTheDocument();
        });

        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: 'Đặt lại mật khẩu thành công! Bạn có thể đóng cửa sổ và đăng nhập ngay.',
            data: null
        });

        const newPassInput = screen.getByLabelText(/Mật khẩu mới \(tối thiểu 8 ký tự\)/i);
        const confirmPassInput = screen.getByLabelText(/Xác nhận mật khẩu mới/i);

        fireEvent.change(newPassInput, { target: { value: 'StrongPass@123' } });
        fireEvent.change(confirmPassInput, { target: { value: 'StrongPass@123' } });

        const resetBtn = screen.getByRole('button', { name: /Đặt lại mật khẩu/i });
        fireEvent.click(resetBtn);

        await waitFor(() => {
            expect(axiosClient.post).toHaveBeenLastCalledWith('/auth/reset-password', {
                email: 'patient@cliniccare.vn',
                token: 'test-token-abc',
                newPassword: 'StrongPass@123'
            });
        });

        await waitFor(() => {
            expect(screen.getByText(/Đặt lại mật khẩu thành công!/i)).toBeInTheDocument();
        });
    });

    it('blocks new password with less than 8 characters', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: 'Token created',
            data: { resetToken: 'token123' }
        });

        render(<ForgotPasswordModal isOpen={true} onClose={mockOnClose} />);

        fireEvent.change(screen.getByLabelText(/Địa chỉ Email tài khoản/i), { target: { value: 'patient@cliniccare.vn' } });
        fireEvent.click(screen.getByRole('button', { name: /Gửi mã đặt lại mật khẩu/i }));

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Đặt lại mật khẩu' })).toBeInTheDocument();
        });

        fireEvent.change(screen.getByLabelText(/Mật khẩu mới \(tối thiểu 8 ký tự\)/i), { target: { value: 'Pass1!' } });
        fireEvent.change(screen.getByLabelText(/Xác nhận mật khẩu mới/i), { target: { value: 'Pass1!' } });

        const resetBtn = screen.getByRole('button', { name: /Đặt lại mật khẩu/i });
        fireEvent.click(resetBtn);

        expect(axiosClient.post).toHaveBeenCalledTimes(1);
        expect(screen.getByText('Mật khẩu mới phải có ít nhất 8 ký tự.')).toBeInTheDocument();
    });

    it('blocks reset submission when password confirmation does not match', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: 'Token created',
            data: { resetToken: 'token123' }
        });

        render(<ForgotPasswordModal isOpen={true} onClose={mockOnClose} />);

        fireEvent.change(screen.getByLabelText(/Địa chỉ Email tài khoản/i), { target: { value: 'patient@cliniccare.vn' } });
        fireEvent.click(screen.getByRole('button', { name: /Gửi mã đặt lại mật khẩu/i }));

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Đặt lại mật khẩu' })).toBeInTheDocument();
        });

        fireEvent.change(screen.getByLabelText(/Mật khẩu mới \(tối thiểu 8 ký tự\)/i), { target: { value: 'ValidPass@123' } });
        fireEvent.change(screen.getByLabelText(/Xác nhận mật khẩu mới/i), { target: { value: 'DifferentPass@456' } });

        const resetBtn = screen.getByRole('button', { name: /Đặt lại mật khẩu/i });
        fireEvent.click(resetBtn);

        expect(axiosClient.post).toHaveBeenCalledTimes(1);
        expect(screen.getByText('Mật khẩu xác nhận không khớp.')).toBeInTheDocument();
    });

    it('handles response without resetToken safely without crashing and stays on request step', async () => {
        vi.mocked(axiosClient.post).mockResolvedValueOnce({
            success: true,
            message: 'Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được xử lý.',
            data: { resetToken: null }
        });

        render(<ForgotPasswordModal isOpen={true} onClose={mockOnClose} />);

        fireEvent.change(screen.getByLabelText(/Địa chỉ Email tài khoản/i), { target: { value: 'patient@cliniccare.vn' } });
        fireEvent.click(screen.getByRole('button', { name: /Gửi mã đặt lại mật khẩu/i }));

        await waitFor(() => {
            expect(axiosClient.post).toHaveBeenCalledTimes(1);
        });

        expect(screen.getByRole('heading', { name: 'Quên mật khẩu' })).toBeInTheDocument();
        expect(screen.getByLabelText(/Địa chỉ Email tài khoản/i)).toBeInTheDocument();
        expect(screen.getByText('Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được xử lý.')).toBeInTheDocument();
    });

    it('handles API error and displays user-friendly error message', async () => {
        vi.mocked(axiosClient.post).mockRejectedValueOnce({
            message: 'Không thể kết nối đến máy chủ. Vui lòng thử lại sau.'
        });

        render(<ForgotPasswordModal isOpen={true} onClose={mockOnClose} />);

        fireEvent.change(screen.getByLabelText(/Địa chỉ Email tài khoản/i), { target: { value: 'patient@cliniccare.vn' } });
        fireEvent.click(screen.getByRole('button', { name: /Gửi mã đặt lại mật khẩu/i }));

        await waitFor(() => {
            expect(screen.getByText('Không thể kết nối đến máy chủ. Vui lòng thử lại sau.')).toBeInTheDocument();
        });

        expect(screen.getByRole('heading', { name: 'Quên mật khẩu' })).toBeInTheDocument();
    });
});
