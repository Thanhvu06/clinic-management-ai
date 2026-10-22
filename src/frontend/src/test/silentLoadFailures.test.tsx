import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { billingApi } from '../api/billingApi';
import { mpiApi } from '../api/mpiApi';
import { organizationApi } from '../api/organizationApi';
import { MpiPatientSearchModal } from '../pages/reception/MpiPatientSearchModal';
import { ReceptionBilling } from '../pages/reception/ReceptionBilling';
import { WalkInPatientRegistration } from '../pages/reception/WalkInPatientRegistration';
import { ReceptionPackageRegistrations } from '../pages/reception/ReceptionPackageRegistrations';
import { SearchResults } from '../pages/public/SearchResults';
import { ReceptionWorkspace } from '../pages/reception/ReceptionWorkspace';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));
vi.mock('../api/mpiApi', () => ({ mpiApi: { searchPatients: vi.fn(), getPatientById: vi.fn() } }));
vi.mock('../api/organizationApi', () => ({ organizationApi: { getFacilities: vi.fn(), getMyFacilities: vi.fn(), getDepartments: vi.fn(), getRooms: vi.fn() } }));
vi.mock('../api/billingApi', () => ({ billingApi: { reception: { getTodayKpi: vi.fn(), getInvoices: vi.fn(), getUnbilledVisits: vi.fn() } } }));
vi.mock('../contexts/DialogContext', () => ({ useDialog: () => ({ showAlert: vi.fn(), showConfirm: vi.fn(), showToast: vi.fn() }) }));
const copilot = vi.hoisted(() => ({ setSelection: vi.fn() }));
vi.mock('../components/copilot/copilotResourceContext', () => ({ useCopilotResource: () => copilot }));

const ok = (data: any) => ({ success: true, message: '', data });
const facilities = [{ id: 1, code: 'LOAD', name: 'Cơ sở thử tải', isActive: true }];
const patient = { id: 51, fullName: 'Bệnh nhân thử tải', medicalRecordNumber: 'LOAD-51', allergies: [], emergencyContacts: [] };
const kpi = { todayUnpaidInvoices: 2, todayPaidInvoices: 3, todayCancelledInvoices: 1, todayRevenue: 500000 };
const emptyPage = { items: [], page: 1, totalItems: 0, totalPages: 1 };
beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(axiosClient.get).mockResolvedValue(ok([]));
    vi.mocked(organizationApi.getFacilities).mockResolvedValue(ok(facilities));
    vi.mocked(organizationApi.getMyFacilities).mockResolvedValue(ok(facilities));
    vi.mocked(organizationApi.getDepartments).mockResolvedValue(ok([]));
    vi.mocked(organizationApi.getRooms).mockResolvedValue(ok([]));
    vi.mocked(mpiApi.getPatientById).mockResolvedValue(ok(patient));
    vi.mocked(mpiApi.searchPatients).mockResolvedValue(ok(emptyPage));
    vi.mocked(billingApi.reception.getTodayKpi).mockResolvedValue(ok(kpi));
    vi.mocked(billingApi.reception.getInvoices).mockResolvedValue(ok(emptyPage));
    vi.mocked(billingApi.reception.getUnbilledVisits).mockResolvedValue(ok(emptyPage));
});
afterEach(cleanup);

describe('Retryable load failures', () => {
    it.each(['rejected', 'unsuccessful'])('MPI %s lookup never encourages a duplicate and retries the original parameters', async failure => {
        const api = vi.mocked(mpiApi.searchPatients);
        if (failure === 'rejected') api.mockRejectedValueOnce(new Error('offline'));
        else api.mockResolvedValueOnce({ success: false, message: 'failed' });
        render(<MpiPatientSearchModal isOpen onClose={vi.fn()} onSelectPatient={vi.fn()} />);
        const input = screen.getByPlaceholderText(/Nhập từ khóa tìm kiếm/);
        fireEvent.change(input, { target: { value: '  Nguyễn An  ' } });
        fireEvent.click(screen.getByRole('button', { name: 'Tra cứu' }));
        expect(await screen.findByText('Không thể tra cứu hồ sơ. Vui lòng thử lại trước khi tạo hồ sơ mới.')).toBeInTheDocument();
        expect(screen.queryByText('Không tìm thấy bệnh nhân nào')).not.toBeInTheDocument();
        fireEvent.change(input, { target: { value: 'Từ khóa khác' } });
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByText('Không tìm thấy bệnh nhân nào');
        expect(screen.getByText(/Không có hồ sơ nào trùng khớp với từ khóa "Nguyễn An"/)).toBeInTheDocument();
        expect(api).toHaveBeenNthCalledWith(2, { searchTerm: 'Nguyễn An', page: 1, pageSize: 10 });
        fireEvent.click(screen.getByRole('button', { name: 'Tra cứu' }));
        await waitFor(() => expect(api).toHaveBeenNthCalledWith(3, { searchTerm: 'Từ khóa khác', page: 1, pageSize: 10 }));
        expect(screen.queryByText('Không thể tra cứu hồ sơ. Vui lòng thử lại trước khi tạo hồ sơ mới.')).not.toBeInTheDocument();
    });

    it('unbilled failure replaces the empty queue and retries only that queue', async () => {
        vi.mocked(billingApi.reception.getUnbilledVisits).mockRejectedValueOnce(new Error('offline'));
        render(<ReceptionBilling />);
        fireEvent.click(screen.getByRole('radio', { name: /Hàng đợi chờ lập hóa đơn/ }));
        await screen.findByText('Không thể tải hàng đợi ca khám. Vui lòng thử lại.');
        expect(screen.queryByText('Hiện không có lượt khám nào chờ lập hóa đơn.')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByText('Hiện không có lượt khám nào chờ lập hóa đơn.');
        expect(billingApi.reception.getUnbilledVisits).toHaveBeenNthCalledWith(2, undefined, 1, 10);
        expect(billingApi.reception.getTodayKpi).toHaveBeenCalledTimes(1);
        expect(billingApi.reception.getInvoices).toHaveBeenCalledTimes(1);
    });

    it('billing KPI failure shows four dashes without blocking the queue and can recover', async () => {
        vi.mocked(billingApi.reception.getTodayKpi).mockResolvedValueOnce({ success: false, message: 'failed' });
        render(<ReceptionBilling />);
        await screen.findByText('Không tải được số liệu');
        expect(screen.getAllByText('—')).toHaveLength(4);
        fireEvent.click(screen.getByRole('radio', { name: /Hàng đợi chờ lập hóa đơn/ }));
        await screen.findByText('Hiện không có lượt khám nào chờ lập hóa đơn.');
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await waitFor(() => expect(screen.queryByText('Không tải được số liệu')).not.toBeInTheDocument());
        expect(screen.queryAllByText('—')).toHaveLength(0);
        expect(billingApi.reception.getUnbilledVisits).toHaveBeenCalledTimes(1);
    });

    async function openVisitSetup() {
        render(<MemoryRouter initialEntries={['/reception/register?existingPatientId=51']}><WalkInPatientRegistration /></MemoryRouter>);
        await screen.findByText(patient.fullName);
        fireEvent.click(screen.getByText(/Tiếp tục: Chuẩn bị lượt khám/));
    }

    it('walk-in department error stays below the field and retry loads only departments', async () => {
        const api = vi.mocked(organizationApi.getDepartments);
        api.mockRejectedValueOnce(new Error('offline'));
        await openVisitSetup();
        const message = await screen.findByText('Không thể tải khoa phòng tiếp nhận. Vui lòng thử lại.');
        const field = message.closest('[class*="formField"]')! as HTMLElement;
        expect(within(field).getByText('Khoa phòng tiếp nhận *')).toBeInTheDocument();
        expect(within(field).getByRole('combobox').closest('.ant-select')).toHaveClass('ant-select-status-error');
        let resolve!: (value: any) => void;
        api.mockReturnValueOnce(new Promise(value => { resolve = value; }));
        const otherCalls = [organizationApi.getFacilities, organizationApi.getRooms, mpiApi.getPatientById, axiosClient.get].map(mock => vi.mocked(mock).mock.calls.length);
        fireEvent.click(within(field).getByRole('button', { name: 'Thử lại' }));
        await waitFor(() => expect(api).toHaveBeenCalledTimes(2));
        expect(within(field).getByRole('combobox')).toBeDisabled();
        expect(within(field).getByText('Đang tải...')).toBeInTheDocument();
        expect([organizationApi.getFacilities, organizationApi.getRooms, mpiApi.getPatientById, axiosClient.get].map(mock => vi.mocked(mock).mock.calls.length)).toEqual(otherCalls);
        await act(async () => resolve(ok([])));
        await waitFor(() => expect(screen.queryByText('Không thể tải khoa phòng tiếp nhận. Vui lòng thử lại.')).not.toBeInTheDocument());
        expect(api).toHaveBeenNthCalledWith(2, 1);
    });

    it.each(['facilities', 'doctors', 'rooms'])('walk-in %s failure offers a source-specific retry', async source => {
        if (source === 'rooms') vi.mocked(organizationApi.getDepartments).mockResolvedValue(ok([{ id: 7, name: 'Khoa thử tải', code: 'LOAD' }]));
        const api = source === 'facilities' ? organizationApi.getFacilities : source === 'doctors' ? axiosClient.get : organizationApi.getRooms;
        vi.mocked(api).mockRejectedValueOnce(new Error('offline'));
        await openVisitSetup();
        const message = source === 'facilities' ? 'Không thể tải cơ sở khám chữa bệnh. Vui lòng thử lại.' : source === 'doctors' ? 'Không thể tải bác sĩ phụ trách. Vui lòng thử lại.' : 'Không thể tải phòng khám. Vui lòng thử lại.';
        await screen.findByText(message);
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await waitFor(() => expect(vi.mocked(api)).toHaveBeenCalledTimes(2));
        await waitFor(() => expect(screen.queryByText(message)).not.toBeInTheDocument());
    });

    it('walk-in patient prefill failure retries that patient without reloading lookup sources', async () => {
        vi.mocked(mpiApi.getPatientById).mockRejectedValueOnce(new Error('offline'));
        render(<MemoryRouter initialEntries={['/reception/register?existingPatientId=51']}><WalkInPatientRegistration /></MemoryRouter>);
        await screen.findByText('Không thể nạp hồ sơ bệnh nhân đã chọn. Vui lòng thử lại.');
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByText(patient.fullName);
        expect(mpiApi.getPatientById).toHaveBeenNthCalledWith(2, 51);
        expect(organizationApi.getFacilities).toHaveBeenCalledTimes(1);
        expect(organizationApi.getDepartments).toHaveBeenCalledTimes(1);
        expect(axiosClient.get).toHaveBeenCalledTimes(1);
    });

    it('package registration failure replaces the empty state and retries the same list', async () => {
        vi.mocked(axiosClient.get).mockRejectedValueOnce(new Error('offline')).mockResolvedValue(ok(emptyPage));
        render(<ReceptionPackageRegistrations />);
        await screen.findByText('Không thể tải danh sách đăng ký gói khám. Vui lòng thử lại.');
        expect(screen.queryByText('Không có dữ liệu đăng ký gói khám nào phù hợp.')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByText('Không có dữ liệu đăng ký gói khám nào phù hợp.');
        expect(vi.mocked(axiosClient.get).mock.calls[1]).toEqual(vi.mocked(axiosClient.get).mock.calls[0]);
    });

    it.each(['/specialties', '/doctors', '/health-packages'])('search index failure in %s replaces empty results and retries', async failedUrl => {
        let fail = true;
        vi.mocked(axiosClient.get).mockImplementation(async url => {
            if (url === failedUrl && fail) throw new Error('offline');
            return ok([]);
        });
        render(<MemoryRouter initialEntries={['/search?q=khám']}><SearchResults /></MemoryRouter>);
        await screen.findByText('Không thể tải dữ liệu tìm kiếm. Vui lòng thử lại.');
        expect(screen.queryByText('Không tìm thấy kết quả phù hợp')).not.toBeInTheDocument();
        fail = false;
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await screen.findByText('Không tìm thấy kết quả phù hợp');
        expect(axiosClient.get).toHaveBeenCalledTimes(6);
        expect(screen.getByRole('textbox', { name: 'Từ khóa tìm kiếm' })).toHaveValue('khám');
    });

    it('workspace statistics failure leaves the worklist usable and retries only statistics', async () => {
        let fail = true;
        vi.mocked(axiosClient.get).mockImplementation(async url => {
            if (url.startsWith('/reception/stats')) {
                if (fail) throw new Error('offline');
                return ok({ appointmentsToday: 5, pendingAppointmentsToday: 1, confirmedAppointmentsToday: 2, completedAppointmentsToday: 2 });
            }
            return ok(emptyPage);
        });
        render(<MemoryRouter><ReceptionWorkspace /></MemoryRouter>);
        await screen.findByText('Không tải được số liệu');
        expect(screen.getAllByText('—')).toHaveLength(4);
        await screen.findByText('Không có lịch hẹn nào trong mục này.');
        const worklistCalls = vi.mocked(axiosClient.get).mock.calls.filter(([url]) => url.startsWith('/reception/appointments')).length;
        fail = false;
        fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));
        await waitFor(() => expect(screen.queryByText('Không tải được số liệu')).not.toBeInTheDocument());
        expect(vi.mocked(axiosClient.get).mock.calls.filter(([url]) => url.startsWith('/reception/appointments'))).toHaveLength(worklistCalls);
    });
});
