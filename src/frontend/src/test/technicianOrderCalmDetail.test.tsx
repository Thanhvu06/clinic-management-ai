import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { readFileSync } from 'node:fs';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TechnicianOrderDetail } from '../pages/technician/TechnicianOrderDetail';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import type { DiagnosticOrderDto } from '../types';

const mocks = vi.hoisted(() => ({ detail: vi.fn() }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getTechnicianOrderById: mocks.detail } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showToast: vi.fn(), showConfirm: vi.fn() }) }));

const order: DiagnosticOrderDto = {
    id: 1, appointmentId: 10, appointmentCode: 'APT-CALM', orderCode: 'DX-CALM-001', patientId: 2,
    patientName: 'Nguyễn Văn An', patientPhone: '0901234567', patientGender: 'Male', patientAge: 35,
    orderingDoctorId: 5, orderingDoctorName: 'BS. Lê Minh', specialtyName: 'Nội khoa', clinicalIndication: 'Kiểm tra',
    status: 'InProgress', orderedAtUtc: '2026-10-12T01:15:00Z', startedAtUtc: '2026-10-12T01:30:00Z', rowVersion: 'v1',
    items: [{ id: 102, diagnosticOrderId: 1, diagnosticServiceId: 2, serviceCode: 'US', serviceName: 'Siêu âm ổ bụng', category: 'Ultrasound',
        status: 'InProgress', preparationInstructions: 'Nhịn ăn 6 tiếng', result: null },
    { id: 103, diagnosticOrderId: 1, diagnosticServiceId: 3, serviceCode: 'XQ', serviceName: 'X-quang ngực', category: 'Imaging', status: 'InProgress', result: null }],
};

beforeEach(() => {
    vi.clearAllMocks();
    mocks.detail.mockResolvedValue({ success: true, message: '', data: order });
});
afterEach(cleanup);

const mount = () => render(<ConfigProvider theme={{ token: { motion: false } }}><CopilotResourceProvider><MemoryRouter initialEntries={['/diagnostics/orders/1']}>
    <Routes><Route path="/diagnostics/orders/:id" element={<TechnicianOrderDetail />} /></Routes>
</MemoryRouter></CopilotResourceProvider></ConfigProvider>);

describe('U3 technician order detail', () => {
    it('drops the uppercase eyebrow and shows preparation as one plain line', async () => {
        mount();
        expect(await screen.findByText('DX-CALM-001')).toBeInTheDocument();
        expect(screen.queryByText('PHIẾU CHỈ ĐỊNH CẬN LÂM SÀNG')).not.toBeInTheDocument();
        expect(screen.getByText('Chuẩn bị: Nhịn ăn 6 tiếng').querySelector('strong')).toBeNull();
        const css = readFileSync('src/pages/technician/TechnicianOrderDetail.module.css', 'utf8');
        const rule = css.slice(css.indexOf('.preparationNote {'), css.indexOf('}', css.indexOf('.preparationNote {')));
        expect(rule).not.toMatch(/background|border/);
    });

    it('keeps the assistant button as a default button with the original aria-label', async () => {
        mount();
        const button = await screen.findByRole('button', { name: 'Chọn Siêu âm ổ bụng cho Copilot' });
        expect(button).toHaveTextContent('Hỏi trợ lý');
        expect(button).not.toHaveClass('ant-btn-primary');
        fireEvent.click(button);
        expect(button).toHaveTextContent('Đang hỏi trợ lý');
        expect(button).not.toHaveClass('ant-btn-primary');
    });

    it('places the submit block in normal flow right below the progress card, without sticky positioning', async () => {
        mount();
        const progress = await screen.findByRole('progressbar', { name: 'Tiến độ kết quả dịch vụ' });
        const title = screen.getByText(/^(Gửi duyệt kết quả cận lâm sàng|Hoàn tất phiếu chỉ định cận lâm sàng)$/);
        const bar = title.closest('.ant-card')!;
        expect(progress.closest('.ant-card')!.nextElementSibling).toBe(bar);
        expect(bar.className).toMatch(/completionBar/);
        const css = readFileSync('src/pages/technician/TechnicianOrderDetail.module.css', 'utf8');
        const rule = css.slice(css.indexOf('.completionBar {'), css.indexOf('}', css.indexOf('.completionBar {')));
        expect(rule).not.toMatch(/sticky|position|z-index/);
    });
});
