import { useState } from 'react';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfigProvider, Input } from 'antd';
import { afterEach, expect, it, vi } from 'vitest';
import { DoctorPrescriptionTable } from '../pages/doctor/DoctorPrescriptionTable';
import { useDangerMedicineIds } from '../pages/doctor/useDangerMedicineIds';
import { DoctorDiagnosticItemsTable } from '../pages/doctor/DoctorDiagnosticItemsTable';
import { DoctorVitalHistoryTable } from '../pages/doctor/DoctorVitalHistoryTable';
import { VerificationParameterTable } from '../pages/common/VerificationParameterTable';
import { createPrescriptionItem } from '../pages/doctor/prescriptionQuantity';
import type { DiagnosticOrderDto, PatientClinicalContextDto } from '../types';
import type { DiagnosticResultValueDto } from '../types/diagnostic';

const tableRender = vi.hoisted(() => vi.fn());
vi.mock('antd', async importOriginal => {
    const actual = await importOriginal<typeof import('antd')>();
    return { ...actual, Table: (props: React.ComponentProps<typeof actual.Table>) => {
        tableRender(props);
        return <actual.Table {...props} />;
    } };
});
afterEach(() => { cleanup(); vi.clearAllMocks(); vi.restoreAllMocks(); });

it('isolates the prescription table from notes while keeping row editing and quantity calculation current', async () => {
    function Form() {
        const [notes, setNotes] = useState('');
        const [items, setItems] = useState([createPrescriptionItem({ id: 1, code: 'MED-1', name: 'Thuốc kiểm thử', unit: 'Viên', stockQuantity: 100 })]);
        const dangerMedicineIds = useDangerMedicineIds();
        return <>
            <DoctorPrescriptionTable prescriptionItems={items} dangerMedicineIds={dangerMedicineIds} setPrescriptionItems={setItems} />
            <Input.TextArea aria-label="Ghi chú đơn thuốc" value={notes} onChange={event => setNotes(event.target.value)} />
        </>;
    }
    const error = vi.spyOn(console, 'error');
    const user = userEvent.setup();
    render(<ConfigProvider><Form /></ConfigProvider>);
    const renders = tableRender.mock.calls.length;
    const text = 'Hướng dẫn dùng thuốc theo chỉ định. '.repeat(6).slice(0, 150);
    await user.type(screen.getByRole('textbox', { name: 'Ghi chú đơn thuốc' }), text);
    expect(tableRender).toHaveBeenCalledTimes(renders);
    expect(screen.getByRole('textbox', { name: 'Ghi chú đơn thuốc' })).toHaveValue(text);

    const instructions = screen.getByPlaceholderText('Uống sau khi ăn 30 phút...');
    await user.clear(instructions);
    await user.type(instructions, text);
    expect(instructions).toHaveValue(text);
    const dosage = screen.getByPlaceholderText('1 viên');
    await user.clear(dosage);
    await user.type(dosage, '2 viên');
    expect(dosage).toHaveValue('2 viên');
    expect(screen.getAllByRole('spinbutton')[0]).toHaveValue(20);
    expect(error.mock.calls.flat().map(String).join(' ')).not.toContain('Maximum update depth');
}, 20000);

it('does not rerender clinical history and result tables for notes, but displays refreshed results', async () => {
    const values: DiagnosticResultValueDto[] = [{ parameterId: 11, parameterCode: 'WBC', parameterName: 'Bạch cầu', unit: '10^9/L', value: 7, refLow: 4, refHigh: 10, referenceRangeText: '4 – 10', flag: 'Normal', sortOrder: 0 }];
    const items: DiagnosticOrderDto['items'] = [{ id: 1, diagnosticOrderId: 1, diagnosticServiceId: 1, serviceCode: 'LAB-CBC', serviceName: 'Tổng phân tích máu', category: 'Laboratory', status: 'Completed', result: { id: 1, diagnosticOrderItemId: 1, resultText: '', resultedAtUtc: '2026-11-09T01:00:00Z', resultedByUserId: 'tech', resultedByUserName: 'Kỹ thuật viên', values } }];
    const history: NonNullable<PatientClinicalContextDto['vitalHistory']> = [{ appointmentId: 10, appointmentCode: 'APT-TYPING', appointmentDate: '2026-11-09', recordedAtUtc: '2026-11-09T01:00:00Z', recordedByUserName: 'Bác sĩ kiểm thử', bloodPressureSystolic: 120, bloodPressureDiastolic: 80 }];
    function Form() {
        const [notes, setNotes] = useState('');
        const [rows, setRows] = useState(items);
        return <>
            <DoctorDiagnosticItemsTable items={rows} verificationStatus="Verified" />
            <DoctorVitalHistoryTable data={history} />
            <VerificationParameterTable serviceName="Tổng phân tích máu" values={rows[0].result!.values!} />
            <Input.TextArea aria-label="Ghi chú lâm sàng" value={notes} onChange={event => setNotes(event.target.value)} />
            <button onClick={() => setRows([{ ...rows[0], result: { ...rows[0].result!, values: [{ ...values[0], value: 13, flag: 'High' }] } }])}>Tải kết quả mới</button>
        </>;
    }
    const user = userEvent.setup();
    render(<ConfigProvider><Form /></ConfigProvider>);
    const renders = tableRender.mock.calls.length;
    const text = 'Nhận xét lâm sàng và ghi chú duyệt. '.repeat(6).slice(0, 150);
    await user.type(screen.getByRole('textbox', { name: 'Ghi chú lâm sàng' }), text);
    expect(tableRender).toHaveBeenCalledTimes(renders);
    expect(screen.getByRole('textbox', { name: 'Ghi chú lâm sàng' })).toHaveValue(text);
    await user.click(screen.getByRole('button', { name: 'Tải kết quả mới' }));
    expect(screen.getByText('13')).toBeInTheDocument();
    expect(screen.getByText('Cao')).toBeInTheDocument();
    expect(screen.getByText('120/80')).toBeInTheDocument();
}, 20000);
