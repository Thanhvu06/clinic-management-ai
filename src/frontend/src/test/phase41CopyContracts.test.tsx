import { cleanup, render, screen } from '@testing-library/react';
import { readFileSync } from 'node:fs';
import { afterEach, expect, it } from 'vitest';
import type { AiCopilotCard } from '../api/aiCopilotApi';
import { copilotCardEmptyMessage, renderCopilotCardData } from '../components/copilot/copilotDataRenderers';

afterEach(cleanup);

it.each([
    ['pages/reception/ReceptionBilling.tsx', 'Lượt khám ngoại trú', 'Lượt khám ngoại trú (Visit)'],
    ['pages/reception/ReceptionBilling.tsx', 'Lịch hẹn đã khám', 'Lịch khám bệnh (Appointment)'],
    ['pages/pharmacy/PharmacyInventory.tsx', 'Nhập thêm hàng', 'Nhập hàng thêm (Stock In)'],
    ['pages/pharmacy/PharmacyInventory.tsx', 'Điều chỉnh sau kiểm kê', 'Kiểm kê điều chỉnh (Adjustment)'],
    ['pages/admin/AdminUsers.tsx', 'Không thể khóa tài khoản quản trị viên', 'Không thể đổi trạng thái Admin'],
    ['pages/Register.tsx', 'Địa chỉ email', 'Địa chỉ Email'],
    ['pages/reception/ReceptionAppointments.tsx', 'nên trợ lý chưa thể chuẩn bị thao tác.', 'nên Copilot chưa thể chuẩn bị thao tác.']
])('uses the requested Vietnamese label in %s: %s', (path, label, oldLabel) => {
    const source = readFileSync(`src/${path}`, 'utf8');
    expect(source).toContain(label);
    expect(source).not.toContain(oldLabel);
});

it('removes promotional copy from the internal shell and diagnostic printout', () => {
    expect(readFileSync('src/layouts/MainLayout.tsx', 'utf8')).not.toContain('Hệ thống y tế thông minh');
    expect(readFileSync('src/pages/doctor/DiagnosticOrderPrint.tsx', 'utf8')).not.toContain('Phiếu chỉ định được khởi tạo từ Hệ thống');
});

it('shows the same concise fallback for unsupported assistant data', () => {
    const card = { type: 'unsupported_type', data: { raw: 'internal payload' } } as unknown as AiCopilotCard;
    expect(copilotCardEmptyMessage(card)).toBe('Trợ lý chưa hiển thị được loại dữ liệu này.');
    render(<>{renderCopilotCardData(card)}</>);
    expect(screen.getByText('Trợ lý chưa hiển thị được loại dữ liệu này.')).toBeInTheDocument();
    expect(screen.queryByText('internal payload')).not.toBeInTheDocument();
});
