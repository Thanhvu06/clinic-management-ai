export interface PrescriptionQuantity {
    quantity: number;
    dosage: string;
    frequency: string;
    durationDays: number;
    quantityEdited?: boolean;
}

export interface PrescriptionEditorItem extends PrescriptionQuantity {
    medicineId: number;
    medicineCode: string;
    medicineName: string;
    unit: string;
    availableStock: number;
    instructions: string;
}

export function calculatePrescriptionQuantity(item: PrescriptionQuantity): number {
    if (item.quantityEdited) return item.quantity;
    const parseFirst = (text: string) => {
        const match = text.match(/\d+(?:[.,]\d+)?/);
        return match ? Number(match[0].replace(',', '.')) : NaN;
    };
    const quantity = parseFirst(item.dosage) * parseFirst(item.frequency) * item.durationDays;
    return Number.isFinite(quantity) && quantity > 0 ? quantity : item.quantity;
}

export function createPrescriptionItem(medicine: { id: number; code: string; name: string; unit: string; stockQuantity: number }): PrescriptionEditorItem {
    const item = {
        medicineId: medicine.id, medicineCode: medicine.code, medicineName: medicine.name,
        unit: medicine.unit, availableStock: medicine.stockQuantity, quantity: 1,
        dosage: '1 viên', frequency: 'Ngày 2 lần', durationDays: 5, instructions: 'Uống sau bữa ăn',
    };
    return { ...item, quantity: calculatePrescriptionQuantity(item) };
}

export function updatePrescriptionItem(item: PrescriptionEditorItem, field: string, value: string | number): PrescriptionEditorItem {
    const updated = { ...item, [field]: value, quantityEdited: item.quantityEdited || field === 'quantity' };
    if (['dosage', 'frequency', 'durationDays'].includes(field)) updated.quantity = calculatePrescriptionQuantity(updated);
    return updated;
}
