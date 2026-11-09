import { memo, useCallback, useMemo } from 'react';
import type { Dispatch, SetStateAction } from 'react';
import { Button, Input, Tag } from 'antd';
import { Trash2 } from 'lucide-react';
import { DataTable, type DataTableColumn } from '../../components/common/DataTable';
import { updatePrescriptionItem, type PrescriptionEditorItem } from './prescriptionQuantity';
import tabStyles from './DoctorExaminationWorkspace.module.css';

const medicineKey = (item: PrescriptionEditorItem) => item.medicineId;
export const DoctorPrescriptionTable = memo(function DoctorPrescriptionTable({ prescriptionItems, dangerMedicineIds, setPrescriptionItems }: {
    prescriptionItems: PrescriptionEditorItem[];
    dangerMedicineIds: Set<number>;
    setPrescriptionItems: Dispatch<SetStateAction<PrescriptionEditorItem[]>>;
}) {
    const handleRemoveMedicine = useCallback((index: number) => {
        setPrescriptionItems(prev => prev.filter((_, idx) => idx !== index));
    }, [setPrescriptionItems]);
    const handleItemChange = useCallback((index: number, field: string, value: string | number) => {
        setPrescriptionItems(prev => {
            const updated = [...prev];
            updated[index] = updatePrescriptionItem(updated[index], field, value);
            return updated;
        });
    }, [setPrescriptionItems]);
    const columns = useMemo<DataTableColumn<PrescriptionEditorItem>[]>(() => [
        { header: "#", accessor: (item) => { const idx = prescriptionItems.indexOf(item); return <>{idx + 1}</>; } },
        {
            header: "Tên thuốc", accessor: (item) => {
                return <>
                    <div className={tabStyles.itemTitle}>{item.medicineName}</div>
                    {dangerMedicineIds.has(item.medicineId) && <Tag color="error">Nguy hiểm</Tag>}
                    <div className={tabStyles.secondaryCaption}>
                        {item.medicineCode} • {item.unit}
                    </div>
                </>;
            }
        },
        {
            header: "Số lượng", width: '90px', accessor: (item) => {
                const idx = prescriptionItems.indexOf(item); return <>
                    <Input
                        type="number"
                        min="1"
                        value={item.quantity}
                        onChange={(e) => handleItemChange(idx, 'quantity', parseInt(e.target.value, 10) || 1)}
                        className={[tabStyles.formControl].join(' ')}
                    />
                </>;
            }
        },
        {
            header: "Liều dùng", width: '90px', accessor: (item) => {
                const idx = prescriptionItems.indexOf(item); return <>
                    <Input
                        type="text"
                        value={item.dosage}
                        onChange={(e) => handleItemChange(idx, 'dosage', e.target.value)}
                        placeholder="1 viên"
                        className={[tabStyles.formControl].join(' ')}
                    />
                </>;
            }
        },
        {
            header: "Tần suất", width: '220px', accessor: (item) => {
                const idx = prescriptionItems.indexOf(item); return <>
                    <Input
                        type="text"
                        value={item.frequency}
                        onChange={(e) => handleItemChange(idx, 'frequency', e.target.value)}
                        placeholder="Ngày 2 lần"
                        className={[tabStyles.formControl].join(' ')}
                    />
                </>;
            }
        },
        {
            header: "Số ngày", width: '70px', accessor: (item) => {
                const idx = prescriptionItems.indexOf(item); return <>
                    <Input
                        type="number"
                        min="1"
                        max="90"
                        value={item.durationDays}
                        onChange={(e) => handleItemChange(idx, 'durationDays', parseInt(e.target.value, 10) || 1)}
                        className={[tabStyles.formControl].join(' ')}
                    />
                </>;
            }
        },
        {
            header: "Hướng dẫn uống", width: '230px', accessor: (item) => {
                const idx = prescriptionItems.indexOf(item); return <>
                    <Input
                        type="text"
                        value={item.instructions}
                        onChange={(e) => handleItemChange(idx, 'instructions', e.target.value)}
                        placeholder="Uống sau khi ăn 30 phút..."
                        className={[tabStyles.formControl].join(' ')}
                    />
                </>;
            }
        },
        {
            header: "", width: '36px', accessor: (item) => {
                const idx = prescriptionItems.indexOf(item); return <>
                    <Button
                        htmlType="button"
                        onClick={() => handleRemoveMedicine(idx)}

                        title="Xóa thuốc khỏi đơn" className={[tabStyles.iconActionButton, tabStyles.dangerText].join(' ')}
                    >
                        <Trash2 size={16} />
                    </Button>
                </>;
            }
        }
    ], [dangerMedicineIds, handleItemChange, handleRemoveMedicine, prescriptionItems]);
    return <DataTable data={prescriptionItems} keyExtractor={medicineKey} columns={columns} />;
});
