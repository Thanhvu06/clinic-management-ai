import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { readFileSync } from 'node:fs';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DoctorExaminationPage } from '../pages/doctor/DoctorExaminationPage';
import { DoctorAppointmentDetail } from '../pages/doctor/DoctorAppointmentDetail';
import type { PatientClinicalContextDto } from '../types';

const mocks = vi.hoisted(() => ({ visitContext: vi.fn(), context: vi.fn(), history: vi.fn(), medicines: vi.fn(), orders: vi.fn(), catalog: vi.fn() }));
vi.mock('../api/doctorApi', () => ({ doctorApi: {
    getVisitPatientClinicalContext: mocks.visitContext, getPatientClinicalContext: mocks.context,
    getAppointmentHistory: mocks.history, getActiveMedicines: mocks.medicines,
} }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: { getDoctorOrdersByVisit: mocks.orders, getCatalog: mocks.catalog } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showToast: vi.fn() }) }));
vi.mock('../components/copilot/copilotResourceContext', () => ({ useCopilotResource: () => ({ setSelection: vi.fn() }) }));

const ok = <T,>(data: T) => ({ success: true, message: '', data });
const vitals = {
    id: 1, appointmentId: 1, recordedAtUtc: '2026-10-20T02:00:00Z', recordedByUserName: 'BS. Hòa', temperature: 37.2,
    bloodPressureSystolic: 120, bloodPressureDiastolic: 80, heartRate: 75, respiratoryRate: 18, weight: 65, height: 170, bmi: 22.5, spO2: 98,
};
const context = {
    patientId: 1, patientName: 'Bệnh nhân bàn khám', patientPhone: '0901234567', patientGender: 'Male', patientDob: '1990-01-01',
    totalPastVisits: 0, pastVisits: [],
    currentAppointment: { id: 1, appointmentCode: 'APT-1', appointmentDate: '2026-10-20', reason: 'Đau đầu', startTime: '08:00:00', endTime: '08:30:00', status: 'InConsultation' },
    encounter: { diagnosis: 'Viêm họng cấp', diagnosisCode: 'J02.9', chiefComplaint: 'Đau họng', summary: 'Bệnh nhẹ' },
    vitalSigns: vitals,
    vitalHistory: [{ ...vitals, appointmentCode: 'APT-1', appointmentDate: '2026-10-20' }],
} as unknown as PatientClinicalContextDto;
const cssRule = (path: string, selector: string) => {
    const css = readFileSync(path, 'utf8');
    const start = css.indexOf(`${selector} {`);
    return css.slice(start, css.indexOf('}', start));
};

beforeEach(() => {
    vi.resetAllMocks();
    mocks.visitContext.mockResolvedValue(ok(structuredClone(context)));
    mocks.context.mockResolvedValue(ok(structuredClone(context)));
    mocks.history.mockResolvedValue(ok([{ id: 1, action: 'Confirmed', createdAt: '2026-10-20T01:00:00Z' }]));
    mocks.medicines.mockResolvedValue(ok([]));
    mocks.orders.mockResolvedValue(ok([]));
    mocks.catalog.mockResolvedValue(ok([]));
});
afterEach(cleanup);

const mount = (element: React.ReactNode, route: string, pattern: string) => render(
    <ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={[route]}><Routes><Route path={pattern} element={element} /></Routes></MemoryRouter></ConfigProvider>
);

describe('U1 doctor examination workspace', () => {
    const tabNames = ['Diễn tiến', 'Sinh hiệu', 'Cận lâm sàng (0)', 'Kê đơn (0)', 'Lịch sử (0)'];

    it('puts the back link above the title and shows calm patient facts', async () => {
        mount(<DoctorExaminationPage />, '/doctor/visits/1/examination', '/doctor/visits/:visitId/examination');
        await screen.findByText('Bệnh nhân bàn khám');
        const backLink = screen.getByRole('link', { name: 'Quay lại bàn làm việc bác sĩ' });
        const title = screen.getByRole('heading', { level: 1, name: 'Bàn khám lâm sàng' });
        expect(backLink.compareDocumentPosition(title) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        expect(screen.getByText(/NS: 01\/01\/1990/)).toBeInTheDocument();
        expect(screen.queryByText(/1990-01-01/)).not.toBeInTheDocument();
        expect(screen.getByText('0 lượt khám trước')).toBeInTheDocument();
        expect(screen.queryByText('Lịch sử tại phòng khám')).not.toBeInTheDocument();
        expect(cssRule('src/pages/doctor/DoctorExaminationWorkspace.module.css', '.patientHeaderCard')).not.toMatch(/border-left/);
    });

    it('shows the five short tab labels without icons on one scrollable row', async () => {
        mount(<DoctorExaminationPage />, '/doctor/visits/1/examination', '/doctor/visits/:visitId/examination');
        await screen.findByText('Bệnh nhân bàn khám');
        const tabs = tabNames.map(name => screen.getByRole('button', { name }));
        const bar = tabs[0].parentElement!;
        expect(tabs.every(tab => tab.parentElement === bar)).toBe(true);
        expect(bar.querySelector('svg')).toBeNull();
        const barRule = cssRule('src/pages/doctor/DoctorExaminationWorkspace.module.css', '.workspaceTabs');
        expect(barRule).toMatch(/flex-wrap: nowrap/);
        expect(barRule).toMatch(/overflow-x: auto/);
        expect(readFileSync('src/pages/doctor/DoctorExaminationWorkspace.module.css', 'utf8')).not.toMatch(/white-space: normal/);
        expect(screen.getByRole('button', { name: 'Hoàn tất khám' })).toBeInTheDocument();
        expect(screen.queryByText('HOÀN TẤT KHÁM BỆNH')).not.toBeInTheDocument();
    });

    it('uses short vitals copy and merges weight and height into the BMI column', async () => {
        mount(<DoctorExaminationPage />, '/doctor/visits/1/examination', '/doctor/visits/:visitId/examination');
        fireEvent.click(await screen.findByRole('button', { name: 'Sinh hiệu' }));
        expect(screen.getByRole('heading', { name: 'Sinh hiệu lần này' })).toBeInTheDocument();
        expect(screen.queryByText(/VÙNG 1/)).not.toBeInTheDocument();
        expect(screen.queryByText(/Đây là lần đầu bệnh nhân ghi nhận/)).not.toBeInTheDocument();
        expect(screen.getByText('Phân loại BMI theo ngưỡng cấu hình của hệ thống')).toBeInTheDocument();
        const table = screen.getByRole('table');
        expect(within(table).getByRole('columnheader', { name: 'BMI' })).toBeInTheDocument();
        expect(within(table).queryByRole('columnheader', { name: /Cân nặng|Chiều cao/ })).not.toBeInTheDocument();
        expect(within(table).getByText('22,5 · 65 kg / 170 cm')).toBeInTheDocument();
    });

    it('uses short titles on the diagnostics and prescription tabs', async () => {
        mount(<DoctorExaminationPage />, '/doctor/visits/1/examination', '/doctor/visits/:visitId/examination');
        fireEvent.click(await screen.findByRole('button', { name: 'Cận lâm sàng (0)' }));
        expect(screen.getByRole('heading', { name: 'Tạo phiếu chỉ định' })).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Phiếu đã chỉ định (0)' })).toBeInTheDocument();
        expect(screen.queryByText(/Chọn các xét nghiệm hoặc chẩn đoán hình ảnh/)).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Kê đơn (0)' }));
        expect(screen.getByRole('heading', { name: 'Kê đơn' })).toBeInTheDocument();
        expect(screen.queryByText(/Tìm kiếm thuốc từ danh mục hoạt động/)).not.toBeInTheDocument();
    });
});

describe('U2 doctor appointment detail', () => {
    it('shows sentence-case labels, no title icons and vitals in a MetricStrip', async () => {
        const { container } = mount(<DoctorAppointmentDetail />, '/doctor/appointments/1', '/doctor/appointments/:id');
        await screen.findByText('Bệnh nhân bàn khám');
        expect(screen.getByText('Chẩn đoán bệnh')).toBeInTheDocument();
        expect(screen.getByText('Triệu chứng chính / Lý do khám')).toBeInTheDocument();
        for (const upper of ['NHIỆT ĐỘ', 'HUYẾT ÁP', 'NHỊP TIM', 'CHỈ SỐ BMI', 'SPO2']) expect(screen.queryByText(upper)).not.toBeInTheDocument();
        const css = readFileSync('src/pages/doctor/DoctorAppointmentDetail.module.css', 'utf8');
        expect(css).not.toMatch(/text-transform/);
        expect(cssRule('src/pages/doctor/DoctorAppointmentDetail.module.css', '.spotlightCard')).not.toMatch(/border-inline-start/);
        for (const heading of container.querySelectorAll('h2')) expect(heading.parentElement!.querySelector('svg')).toBeNull();
        const strip = screen.getByRole('group', { name: 'Dấu hiệu sinh tồn' });
        expect(within(strip).getByText('Nhiệt độ')).toBeInTheDocument();
        expect(within(strip).getByText('120/80')).toBeInTheDocument();
        expect(within(strip).getByText('170 / 65')).toBeInTheDocument();
        expect(within(strip).getByText('22.5')).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Nhật ký lịch hẹn' })).toBeInTheDocument();
    });
});
