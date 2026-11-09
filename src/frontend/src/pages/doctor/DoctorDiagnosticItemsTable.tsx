import { memo, useMemo } from 'react';
import { Tag } from 'antd';
import { DataTable, type DataTableColumn } from '../../components/common/DataTable';
import { DiagnosticAttachmentList } from '../../components/diagnostics/DiagnosticAttachmentList';
import type { DiagnosticOrderDto } from '../../types';
import type { DiagnosticResultValueDto } from '../../types/diagnostic';
import { diagnosticFlagTone, diagnosticResultFlagLabels, formatDelta, specimenStatusLabels, specimenRejectionLabels } from '../../utils/diagnosticLabels';
import { categoryMap, statusBadge } from './diagnosticTableLabels';
import tabStyles from './DoctorExaminationWorkspace.module.css';

const valueKey = (value: DiagnosticResultValueDto) => value.parameterId;
const itemKey = (item: DiagnosticOrderDto['items'][number]) => item.id;
const DoctorDiagnosticValuesTable = memo(function DoctorDiagnosticValuesTable({ serviceName, values }: { serviceName: string; values: DiagnosticResultValueDto[] }) {
    const columns = useMemo<DataTableColumn<DiagnosticResultValueDto>[]>(() => [
        { header: 'Chỉ số', accessor: value => `${value.parameterName} (${value.parameterCode})` },
        { header: 'Kết quả + đơn vị', accessor: value => <span className={tabStyles[diagnosticFlagTone(value.flag)]}>{value.value} {value.unit}</span> },
        { header: 'Tham chiếu', accessor: 'referenceRangeText' },
        { header: 'Đánh giá', accessor: value => <span className={tabStyles[diagnosticFlagTone(value.flag)]}>{diagnosticResultFlagLabels[value.flag]}</span> },
        { header: 'Lần trước', accessor: value => <>{formatDelta(value)}{value.isDeltaFailure && <> <Tag color="warning">Lệch delta</Tag></>}</> },
    ], []);
    return <DataTable ariaLabel={'Chỉ số: ' + serviceName} data={values} keyExtractor={valueKey} columns={columns} />;
});

export const DoctorDiagnosticItemsTable = memo(function DoctorDiagnosticItemsTable({ items, verificationStatus }: {
    items: DiagnosticOrderDto['items'];
    verificationStatus: DiagnosticOrderDto['verificationStatus'];
}) {
    const columns = useMemo<DataTableColumn<DiagnosticOrderDto['items'][number]>[]>(() => [
        {
            header: "Dịch vụ chỉ định", accessor: (item) => {
                return <>
                    <div className={tabStyles.itemTitle}>{item.serviceName}</div>
                    {item.specimenStatus && item.specimenStatus !== 'NotRequired' && <span className={item.specimenStatus === 'Rejected' ? tabStyles.specimenRejected : tabStyles.specimenStatus}>
                        {item.specimenStatus === 'Rejected' ? `Mẫu bị từ chối – đang lấy lại: ${item.rejectionReason ? specimenRejectionLabels[item.rejectionReason] : '—'}` : specimenStatusLabels[item.specimenStatus]}
                    </span>}
                    {item.result?.hasCriticalValue && <span className={tabStyles.criticalBadge}>Giá trị nguy hiểm</span>}
                    <div className={tabStyles.secondaryCaption}>{item.serviceCode}</div>
                </>;
            }
        },
        {
            header: "Phân loại", accessor: (item) => {
                return <>
                    {categoryMap[item.category] || item.category}
                </>;
            }
        },
        {
            header: "Trạng thái", accessor: (item) => {
                return <>
                    {statusBadge(item.status)}
                </>;
            }
        },
        {
            header: "Kết quả đo / Trị số", accessor: (item) => {
                return <>
                    {verificationStatus === 'Pending' ? <span className={tabStyles.emptyValue}>Đang chờ phòng xét nghiệm duyệt</span> : verificationStatus === 'Returned' ? <span className={tabStyles.emptyValue}>KTV đang sửa kết quả</span> : item.result?.values?.length ? (
                        <div className={tabStyles.parameterTable}><DoctorDiagnosticValuesTable serviceName={item.serviceName} values={item.result.values} /></div>
                    ) : item.result ? (
                        <div className={tabStyles.diagnosticResultText}>
                            {item.result.resultText || '--'} {item.result.unit}
                        </div>
                    ) : (
                        <span className={tabStyles.emptyValue}>Chưa có</span>
                    )}
                    {!!item.attachments?.length && <DiagnosticAttachmentList attachments={item.attachments} compact />}
                </>;
            }
        },
        {
            header: "Chỉ số tham chiếu", accessor: (item) => {
                return <>
                    {item.result?.referenceRange || '--'}
                </>;
            }
        },
        {
            header: "Kết luận / Nhận xét", accessor: (item) => {
                return <>
                    {item.result ? (
                        <div>
                            {item.result.conclusion && (
                                <div className={tabStyles.itemTitle}>{item.result.conclusion}</div>
                            )}
                            <div className={tabStyles.diagnosticResultMetadata}>
                                KTV: {item.result.resultedByUserName} • {new Date(item.result.resultedAtUtc).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}
                            </div>
                        </div>
                    ) : (
                        <span className={tabStyles.emptyValue}>--</span>
                    )}
                </>;
            }
        }
    ], [verificationStatus]);
    return <DataTable data={items} keyExtractor={itemKey} columns={columns} />;
});
