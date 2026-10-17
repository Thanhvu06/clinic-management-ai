import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { doctorApi } from '../api/doctorApi';
import { diagnosticApi } from '../api/diagnosticApi';
import { icd10Api } from '../api/icd10Api';
import { DialogProvider } from '../contexts/DialogContext';
import { DoctorExaminationWorkspace } from '../pages/doctor/DoctorExaminationWorkspace';
import { Icd10CodeInput } from '../components/diagnoses/Icd10CodeInput';

const { setSelection } = vi.hoisted(() => ({ setSelection: vi.fn() }));
vi.mock('../components/copilot/copilotResourceContext', () => ({ useCopilotResource: () => ({ setSelection }) }));
vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));
vi.mock('../api/doctorApi', () => ({ doctorApi: {
    getPatientClinicalContext: vi.fn(), getActiveMedicines: vi.fn(), saveEncounter: vi.fn()
} }));
vi.mock('../api/diagnosticApi', () => ({ diagnosticApi: {
    getCatalog: vi.fn(), getDoctorOrdersByAppointment: vi.fn()
} }));

const code = { code: 'J02.9', nameVi: 'Viêm họng cấp, không đặc hiệu', chapterCode: 'J00-J99' };
const ok = <T,>(data: T) => ({ success: true, message: '', data });

beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(axiosClient.get).mockResolvedValue(ok([code]));
    vi.mocked(doctorApi.getActiveMedicines).mockResolvedValue(ok([]));
    vi.mocked(diagnosticApi.getCatalog).mockResolvedValue(ok([]));
    vi.mocked(diagnosticApi.getDoctorOrdersByAppointment).mockResolvedValue(ok([]));
    vi.mocked(doctorApi.getPatientClinicalContext).mockResolvedValue(ok({
        patientId: 1, patientName: 'Người bệnh ICD', patientPhone: '0901234567', patientGender: 'Male',
        patientDob: '1990-01-01', totalPastVisits: 0, pastVisits: [],
        currentAppointment: { id: 81, appointmentCode: 'ICD-81', reason: 'Đau họng', status: 'InConsultation', startTime: '08:00:00', endTime: '08:30:00' }
    } as never));
});
afterEach(() => { cleanup(); vi.useRealTimers(); });

const mount = async () => {
    render(<ConfigProvider theme={{ token: { motion: false } }}><MemoryRouter initialEntries={['/doctor/appointments/81']}>
        <DialogProvider><Routes><Route path="/doctor/appointments/:id" element={<DoctorExaminationWorkspace />} /></Routes></DialogProvider>
    </MemoryRouter></ConfigProvider>);
    return screen.findByRole('combobox', { name: 'Mã ICD-10' });
};

describe('ICD-10 lookup', () => {
    it('encodes the search and limit in the API request', async () => {
        await icd10Api.search('viêm họng', 7);
        expect(axiosClient.get).toHaveBeenCalledExactlyOnceWith('/icd10', { params: { search: 'viêm họng', limit: 7 } });
    });

    it('waits 300ms after the last keystroke before calling the API', async () => {
        vi.useFakeTimers();
        const { rerender } = render(<Icd10CodeInput value="" onChange={vi.fn()} onSelect={vi.fn()} />);
        fireEvent.change(screen.getByRole('combobox'), { target: { value: 'J0' } });
        rerender(<Icd10CodeInput value="J0" onChange={vi.fn()} onSelect={vi.fn()} />);
        await act(async () => { await vi.advanceTimersByTimeAsync(200); });
        fireEvent.change(screen.getByRole('combobox'), { target: { value: 'J02' } });
        rerender(<Icd10CodeInput value="J02" onChange={vi.fn()} onSelect={vi.fn()} />);
        await act(async () => { await vi.advanceTimersByTimeAsync(299); });
        expect(axiosClient.get).not.toHaveBeenCalled();
        await act(async () => { await vi.advanceTimersByTimeAsync(1); });
        expect(axiosClient.get).toHaveBeenCalledExactlyOnceWith('/icd10', { params: { search: 'J02', limit: 20 } });
        expect(screen.getByText('J02.9 – Viêm họng cấp, không đặc hiệu')).toBeInTheDocument();
    });

    it('selects the code and fills an empty diagnosis before saving', async () => {
        const input = await mount();
        fireEvent.change(input, { target: { value: 'J02' } });
        fireEvent.click(await screen.findByText('J02.9 – Viêm họng cấp, không đặc hiệu'));
        expect(input).toHaveValue('J02.9');
        expect(screen.getByPlaceholderText('Ví dụ: Viêm họng cấp / Tăng huyết áp độ 1...')).toHaveValue(code.nameVi);
        vi.mocked(doctorApi.saveEncounter).mockResolvedValue(ok({} as never));
        fireEvent.click(screen.getByRole('button', { name: /Lưu nháp diễn tiến/ }));
        await waitFor(() => expect(doctorApi.saveEncounter).toHaveBeenCalledWith(81, expect.objectContaining({ diagnosis: code.nameVi, diagnosisCode: 'J02.9' })));
    });

    it('keeps a diagnosis already entered by the doctor', async () => {
        const input = await mount();
        const diagnosis = screen.getByPlaceholderText('Ví dụ: Viêm họng cấp / Tăng huyết áp độ 1...');
        fireEvent.change(diagnosis, { target: { value: 'Chẩn đoán đã nhập' } });
        fireEvent.change(input, { target: { value: 'J02' } });
        fireEvent.click(await screen.findByText('J02.9 – Viêm họng cấp, không đặc hiệu'));
        expect(diagnosis).toHaveValue('Chẩn đoán đã nhập');
    });

    it('shows lookup errors, allows manual input and supports retry', async () => {
        vi.mocked(axiosClient.get).mockRejectedValueOnce(new Error('Network Error'));
        const input = await mount();
        fireEvent.change(input, { target: { value: 'J02' } });
        expect(await screen.findByText('Không tải được danh mục ICD-10')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        expect(await screen.findByText('J02.9 – Viêm họng cấp, không đặc hiệu')).toBeInTheDocument();
        fireEvent.change(input, { target: { value: 'ZZZ.9' } });
        expect(input).toHaveValue('ZZZ.9');
    });

    it('shows the empty state for a successful search with no matches', async () => {
        vi.mocked(axiosClient.get).mockResolvedValue(ok([]));
        const input = await mount();
        fireEvent.change(input, { target: { value: 'ZZZ' } });
        await waitFor(() => expect(axiosClient.get).toHaveBeenCalled());
        expect(await screen.findByText('Không tìm thấy mã phù hợp')).toBeInTheDocument();
    });

    it('shows INVALID_ICD10 from the save response', async () => {
        vi.mocked(doctorApi.saveEncounter).mockRejectedValue({ status: 400, message: 'Mã ICD-10 không có trong danh mục.', errorCode: 'INVALID_ICD10' });
        const input = await mount();
        fireEvent.change(input, { target: { value: 'ZZZ.9' } });
        fireEvent.click(screen.getByRole('button', { name: /Lưu nháp diễn tiến/ }));
        expect(await screen.findByText('Mã ICD-10 không có trong danh mục.')).toBeInTheDocument();
    });
});
