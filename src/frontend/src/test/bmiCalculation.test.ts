import { describe, it, expect } from 'vitest';

export const calculateBmi = (weightKg?: number, heightCm?: number): { bmi: number | null; category: string } => {
    if (!weightKg || !heightCm || weightKg <= 0 || heightCm <= 0) {
        return { bmi: null, category: '' };
    }
    const heightM = heightCm / 100;
    const rawBmi = weightKg / (heightM * heightM);
    const bmi = Math.round(rawBmi * 10) / 10;

    let category = '';
    if (bmi < 18.5) category = 'Thiếu cân (Gầy)';
    else if (bmi < 25.0) category = 'Bình thường';
    else if (bmi < 30.0) category = 'Thừa cân / Tiền béo phì';
    else category = 'Béo phì';

    return { bmi, category };
};

describe('Clinical BMI Calculation & System Standard Classification', () => {
    it('should correctly calculate normal BMI for 68.5kg and 172cm', () => {
        // 68.5 / (1.72 * 1.72) = 23.154... -> 23.2
        const result = calculateBmi(68.5, 172);
        expect(result.bmi).toBe(23.2);
        expect(result.category).toBe('Bình thường');
    });

    it('should correctly calculate normal BMI for 70kg and 175cm', () => {
        // 70kg, 175cm -> 70 / (1.75 * 1.75) = 22.857... -> 22.9
        const result = calculateBmi(70, 175);
        expect(result.bmi).toBe(22.9);
        expect(result.category).toBe('Bình thường');
    });

    it('should classify underweight correctly', () => {
        // 45kg, 165cm -> 45 / (1.65 * 1.65) = 16.5
        const result = calculateBmi(45, 165);
        expect(result.bmi).toBe(16.5);
        expect(result.category).toBe('Thiếu cân (Gầy)');
    });

    it('should classify overweight / pre-obesity correctly', () => {
        // 75kg, 165cm -> 75 / (1.65 * 1.65) = 27.54 -> 27.5
        const result = calculateBmi(75, 165);
        expect(result.bmi).toBe(27.5);
        expect(result.category).toBe('Thừa cân / Tiền béo phì');
    });

    it('should classify obesity correctly', () => {
        // 85kg, 165cm -> 85 / (1.65 * 1.65) = 31.22 -> 31.2
        const result = calculateBmi(85, 165);
        expect(result.bmi).toBe(31.2);
        expect(result.category).toBe('Béo phì');
    });

    it('should return null and empty category for missing or non-positive values', () => {
        expect(calculateBmi(undefined, 170).bmi).toBeNull();
        expect(calculateBmi(60, undefined).bmi).toBeNull();
        expect(calculateBmi(0, 170).bmi).toBeNull();
        expect(calculateBmi(60, 0).bmi).toBeNull();
        expect(calculateBmi(-10, 170).bmi).toBeNull();
    });
});

