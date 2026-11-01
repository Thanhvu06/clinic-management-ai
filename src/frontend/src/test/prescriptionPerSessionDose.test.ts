import { describe, expect, it } from 'vitest';
import { calculatePrescriptionQuantity, createPrescriptionItem, updatePrescriptionItem } from '../pages/doctor/prescriptionQuantity';

describe('Prescription doses for individual day periods', () => {
    it.each([
        ['Sáng 0,5, tối 1', '1 viên', 3, 5],
        ['Sáng 1 trưa 1 tối 1', '1 viên', 5, 15],
        ['sáng 2 viên, tối 1 viên', '2 viên', 4, 12],
        ['Sáng - tối', '1 viên', 5, 10],
    ])('calculates %s, dosage %s, for %i days as %i tablets', (frequency, dosage, durationDays, expected) => {
        expect(calculatePrescriptionQuantity({ quantity: 23, dosage, frequency, durationDays })).toBe(expected);
    });

    it('ignores an unparseable dosage when every day period specifies its own dose', () => {
        expect(calculatePrescriptionQuantity({ quantity: 23, dosage: 'Theo chỉ dẫn', frequency: 'sáng 2 viên, tối 1 viên', durationDays: 4 })).toBe(12);
    });

    it('keeps explicit daily frequency precedence when a day period has no dose', () => {
        expect(calculatePrescriptionQuantity({ quantity: 23, dosage: '2 viên', frequency: 'Ngày 1 lần (sáng 2 hoặc tối)', durationDays: 3 })).toBe(6);
    });

    it('counts periods using dosage when only some periods have a dose', () => {
        expect(calculatePrescriptionQuantity({ quantity: 23, dosage: '2 viên', frequency: 'sáng 0,5, tối', durationDays: 3 })).toBe(12);
    });

    it('matches uppercase Vietnamese periods with decomposed accents', () => {
        expect(calculatePrescriptionQuantity({ quantity: 23, dosage: '2 viên', frequency: 'CHIỀU 0,5, TỐI 1'.normalize('NFD'), durationDays: 3 })).toBe(5);
    });

    it('does not match period names embedded in Unicode words', () => {
        expect(calculatePrescriptionQuantity({ quantity: 23, dosage: '1 viên', frequency: 'đósáng 2, tốiđa 1', durationDays: 3 })).toBe(23);
    });

    it('preserves a manually edited quantity for a schedule with per-period doses', () => {
        const medicine = { id: 1, code: 'TEST', name: 'Thuốc kiểm thử', unit: 'viên', stockQuantity: 100 };
        const edited = updatePrescriptionItem(createPrescriptionItem(medicine), 'quantity', 17);
        expect(updatePrescriptionItem(edited, 'frequency', 'sáng 2 viên, tối 1 viên').quantity).toBe(17);
    });
});
