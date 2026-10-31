import { describe, expect, it } from 'vitest';
import { calculatePrescriptionQuantity, createPrescriptionItem, updatePrescriptionItem } from '../pages/doctor/prescriptionQuantity';

const medicine = { id: 1, code: 'PARA', name: 'Paracetamol', unit: 'viên', stockQuantity: 100 };

describe('Prescription quantity review additions', () => {
    it('rounds fractional prescription quantities up with Math.ceil', () => {
        expect(calculatePrescriptionQuantity({
            quantity: 1,
            dosage: '0.5 viên',
            frequency: 'Ngày 1 lần',
            durationDays: 5,
        })).toBe(3);

        let item = createPrescriptionItem(medicine);
        item = updatePrescriptionItem(item, 'dosage', '1,5 viên');
        item = updatePrescriptionItem(item, 'frequency', 'Ngày 1 lần');
        item = updatePrescriptionItem(item, 'durationDays', 3);
        expect(item.quantity).toBe(5);
    });

    it('sums multiple numbers in frequency to determine daily occurrences', () => {
        const morningEvening = updatePrescriptionItem(createPrescriptionItem(medicine), 'frequency', 'Sáng 1, tối 1');
        expect(morningEvening.quantity).toBe(10);

        const threeTimes = updatePrescriptionItem(createPrescriptionItem(medicine), 'frequency', 'Sáng 1 trưa 1 tối 1');
        expect(threeTimes.quantity).toBe(15);

        expect(calculatePrescriptionQuantity({
            quantity: 1,
            dosage: '1 viên',
            frequency: 'Sáng 0,5, tối 1',
            durationDays: 3,
        })).toBe(5);
    });
});
