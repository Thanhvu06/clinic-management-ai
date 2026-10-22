import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import { StatusBadge } from '../components/common/StatusBadge';
import { getAppointmentHistoryActionLabel } from '../utils/appointmentHistoryLabels';

afterEach(cleanup);

it.each([
    ['Paid', 'Đã thanh toán', 'success'], ['Unpaid', 'Chờ thanh toán', 'warning'],
    ['Issued', 'Đã phát hành', 'processing'], ['Dispensed', 'Đã cấp phát', 'success']
])('renders %s with its label and color', (status, label, color) => {
    render(<StatusBadge status={status} />);
    expect(screen.getByText(label)).toHaveClass(`ant-tag-${color}`);
});

it.each(['Paid', 'Unpaid', 'Issued', 'Dispensed', 'FutureStatus'])('prioritizes a supplied label for %s', status => {
    render(<StatusBadge status={status} label="Nhãn được cung cấp" />);
    expect(screen.getByText('Nhãn được cung cấp')).toBeInTheDocument();
});

it.each([undefined, '', '   ', 'FutureStatus', ' futureSTATUS ', 'FUTURESTATUS'])('rejects an empty label or raw status code (%s)', label => {
    render(<StatusBadge status=" FutureStatus " label={label} />);
    expect(screen.getByText('Không xác định')).toBeInTheDocument();
});

it('retains a human label that differs from the unknown status code', () => {
    render(<StatusBadge status="FutureStatus" label="  Nhãn được cung cấp  " />);
    expect(screen.getByText('Nhãn được cung cấp')).toBeInTheDocument();
});

it.each([
    ['CheckedIn', 'Tiếp nhận'], ['InConsultation', 'Bắt đầu khám'], ['RevisitCreated', 'Tạo lịch tái khám']
])('translates history action %s', (action, label) => {
    expect(getAppointmentHistoryActionLabel(action)).toBe(label);
});
