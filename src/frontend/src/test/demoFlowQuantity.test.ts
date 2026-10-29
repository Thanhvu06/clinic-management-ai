import { describe, expect, it } from 'vitest';
import { calculatePrescriptionQuantity, createPrescriptionItem, updatePrescriptionItem } from '../pages/doctor/prescriptionQuantity';

const medicine = { id: 1, code: 'PARA', name: 'Paracetamol', unit: 'viên', stockQuantity: 100 };
describe('Prescription quantity calculation', () => {
    it('sets quantity to 10 when adding medicine with the default 1 tablet, twice daily for 5 days', () => {
        expect(createPrescriptionItem(medicine).quantity).toBe(10);
    });
    it.each([
        ['dosage', '2 viên', 20], ['frequency', 'Ngày 3 lần', 15], ['durationDays', 7, 14],
        ['dosage', '0,5 viên', 5], ['frequency', '2 lần/ngày', 10],
    ])('recalculates after editing %s to %s', (field, value, expected) => {
        expect(updatePrescriptionItem(createPrescriptionItem(medicine), field as string, value as string | number).quantity).toBe(expected);
    });
    it('preserves a manually entered quantity through all dosage schedule changes', () => {
        let item = updatePrescriptionItem(createPrescriptionItem(medicine), 'quantity', 12);
        item = updatePrescriptionItem(item, 'dosage', '2 viên');
        item = updatePrescriptionItem(item, 'frequency', 'Ngày 3 lần');
        item = updatePrescriptionItem(item, 'durationDays', 7);
        expect(item.quantity).toBe(12);
        expect(calculatePrescriptionQuantity(item)).toBe(12);
    });
    it.each([['dosage', 'Theo chỉ dẫn'], ['frequency', 'Khi cần'], ['durationDays', 0]])('keeps the current quantity when %s cannot produce a valid quantity', (field, value) => {
        expect(updatePrescriptionItem(createPrescriptionItem(medicine), field, value).quantity).toBe(10);
    });
});
