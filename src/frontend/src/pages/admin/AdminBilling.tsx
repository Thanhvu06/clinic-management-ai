import React, { useState, useEffect, useRef } from 'react';
import { Alert, Button, Card, DatePicker, Form, InputNumber, Modal, Tabs, Tag, Typography } from 'antd';
import dayjs from 'dayjs';
import { DollarSign, Edit, RefreshCw, CheckCircle, Clock, XCircle } from 'lucide-react';
import { billingApi } from '../../api/billingApi';
import { diagnosticApi } from '../../api/diagnosticApi';
import type { RevenueReportDto, SpecialtyFeeDto, DiagnosticServiceDto } from '../../types';
import { useDialog } from '../../contexts/DialogContext';
import { PageHeader, DataTable, StatCard, StatusBadge, EmptyState, LoadingState, InlineError } from '../../components/common';
import { toLocalDateString } from '../../utils/formatters';
import styles from './AdminBilling.module.css';

const recentRange = () => {
    const today = new Date();
    const from = new Date(today); from.setDate(from.getDate() - 30);
    return { from: toLocalDateString(from), to: toLocalDateString(today) };
};
const formatCurrency = (value: number) => new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(value);
const formatDateOnly = (value: string) => {
    const [year, month, day] = value.split('-').map(Number);
    const localDate = new Date(year, month - 1, day);
    return Number.isNaN(localDate.getTime()) ? value : new Intl.DateTimeFormat('vi-VN', { year: 'numeric', month: '2-digit', day: '2-digit' }).format(localDate);
};
const errorMessage = (error: unknown, fallback: string) => (error as { message?: string })?.message || fallback;

export const AdminBilling: React.FC = () => {
    const { showAlert } = useDialog();
    const [activeTab, setActiveTab] = useState('revenue');
    const [initialRange] = useState(recentRange);
    const [fromDate, setFromDate] = useState(initialRange.from);
    const [toDate, setToDate] = useState(initialRange.to);
    const [rangeError, setRangeError] = useState('');
    const [revenueReport, setRevenueReport] = useState<RevenueReportDto | null>(null);
    const [revLoading, setRevLoading] = useState(true);
    const [revError, setRevError] = useState('');
    const [specialtyFees, setSpecialtyFees] = useState<SpecialtyFeeDto[]>([]);
    const [feesLoading, setFeesLoading] = useState(true);
    const [feesError, setFeesError] = useState('');
    const [diagnosticServices, setDiagnosticServices] = useState<DiagnosticServiceDto[]>([]);
    const [diagLoading, setDiagLoading] = useState(true);
    const [diagError, setDiagError] = useState('');
    const [selectedSpecialty, setSelectedSpecialty] = useState<SpecialtyFeeDto | null>(null);
    const [editModalOpen, setEditModalOpen] = useState(false);
    const [feeInput, setFeeInput] = useState<number | null>(null);
    const [submittingFee, setSubmittingFee] = useState(false);
    const [selectedDiag, setSelectedDiag] = useState<DiagnosticServiceDto | null>(null);
    const [editDiagModalOpen, setEditDiagModalOpen] = useState(false);
    const [diagPriceInput, setDiagPriceInput] = useState<number | null>(null);
    const [submittingDiagPrice, setSubmittingDiagPrice] = useState(false);
    const feePending = useRef(false);
    const diagPending = useRef(false);
    const revenueRequest = useRef(0);
    const feesRequest = useRef(0);
    const diagRequest = useRef(0);

    const fetchRevenueReport = async (from: string, to: string) => {
        if (from && to && from > to) { setRangeError('Từ ngày phải trước hoặc bằng đến ngày.'); return; }
        const request = ++revenueRequest.current;
        setRangeError(''); setRevError(''); setRevLoading(true);
        try {
            const res = await billingApi.admin.getRevenueReport(from || undefined, to || undefined);
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải báo cáo doanh thu.');
            if (request === revenueRequest.current) setRevenueReport(res.data);
        } catch (error) { if (request === revenueRequest.current) setRevError(errorMessage(error, 'Không thể tải báo cáo doanh thu.')); }
        finally { if (request === revenueRequest.current) setRevLoading(false); }
    };
    const fetchSpecialtyFees = async () => {
        const request = ++feesRequest.current;
        setFeesLoading(true); setFeesError('');
        try {
            const res = await billingApi.admin.getSpecialtyFees();
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải danh sách biểu phí chuyên khoa.');
            if (request === feesRequest.current) setSpecialtyFees(res.data);
        } catch (error) { if (request === feesRequest.current) setFeesError(errorMessage(error, 'Không thể tải danh sách biểu phí chuyên khoa.')); }
        finally { if (request === feesRequest.current) setFeesLoading(false); }
    };
    const fetchDiagnosticServices = async () => {
        const request = ++diagRequest.current;
        setDiagLoading(true); setDiagError('');
        try {
            const res = await diagnosticApi.getDiagnosticPricing();
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể tải bảng giá cận lâm sàng.');
            if (request === diagRequest.current) setDiagnosticServices(res.data);
        } catch (error) { if (request === diagRequest.current) setDiagError(errorMessage(error, 'Không thể tải bảng giá cận lâm sàng.')); }
        finally { if (request === diagRequest.current) setDiagLoading(false); }
    };
    useEffect(() => {
        const requests = [revenueRequest, feesRequest, diagRequest];
        if (activeTab === 'revenue') void fetchRevenueReport(fromDate, toDate);
        else if (activeTab === 'fees') void fetchSpecialtyFees();
        else if (activeTab === 'diagnostics') void fetchDiagnosticServices();
        return () => { requests.forEach(request => { ++request.current; }); };
    }, [activeTab]);
    const handleSaveFee = async (event: React.FormEvent) => {
        event.preventDefault(); if (!selectedSpecialty || feePending.current) return;
        if (feeInput === null || !Number.isFinite(feeInput) || feeInput < 0) { showAlert('Mức phí khám phải là số lớn hơn hoặc bằng 0.', 'Dữ liệu không hợp lệ', 'warning'); return; }
        feePending.current = true; setSubmittingFee(true);
        try {
            const res = await billingApi.admin.updateSpecialtyFee(selectedSpecialty.id, { consultationFee: feeInput });
            if (!res.success || !res.data) throw new Error(res.message || 'Không thể cập nhật biểu phí.');
            showAlert('Cập nhật phí khám chuyên khoa "' + selectedSpecialty.name + '" thành ' + formatCurrency(feeInput) + ' thành công!', 'Thành công', 'success');
            setEditModalOpen(false); void fetchSpecialtyFees();
        } catch (error) { showAlert(errorMessage(error, 'Không thể cập nhật biểu phí.'), 'Lỗi cập nhật', 'error'); }
        finally { feePending.current = false; setSubmittingFee(false); }
    };
    const handleSaveDiagPrice = async (event: React.FormEvent) => {
        event.preventDefault(); if (!selectedDiag || diagPending.current) return;
        if (diagPriceInput === null || !Number.isFinite(diagPriceInput) || diagPriceInput < 0) { showAlert('Đơn giá dịch vụ phải là số lớn hơn hoặc bằng 0.', 'Dữ liệu không hợp lệ', 'warning'); return; }
        diagPending.current = true; setSubmittingDiagPrice(true);
        try {
            const res = await diagnosticApi.updateDiagnosticPrice(selectedDiag.id, diagPriceInput);
            if (!res.success) throw new Error(res.message || 'Không thể cập nhật đơn giá.');
            showAlert('Cập nhật giá dịch vụ "' + selectedDiag.name + '" thành ' + formatCurrency(diagPriceInput) + ' thành công!', 'Thành công', 'success');
            setEditDiagModalOpen(false); void fetchDiagnosticServices();
        } catch (error) { showAlert(errorMessage(error, 'Không thể cập nhật đơn giá.'), 'Lỗi cập nhật', 'error'); }
        finally { diagPending.current = false; setSubmittingDiagPrice(false); }
    };
    const modalProps = (busy: boolean) => ({ footer: null, maskClosable: !busy, closable: !busy, keyboard: !busy, destroyOnHidden: true });
    const revenueContent = <div className={styles.tabContent}>
        <Card><Form className={styles.filterForm} onSubmitCapture={event => { event.preventDefault(); void fetchRevenueReport(fromDate, toDate); }}>
            <strong>Khoảng thời gian:</strong>
            <Form.Item htmlFor="revenueFrom"><DatePicker id="revenueFrom" aria-label="Từ ngày" title="Từ ngày" format="YYYY-MM-DD" value={fromDate ? dayjs(fromDate) : null} onChange={value => { setFromDate(value?.format('YYYY-MM-DD') || ''); setRangeError(''); }} /></Form.Item>
            <span>-</span>
            <Form.Item htmlFor="revenueTo"><DatePicker id="revenueTo" aria-label="Đến ngày" title="Đến ngày" format="YYYY-MM-DD" value={toDate ? dayjs(toDate) : null} onChange={value => { setToDate(value?.format('YYYY-MM-DD') || ''); setRangeError(''); }} /></Form.Item>
            <Button htmlType="submit">Xem báo cáo</Button>
            <Button icon={<RefreshCw size={14} />} onClick={() => { const range = recentRange(); setFromDate(range.from); setToDate(range.to); void fetchRevenueReport(range.from, range.to); }}>30 ngày gần nhất</Button>
        </Form></Card>
        {rangeError && <Alert type="error" title={rangeError} showIcon />}
        {revLoading ? <LoadingState message="Đang tải báo cáo doanh thu..." /> : revError ? <InlineError message={revError} onRetry={() => { void fetchRevenueReport(fromDate, toDate); }} /> : !revenueReport ? <EmptyState title="Không có dữ liệu báo cáo" /> : <>
            <div className={styles.kpiGrid}>
                <StatCard title="Tổng thực thu" value={formatCurrency(revenueReport.totalRevenue)} icon={<DollarSign size={24} />} color="success" />
                <StatCard title="Giao dịch thành công" value={revenueReport.totalSucceededTransactions + ' lượt'} icon={<CheckCircle size={24} />} color="info" />
                <StatCard title="Chờ thanh toán" value={revenueReport.statusBreakdown.unpaidCount + ' HĐ'} subtitle={'(' + formatCurrency(revenueReport.statusBreakdown.unpaidAmount) + ')'} icon={<Clock size={24} />} color="warning" />
                <StatCard title="Hóa đơn đã hủy" value={revenueReport.statusBreakdown.cancelledCount + ' HĐ'} subtitle={'(' + formatCurrency(revenueReport.statusBreakdown.cancelledAmount) + ')'} icon={<XCircle size={24} />} color="danger" />
            </div>
            <Card title="Bảng chi tiết doanh thu theo từng ngày">
                {revenueReport.dailyBreakdown.length === 0 ? <EmptyState title="Không có giao dịch nào phát sinh trong khoảng thời gian đã chọn." /> : <DataTable data={revenueReport.dailyBreakdown} keyExtractor={row => row.date} columns={[
                    { header: 'Ngày giao dịch', accessor: row => formatDateOnly(row.date) },
                    { header: 'Số GD thành công', accessor: 'succeededPaymentsCount', align: 'center' },
                    { header: 'Số HĐ đã thu', accessor: 'paidInvoicesCount', align: 'center' },
                    { header: 'Doanh thu trong ngày', accessor: row => formatCurrency(row.revenue), align: 'right' },
                ]} />}
            </Card>
        </>}
    </div>;
    const feesContent = <Card>
        <div className={styles.sectionHeading}><div><h3>Danh mục biểu phí khám chuyên khoa</h3><Typography.Text type="secondary">Mức phí này sẽ được tự động snapshot vào hóa đơn khi lượt khám hoàn thành.</Typography.Text></div><Button icon={<RefreshCw size={14} />} loading={feesLoading} onClick={() => { void fetchSpecialtyFees(); }}>Làm mới</Button></div>
        {feesLoading ? <LoadingState message="Đang tải biểu phí chuyên khoa..." /> : feesError ? <InlineError message={feesError} onRetry={fetchSpecialtyFees} /> : specialtyFees.length === 0 ? <EmptyState title="Chưa có chuyên khoa nào." /> : <DataTable data={specialtyFees} keyExtractor={spec => spec.id} columns={[
            { header: 'Mã chuyên khoa', accessor: 'specialtyCode' }, { header: 'Tên chuyên khoa', accessor: 'name' },
            { header: 'Phí khám hiện tại', align: 'right', accessor: spec => formatCurrency(spec.consultationFee) },
            { header: 'Trạng thái', align: 'center', accessor: spec => <StatusBadge status={spec.isActive ? 'Approved' : 'Pending'} label={spec.isActive ? 'Đang hoạt động' : 'Tạm ngưng'} /> },
            { header: 'Cập nhật', align: 'right', accessor: spec => <Button icon={<Edit size={13} />} onClick={() => { setSelectedSpecialty(spec); setFeeInput(spec.consultationFee); setEditModalOpen(true); }}>Sửa mức phí</Button> },
        ]} />}
    </Card>;
    const diagnosticsContent = <Card>
        <div className={styles.sectionHeading}><div><h3>Bảng giá Dịch vụ Cận lâm sàng ({diagnosticServices.length} dịch vụ)</h3><Typography.Text type="secondary">Bảng giá áp dụng khi bác sĩ chỉ định và tính hóa đơn viện phí thực tế</Typography.Text></div><Button icon={<RefreshCw size={14} />} loading={diagLoading} onClick={() => { void fetchDiagnosticServices(); }}>Làm mới</Button></div>
        {diagLoading ? <LoadingState message="Đang tải bảng giá cận lâm sàng..." /> : diagError ? <InlineError message={diagError} onRetry={fetchDiagnosticServices} /> : diagnosticServices.length === 0 ? <EmptyState title="Chưa có dịch vụ cận lâm sàng nào trong hệ thống." /> : <DataTable data={diagnosticServices} keyExtractor={svc => svc.id} columns={[
            { header: 'Mã dịch vụ', accessor: svc => <Tag>{svc.code}</Tag> }, { header: 'Tên kỹ thuật / dịch vụ', accessor: 'name' },
            { header: 'Loại dịch vụ', accessor: svc => <Tag color="processing">{svc.category === 'Laboratory' ? 'Xét nghiệm' : svc.category === 'Ultrasound' ? 'Siêu âm' : svc.category === 'Imaging' ? 'Chẩn đoán hình ảnh' : 'Khác'}</Tag> },
            { header: 'Mô tả quy trình', accessor: svc => svc.preparationInstructions || '-' },
            { header: 'Đơn giá (VNĐ)', align: 'right', accessor: svc => formatCurrency(svc.price ?? 0) },
            { header: 'Thao tác', align: 'right', accessor: svc => <Button icon={<Edit size={13} />} onClick={() => { setSelectedDiag(svc); setDiagPriceInput(svc.price ?? 0); setEditDiagModalOpen(true); }}>Sửa giá</Button> },
        ]} />}
    </Card>;
    return <div className={styles.page}>
        <PageHeader title="Doanh thu & Biểu phí phòng khám" subtitle="Theo dõi thực thu tài chính, cấu hình phí khám chuyên khoa và bảng giá cận lâm sàng" />
        <Tabs activeKey={activeTab} onChange={setActiveTab} destroyOnHidden items={[
            { key: 'revenue', label: 'Báo cáo doanh thu', children: revenueContent },
            { key: 'fees', label: 'Phí khám chuyên khoa', children: feesContent },
            { key: 'diagnostics', label: 'Bảng giá Cận lâm sàng', children: diagnosticsContent },
        ]} />
        <Modal open={editModalOpen} title={'Cập nhật phí khám: ' + (selectedSpecialty?.name || '')} {...modalProps(submittingFee)} onCancel={() => { if (!feePending.current) setEditModalOpen(false); }}>
            <Form layout="vertical" onSubmitCapture={handleSaveFee}>
                <div className={styles.currentPrice}><div><strong>Mã chuyên khoa:</strong> {selectedSpecialty?.specialtyCode}</div><div><strong>Mức phí hiện tại:</strong> {formatCurrency(selectedSpecialty?.consultationFee ?? 0)}</div></div>
                <Form.Item label="Mức phí khám mới (VNĐ) *" htmlFor="newSpecialtyFee" extra="Mức phí áp dụng cho tất cả các ca khám mới hoàn thành thuộc chuyên khoa này."><InputNumber id="newSpecialtyFee" required min={0} step={1000} value={feeInput} onChange={setFeeInput} disabled={submittingFee} className={styles.priceInput} /></Form.Item>
                <div className={styles.formActions}><Button disabled={submittingFee} onClick={() => setEditModalOpen(false)}>Hủy bỏ</Button><Button type="primary" htmlType="submit" disabled={submittingFee}>{submittingFee ? 'Đang lưu...' : 'Lưu mức phí'}</Button></div>
            </Form>
        </Modal>
        <Modal open={editDiagModalOpen} title={'Cập nhật giá dịch vụ: ' + (selectedDiag?.name || '')} {...modalProps(submittingDiagPrice)} onCancel={() => { if (!diagPending.current) setEditDiagModalOpen(false); }}>
            <Form layout="vertical" onSubmitCapture={handleSaveDiagPrice}>
                <div className={styles.currentPrice}><div><strong>Mã dịch vụ:</strong> {selectedDiag?.code}</div><div><strong>Đơn giá hiện tại:</strong> {formatCurrency(selectedDiag?.price ?? 0)}</div></div>
                <Form.Item label="Đơn giá mới (VNĐ) *" htmlFor="newDiagnosticPrice" extra="Mức giá áp dụng cho tất cả chỉ định cận lâm sàng mới và tính hóa đơn viện phí."><InputNumber id="newDiagnosticPrice" required min={0} step={1000} value={diagPriceInput} onChange={setDiagPriceInput} disabled={submittingDiagPrice} className={styles.priceInput} /></Form.Item>
                <div className={styles.formActions}><Button disabled={submittingDiagPrice} onClick={() => setEditDiagModalOpen(false)}>Hủy bỏ</Button><Button type="primary" htmlType="submit" disabled={submittingDiagPrice}>{submittingDiagPrice ? 'Đang lưu...' : 'Lưu đơn giá'}</Button></div>
            </Form>
        </Modal>
    </div>;
};
