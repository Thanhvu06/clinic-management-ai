import { describe, it, expect } from 'vitest';

export const calculateBmi = (weightKg?: number, heightCm?: number): { bmi: number | null; category: string } => {
    if (!weightKg || !heightCm || weightKg <= 0 || heightCm <= 0) {
        return { bmi: null, category: '' };
    }
    const heightM = heightCm / 100;
    const rawBmi = weightKg / (heightM * heightM);
    const bmi = Math.round(rawBmi * 10) / 10;

    let category = '';
    if (bmi < 18.5) category = 'Gầy / Thiếu cân';
    else if (bmi < 23) category = 'Bình thường';
    else if (bmi < 25) category = 'Tiền béo phì / Thừa cân';
    else if (bmi < 30) category = 'Béo phì độ I';
    else category = 'Béo phì độ II trở lên';

    return { bmi, category };
};

describe('Clinical BMI Calculation & WHO Asian Classification', () => {
    it('should correctly calculate normal BMI', () => {
        // 70kg, 175cm -> 70 / (1.75 * 1.75) = 22.857... -> 22.9
        const result = calculateBmi(70, 175);
        expect(result.bmi).toBe(22.9);
        expect(result.category).toBe('Bình thường');
    });

    it('should classify underweight correctly', () => {
        // 45kg, 165cm -> 45 / (1.65 * 1.65) = 16.5
        const result = calculateBmi(45, 165);
        expect(result.bmi).toBe(16.5);
        expect(result.category).toBe('Gầy / Thiếu cân');
    });

    it('should classify pre-obesity / overweight correctly', () => {
        // 65kg, 165cm -> 65 / (1.65 * 1.65) = 23.87 -> 23.9
        const result = calculateBmi(65, 165);
        expect(result.bmi).toBe(23.9);
        expect(result.category).toBe('Tiền béo phì / Thừa cân');
    });

    it('should classify obesity grade 1 correctly', () => {
        // 78kg, 165cm -> 78 / (1.65 * 1.65) = 28.65 -> 28.7
        const result = calculateBmi(78, 165);
        expect(result.bmi).toBe(28.7);
        expect(result.category).toBe('Béo phì độ I');
    });

    it('should return null and empty category for missing or non-positive values', () => {
        expect(calculateBmi(undefined, 170).bmi).toBeNull();
        expect(calculateBmi(60, undefined).bmi).toBeNull();
        expect(calculateBmi(0, 170).bmi).toBeNull();
        expect(calculateBmi(60, 0).bmi).toBeNull();
        expect(calculateBmi(-10, 170).bmi).toBeNull();
    });
});
