import { describe, expect, it } from 'vitest';
import { calculatePrescriptionQuantity, createPrescriptionItem, updatePrescriptionItem } from '../pages/doctor/prescriptionQuantity';

const medicine = { id: 1, code: 'TEST', name: 'Thuốc kiểm thử', unit: 'viên', stockQuantity: 100 };

describe('Prescription frequency precedence', () => {
    it.each([
        ['Ngày 2 lần', 2],
        ['Cách 8 giờ 1 lần', 3],
        ['cách 6 tiếng', 4],
        ['Ngày 2 lần, mỗi lần 2 viên', 2],
        ['1 lần/ngày buổi sáng', 1],
        ['Sáng - tối', 2],
        ['sáng, trưa, tối', 3],
        ['2 lần / ngày (sáng - tối)', 2],
    ])('parses %s as %i occurrences per day', (frequency, expected) => {
        expect(calculatePrescriptionQuantity({ quantity: 17, dosage: '1 viên', frequency, durationDays: 1 })).toBe(expected);
    });

    it('preserves a manually edited quantity when the frequency changes', () => {
        const edited = updatePrescriptionItem(createPrescriptionItem(medicine), 'quantity', 17);
        const updated = updatePrescriptionItem(edited, 'frequency', 'Cách 8 giờ 1 lần');
        expect(updated.quantityEdited).toBe(true);
        expect(updated.quantity).toBe(17);
    });

    it.each(['Khi cần', 'Tôi dùng khi cần', '2 hoặc 3', 'cách 0 giờ'])('keeps the current quantity for an indeterminate frequency: %s', (frequency) => {
        expect(calculatePrescriptionQuantity({ quantity: 17, dosage: '1 viên', frequency, durationDays: 3 })).toBe(17);
    });

    it.each([
        ['cách 7 giờ', 3],
        ['cách 48 tiếng', 1],
        ['2', 2],
        ['sáng, sáng và tối', 2],
    ])('handles interval rounding and frequency fallbacks: %s', (frequency, expected) => {
        expect(calculatePrescriptionQuantity({ quantity: 17, dosage: '1 viên', frequency, durationDays: 1 })).toBe(expected);
    });

    it('rounds the final quantity up for fractional dosages', () => {
        expect(calculatePrescriptionQuantity({ quantity: 17, dosage: '0,5 viên', frequency: 'Cách 8 giờ 1 lần', durationDays: 3 })).toBe(5);
    });
});
