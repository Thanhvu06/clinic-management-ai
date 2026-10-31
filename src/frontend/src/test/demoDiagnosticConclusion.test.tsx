import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { TechnicianOrderDetail } from '../pages/technician/TechnicianOrderDetail';

const mocks = vi.hoisted(() => ({ detail: vi.fn(), save: vi.fn(), complete: vi.fn(), confirm: vi.fn(), selection: vi.fn() }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getTechnicianOrderById: mocks.detail, recordTechnicianItemResult: mocks.save, completeTechnicianOrder: mocks.complete } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showToast: vi.fn(), showConfirm: mocks.confirm }) }));
vi.mock('../components/copilot/copilotResourceContext', () => ({ useCopilotResource: () => ({ setSelection: mocks.selection }) }));
const conclusion = 'Bất thường — đề nghị bác sĩ xem';
const order = {
    id: 1, orderCode: 'DX-DEMO', patientId: 1, patientName: 'Người bệnh thử', status: 'InProgress', rowVersion: 'v1',
    orderedAtUtc: '2026-10-31T01:00:00Z', items: [{ id: 2, diagnosticServiceId: 3, serviceCode: 'US', serviceName: 'Siêu âm',
        category: 'Ultrasound', status: 'Completed', rowVersion: 'i1', result: { resultText: 'Mô tả đã lưu', conclusion: '' } }],
};
beforeEach(() => {
    vi.clearAllMocks();
    mocks.detail.mockResolvedValue({ success: true, data: order });
    mocks.save.mockResolvedValue({ success: true, data: { ...order, rowVersion: 'v2', items: [{ ...order.items[0], result: { ...order.items[0].result, conclusion } }] } });
    mocks.complete.mockResolvedValue({ success: true, data: { ...order, status: 'Completed' } });
    mocks.confirm.mockImplementation((_message, action) => action());
});
afterEach(cleanup);
it('persists a changed conclusion chip before completing an already resulted order', async () => {
    render(<MemoryRouter initialEntries={['/diagnostics/orders/1']}><Routes><Route path="/diagnostics/orders/:id" element={<TechnicianOrderDetail />} /></Routes></MemoryRouter>);
    await screen.findByText('DX-DEMO');
    fireEvent.click(screen.getByRole('button', { name: 'Xem/Sửa kết quả' }));
    fireEvent.click(screen.getByRole('button', { name: conclusion }));
    fireEvent.click(screen.getByRole('button', { name: 'Hoàn tất phiếu' }));
    await waitFor(() => expect(mocks.complete).toHaveBeenCalled());
    expect(mocks.save).toHaveBeenCalledWith(1, 2, expect.objectContaining({ conclusion }));
    expect(mocks.complete).toHaveBeenCalledWith(1, { rowVersion: 'v2' });
});
