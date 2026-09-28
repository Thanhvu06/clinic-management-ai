import React from 'react';
import type { AiCopilotCard } from '../../api/aiCopilotApi';
import styles from './UnifiedCopilotPanel.module.css';

type RecordData = Record<string, unknown>;

const asRecord = (value: unknown): RecordData | null =>
    value && typeof value === 'object' && !Array.isArray(value) ? value as RecordData : null;

const asRecords = (value: unknown): RecordData[] =>
    Array.isArray(value) ? value.filter(item => Boolean(asRecord(item))).map(item => item as RecordData) : [];

const text = (value: unknown): string | null =>
    typeof value === 'string' && value.trim() ? value.trim() : null;

const formatDateTime = (value: unknown): string | null => {
    const raw = text(value);
    if (!raw) return null;
    const date = new Date(raw);
    return Number.isNaN(date.valueOf()) ? raw : date.toLocaleString('vi-VN');
};

const formatMoney = (value: unknown, currency = 'VND'): string | null => {
    if (typeof value !== 'number' || !Number.isFinite(value)) return null;
    return `${value.toLocaleString('vi-VN')} ${currency === 'VND' ? '₫' : currency}`;
};

const valueLabel = (value: unknown): string => {
    if (typeof value === 'number' && Number.isFinite(value)) return String(value);
    return text(value) ?? 'Chưa có dữ liệu';
};

const StatusLine: React.FC<{ label: string; value: unknown }> = ({ label, value }) => {
    const rendered = typeof value === 'number' && Number.isFinite(value) ? String(value) : text(value);
    return rendered ? <div className={styles.typedLine}><strong>{label}:</strong> {rendered}</div> : null;
};

const EmptyData: React.FC<{ message: string }> = ({ message }) => <p className={styles.typedNotice} role="status">{message}</p>;

const PublicCatalogCard: React.FC<{ data: unknown }> = ({ data }) => {
    const envelope = asRecord(data);
    if (!envelope) return <EmptyData message="Cấu trúc danh mục công khai chưa được hỗ trợ." />;
    const items = asRecords(envelope.items);
    if (text(envelope.status)?.toLowerCase() === 'not_found' || items.length === 0) {
        return <EmptyData message="Không có bản ghi công khai phù hợp." />;
    }
    return <div className={styles.typedList}>
        {items.slice(0, 20).map((item, index) => {
            const title = text(item.title) ?? text(item.name) ?? 'Bản ghi công khai';
            const currency = text(item.currency) ?? 'VND';
            const details = asRecord(item.details);
            return <article className={styles.typedItem} key={`${title}-${index}`}>
                <strong>{title}</strong>
                <StatusLine label="Địa chỉ" value={item.address} />
                <StatusLine label="Giờ làm việc" value={item.openingHours} />
                <StatusLine label="Mã chuyên khoa" value={details?.specialtyCode} />
                {formatMoney(item.publishedPrice, currency) && <div className={styles.typedLine}><strong>Giá công bố:</strong> {formatMoney(item.publishedPrice, currency)}</div>}
            </article>;
        })}
    </div>;
};

const DoctorSummaryCard: React.FC<{ data: unknown }> = ({ data }) => {
    const item = asRecord(data);
    if (!item) return <EmptyData message="Cấu trúc tóm tắt bệnh nhân chưa được hỗ trợ." />;
    const summary = asRecord(item.summary);
    const vitals = asRecord(item.vitals);
    return <div className={styles.typedList}>
        <article className={styles.typedItem}>
            <StatusLine label="Bệnh nhân" value={item.patientName} />
            <StatusLine label="Trạng thái dữ liệu" value={item.clinicalDataStatus === 'visit_reassigned' ? 'Lượt khám đã chuyển bác sĩ; dữ liệu lâm sàng không hiển thị' : 'Được phép đọc'} />
            <StatusLine label="Lý do khám" value={item.reason ?? item.chiefComplaint} />
            <StatusLine label="Tóm tắt" value={summary?.Summary ?? summary?.summary} />
            <StatusLine label="Chẩn đoán" value={summary?.Diagnosis ?? summary?.diagnosis} />
            <StatusLine label="Hướng điều trị" value={summary?.TreatmentPlan ?? summary?.treatmentPlan} />
            {vitals && <div className={styles.typedSubsection}>
                <strong>Sinh hiệu</strong>
                <StatusLine label="Nhiệt độ" value={vitals.Temperature ?? vitals.temperature} />
                <StatusLine label="Huyết áp" value={vitals.BloodPressureSystolic && vitals.BloodPressureDiastolic ? `${valueLabel(vitals.BloodPressureSystolic)}/${valueLabel(vitals.BloodPressureDiastolic)} mmHg` : undefined} />
                <StatusLine label="Nhịp tim" value={vitals.HeartRate ?? vitals.heartRate} />
                <StatusLine label="SpO₂" value={vitals.SpO2 ?? vitals.spO2} />
            </div>}
        </article>
    </div>;
};

const DoctorQueueCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (items.length === 0) return <EmptyData message="Hàng đợi bác sĩ hiện không có dữ liệu phù hợp." />;
    return <div className={styles.typedList}>{items.slice(0, 20).map((item, index) => <article className={styles.typedItem} key={index}>
        <StatusLine label="Bác sĩ" value={item.doctorName} />
        <StatusLine label="Trạng thái" value={item.status} />
        <StatusLine label="Thời gian" value={item.appointmentDate ?? item.visitDate ?? item.checkedInAtUtc} />
    </article>)}</div>;
};

const DoctorOrdersCard: React.FC<{ data: unknown }> = ({ data }) => {
    const orders = asRecords(data);
    if (orders.length === 0) return <EmptyData message="Chưa có chỉ định cận lâm sàng trong ca này." />;
    return <div className={styles.typedList}>{orders.slice(0, 20).map((order, index) => <article className={styles.typedItem} key={index}>
        <StatusLine label="Trạng thái phiếu" value={order.status} />
        <StatusLine label="Chỉ định" value={order.clinicalIndication} />
        {asRecords(order.items).map((item, itemIndex) => {
            const result = asRecord(item.result);
            return <div className={styles.typedSubsection} key={itemIndex}>
                <StatusLine label="Dịch vụ" value={item.service} />
                <StatusLine label="Trạng thái dịch vụ" value={item.status} />
                {result ? <StatusLine label="Kết quả" value={result.ResultText ?? result.resultText} /> : <StatusLine label="Kết quả" value="Chưa có kết quả" />}
            </div>;
        })}
    </article>)}</div>;
};

const DoctorPrescriptionCard: React.FC<{ data: unknown }> = ({ data }) => {
    const prescriptions = asRecords(data);
    if (prescriptions.length === 0) return <EmptyData message="Ca khám chưa có đơn thuốc được công bố trong dữ liệu này." />;
    return <div className={styles.typedList}>{prescriptions.slice(0, 20).map((prescription, index) => <article className={styles.typedItem} key={index}>
        <StatusLine label="Trạng thái đơn" value={prescription.status} />
        <StatusLine label="Kê lúc" value={formatDateTime(prescription.CreatedAt ?? prescription.createdAt)} />
        {asRecords(prescription.items).map((item, itemIndex) => <div className={styles.typedSubsection} key={itemIndex}>
            <StatusLine label="Thuốc" value={item.medicine} />
            <StatusLine label="Liều dùng" value={item.Dosage ?? item.dosage} />
            <StatusLine label="Tần suất" value={item.Frequency ?? item.frequency} />
            <StatusLine label="Thời gian" value={item.DurationDays ?? item.durationDays} />
        </div>)}
    </article>)}</div>;
};

const PatientResultsCard: React.FC<{ data: unknown }> = ({ data }) => {
    const orders = asRecords(data);
    if (orders.length === 0) return <EmptyData message="Bạn chưa có kết quả cận lâm sàng được công bố." />;
    return <div className={styles.typedList}>{orders.slice(0, 20).map((order, index) => {
        const published = order.publishedToPatient === true;
        return <article className={styles.typedItem} key={index}>
            <StatusLine label="Trạng thái công bố" value={published ? 'Đã được bác sĩ duyệt' : 'Chưa công bố'} />
            <StatusLine label="Duyệt lúc" value={formatDateTime(order.reviewedAtUtc)} />
            {asRecords(order.items).map((item, itemIndex) => {
                const result = asRecord(item.result);
                return <div className={styles.typedSubsection} key={itemIndex}>
                    <StatusLine label="Dịch vụ" value={item.service} />
                    <StatusLine label="Trạng thái" value={item.status} />
                    {published && result ? <>
                        <StatusLine label="Kết quả" value={result.ResultText ?? result.resultText} />
                        <StatusLine label="Kết luận" value={result.Conclusion ?? result.conclusion} />
                    </> : <StatusLine label="Kết quả" value="Chưa được công bố" />}
                </div>;
            })}
        </article>;
    })}</div>;
};

const ReceptionCard: React.FC<{ data: unknown; queue?: boolean }> = ({ data, queue = false }) => {
    const items = asRecords(data);
    if (items.length === 0) return <EmptyData message={queue ? 'Hàng đợi hiện không có lượt phù hợp.' : 'Không có lịch hẹn phù hợp.'} />;
    return <div className={styles.typedList}>{items.slice(0, 20).map((item, index) => <article className={styles.typedItem} key={index}>
        <StatusLine label={queue ? 'Số thứ tự' : 'Mã lịch hẹn'} value={queue ? item.queueNumber : item.appointmentCode} />
        <StatusLine label="Người bệnh" value={item.patientName} />
        <StatusLine label="Bác sĩ" value={item.doctorName} />
        <StatusLine label="Trạng thái" value={item.status} />
        <StatusLine label="Thời gian" value={item.appointmentDate ?? item.visitDate ?? item.checkedInAtUtc} />
    </article>)}</div>;
};

const TechnicianWorklistCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (items.length === 0) return <EmptyData message="Worklist cận lâm sàng hiện không có phiếu phù hợp." />;
    return <div className={styles.typedList}>{items.slice(0, 20).map((item, index) => <article className={styles.typedItem} key={index}>
        <StatusLine label="Mã phiếu" value={item.orderCode} />
        <StatusLine label="Trạng thái" value={item.status} />
        <StatusLine label="Chỉ định" value={item.clinicalIndication} />
        {asRecords(item.items).map((service, serviceIndex) => <div className={styles.typedSubsection} key={serviceIndex}>
            <StatusLine label="Dịch vụ" value={service.service} />
            <StatusLine label="Trạng thái dịch vụ" value={service.status} />
        </div>)}
    </article>)}</div>;
};

const PharmacyQueueCard: React.FC<{ data: unknown }> = ({ data }) => {
    const items = asRecords(data);
    if (items.length === 0) return <EmptyData message="Hàng đợi đơn thuốc hiện không có dữ liệu phù hợp." />;
    return <div className={styles.typedList}>{items.slice(0, 20).map((item, index) => <article className={styles.typedItem} key={index}>
        <StatusLine label="Trạng thái đơn" value={item.status} />
        <StatusLine label="Thanh toán" value={item.paymentStatus} />
        {asRecords(item.paymentItems).map((paymentItem, paymentIndex) => <div className={styles.typedSubsection} key={paymentIndex}>
            <StatusLine label="Thuốc" value={paymentItem.medicine} />
            <StatusLine label="Số lượng yêu cầu" value={paymentItem.requiredQuantity} />
            <StatusLine label="Đã thanh toán" value={paymentItem.paidQuantity} />
            <StatusLine label="Trạng thái dòng thanh toán" value={paymentItem.itemPaymentStatus} />
        </div>)}
    </article>)}</div>;
};

export const renderCopilotCardData = (card: AiCopilotCard): React.ReactNode => {
    switch (card.type) {
        case 'clinic_knowledge': return <PublicCatalogCard data={card.data} />;
        case 'doctor_patient_summary': return <DoctorSummaryCard data={card.data} />;
        case 'doctor_summary': return <DoctorQueueCard data={card.data} />;
        case 'doctor_queue': return <DoctorQueueCard data={card.data} />;
        case 'doctor_diagnostic_orders': return <DoctorOrdersCard data={card.data} />;
        case 'doctor_prescription_status': return <DoctorPrescriptionCard data={card.data} />;
        case 'patient_diagnostic_results': return <PatientResultsCard data={card.data} />;
        case 'reception_appointments': return <ReceptionCard data={card.data} />;
        case 'reception_queue': return <ReceptionCard data={card.data} queue />;
        case 'technician_worklist': return <TechnicianWorklistCard data={card.data} />;
        case 'pharmacist_prescription_queue': return <PharmacyQueueCard data={card.data} />;
        default: return <EmptyData message="Copilot chưa hỗ trợ trình bày đầy đủ cấu trúc dữ liệu này; không hiển thị dữ liệu thô." />;
    }
};
