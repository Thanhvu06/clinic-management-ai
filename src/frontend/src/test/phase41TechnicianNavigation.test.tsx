import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { TechnicianOrderDetail } from '../pages/technician/TechnicianOrderDetail';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';

vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getTechnicianOrderById: vi.fn(async () => ({ success: true, data: {
    id: 1, orderCode: 'DX-1', status: 'InProgress', orderedAtUtc: '2026-10-22T01:00:00Z', patientName: 'Người bệnh',
    items: [1, 2].map(id => ({ id, serviceName: `Dịch vụ ${id}`, serviceCode: `SV-${id}`, category: 'Other', status: 'InProgress' }))
} })) } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showToast: vi.fn(), showConfirm: vi.fn() }) }));
afterEach(cleanup);

it('places a small back link above h1 and changes only the selected assistant button style', async () => {
    render(<CopilotResourceProvider><MemoryRouter initialEntries={['/diagnostics/orders/1']}>
        <Routes><Route path="/diagnostics/orders/:id" element={<TechnicianOrderDetail />} /></Routes>
    </MemoryRouter></CopilotResourceProvider>);
    const title = await screen.findByRole('heading', { name: 'Thực hiện phiếu chỉ định CLS' });
    const back = screen.getByRole('link', { name: 'Quay lại danh sách' });
    expect(back).toHaveAttribute('href', '/diagnostics');
    expect(back.compareDocumentPosition(title) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    const button = screen.getByRole('button', { name: 'Chọn Dịch vụ 1 cho Copilot' });
    expect(button).toHaveTextContent('Hỏi trợ lý');
    expect(button.className).not.toContain('assistantSelected');
    fireEvent.click(button);
    expect(button).toHaveTextContent('Hỏi trợ lý');
    expect(button.className).toContain('assistantSelected');
});
