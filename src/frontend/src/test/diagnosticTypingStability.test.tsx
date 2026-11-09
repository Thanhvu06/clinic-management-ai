import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { TechnicianOrderDetail } from '../pages/technician/TechnicianOrderDetail';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import type { DiagnosticOrderDto } from '../types';
import type { DiagnosticResultValueDto, DiagnosticServiceParameterDto } from '../types/diagnostic';

const mocks = vi.hoisted(() => ({ detail: vi.fn(), save: vi.fn(), tableRender: vi.fn(), alert: vi.fn(), toast: vi.fn(), confirm: vi.fn() }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getTechnicianOrderById: mocks.detail, recordTechnicianItemResult: mocks.save } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: mocks.alert, showToast: mocks.toast, showConfirm: mocks.confirm }) }));
vi.mock('antd', async importOriginal => {
    const actual = await importOriginal<typeof import('antd')>();
    return { ...actual, Table: (props: React.ComponentProps<typeof actual.Table>) => {
        mocks.tableRender(props);
        return <actual.Table {...props} />;
    } };
});

const parameters: DiagnosticServiceParameterDto[] = [
    { id: 11, code: 'WBC', name: 'Bạch cầu', unit: '10^9/L', refLow: 4, refHigh: 10, criticalLow: 2.5, criticalHigh: 25, referenceRangeText: '4 – 10' },
];
const savedValues: DiagnosticResultValueDto[] = [
    { parameterId: 11, parameterCode: 'WBC', parameterName: 'Bạch cầu', value: 7, unit: '10^9/L', refLow: 4, refHigh: 10, referenceRangeText: '4 – 10', flag: 'Normal', sortOrder: 0 },
];
const order: DiagnosticOrderDto = {
    id: 1, appointmentId: 10, appointmentCode: 'APT-TYPING', orderCode: 'DX-TYPING', patientId: 2,
    patientName: 'Người bệnh kiểm thử', patientPhone: '', patientGender: 'Female', patientAge: 35,
    orderingDoctorId: 5, orderingDoctorName: 'Bác sĩ kiểm thử', specialtyName: 'Nội tổng quát',
    clinicalIndication: 'Kiểm tra sức khỏe', status: 'InProgress', orderedAtUtc: '2026-11-09T01:00:00Z', rowVersion: 'order-v1',
    items: [
        { id: 101, diagnosticOrderId: 1, diagnosticServiceId: 1, serviceCode: 'LAB-CBC', serviceName: 'Tổng phân tích máu', category: 'Laboratory', status: 'Completed', parameters,
            result: { id: 501, diagnosticOrderItemId: 101, resultText: '', resultedAtUtc: '2026-11-09T01:30:00Z', resultedByUserId: 'tech', resultedByUserName: 'Kỹ thuật viên', values: savedValues } },
        { id: 102, diagnosticOrderId: 1, diagnosticServiceId: 2, serviceCode: 'IMG-CXR', serviceName: 'X-quang ngực', category: 'Imaging', status: 'InProgress', parameters: [] },
    ],
};

function mount() {
    return render(<ConfigProvider><CopilotResourceProvider><MemoryRouter initialEntries={['/diagnostics/orders/1']}>
        <Routes><Route path="/diagnostics/orders/:id" element={<TechnicianOrderDetail />} /></Routes>
    </MemoryRouter></CopilotResourceProvider></ConfigProvider>);
}
const rendersFor = (data: unknown) => mocks.tableRender.mock.calls.filter(([props]) => props.dataSource === data).length;
beforeEach(() => { vi.clearAllMocks(); mocks.detail.mockResolvedValue({ success: true, data: order }); });
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it('keeps the saved CBC table stable while typing 150 characters into the X-ray result', async () => {
    const error = vi.spyOn(console, 'error');
    const user = userEvent.setup();
    mount();
    const table = await screen.findByRole('table', { name: 'Chỉ số đã lưu: Tổng phân tích máu' });
    const textarea = screen.getByRole('textbox', { name: /Kết quả chi tiết/ });
    const renders = rendersFor(savedValues);
    const text = 'Mô tả kết quả X quang ngực. '.repeat(8).slice(0, 150);

    await user.type(textarea, text);

    expect(textarea).toHaveValue(text);
    expect(within(table).getByText('7')).toBeInTheDocument();
    expect(rendersFor(savedValues)).toBe(renders);
    expect(screen.queryByText(/Có lỗi hiển thị/)).not.toBeInTheDocument();
    expect(error.mock.calls.flat().map(String).join(' ')).not.toContain('Maximum update depth');
    expect(mocks.save).not.toHaveBeenCalled();
}, 20000);

it('keeps both CBC tables stable during X-ray typing and still updates edited numeric values', async () => {
    const user = userEvent.setup();
    mount();
    await screen.findByRole('table', { name: 'Chỉ số đã lưu: Tổng phân tích máu' });
    await user.click(screen.getByRole('button', { name: 'Xem/Sửa kết quả' }));
    const entry = screen.getByRole('table', { name: 'Nhập chỉ số: Tổng phân tích máu' });
    const savedRenders = rendersFor(savedValues);
    const inputRenders = rendersFor(parameters);
    const text = 'Kết quả hình ảnh bình thường. '.repeat(8).slice(0, 150);

    await user.type(screen.getByRole('textbox', { name: /Kết quả chi tiết/ }), text);

    expect(rendersFor(savedValues)).toBe(savedRenders);
    expect(rendersFor(parameters)).toBe(inputRenders);
    const numericInput = within(entry).getByRole('spinbutton', { name: 'Kết quả Bạch cầu (WBC)' });
    await user.clear(numericInput);
    await user.type(numericInput, '3');
    expect(numericInput).toHaveValue(3);
    expect(within(entry).getByText('Thấp')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: /Kết quả chi tiết/ })).toHaveValue(text);
    expect(screen.queryByText(/Có lỗi hiển thị/)).not.toBeInTheDocument();
}, 20000);
