import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DoctorExaminationWorkspace } from '../pages/doctor/DoctorExaminationWorkspace';
import type { DiagnosticOrderDto } from '../types';
import type { DiagnosticResultFlag } from '../types/diagnostic';

const mocks = vi.hoisted(() => ({ context: vi.fn(), orders: vi.fn(), catalog: vi.fn(), medicines: vi.fn(), alert: vi.fn(), toast: vi.fn(), selection: vi.fn() }));
vi.mock('../api/doctorApi', () => ({ doctorApi: { getVisitPatientClinicalContext: mocks.context, getActiveMedicines: mocks.medicines } }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getDoctorOrdersByVisit: mocks.orders, getCatalog: mocks.catalog } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: mocks.alert, showToast: mocks.toast }) }));
vi.mock('../components/copilot/copilotResourceContext', () => ({ useCopilotResource: () => ({ setSelection: mocks.selection }) }));
const ok = <T,>(data: T) => ({ success: true, data });
function order(id: number, status: DiagnosticOrderDto['status'], flags: DiagnosticResultFlag[] = []): DiagnosticOrderDto {
    return {
        id, orderCode: `DX-${id}`, appointmentId: 1, appointmentCode: 'APT-1', patientId: 1, patientName: 'Bệnh nhân',
        patientGender: 'Male', patientPhone: '', orderingDoctorId: 1, orderingDoctorName: 'Bác sĩ', specialtyName: 'Nội khoa', clinicalIndication: '',
        status, orderedAtUtc: '2026-10-15T01:00:00Z',
        items: [{ id: id * 10, diagnosticOrderId: id, diagnosticServiceId: 1, serviceCode: 'LAB', serviceName: `Xét nghiệm ${id}`, category: 'Laboratory', status: 'Completed',
            result: { id, diagnosticOrderItemId: id * 10, resultText: '', resultedAtUtc: '2026-10-15T02:00:00Z', resultedByUserId: 'tech', resultedByUserName: 'KTV',
                values: flags.map((flag, index) => ({ parameterId: index, parameterCode: `P${index}`, parameterName: `Chỉ số ${index}`, unit: '', value: 1, refLow: null, refHigh: null, referenceRangeText: '', flag, sortOrder: index })) } }],
    };
}
beforeEach(() => {
    vi.resetAllMocks();
    mocks.context.mockResolvedValue(ok({ patientId: 1, patientName: 'Bệnh nhân thử nghiệm', patientPhone: '', patientGender: 'Male', totalPastVisits: 0, pastVisits: [],
        currentAppointment: { id: 1, appointmentCode: 'APT-1', status: 'InConsultation', startTime: '08:00:00', endTime: '08:30:00' } }));
    mocks.catalog.mockResolvedValue(ok([])); mocks.medicines.mockResolvedValue(ok([])); mocks.orders.mockResolvedValue(ok([]));
});
afterEach(cleanup);
async function mount() {
    render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={['/doctor/visits/1']}><Routes><Route path="/doctor/visits/:visitId" element={<DoctorExaminationWorkspace />} /></Routes></MemoryRouter></ConfigProvider>);
    fireEvent.click(await screen.findByRole('button', { name: /^Cận lâm sàng/ }));
}
describe('Doctor diagnostic order groups', () => {
    it('groups orders and puts unreviewed completed orders first with critical, abnormal and normal summaries', async () => {
        const reviewed = { ...order(2, 'Completed', ['Normal']), reviewedAtUtc: '2026-10-15T03:00:00Z' };
        const critical = order(3, 'Completed', ['CriticalLow', 'CriticalHigh', 'High']);
        critical.items.push({ ...order(9, 'Completed', ['CriticalLow']).items[0], status: 'Cancelled' });
        mocks.orders.mockResolvedValue(ok([reviewed, order(1, 'Ordered'), critical, order(4, 'Completed', ['High', 'Low']), order(5, 'Cancelled'), order(6, 'InProgress')]));
        await mount();
        expect(screen.getByText('Phiếu đã chỉ định (6)')).toBeInTheDocument();
        const waiting = screen.getByRole('region', { name: 'Chờ kết quả' });
        const completed = screen.getByRole('region', { name: 'Đã có kết quả' });
        const cancelled = screen.getByRole('region', { name: 'Đã hủy' });
        expect(within(waiting).getByText('Chờ kết quả')).toBeInTheDocument();
        expect(within(completed).getAllByText(/^DX-/).map(node => node.textContent)).toEqual(['DX-3', 'DX-4', 'DX-2']);
        expect(within(completed).getByText('2 chỉ số NGUY HIỂM').className).toContain('critical');
        expect(within(completed).getByText('2 chỉ số bất thường').className).toContain('warning');
        expect(within(completed).getByText('Chỉ số trong giới hạn').className).toContain('normal');
        expect(within(cancelled).queryByRole('link', { name: 'In phiếu chỉ định' })).not.toBeInTheDocument();
        expect(within(waiting).getAllByRole('link', { name: 'In phiếu chỉ định' })).toHaveLength(2);
        expect(within(completed).getAllByText('Chưa duyệt kết quả – bác sĩ cần xác nhận đã xem')).toHaveLength(2);
    });
    it('separates pending lab verification from returned corrections in badges and result cells', async () => {
        mocks.orders.mockResolvedValue(ok([{ ...order(1, 'InProgress'), verificationStatus: 'Pending' }, { ...order(2, 'InProgress'), verificationStatus: 'Returned' }]));
        await mount();
        expect(screen.getByText('Phòng xét nghiệm đang duyệt')).toBeInTheDocument();
        expect(screen.getByText('Bị trả lại – KTV đang sửa kết quả')).toBeInTheDocument();
        expect(screen.getByText('Đang chờ phòng xét nghiệm duyệt')).toBeInTheDocument();
        expect(screen.getByText('KTV đang sửa kết quả')).toBeInTheDocument();
        expect(screen.queryByRole('region', { name: 'Đã có kết quả' })).not.toBeInTheDocument();
        expect(screen.queryByRole('region', { name: 'Đã hủy' })).not.toBeInTheDocument();
        expect(screen.getByText('Phòng xét nghiệm đang duyệt').parentElement).not.toHaveTextContent(/^Thời gian lập:/);
    });
    it('keeps first-load error separate from empty state, with one retry and disabled refresh', async () => {
        mocks.orders.mockRejectedValue(new Error('Network unavailable'));
        await mount();
        expect(screen.queryByText('Chưa có phiếu chỉ định cận lâm sàng nào trong ca khám này.')).not.toBeInTheDocument();
        expect(screen.getAllByRole('button', { name: /Thử lại/ })).toHaveLength(1);
        let resolve!: (value: unknown) => void;
        mocks.orders.mockReturnValueOnce(new Promise(done => { resolve = done; }));
        fireEvent.click(await screen.findByRole('button', { name: /Cập nhật kết quả/i }));
        await waitFor(() => expect(screen.getByRole('button', { name: 'Đang cập nhật...' })).toBeDisabled());
        expect(screen.getByText('Đang tải phiếu chỉ định...')).toBeInTheDocument();
        await act(async () => resolve(ok([])));
        expect(screen.getByText('Chưa có phiếu chỉ định cận lâm sàng nào trong ca khám này.')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /Thử lại/ })).not.toBeInTheDocument();
        expect(screen.getByRole('button', { name: /Cập nhật kết quả/i })).toBeEnabled();
    });
});
