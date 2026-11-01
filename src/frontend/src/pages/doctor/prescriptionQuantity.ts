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
    const parseFrequency = (text: string) => {
        const normalized = text.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '');
        const interval = normalized.match(/\bcach\s+(\d+(?:[.,]\d+)?)\s*(?:gio|tieng)\b/);
        if (interval) {
            const hours = parseFirst(interval[1]);
            return hours > 0 ? Math.max(1, Math.floor(24 / hours)) : NaN;
        }
        const occurrences = normalized.match(/\b(\d+(?:[.,]\d+)?)\s*lan\b/);
        if (occurrences) return parseFirst(occurrences[1]);
        const periods = text.toLowerCase().normalize('NFC').match(/\b(?:sáng|trưa|chiều|tối)\b/g);
        if (periods) return new Set(periods).size;
        const numbers = normalized.match(/\d+(?:[.,]\d+)?/g);
        return numbers?.length === 1 ? parseFirst(numbers[0]) : NaN;
    };
    const quantity = Math.ceil(parseFirst(item.dosage) * parseFrequency(item.frequency) * item.durationDays);
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
