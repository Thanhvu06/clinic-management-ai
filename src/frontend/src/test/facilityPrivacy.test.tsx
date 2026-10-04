import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { AppointmentLookupModal } from '../components/AppointmentLookupModal';
import { BookingWizard } from '../components/BookingWizard';
import type { AiBookingWizardResponse } from '../types/ai';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));
afterEach(() => { cleanup(); vi.resetAllMocks(); });

describe('P5 anonymous appointment privacy', () => {
    it('requires both fields and submits the code and phone', async () => {
        vi.mocked(axiosClient.get).mockResolvedValue({ success: true, data: [] });
        render(<AppointmentLookupModal isOpen onClose={vi.fn()} />);
        const code = screen.getByRole('textbox', { name: 'Mã lịch hẹn' });
        expect(screen.getAllByRole('textbox')).toHaveLength(2);
        expect(code).toBeRequired();
        const phone = screen.getByRole('textbox', { name: 'Số điện thoại chủ lịch hẹn' });
        expect(phone).toBeRequired();
        fireEvent.change(code, { target: { value: ' APT-P5 ' } });
        fireEvent.change(phone, { target: { value: ' 0901234567 ' } });
        fireEvent.click(screen.getByRole('button', { name: 'Tra cứu' }));
        await waitFor(() => expect(axiosClient.get).toHaveBeenCalledWith('/appointments/lookup?query=APT-P5&phone=0901234567'));
    });
    it('does not call the API when only a code is supplied', () => {
        render(<AppointmentLookupModal isOpen onClose={vi.fn()} />);
        fireEvent.change(screen.getByRole('textbox', { name: 'Mã lịch hẹn' }), { target: { value: 'APT-P5' } });
        fireEvent.submit(screen.getByRole('button', { name: 'Tra cứu' }).closest('form')!);
        expect(axiosClient.get).not.toHaveBeenCalled();
    });
});

describe('P5 facility step in the existing wizard', () => {
    it('shows facility progress, selection and back buttons', () => {
        const onStep = vi.fn().mockResolvedValue(undefined);
        const state = { step: 'facility', title: 'Chọn cơ sở', message: 'Chọn cơ sở khám', options: [{ token: 'opaque-facility', label: 'Cơ sở A' }], canGoBack: true, backToken: 'opaque-back', actions: [], suggestions: [], assistantMode: 'Ready', providerWasCalled: false } as unknown as AiBookingWizardResponse;
        render(<BookingWizard state={state} busy={false} onStep={onStep} />);
        expect(screen.getByLabelText(/Bước 5\/7/)).toHaveTextContent('Cơ sở');
        fireEvent.click(screen.getByRole('button', { name: 'Cơ sở A' }));
        expect(onStep).toHaveBeenCalledWith('pick', 'opaque-facility');
        fireEvent.click(screen.getByRole('button', { name: 'Quay lại bước trước' }));
        expect(onStep).toHaveBeenCalledWith('back', 'opaque-back');
    });
    it('includes the chosen facility in the review summary', () => {
        const state = { step: 'review', title: 'Xem lại', message: 'Thông tin đã chọn', options: [], canGoBack: false, summary: { specialtyName: 'Nội khoa', doctorName: 'Bác sĩ A', facilityName: 'Cơ sở A', reasonProvided: true }, actions: [], suggestions: [], assistantMode: 'Ready', providerWasCalled: false } as AiBookingWizardResponse;
        render(<BookingWizard state={state} busy={false} onStep={vi.fn()} />);
        expect(screen.getByText('Cơ sở: Cơ sở A')).toBeInTheDocument();
    });
});
