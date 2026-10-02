import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi, afterEach } from 'vitest';
import { BookingActionChoices } from '../components/BookingActionChoices';
import { BookingWizard } from '../components/BookingWizard';
import { ProviderStatus } from '../components/copilot/ProviderStatus';
import { patientSuggestions, isLocalHelpPhrase, isPatientReadAlias } from '../components/copilot/patientPresentation';
import { renderCopilotCardData } from '../components/copilot/copilotDataRenderers';
import type { AiAction, AiBookingWizardResponse } from '../types/ai';

afterEach(() => vi.unstubAllEnvs());
describe('chat presentation contracts', () => {
    it.each(['NotCalled', 'Disabled', 'NotConfigured'])('%s is neutral in normal UI', state => {
        vi.stubEnv('DEV', false);
        render(<ProviderStatus state={state} detail="Ready · Deterministic" />);
        const status = screen.getByText('Chế độ nội bộ').closest('[data-provider-status]');
        expect(status).toHaveAttribute('data-tone', 'local');
        expect(status).toHaveAttribute('title', 'Ready · Deterministic');
        expect(screen.queryByText(/Ready|Deterministic/)).not.toBeInTheDocument();
    });
    it.each(['Degraded', 'Unavailable', 'Timeout', '429'])('%s uses the amber local fallback', state => {
        render(<ProviderStatus state={state} />);
        expect(screen.getByText('Đang dùng chế độ nội bộ').closest('[data-provider-status]')).toHaveAttribute('data-tone', 'amber');
    });
    it('uses green for online, amber for current 429, red for an unrecovered connection failure', () => {
        const { rerender } = render(<ProviderStatus state="Online" />);
        expect(screen.getByText('Trực tuyến').closest('[data-provider-status]')).toHaveAttribute('data-tone', 'online');
        rerender(<ProviderStatus error="Bạn đã gửi quá nhiều yêu cầu." />);
        expect(screen.getByText('Đang dùng chế độ nội bộ').closest('[data-provider-status]')).toHaveAttribute('data-tone', 'amber');
        rerender(<ProviderStatus error="Không thể kết nối máy chủ lúc này." />);
        expect(screen.getByText(/Không thể kết nối/).closest('[data-provider-status]')).toHaveAttribute('data-tone', 'error');
    });
    it('limits legacy slots to six and expanding preserves the original callback', () => {
        const callback = vi.fn();
        const actions = Array.from({ length: 8 }, (_, index) => ({ id: `synthetic-choice-${index}`, type: 'SelectSlot', label: `Giờ ${index}` } as AiAction));
        render(<BookingActionChoices actions={actions} renderAction={action => <button key={action.id} onClick={() => callback(action)}>{action.label}</button>} />);
        expect(within(screen.getByLabelText('Chọn giờ khám')).getAllByRole('button')).toHaveLength(6);
        fireEvent.click(screen.getByRole('button', { name: 'Xem thêm' }));
        expect(within(screen.getByLabelText('Chọn giờ khám')).getAllByRole('button')).toHaveLength(8);
        fireEvent.click(screen.getByRole('button', { name: 'Giờ 7' }));
        expect(callback).toHaveBeenCalledWith(actions[7]);
    });
    it('collapses old legacy choices rather than leaving selectable history', () => {
        render(<BookingActionChoices latest={false} actions={[{ id: 'synthetic', type: 'SelectDoctor', label: 'Bác sĩ kiểm thử' } as AiAction]} renderAction={action => <button key={action.id}>{action.label}</button>} />);
        expect(screen.getByText('Lựa chọn ở bước trước ✓')).toBeInTheDocument();
        expect(screen.queryByRole('button')).not.toBeInTheDocument();
    });
    it('prioritizes the existing booking suggestion when a legacy reply is incomplete', () => {
        const start = { code: 'patient.start_booking', label: 'Đặt lịch khám' };
        const read = { code: 'patient.my_bills', label: 'Hóa đơn của tôi' };
        expect(patientSuggestions({ role: 'model', content: 'Chọn giờ khám', missingFields: ['slotId'], suggestionChips: [read] }, [read, start], false)).toEqual([start, read]);
    });
    it('recognizes only explicit help and retains clinic knowledge questions', () => {
        expect(isLocalHelpPhrase('TÔI CÓ QUYỀN HẠN GÌ?')).toBe(true);
        expect(isLocalHelpPhrase('toi co quyen han gi')).toBe(true);
        expect(isLocalHelpPhrase('hướng dẫn trước xét nghiệm')).toBe(false);
        expect(isPatientReadAlias('LỊCH HẸN CỦA MÌNH?')).toBe(true);
        expect(isPatientReadAlias('đặt lịch khám')).toBe(false);
    });
    it('localizes card statuses while preserving the wire value in the tooltip', () => {
        render(<>{renderCopilotCardData({ type: 'appointments', title: 'Lịch hẹn', data: { items: [{ appointmentCode: 'TEST-READ', status: 'Confirmed' }] }, sources: [] })}</>);
        expect(screen.getByText('Đã xác nhận').closest('[title]')).toHaveAttribute('title', 'Confirmed');
        expect(screen.queryByText('Confirmed')).not.toBeInTheDocument();
    });
    it('shows one active wizard with progress, collapsed choices and top focus when the step changes', () => {
        HTMLElement.prototype.scrollIntoView = vi.fn();
        const onStep = vi.fn().mockResolvedValue(undefined);
        const state: AiBookingWizardResponse = { step: 'specialty', title: 'Chọn chuyên khoa', message: 'Chọn để tiếp tục', options: [{ token: 'opaque', label: 'Khoa kiểm thử' }], canGoBack: false, actions: [], suggestions: [], assistantMode: 'Ready', providerWasCalled: false };
        const { rerender } = render(<BookingWizard state={state} busy={false} onStep={onStep} />);
        expect(screen.getByLabelText(/Bước 1\/6/)).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Khoa kiểm thử' }));
        rerender(<BookingWizard state={{ ...state, step: 'doctor', title: 'Chọn bác sĩ', options: [] }} busy={false} onStep={onStep} />);
        expect(screen.getByLabelText(/Bước 2\/6/)).toBeInTheDocument();
        expect(screen.getAllByRole('region')).toHaveLength(1);
        expect(screen.getByText('Chuyên khoa: Khoa kiểm thử ✓')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Khoa kiểm thử' })).not.toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Chọn bác sĩ' })).toHaveFocus();
        expect(HTMLElement.prototype.scrollIntoView).toHaveBeenLastCalledWith({ block: 'start', behavior: 'instant' });
    });
});
