import { describe, it, expect } from 'vitest';
import { formatDoctorName, getNextWorkingDateString, isSundayDateString } from '../utils/doctorNameHelper';

describe('doctorNameHelper', () => {
    describe('formatDoctorName', () => {
        it('formats simple title and name correctly', () => {
            expect(formatDoctorName('BS', 'Nguyễn Minh Khải')).toBe('BS. Nguyễn Minh Khải');
            expect(formatDoctorName('BS.', 'Nguyễn Minh Khải')).toBe('BS. Nguyễn Minh Khải');
        });

        it('formats specialized title correctly without duplicate dot', () => {
            expect(formatDoctorName('BS.CKI', 'Nguyễn Minh Khải')).toBe('BS.CKI Nguyễn Minh Khải');
            expect(formatDoctorName('BS.CKII', 'Lê Hoàng Nam')).toBe('BS.CKII Lê Hoàng Nam');
            expect(formatDoctorName('ThS.BS', 'Phạm Văn Hùng')).toBe('ThS.BS Phạm Văn Hùng');
            expect(formatDoctorName('TS.BS', 'Bùi Hải Yến')).toBe('TS.BS Bùi Hải Yến');
        });

        it('removes duplicated prefix from fullName if already embedded', () => {
            expect(formatDoctorName('BS.CKI', 'BS.CKI Nguyễn Minh Khải')).toBe('BS.CKI Nguyễn Minh Khải');
            expect(formatDoctorName('BS.CKI', 'BS. Nguyễn Minh Khải')).toBe('BS.CKI Nguyễn Minh Khải');
            expect(formatDoctorName('BS', 'BS. Trần Thu Hà')).toBe('BS. Trần Thu Hà');
        });

        it('returns clean name if academicTitle is empty or missing', () => {
            expect(formatDoctorName(null, 'Nguyễn Minh Khải')).toBe('Nguyễn Minh Khải');
            expect(formatDoctorName('', 'Nguyễn Minh Khải')).toBe('Nguyễn Minh Khải');
            expect(formatDoctorName(undefined, 'Nguyễn Minh Khải')).toBe('Nguyễn Minh Khải');
        });
    });

    describe('getNextWorkingDateString and isSundayDateString', () => {
        it('identifies Sunday correctly', () => {
            // 2026-09-06 is Sunday
            expect(isSundayDateString('2026-09-06')).toBe(true);
            // 2026-09-07 is Monday
            expect(isSundayDateString('2026-09-07')).toBe(false);
            // 2026-09-12 is Saturday
            expect(isSundayDateString('2026-09-12')).toBe(false);
        });

        it('rolls forward to Monday if next day is Sunday', () => {
            // Base date is Saturday 2026-09-05: next day is Sunday 2026-09-06 -> rolls forward to Monday 2026-09-07
            const saturday = new Date(2026, 8, 5); // September 5, 2026
            expect(getNextWorkingDateString(saturday)).toBe('2026-09-07');
        });

        it('returns next day if next day is a weekday', () => {
            // Base date is Sunday 2026-09-06: next day is Monday 2026-09-07
            const sunday = new Date(2026, 8, 6);
            expect(getNextWorkingDateString(sunday)).toBe('2026-09-07');

            // Base date is Monday 2026-09-07: next day is Tuesday 2026-09-08
            const monday = new Date(2026, 8, 7);
            expect(getNextWorkingDateString(monday)).toBe('2026-09-08');
        });
    });
});
