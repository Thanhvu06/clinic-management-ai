import { memo, useCallback, useMemo } from 'react';
import { DataTable, type DataTableColumn } from '../../components/common/DataTable';
import type { PatientClinicalContextDto } from '../../types';
import { formatDisplayDate } from '../../utils/formatters';
import { formatDecimalVi } from '../../utils/formatDecimalVi';
import { formatVitalBodyMeasures } from './vitalHistoryFormat';

type History = NonNullable<PatientClinicalContextDto['vitalHistory']>;
export const DoctorVitalHistoryTable = memo(function DoctorVitalHistoryTable({ data }: { data: History }) {
    const keyExtractor = useCallback((item: History[number]) => data.indexOf(item), [data]);
    const columns = useMemo<DataTableColumn<History[number]>[]>(() => [
        {
            header: "Thời điểm đo", accessor: (item) => {
                return <>
                    {formatDisplayDate(item.recordedAtUtc)} {new Date(item.recordedAtUtc).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}
                </>;
            }
        },
        {
            header: "Huyết áp (mmHg)", accessor: (item) => {
                return <>
                    {item.bloodPressureSystolic && item.bloodPressureDiastolic ? `${item.bloodPressureSystolic}/${item.bloodPressureDiastolic}` : '--'}
                </>;
            }
        },
        { header: "Mạch (nhịp/phút)", accessor: (item) => { return <>{item.heartRate || '--'}</>; } },
        { header: "Nhiệt độ (°C)", accessor: (item) => { return <>{item.temperature ? `${formatDecimalVi(item.temperature)}°C` : '--'}</>; } },
        { header: "SpO2 (%)", accessor: (item) => { return <>{item.spO2 ? `${formatDecimalVi(item.spO2)}%` : '--'}</>; } },
        { header: "BMI", accessor: formatVitalBodyMeasures },
        { header: "Người đo", accessor: (item) => { return <>{item.recordedByUserName || '--'}</>; } }
    ], []);
    return <DataTable data={data} keyExtractor={keyExtractor} columns={columns} />;
});
