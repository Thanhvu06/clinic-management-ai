import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Button, Input, Select, Typography } from 'antd';
import { CheckCircle2, ClipboardList, Clock, RefreshCw, Timer } from 'lucide-react';
import { diagnosticApi } from '../../api/diagnosticApi';
import { DataTable, EmptyState, FilterBar, InlineError, LoadingState, PageHeader, StatCard } from '../../components/common';
import type { DiagnosticCategory } from '../../types';
import type {
    DiagnosticTurnaroundReportDto,
    DiagnosticTurnaroundReportParams,
    DiagnosticTurnaroundSegmentDto,
    DiagnosticTurnaroundSegmentKey,
} from '../../types/diagnostic';
import { diagnosticCategoryLabels, diagnosticNotificationTime } from '../../utils/diagnosticLabels';
import { toLocalDateString } from '../../utils/formatters';
import styles from './LabTurnaroundReport.module.css';

const MAX_RANGE_DAYS = 31;
const DEFAULT_RANGE_DAYS = 7;
const ALL_CATEGORIES = 'All';
const DAY_MS = 24 * 60 * 60 * 1000;

const SEGMENT_ORDER: DiagnosticTurnaroundSegmentKey[] = ['OrderToReceive', 'ReceiveToResult', 'Total'];
const SEGMENT_LABELS: Partial<Record<DiagnosticTurnaroundSegmentKey, string>> = {
    OrderToReceive: 'Chỉ định → nhận mẫu',
    ReceiveToResult: 'Nhận mẫu → có kết quả',
    Total: 'Tổng (chỉ định → trả kết quả)',
};

const categoryOptions = [
    { value: ALL_CATEGORIES, label: 'Tất cả' },
    ...(Object.keys(diagnosticCategoryLabels) as DiagnosticCategory[]).map(value => ({ value, label: diagnosticCategoryLabels[value] })),
];

/** Today's calendar date in Vietnam, regardless of the device timezone. */
function vietnamToday(): string {
    const parts = new Intl.DateTimeFormat('en-US', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit' })
        .formatToParts(new Date());
    const part = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find(item => item.type === type)?.value);
    return toLocalDateString(new Date(part('year'), part('month') - 1, part('day')));
}

function addDays(date: string, days: number): string {
    const [year, month, day] = date.split('-').map(Number);
    return toLocalDateString(new Date(year, month - 1, day + days));
}

function utcDay(date: string): number {
    const [year, month, day] = date.split('-').map(Number);
    return Date.UTC(year, month - 1, day);
}

function validateRange(from: string, to: string): string | null {
    if (!from || !to) return 'Vui lòng chọn đủ Từ ngày và Đến ngày.';
    if (from > to) return 'Từ ngày phải trước hoặc bằng Đến ngày.';
    if ((utcDay(to) - utcDay(from)) / DAY_MS + 1 > MAX_RANGE_DAYS) return `Khoảng thời gian tối đa là ${MAX_RANGE_DAYS} ngày.`;
    return null;
}

const formatMinutes = (value: number | null | undefined) =>
    value == null ? '—' : value.toLocaleString('vi-VN', { maximumFractionDigits: 1 });
const formatPercent = (value: number | null | undefined) => value == null ? '—' : `${formatMinutes(value)}%`;

function defaultParams(): DiagnosticTurnaroundReportParams {
    const to = vietnamToday();
    return { from: addDays(to, -(DEFAULT_RANGE_DAYS - 1)), to };
}

export const LabTurnaroundReport = () => {
    const [applied, setApplied] = useState<DiagnosticTurnaroundReportParams>(defaultParams);
    const [draftFrom, setDraftFrom] = useState(applied.from);
    const [draftTo, setDraftTo] = useState(applied.to);
    const [draftCategory, setDraftCategory] = useState<DiagnosticCategory | typeof ALL_CATEGORIES>(ALL_CATEGORIES);
    const [validationError, setValidationError] = useState('');
    const [report, setReport] = useState<DiagnosticTurnaroundReportDto | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');

    const load = useCallback(async (params: DiagnosticTurnaroundReportParams, signal?: AbortSignal) => {
        setLoading(true); setError('');
        try {
            const response = await diagnosticApi.getTurnaroundReport(params);
            if (signal?.aborted) return;
            if (!response.success || !response.data) throw new Error(response.message || 'Không thể tải báo cáo thời gian trả kết quả.');
            setReport(response.data);
        } catch (failure: unknown) {
            if (!signal?.aborted) {
                setReport(null);
                setError((failure as { message?: string })?.message || 'Không thể tải báo cáo thời gian trả kết quả.');
            }
        } finally {
            if (!signal?.aborted) setLoading(false);
        }
    }, []);

    useEffect(() => {
        const controller = new AbortController();
        void Promise.resolve().then(() => { if (!controller.signal.aborted) void load(applied, controller.signal); });
        return () => controller.abort();
    }, [applied, load]);

    const apply = () => {
        const message = validateRange(draftFrom, draftTo);
        setValidationError(message ?? '');
        if (message) return;
        setApplied({ from: draftFrom, to: draftTo, ...(draftCategory === ALL_CATEGORIES ? {} : { category: draftCategory }) });
    };

    const segments = SEGMENT_ORDER.map(key => report?.segments.find(segment => segment.key === key) ?? { key, count: 0, medianMinutes: null, p90Minutes: null });
    const total = segments[segments.length - 1];

    return <div className={styles.page}>
        <PageHeader
            title="Thời gian trả kết quả"
            subtitle="Từ lúc chỉ định đến lúc hoàn tất phiếu, trong các cơ sở được phân công"
            actions={<Button disabled={loading} onClick={() => void load(applied)}><RefreshCw size={16} aria-hidden="true" />{loading ? 'Đang tải...' : 'Làm mới'}</Button>}
        />
        <FilterBar>
            <label className={styles.field}>
                <span className={styles.fieldLabel}>Từ ngày</span>
                <Input type="date" className={styles.dateInput} value={draftFrom} max={draftTo || undefined} onChange={event => setDraftFrom(event.target.value)} />
            </label>
            <label className={styles.field}>
                <span className={styles.fieldLabel}>Đến ngày</span>
                <Input type="date" className={styles.dateInput} value={draftTo} min={draftFrom || undefined} onChange={event => setDraftTo(event.target.value)} />
            </label>
            <div className={styles.field}>
                <span className={styles.fieldLabel} id="lab-tat-category-label">Loại dịch vụ</span>
                <Select
                    aria-labelledby="lab-tat-category-label"
                    className={styles.categorySelect}
                    value={draftCategory}
                    options={categoryOptions}
                    onChange={value => setDraftCategory(value)}
                />
            </div>
            <Button type="primary" className={styles.applyButton} disabled={loading} onClick={apply}>Xem</Button>
        </FilterBar>
        {validationError && <Typography.Text type="danger" role="alert" className={styles.validation}>{validationError}</Typography.Text>}

        {loading ? <LoadingState message="Đang tải báo cáo thời gian trả kết quả..." />
            : error ? <InlineError message={error} onRetry={() => void load(applied)} />
            : !report || report.orderCount === 0 ? <EmptyState title="Không có phiếu hoàn tất trong khoảng thời gian này" />
            : <>
                <div className={styles.stats}>
                    <StatCard title="Số phiếu hoàn tất" value={report.orderCount} icon={<ClipboardList size={24} />} />
                    <StatCard title="Trung vị tổng TAT (phút)" value={formatMinutes(total.medianMinutes)} icon={<Timer size={24} />} color="info" />
                    <StatCard title="P90 tổng TAT (phút)" value={formatMinutes(total.p90Minutes)} icon={<Clock size={24} />} color="warning" />
                    <StatCard title="Đạt chỉ tiêu" value={formatPercent(report.withinTargetPercent)} subtitle={`${report.withinTargetCount}/${total.count} phiếu`} icon={<CheckCircle2 size={24} />} color="success" />
                </div>

                <section className={styles.section} aria-labelledby="lab-tat-segments">
                    <Typography.Title level={2} id="lab-tat-segments" className={styles.sectionTitle}>Theo giai đoạn</Typography.Title>
                    <div className={styles.table}>
                        <DataTable<DiagnosticTurnaroundSegmentDto> ariaLabel="Theo giai đoạn" data={segments} keyExtractor={segment => segment.key} columns={[
                            { header: 'Giai đoạn', accessor: segment => SEGMENT_LABELS[segment.key] },
                            { header: 'Số phiếu có dữ liệu', accessor: 'count', align: 'right' },
                            { header: 'Trung vị', accessor: segment => formatMinutes(segment.medianMinutes), align: 'right' },
                            { header: 'P90', accessor: segment => formatMinutes(segment.p90Minutes), align: 'right' },
                        ]} />
                    </div>
                </section>

                <section className={styles.section} aria-labelledby="lab-tat-services">
                    <Typography.Title level={2} id="lab-tat-services" className={styles.sectionTitle}>Theo dịch vụ</Typography.Title>
                    <div className={styles.table}>
                        <DataTable ariaLabel="Theo dịch vụ" data={report.byService} keyExtractor={service => service.serviceCode} columns={[
                            { header: 'Mã DV', accessor: 'serviceCode' },
                            { header: 'Dịch vụ', accessor: 'serviceName' },
                            { header: 'Loại', accessor: service => diagnosticCategoryLabels[service.category] ?? service.category },
                            { header: 'Số phiếu', accessor: 'orderCount', align: 'right' },
                            { header: 'Trung vị (phút)', accessor: service => formatMinutes(service.medianTotalMinutes), align: 'right' },
                            {
                                header: 'P90 (phút)', align: 'right',
                                accessor: service => <span className={service.p90TotalMinutes != null && service.p90TotalMinutes > service.targetMinutes ? styles.overTarget : undefined}>
                                    {formatMinutes(service.p90TotalMinutes)}
                                </span>,
                            },
                            { header: 'Chỉ tiêu (phút)', accessor: 'targetMinutes', align: 'right' },
                            { header: 'Đạt chỉ tiêu', accessor: service => formatPercent(service.withinTargetPercent), align: 'right' },
                        ]} />
                    </div>
                </section>

                <section className={styles.section} aria-labelledby="lab-tat-slowest">
                    <Typography.Title level={2} id="lab-tat-slowest" className={styles.sectionTitle}>Phiếu vượt chỉ tiêu</Typography.Title>
                    <div className={styles.table}>
                        <DataTable ariaLabel="Phiếu vượt chỉ tiêu" data={report.slowestOrders} keyExtractor={order => order.orderId}
                            emptyText="Không có phiếu vượt chỉ tiêu." columns={[
                                { header: 'Mã phiếu', accessor: order => <Link className={styles.link} to={`/lab/orders/${order.orderId}`}>{order.orderCode}</Link> },
                                { header: 'Bệnh nhân', accessor: 'patientName' },
                                { header: 'Dịch vụ', accessor: order => order.serviceNames.join(', ') },
                                { header: 'Chỉ định lúc', accessor: order => diagnosticNotificationTime(order.orderedAtUtc) },
                                { header: 'Hoàn tất lúc', accessor: order => diagnosticNotificationTime(order.completedAtUtc) },
                                { header: 'Tổng TAT (phút)', accessor: order => <span className={styles.overTarget}>{formatMinutes(order.totalMinutes)}</span>, align: 'right' },
                                { header: 'Chỉ tiêu (phút)', accessor: 'targetMinutes', align: 'right' },
                            ]} />
                    </div>
                </section>
            </>}
    </div>;
};
