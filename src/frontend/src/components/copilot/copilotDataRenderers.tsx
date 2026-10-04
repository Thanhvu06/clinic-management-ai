import React from 'react';
import type { AiCopilotCard } from '../../api/aiCopilotApi';
import styles from './UnifiedCopilotPanel.module.css';

type DataRecord = Record<string, unknown>;

const asRecord = (value: unknown): DataRecord | null =>
    value && typeof value === 'object' && !Array.isArray(value) ? value as DataRecord : null;

const asRecords = (value: unknown): DataRecord[] =>
    Array.isArray(value) ? value.filter(item => Boolean(asRecord(item))).map(item => item as DataRecord) : [];

const rowsFrom = (value: unknown): DataRecord[] => {
    const record = asRecord(value);
    return record && Array.isArray(record.items) ? asRecords(record.items) : asRecords(value);
};

const text = (value: unknown): string | null =>
    typeof value === 'string' && value.trim() ? value.trim() : null;

const numberValue = (value: unknown): number | null =>
    typeof value === 'number' && Number.isFinite(value) ? value : null;

const formatDateTime = (value: unknown): string | null => {
    const raw = text(value);
    if (!raw) return null;
    const dateOnly = /^(\d{4})-(\d{2})-(\d{2})$/.exec(raw);
    if (dateOnly) return `${dateOnly[3]}/${dateOnly[2]}/${dateOnly[1]}`;
    const date = new Date(raw);
    return Number.isNaN(date.valueOf()) ? raw : date.toLocaleString('vi-VN');
};

const formatMoney = (value: unknown, currency = 'VND'): string | null => {
    const amount = numberValue(value);
    if (amount === null) return null;
    return `${amount.toLocaleString('vi-VN')} ${currency === 'VND' ? '₫' : currency}`;
};

const VIETNAM_TIME_ZONE = 'Asia/Ho_Chi_Minh';

// Server UTC timestamps may arrive without an offset; read them as UTC.
const parseUtc = (value: unknown): Date | null => {
    const raw = text(value);
    if (!raw) return null;
    const date = new Date(/(?:z|[+-]\d{2}:?\d{2})$/i.test(raw) ? raw : `${raw}Z`);
    return Number.isNaN(date.valueOf()) ? null : date;
};

const formatVietnamDateTime = (value: unknown): string | null =>
    parseUtc(value)?.toLocaleString('vi-VN', { timeZone: VIETNAM_TIME_ZONE, hour: '2-digit', minute: '2-digit', hour12: false, day: '2-digit', month: '2-digit', year: 'numeric' }) ?? null;

const formatVietnamTime = (value: unknown): string | null =>
    parseUtc(value)?.toLocaleTimeString('vi-VN', { timeZone: VIETNAM_TIME_ZONE, hour: '2-digit', minute: '2-digit', hour12: false }) ?? null;

// "08:30:00" -> "08:30"
const formatClock = (value: unknown): string | null => {
    const raw = text(value);
    const match = raw ? /^(\d{1,2}):(\d{2})/.exec(raw) : null;
    return match ? `${match[1].padStart(2, '0')}:${match[2]}` : null;
};

const revenuePeriodLabels: Record<string, string> = { today: 'Hôm nay', this_month: 'Tháng này' };

const valueLabel = (value: unknown): string | null => {
    if (typeof value === 'number' && Number.isFinite(value)) return String(value);
    return text(value);
};

const statusLabels: Record<string, string> = {
    Pending: 'Chờ xử lý', Confirmed: 'Đã xác nhận', PendingReschedule: 'Chờ đổi lịch', PendingCancellation: 'Chờ hủy',
    Cancelled: 'Đã hủy', Completed: 'Đã hoàn tất', NoShow: 'Không đến', CheckedIn: 'Đã tiếp nhận', Registered: 'Đã đăng ký',
    WaitingForDoctor: 'Chờ bác sĩ', WaitingDoctor: 'Chờ bác sĩ', InConsultation: 'Đang khám',
    WaitingForDiagnostics: 'Chờ cận lâm sàng', InDiagnostics: 'Đang làm cận lâm sàng', ResultsReady: 'Đã có kết quả',
    Transferred: 'Đã chuyển', ConsultationCompleted: 'Đã kết thúc khám', InPharmacy: 'Đang tại quầy thuốc', InBilling: 'Đang thanh toán',
    Draft: 'Bản nháp', Issued: 'Đã phát hành', Dispensed: 'Đã cấp phát', ReservedForPurchase: 'Đã giữ thuốc',
    Ordered: 'Đã chỉ định', InProgress: 'Đang thực hiện', Unpaid: 'Chưa thanh toán', Paid: 'Đã thanh toán',
    PartiallyPaid: 'Thanh toán một phần', Succeeded: 'Thành công', Voided: 'Đã hủy giao dịch'
};

const StatusLine: React.FC<{ label: string; value: unknown; date?: boolean }> = ({ label, value, date = false }) => {
    const rendered = date ? formatDateTime(value) : valueLabel(value);
    const localized = rendered && /trạng thái|thanh toán/i.test(label) ? statusLabels[rendered] : undefined;
    return rendered ? <div className={styles.typedLine} title={localized ? rendered : undefined}><strong>{label}:</strong> {localized ?? rendered}</div> : null;
};

const EmptyData: React.FC<{ message: string }> = ({ message }) => <p className={styles.typedNotice} role="status">{message}</p>;

// Match the typed renderers' empty states, including unsupported envelopes.
export const copilotCardEmptyMessage = (card: AiCopilotCard): string | null => {
    const record = asRecord(card.data);
    const noRows = asRecords(card.data).length === 0;
    switch (card.type) {
        case 'clinic_knowledge':
            if (!record) return 'Cấu trúc danh mục công khai chưa được hỗ trợ.';
            return text(record.status)?.toLowerCase() === 'not_found' || !asRecords(record.items).length ? 'Không có bản ghi công khai phù hợp.' : null;
        case 'specialties': return noRows ? 'Không có chuyên khoa phù hợp.' : null;
        case 'doctors': return noRows ? 'Không có bác sĩ phù hợp.' : null;
        case 'available_slots': return noRows ? 'Không có khung giờ trống phù hợp.' : null;
        case 'facilities': return noRows ? 'Không có cơ sở đang hoạt động phù hợp.' : null;
        case 'pricing_catalog':
            if (!record) return 'Bảng giá chưa có cấu trúc được hỗ trợ.';
            return !asRecords(record.consultation).length && !asRecords(record.diagnostics).length ? 'Chưa có giá công bố phù hợp.' : null;
        case 'appointments': return !rowsFrom(card.data).length ? 'Bạn chưa có lịch hẹn phù hợp.' : null;
        case 'appointment_detail': return !record ? 'Bạn chưa có lịch hẹn phù hợp.' : null;
        case 'patient_visits': return noRows ? 'Bạn chưa có lượt khám nào.' : null;
        case 'patient_diagnostic_results': return noRows ? 'Bạn chưa có kết quả cận lâm sàng được công bố.' : null;
        case 'patient_prescriptions': return noRows ? 'Bạn chưa có đơn thuốc đã phát hành.' : null;
        case 'patient_bills': return noRows ? 'Bạn chưa có hóa đơn nào.' : null;
        case 'reception_appointments': return noRows ? 'Không có lịch hẹn phù hợp.' : null;
        case 'reception_queue': return noRows ? 'Hàng đợi hiện không có lượt phù hợp.' : null;
        case 'appointment_lookup': return !record || text(record.status)?.toLowerCase() === 'not_found' || !text(record.appointmentCode) ? 'Không tìm thấy lịch hẹn phù hợp.' : null;
        case 'doctor_patient_summary': return !record ? 'Cấu trúc tóm tắt bệnh nhân chưa được hỗ trợ.' : null;
        case 'doctor_summary':
        case 'doctor_queue': return noRows ? 'Hàng đợi bác sĩ hiện không có dữ liệu phù hợp.' : null;
        case 'doctor_diagnostic_orders': return noRows ? 'Chưa có chỉ định cận lâm sàng trong ca này.' : null;
        case 'doctor_prescription_status': return noRows ? 'Ca khám chưa có đơn thuốc được công bố trong dữ liệu này.' : null;
        case 'technician_worklist': return noRows ? 'Worklist cận lâm sàng hiện không có phiếu phù hợp.' : null;
        case 'pharmacist_prescription_queue': return noRows ? 'Hàng đợi đơn thuốc hiện không có dữ liệu phù hợp.' : null;
        case 'pharmacist_prescription_payment': return !record ? 'Chưa thể hiển thị trạng thái thanh toán của đơn thuốc.' : null;
        case 'pharmacy_inventory': return noRows ? 'Kho thuốc hiện không có dữ liệu phù hợp.' : null;
        case 'admin_dashboard_metrics': return !record ? 'Chưa có chỉ số vận hành được hỗ trợ.' : null;
        case 'admin_ai_health': return !record ? 'Chưa có chỉ số sức khỏe AI được hỗ trợ.' : null;
        case 'reception_pending_payments': return noRows ? 'Không có hóa đơn chờ thanh toán.' : null;
        case 'doctor_appointments_today': return noRows ? 'Hôm nay bạn không có lịch hẹn phù hợp.' : null;
        case 'technician_completed_today': return noRows ? 'Hôm nay chưa có chỉ định hoàn tất.' : null;
        case 'pharmacy_low_stock': return noRows ? 'Không có thuốc dưới mức đặt lại.' : null;
        case 'admin_revenue_summary': return !record || numberValue(record.invoiceCount) === 0 ? 'Chưa có doanh thu trong kỳ này.' : null;
        case 'booking_preview':
        case 'pending_action':
        case 'change_request':
        case 'idempotent_replay': return null;
        default: return 'Copilot chưa hỗ trợ trình bày đầy đủ cấu trúc dữ liệu này; không hiển thị dữ liệu thô.';
    }
};

const List: React.FC<{ children: React.ReactNode }> = ({ children }) => <div className={styles.typedList}>{children}</div>;

const PublicCatalogCard: React.FC<{ data: unknown }> = ({ data }) => {
    const envelope = asRecord(data);
    if (!envelope) return <EmptyData message="Cấu trúc danh mục công khai chưa được hỗ trợ." />;
    const items = asRecords(envelope.items);
    if (text(envelope.status)?.toLowerCase() === 'not_found' || items.length === 0) return <EmptyData message="Không có bản ghi công khai phù hợp." />;
    return <List>{items.slice(0, 20).map((item, index) => {
        const title = text(item.title) ?? 'Bản ghi công khai';
        const details = asRecord(item.details);
        return <article className={styles.typedItem} key={`${title}-${index}`}>
            <strong>{title}</strong>
            <StatusLine label="Mô tả" value={item.description} />
            <StatusLine label="Địa chỉ" value={item.address} />
            <StatusLine label="Giờ làm việc" value={item.openingHours} />
            <StatusLine label="Mã chuyên khoa" value={details?.specialtyCode} />
            <StatusLine label="Chuyên khoa" value={details?.specialties} />
            <StatusLine label="Cơ sở" value={details?.facilities} />
            <StatusLine label="Mã dịch vụ" value={details?.code} />
            {formatMoney(item.publishedPrice, text(item.currency) ?? 'VND') && <div className={styles.typedLine}><strong>Giá công bố:</strong> {formatMoney(item.publishedPrice, text(item.currency) ?? 'VND')}</div>}
        </article>;
    })}</List>;
};

const SpecialtyCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message="Không có chuyên khoa phù hợp." />;
    return <List>{items.slice(0, 20).map((item, index) => <article className={styles.typedItem} key={index}>
        <strong>{text(item.name) ?? 'Chuyên khoa'}</strong>
        <StatusLine label="Mã" value={item.code} />
        <StatusLine label="Mô tả" value={item.description} />
    </article>)}</List>;
};

const DoctorCatalogCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message="Không có bác sĩ phù hợp." />;
    return <List>{items.slice(0, 20).map((item, index) => <article className={styles.typedItem} key={index}>
        <strong>{text(item.academicTitle) ? `${text(item.academicTitle)} ` : ''}{text(item.name) ?? 'Bác sĩ'}</strong>
        <StatusLine label="Chuyên khoa" value={item.specialtyName} />
    </article>)}</List>;
};

const SlotCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message="Không có khung giờ trống phù hợp." />;
    return <List>{items.slice(0, 20).map((item, index) => <article className={styles.typedItem} key={index}>
        <StatusLine label="Ngày" value={item.slotDate ?? item.date} date />
        <StatusLine label="Giờ" value={item.startTime && item.endTime ? `${valueLabel(item.startTime)} – ${valueLabel(item.endTime)}` : item.startTime} />
        <StatusLine label="Bác sĩ" value={item.doctorName} />
        <StatusLine label="Chuyên khoa" value={item.specialtyName} />
        <StatusLine label="Cơ sở" value={item.facilityName} />
    </article>)}</List>;
};

const FacilityCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message="Không có cơ sở đang hoạt động phù hợp." />;
    return <List>{items.slice(0, 20).map((item, index) => <article className={styles.typedItem} key={index}>
        <strong>{text(item.name) ?? 'Cơ sở'}</strong>
        <StatusLine label="Địa chỉ" value={item.address} />
        <StatusLine label="Thành phố" value={item.city} />
        <StatusLine label="Mô tả" value={item.description} />
    </article>)}</List>;
};

const PricingCard: React.FC<{ data: unknown }> = ({ data }) => {
    const record = asRecord(data);
    if (!record) return <EmptyData message="Bảng giá chưa có cấu trúc được hỗ trợ." />;
    const consultation = asRecords(record.consultation);
    const diagnostics = asRecords(record.diagnostics);
    if (!consultation.length && !diagnostics.length) return <EmptyData message="Chưa có giá công bố phù hợp." />;
    return <List>
        {consultation.slice(0, 20).map((item, index) => <article className={styles.typedItem} key={`consultation-${index}`}><strong>{text(item.specialty) ?? 'Khám chuyên khoa'}</strong><div className={styles.typedLine}><strong>Phí khám:</strong> {formatMoney(item.consultationFee, text(item.currency) ?? 'VND') ?? 'Chưa công bố'}</div></article>)}
        {diagnostics.slice(0, 50).map((item, index) => <article className={styles.typedItem} key={`diagnostic-${index}`}><strong>{text(item.name) ?? 'Dịch vụ cận lâm sàng'}</strong><StatusLine label="Nhóm" value={item.category} /><div className={styles.typedLine}><strong>Giá:</strong> {formatMoney(item.price, text(item.currency) ?? 'VND') ?? 'Chưa công bố'}</div></article>)}
    </List>;
};

const AppointmentCard: React.FC<{ data: unknown; detail?: boolean }> = ({ data, detail = false }) => {
    const source = detail ? [asRecord(data)].filter(Boolean) as DataRecord[] : rowsFrom(data);
    if (!source.length) return <EmptyData message="Bạn chưa có lịch hẹn phù hợp." />;
    return <List>{source.slice(0, 20).map((item, index) => <article className={styles.typedItem} key={index}>
        <strong>{text(item.appointmentCode) ?? 'Lịch hẹn'}</strong>
        <StatusLine label="Trạng thái" value={item.status} />
        <StatusLine label="Ngày" value={item.appointmentDate ?? item.slotDate} date />
        <StatusLine label="Giờ" value={item.startTime && item.endTime ? `${valueLabel(item.startTime)} – ${valueLabel(item.endTime)}` : item.startTime} />
        <StatusLine label="Bác sĩ" value={item.doctorName} />
        <StatusLine label="Chuyên khoa" value={item.specialtyName} />
        <StatusLine label="Cơ sở" value={item.facilityName} />
        <StatusLine label="Lý do khám" value={item.reason} />
    </article>)}</List>;
};

const PatientVisitsCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message="Bạn chưa có lượt khám nào." />;
    return <List>{items.slice(0, 20).map((item, index) => {
        const summary = asRecord(item.summary);
        return <article className={styles.typedItem} key={index}>
            <strong>{text(item.visitCode) ?? 'Lượt khám'}</strong>
            <StatusLine label="Ngày khám" value={item.visitDate} date />
            <StatusLine label="Trạng thái" value={item.status} />
            <StatusLine label="Lý do khám" value={item.chiefComplaint} />
            <StatusLine label="Tóm tắt đã ghi nhận" value={summary?.Summary ?? summary?.summary} />
            <StatusLine label="Chẩn đoán" value={summary?.Diagnosis ?? summary?.diagnosis} />
            <StatusLine label="Hướng điều trị" value={summary?.TreatmentPlan ?? summary?.treatmentPlan} />
        </article>;
    })}</List>;
};

const PatientResultsCard: React.FC<{ data: unknown }> = ({ data }) => {
    const orders = asRecords(data);
    if (!orders.length) return <EmptyData message="Bạn chưa có kết quả cận lâm sàng được công bố." />;
    return <List>{orders.slice(0, 20).map((order, index) => {
        const published = order.publishedToPatient === true;
        return <article className={styles.typedItem} key={index}>
            <strong>{text(order.orderCode) ?? 'Kết quả cận lâm sàng'}</strong>
            <StatusLine label="Trạng thái công bố" value={published ? 'Đã được bác sĩ duyệt' : 'Chưa công bố'} />
            <StatusLine label="Duyệt lúc" value={order.reviewedAtUtc} date />
            {asRecords(order.items).map((item, itemIndex) => {
                const result = published ? asRecord(item.result) : null;
                return <div className={styles.typedSubsection} key={itemIndex}>
                    <StatusLine label="Dịch vụ" value={item.service} />
                    <StatusLine label="Trạng thái" value={item.status} />
                    {result ? <><StatusLine label="Kết quả" value={result.ResultText ?? result.resultText} /><StatusLine label="Kết luận" value={result.Conclusion ?? result.conclusion} /><StatusLine label="Trả lúc" value={result.ResultedAtUtc ?? result.resultedAtUtc} date /></> : <StatusLine label="Kết quả" value="Chưa được công bố" />}
                </div>;
            })}
        </article>;
    })}</List>;
};

const PrescriptionCard: React.FC<{ data: unknown; patient?: boolean }> = ({ data, patient = false }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message={patient ? 'Bạn chưa có đơn thuốc đã phát hành.' : 'Ca khám chưa có đơn thuốc được công bố trong dữ liệu này.'} />;
    return <List>{items.slice(0, 20).map((prescription, index) => <article className={styles.typedItem} key={index}>
        <strong>{patient ? 'Đơn thuốc của bạn' : 'Đơn thuốc của ca khám'}</strong>
        <StatusLine label="Trạng thái đơn" value={prescription.status} />
        <StatusLine label="Kê lúc" value={prescription.CreatedAt ?? prescription.createdAt} date />
        <StatusLine label="Phát lúc" value={prescription.DispensedAt ?? prescription.dispensedAt} date />
        <StatusLine label="Hướng dẫn chung" value={prescription.Notes ?? prescription.notes} />
        {asRecords(prescription.items).map((item, itemIndex) => <div className={styles.typedSubsection} key={itemIndex}>
            <StatusLine label="Thuốc" value={item.medicine} />
            <StatusLine label="Liều dùng" value={item.Dosage ?? item.dosage} />
            <StatusLine label="Tần suất" value={item.Frequency ?? item.frequency} />
            <StatusLine label="Số ngày" value={item.DurationDays ?? item.durationDays} />
            <StatusLine label="Cách dùng" value={item.Instructions ?? item.instructions} />
        </div>)}
    </article>)}</List>;
};

const PatientBillsCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message="Bạn chưa có hóa đơn nào." />;
    return <List>{items.slice(0, 20).map((invoice, index) => <article className={styles.typedItem} key={index}>
        <strong>{text(invoice.invoiceCode) ?? 'Hóa đơn của bạn'}</strong>
        <StatusLine label="Trạng thái" value={invoice.status} />
        <StatusLine label="Tạo lúc" value={invoice.createdAtUtc} date />
        <StatusLine label="Thanh toán lúc" value={invoice.paidAtUtc} date />
        <div className={styles.typedLine}><strong>Tổng tiền:</strong> {formatMoney(invoice.totalAmount) ?? 'Chưa có dữ liệu'}</div>
        {asRecords(invoice.items).map((item, itemIndex) => <div className={styles.typedSubsection} key={itemIndex}><StatusLine label="Khoản mục" value={item.Description ?? item.description} /><StatusLine label="Số lượng" value={item.Quantity ?? item.quantity} /><div className={styles.typedLine}><strong>Thành tiền:</strong> {formatMoney(item.LineTotal ?? item.lineTotal) ?? 'Chưa có dữ liệu'}</div></div>)}
    </article>)}</List>;
};

const ReceptionCard: React.FC<{ data: unknown; queue?: boolean }> = ({ data, queue = false }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message={queue ? 'Hàng đợi hiện không có lượt phù hợp.' : 'Không có lịch hẹn phù hợp.'} />;
    return <List>{items.slice(0, 20).map((item, index) => <article className={styles.typedItem} key={index}>
        <strong>{text(queue ? item.visitCode : item.appointmentCode) ?? (queue ? 'Lượt trong hàng đợi' : 'Lịch hẹn')}</strong>
        {queue && <StatusLine label="Số thứ tự" value={item.queueNumber} />}
        <StatusLine label="Người bệnh" value={item.patientName} />
        {!queue && <StatusLine label="Bác sĩ" value={item.doctorName} />}
        <StatusLine label="Trạng thái" value={item.status} />
        <StatusLine label="Thời gian" value={item.appointmentDate ?? item.visitDate ?? item.checkedInAtUtc} date />
    </article>)}</List>;
};

const ReceptionAppointmentLookupCard: React.FC<{ data: unknown }> = ({ data }) => {
    const item = asRecord(data);
    const status = text(item?.status)?.toLowerCase();
    if (!item || status === 'not_found' || !text(item.appointmentCode)) {
        return <EmptyData message="Không tìm thấy lịch hẹn phù hợp." />;
    }
    return <List><article className={styles.typedItem}>
        <strong>{text(item.appointmentCode)}</strong>
        <StatusLine label="Người bệnh" value={item.patientName} />
        <StatusLine label="Trạng thái" value={item.status} />
        <StatusLine label="Ngày" value={item.appointmentDate} date />
        <StatusLine label="Giờ" value={item.startTime && item.endTime ? `${valueLabel(item.startTime)} – ${valueLabel(item.endTime)}` : item.startTime} />
    </article></List>;
};

const DoctorSummaryCard: React.FC<{ data: unknown }> = ({ data }) => {
    const item = asRecord(data);
    if (!item) return <EmptyData message="Cấu trúc tóm tắt bệnh nhân chưa được hỗ trợ." />;
    const summary = asRecord(item.summary);
    const vitals = asRecord(item.vitals);
    return <List><article className={styles.typedItem}>
        <strong>{text(item.visitCode) ?? 'Tóm tắt ca khám'}</strong>
        <StatusLine label="Bệnh nhân" value={item.patientName} />
        <StatusLine label="Trạng thái dữ liệu" value={item.clinicalDataStatus === 'visit_reassigned' ? 'Lượt khám đã chuyển bác sĩ; dữ liệu lâm sàng không hiển thị' : 'Được phép đọc'} />
        <StatusLine label="Lý do khám" value={item.reason ?? item.chiefComplaint} />
        <StatusLine label="Tóm tắt" value={summary?.Summary ?? summary?.summary} />
        <StatusLine label="Chẩn đoán" value={summary?.Diagnosis ?? summary?.diagnosis} />
        <StatusLine label="Hướng điều trị" value={summary?.TreatmentPlan ?? summary?.treatmentPlan} />
        {vitals && <div className={styles.typedSubsection}><strong>Sinh hiệu</strong><StatusLine label="Nhiệt độ" value={vitals.Temperature ?? vitals.temperature} /><StatusLine label="Huyết áp" value={vitals.BloodPressureSystolic && vitals.BloodPressureDiastolic ? `${valueLabel(vitals.BloodPressureSystolic)}/${valueLabel(vitals.BloodPressureDiastolic)} mmHg` : undefined} /><StatusLine label="Nhịp tim" value={vitals.HeartRate ?? vitals.heartRate} /><StatusLine label="SpO₂" value={vitals.SpO2 ?? vitals.spO2} /></div>}
    </article></List>;
};

const DoctorQueueCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message="Hàng đợi bác sĩ hiện không có dữ liệu phù hợp." />;
    return <List>{items.slice(0, 20).map((item, index) => <article className={styles.typedItem} key={index}><strong>{text(item.visitCode) ?? 'Lượt khám'}</strong><StatusLine label="Số thứ tự" value={item.queueNumber} /><StatusLine label="Người bệnh" value={item.patientName} /><StatusLine label="Trạng thái" value={item.status} /><StatusLine label="Lý do khám" value={item.chiefComplaint} /></article>)}</List>;
};

const LegacyDoctorSummaryCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message="Hàng đợi bác sĩ hiện không có dữ liệu phù hợp." />;
    return <List>{items.slice(0, 20).map((item, index) => <article className={styles.typedItem} key={index}><strong>{text(item.doctorName) ?? 'Ca được phân công'}</strong><StatusLine label="Trạng thái" value={item.status} /><StatusLine label="Thời gian" value={item.appointmentDate ?? item.visitDate ?? item.checkedInAtUtc} date /></article>)}</List>;
};

const DoctorOrdersCard: React.FC<{ data: unknown }> = ({ data }) => {
    const orders = asRecords(data);
    if (!orders.length) return <EmptyData message="Chưa có chỉ định cận lâm sàng trong ca này." />;
    return <List>{orders.slice(0, 20).map((order, index) => <article className={styles.typedItem} key={index}><strong>{text(order.orderCode) ?? 'Phiếu chỉ định'}</strong><StatusLine label="Trạng thái phiếu" value={order.status} /><StatusLine label="Chỉ định" value={order.clinicalIndication} /><StatusLine label="Đặt lúc" value={order.orderedAtUtc} date />{asRecords(order.items).map((item, itemIndex) => { const result = asRecord(item.result); return <div className={styles.typedSubsection} key={itemIndex}><StatusLine label="Dịch vụ" value={item.service} /><StatusLine label="Trạng thái dịch vụ" value={item.status} />{result ? <><StatusLine label="Kết quả" value={result.ResultText ?? result.resultText} /><StatusLine label="Kết luận" value={result.Conclusion ?? result.conclusion} /></> : <StatusLine label="Kết quả" value="Chưa có kết quả" />}</div>; })}</article>)}</List>;
};

const TechnicianWorklistCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message="Worklist cận lâm sàng hiện không có phiếu phù hợp." />;
    return <List>{items.slice(0, 20).map((item, index) => <article className={styles.typedItem} key={index}><strong>{text(item.orderCode) ?? 'Phiếu chỉ định'}</strong><StatusLine label="Trạng thái" value={item.status} /><StatusLine label="Chỉ định" value={item.clinicalIndication} /><StatusLine label="Đặt lúc" value={item.orderedAtUtc} date />{asRecords(item.items).map((service, serviceIndex) => <div className={styles.typedSubsection} key={serviceIndex}><StatusLine label="Dịch vụ" value={service.service} /><StatusLine label="Trạng thái dịch vụ" value={service.status} /></div>)}</article>)}</List>;
};

const PharmacyQueueCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message="Hàng đợi đơn thuốc hiện không có dữ liệu phù hợp." />;
    return <List>{items.slice(0, 20).map((item, index) => <article className={styles.typedItem} key={index}><strong>Đơn thuốc trong hàng đợi</strong><StatusLine label="Trạng thái đơn" value={item.status} /><StatusLine label="Thanh toán" value={item.paymentStatus} />{asRecords(item.paymentItems).map((paymentItem, paymentIndex) => <div className={styles.typedSubsection} key={paymentIndex}><StatusLine label="Thuốc" value={paymentItem.medicine} /><StatusLine label="Số lượng yêu cầu" value={paymentItem.requiredQuantity} /><StatusLine label="Đã thanh toán" value={paymentItem.paidQuantity} /><StatusLine label="Trạng thái dòng thanh toán" value={paymentItem.itemPaymentStatus} /></div>)}</article>)}</List>;
};

const PharmacyPaymentCard: React.FC<{ data: unknown }> = ({ data }) => {
    const item = asRecord(data);
    if (!item) return <EmptyData message="Chưa thể hiển thị trạng thái thanh toán của đơn thuốc." />;
    return <List><article className={styles.typedItem}>
        <strong>Đối chiếu thanh toán đơn thuốc</strong>
        <StatusLine label="Trạng thái đơn" value={item.prescriptionStatus} />
        <StatusLine label="Thanh toán" value={item.paymentStatus} />
        {asRecords(item.paymentItems).map((paymentItem, index) => <div className={styles.typedSubsection} key={index}>
            <StatusLine label="Thuốc" value={paymentItem.medicine} />
            <StatusLine label="Số lượng yêu cầu" value={paymentItem.requiredQuantity} />
            <StatusLine label="Đã thanh toán" value={paymentItem.paidQuantity} />
            <StatusLine label="Trạng thái dòng thanh toán" value={paymentItem.itemPaymentStatus} />
        </div>)}
    </article></List>;
};

const PharmacyInventoryCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message="Kho thuốc hiện không có dữ liệu phù hợp." />;
    return <List>{items.slice(0, 200).map((item, index) => <article className={styles.typedItem} key={index}><strong>{text(item.Name ?? item.name) ?? 'Thuốc'}</strong><StatusLine label="Đơn vị" value={item.Unit ?? item.unit} /><StatusLine label="Tồn kho" value={item.StockQuantity ?? item.stockQuantity} /><StatusLine label="Mức đặt lại" value={item.reorderLevel ?? item.ReorderLevel} /></article>)}</List>;
};

const AdminMetricsCard: React.FC<{ data: unknown }> = ({ data }) => {
    const item = asRecord(data);
    if (!item) return <EmptyData message="Chưa có chỉ số vận hành được hỗ trợ." />;
    return <List><article className={styles.typedItem}><strong>Chỉ số vận hành</strong><StatusLine label="Lịch hẹn hôm nay" value={item.appointmentsToday} /><StatusLine label="Lượt khám đang mở" value={item.activeVisits} /><StatusLine label="Phiếu cận lâm sàng đang mở" value={item.openDiagnosticOrders} /><StatusLine label="Đơn thuốc đã phát hành" value={item.issuedPrescriptions} /></article></List>;
};

const AdminHealthCard: React.FC<{ data: unknown }> = ({ data }) => {
    const item = asRecord(data);
    if (!item) return <EmptyData message="Chưa có chỉ số sức khỏe AI được hỗ trợ." />;
    return <List><article className={styles.typedItem}><strong>Sức khỏe AI</strong><StatusLine label="Thao tác chờ xác nhận" value={item.pendingActions} /><StatusLine label="Sự kiện audit" value={item.auditEvents} /></article></List>;
};

const PendingPaymentsCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message="Không có hóa đơn chờ thanh toán." />;
    return <List>{items.slice(0, 50).map((invoice, index) => {
        const amount = formatMoney(invoice.totalAmount);
        return <article className={styles.typedItem} key={index}>
            <strong>{text(invoice.invoiceCode) ?? 'Hóa đơn'}</strong>
            <StatusLine label="Người bệnh" value={invoice.patientName} />
            {amount && <div className={styles.typedLine}><strong>Số tiền:</strong> {amount}</div>}
            <StatusLine label="Trạng thái" value={invoice.status} />
            <StatusLine label="Tạo lúc" value={formatVietnamDateTime(invoice.createdAtUtc)} />
        </article>;
    })}</List>;
};

const DoctorAppointmentsTodayCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message="Hôm nay bạn không có lịch hẹn phù hợp." />;
    return <List>{items.slice(0, 100).map((item, index) => {
        const start = formatClock(item.startTime);
        const end = formatClock(item.endTime);
        return <article className={styles.typedItem} key={index}>
            <strong>{text(item.appointmentCode) ?? 'Lịch hẹn'}</strong>
            <StatusLine label="Giờ khám" value={start && end ? `${start} – ${end}` : start} />
            <StatusLine label="Người bệnh" value={item.patientName} />
            <StatusLine label="Trạng thái" value={item.status} />
            <StatusLine label="Lý do khám" value={item.reason} />
        </article>;
    })}</List>;
};

const TechnicianCompletedTodayCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message="Hôm nay chưa có chỉ định hoàn tất." />;
    return <List>{items.slice(0, 100).map((item, index) => {
        const services = Array.isArray(item.services) ? item.services.map(text).filter((name): name is string => Boolean(name)) : [];
        return <article className={styles.typedItem} key={index}>
            <strong>{text(item.orderCode) ?? 'Phiếu chỉ định'}</strong>
            <StatusLine label="Hoàn tất lúc" value={formatVietnamTime(item.completedAtUtc)} />
            <StatusLine label="Dịch vụ" value={services.join(', ')} />
        </article>;
    })}</List>;
};

const PharmacyLowStockCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (!items.length) return <EmptyData message="Không có thuốc dưới mức đặt lại." />;
    return <List>{items.slice(0, 100).map((item, index) => {
        const name = text(item.name) ?? 'Thuốc';
        const code = text(item.code);
        const stock = numberValue(item.stockQuantity);
        const reorder = numberValue(item.reorderLevel);
        const unit = text(item.unit);
        return <article className={styles.typedItem} key={index}>
            <strong>{code ? `${name} (${code})` : name}</strong>
            <StatusLine label="Tồn / mức đặt lại" value={stock !== null && reorder !== null ? `${stock} / ${reorder}${unit ? ` ${unit}` : ''}` : null} />
        </article>;
    })}</List>;
};

const AdminRevenueCard: React.FC<{ data: unknown }> = ({ data }) => {
    const item = asRecord(data);
    if (!item || numberValue(item.invoiceCount) === 0) return <EmptyData message="Chưa có doanh thu trong kỳ này." />;
    const period = text(item.period);
    const total = formatMoney(item.totalRevenue);
    const facilities = asRecords(item.byFacility);
    return <List><article className={styles.typedItem}>
        <strong>Doanh thu</strong>
        <StatusLine label="Kỳ" value={period ? revenuePeriodLabels[period] ?? period : null} />
        {total && <div className={styles.typedLine}><strong>Tổng doanh thu:</strong> {total}</div>}
        <StatusLine label="Số hóa đơn" value={item.invoiceCount} />
        {facilities.length >= 2 && facilities.map((facility, index) => {
            const id = numberValue(facility.facilityId);
            const amount = formatMoney(facility.totalRevenue);
            const count = numberValue(facility.invoiceCount);
            return id !== null && amount && count !== null
                ? <div className={styles.typedLine} key={index}>Cơ sở #{id}: {amount} · {count} hóa đơn</div>
                : null;
        })}
    </article></List>;
};

const ActionResultCard: React.FC<{ type: string; data: unknown }> = ({ type, data }) => {
    const item = asRecord(data);
    if (type === 'booking_preview') {
        return <List><article className={styles.typedItem}><strong>Xem trước đặt lịch</strong><StatusLine label="Thao tác" value={item?.operation === 'booking' ? 'Đặt lịch khám' : item?.operation} /><StatusLine label="Ngày" value={item?.appointmentDate ?? item?.slotDate} date /><StatusLine label="Giờ" value={item?.startTime && item?.endTime ? `${valueLabel(item.startTime)} – ${valueLabel(item.endTime)}` : item?.startTime} /><StatusLine label="Trạng thái" value="Chờ người dùng xác nhận rõ ràng" /></article></List>;
    }
    if (type === 'pending_action') return <List><article className={styles.typedItem}><strong>Thao tác đang chờ xác nhận</strong><StatusLine label="Lịch hẹn" value={item?.appointmentCode} /><StatusLine label="Hết hạn" value={item?.expiresAtUtc} date /><StatusLine label="Trạng thái" value="Chờ người dùng xác nhận rõ ràng" /></article></List>;
    if (type === 'change_request') return <List><article className={styles.typedItem}><strong>Yêu cầu thay đổi đã được tạo</strong><StatusLine label="Thao tác" value={item?.operation === 'cancel' ? 'Hủy lịch' : item?.operation === 'reschedule' ? 'Đổi lịch' : item?.operation} /><StatusLine label="Trạng thái" value="Đã tiếp nhận" /></article></List>;
    return <List><article className={styles.typedItem}><strong>Thao tác đã hoàn tất trước đó</strong><StatusLine label="Trạng thái" value="Đã xử lý; không ghi lặp dữ liệu" /></article></List>;
};

export const renderCopilotCardData = (card: AiCopilotCard): React.ReactNode => {
    switch (card.type) {
        case 'clinic_knowledge': return <PublicCatalogCard data={card.data} />;
        case 'specialties': return <SpecialtyCard data={card.data} />;
        case 'doctors': return <DoctorCatalogCard data={card.data} />;
        case 'available_slots': return <SlotCard data={card.data} />;
        case 'facilities': return <FacilityCard data={card.data} />;
        case 'pricing_catalog': return <PricingCard data={card.data} />;
        case 'appointments': return <AppointmentCard data={card.data} />;
        case 'appointment_detail': return <AppointmentCard data={card.data} detail />;
        case 'patient_visits': return <PatientVisitsCard data={card.data} />;
        case 'patient_diagnostic_results': return <PatientResultsCard data={card.data} />;
        case 'patient_prescriptions': return <PrescriptionCard data={card.data} patient />;
        case 'patient_bills': return <PatientBillsCard data={card.data} />;
        case 'reception_appointments': return <ReceptionCard data={card.data} />;
        case 'reception_queue': return <ReceptionCard data={card.data} queue />;
        case 'appointment_lookup': return <ReceptionAppointmentLookupCard data={card.data} />;
        case 'doctor_patient_summary': return <DoctorSummaryCard data={card.data} />;
        case 'doctor_summary': return <LegacyDoctorSummaryCard data={card.data} />;
        case 'doctor_queue': return <DoctorQueueCard data={card.data} />;
        case 'doctor_diagnostic_orders': return <DoctorOrdersCard data={card.data} />;
        case 'doctor_prescription_status': return <PrescriptionCard data={card.data} />;
        case 'technician_worklist': return <TechnicianWorklistCard data={card.data} />;
        case 'pharmacist_prescription_queue': return <PharmacyQueueCard data={card.data} />;
        case 'pharmacist_prescription_payment': return <PharmacyPaymentCard data={card.data} />;
        case 'pharmacy_inventory': return <PharmacyInventoryCard data={card.data} />;
        case 'admin_dashboard_metrics': return <AdminMetricsCard data={card.data} />;
        case 'admin_ai_health': return <AdminHealthCard data={card.data} />;
        case 'reception_pending_payments': return <PendingPaymentsCard data={card.data} />;
        case 'doctor_appointments_today': return <DoctorAppointmentsTodayCard data={card.data} />;
        case 'technician_completed_today': return <TechnicianCompletedTodayCard data={card.data} />;
        case 'pharmacy_low_stock': return <PharmacyLowStockCard data={card.data} />;
        case 'admin_revenue_summary': return <AdminRevenueCard data={card.data} />;
        case 'booking_preview':
        case 'pending_action':
        case 'change_request':
        case 'idempotent_replay': return <ActionResultCard type={card.type} data={card.data} />;
        default: return <EmptyData message="Copilot chưa hỗ trợ trình bày đầy đủ cấu trúc dữ liệu này; không hiển thị dữ liệu thô." />;
    }
};
