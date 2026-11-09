import { memo, useMemo } from 'react';
import { Input, Tag } from 'antd';
import { DataTable, type DataTableColumn } from '../../components/common/DataTable';
import type { DiagnosticResultValueDto, DiagnosticServiceParameterDto } from '../../types/diagnostic';
import { diagnosticFlagTone, diagnosticResultFlagLabels, formatDelta, previewDiagnosticFlag } from '../../utils/diagnosticLabels';
import styles from './TechnicianOrderDetail.module.css';

const resultKey = (value: DiagnosticResultValueDto) => value.parameterId;
const parameterKey = (parameter: DiagnosticServiceParameterDto) => parameter.id;

export const TechnicianSavedParameters = memo(function TechnicianSavedParameters({ serviceName, values }: {
    serviceName: string;
    values: DiagnosticResultValueDto[];
}) {
    const columns = useMemo<DataTableColumn<DiagnosticResultValueDto>[]>(() => [
        { header: 'Chỉ số', accessor: value => `${value.parameterName} (${value.parameterCode})` },
        { header: 'Kết quả', accessor: value => value.value },
        { header: 'Đơn vị', accessor: 'unit' },
        { header: 'Khoảng tham chiếu', accessor: 'referenceRangeText' },
        { header: 'Đánh giá', accessor: value => <span className={styles[diagnosticFlagTone(value.flag)]}>{diagnosticResultFlagLabels[value.flag]}</span> },
        { header: 'So với lần trước', accessor: value => <>{formatDelta(value)}{value.isDeltaFailure && <> <Tag color="warning">Lệch delta</Tag></>}</> },
    ], []);
    return <DataTable ariaLabel={`Chỉ số đã lưu: ${serviceName}`} data={values} keyExtractor={resultKey} columns={columns} />;
});

export const TechnicianParameterInputs = memo(function TechnicianParameterInputs({ itemId, serviceName, parameters, values, disabled, onValueChange }: {
    itemId: number;
    serviceName: string;
    parameters: DiagnosticServiceParameterDto[];
    values?: Record<number, string>;
    disabled: boolean;
    onValueChange: (itemId: number, parameterId: number, value: string) => void;
}) {
    const columns = useMemo<DataTableColumn<DiagnosticServiceParameterDto>[]>(() => [
        { header: 'Chỉ số', accessor: parameter => `${parameter.name} (${parameter.code})` },
        { header: 'Kết quả', accessor: parameter => <Input type="number" step="any" min={0} aria-label={`Kết quả ${parameter.name} (${parameter.code})`} disabled={disabled}
            value={values?.[parameter.id] ?? ''} onChange={event => onValueChange(itemId, parameter.id, event.target.value)} /> },
        { header: 'Đơn vị', accessor: 'unit' },
        { header: 'Khoảng tham chiếu', accessor: 'referenceRangeText' },
        { header: 'Đánh giá', accessor: parameter => {
            const raw = values?.[parameter.id] ?? '';
            if (!raw.trim() || !Number.isFinite(Number(raw))) return '—';
            const flag = previewDiagnosticFlag(Number(raw), parameter);
            return <span className={styles[diagnosticFlagTone(flag)]}>{diagnosticResultFlagLabels[flag]}</span>;
        } },
    ], [disabled, itemId, onValueChange, values]);
    return <DataTable ariaLabel={`Nhập chỉ số: ${serviceName}`} data={parameters} keyExtractor={parameterKey} columns={columns} />;
});
