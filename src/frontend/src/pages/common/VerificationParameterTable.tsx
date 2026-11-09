import { memo, useMemo } from 'react';
import { Tag } from 'antd';
import { DataTable, type DataTableColumn } from '../../components/common/DataTable';
import type { DiagnosticResultValueDto } from '../../types/diagnostic';
import { formatDelta, isCriticalDiagnosticFlag } from '../../utils/diagnosticLabels';
import styles from './DiagnosticVerificationReview.module.css';

const resultKey = (value: DiagnosticResultValueDto) => value.parameterId;

export const VerificationParameterTable = memo(function VerificationParameterTable({ serviceName, values }: {
    serviceName: string;
    values: DiagnosticResultValueDto[];
}) {
    const columns = useMemo<DataTableColumn<DiagnosticResultValueDto>[]>(() => [
        { header: 'Chỉ số', accessor: value => `${value.parameterName} (${value.parameterCode})` },
        { header: 'Kết quả', accessor: value => <span className={isCriticalDiagnosticFlag(value.flag) ? styles.critical : undefined}>{value.value}</span> },
        { header: 'Đơn vị', accessor: 'unit' },
        { header: 'Tham chiếu', accessor: 'referenceRangeText' },
        { header: 'Cờ', accessor: value => <Tag className={isCriticalDiagnosticFlag(value.flag) ? styles.critical : undefined}>{value.flag === 'Normal' ? 'Bình thường' : `${value.flag.endsWith('High') ? 'H' : 'L'}${isCriticalDiagnosticFlag(value.flag) ? ' — NGUY HIỂM' : ''}`}</Tag> },
        { header: 'So với lần trước', accessor: value => <>{formatDelta(value)}{value.isDeltaFailure && <> <Tag color="warning">Lệch delta</Tag></>}</> },
    ], []);
    return <DataTable ariaLabel={`Chỉ số: ${serviceName}`} data={values} keyExtractor={resultKey} columns={columns} />;
});
