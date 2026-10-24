import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { specialtyContent } from './specialtyContent';
import { relatedPackages } from './relatedPackages';

describe('Specialty clinical content', () => {
    it('covers precisely the eleven specialties and their actual names in the development seed', () => {
        const seed = readFileSync('../backend/ClinicManagement.Infrastructure/Persistence/DevelopmentDataSeeder.cs', 'utf8');
        const seeded = [...seed.matchAll(/new Specialty \{ SpecialtyCode = "(SP\d+)", Name = "([^"]+)"/g)];
        expect(seeded).toHaveLength(11);
        expect(Object.keys(specialtyContent).sort()).toEqual(seeded.map(match => match[1]).sort());
        for (const [, code, name] of seeded) {
            const content = specialtyContent[code];
            expect(content.name).toBe(name);
            expect(content.intro.length).toBeGreaterThan(100);
            expect(content.conditions.length).toBeGreaterThanOrEqual(6);
            expect(content.conditions.length).toBeLessThanOrEqual(8);
            expect(content.whenToVisit.length).toBeGreaterThanOrEqual(4);
            expect(content.whenToVisit.length).toBeLessThanOrEqual(5);
            expect(content.services.length).toBeGreaterThanOrEqual(4);
            expect(content.services.length).toBeLessThanOrEqual(6);
            expect(content.prepare.length).toBeGreaterThanOrEqual(3);
            expect(content.prepare.length).toBeLessThanOrEqual(4);
        }
    });
    it('matches relevant package names and excludes inactive and unrelated packages', () => {
        const base = { code: 'PK', targetAudience: '', description: '', price: 100, includedServices: [], isActive: true };
        expect(relatedPackages([
            { ...base, id: 1, name: 'Gói khám nhi khoa' },
            { ...base, id: 2, name: 'Gói khám tim mạch' },
            { ...base, id: 3, name: 'Gói khám nhi khoa', isActive: false },
        ], 'Nhi khoa').map(item => item.id)).toEqual([1]);
    });
});
