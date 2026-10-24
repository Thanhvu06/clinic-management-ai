import { describe, expect, it } from 'vitest';
import { Baby, Bone, Brain, Ear, Eye, HeartPulse, ScanFace, Sparkles, Stethoscope, Utensils, Venus, Wind } from 'lucide-react';
import { packageImage } from '../pages/public/landing/landingImages';
import { specialtyIcon } from '../pages/public/landing/specialtyIcons';
import { slotRange, vietnamDate } from '../pages/public/landing/slotHelpers';

describe('Landing package images', () => {
    it.each([
        ['Gói khám TỔNG QUÁT', 'goi-tong-quat'], ['Tầm soát tim mạch', 'goi-tim-mach'],
        ['Khám chức năng gan', 'goi-gan-mat'], ['Gói chuyển hóa', 'goi-gan-mat'],
        ['Khám nhi khoa', 'goi-nhi-khoa'], ['Khám cơ xương khớp', 'goi-co-xuong-khop'],
        ['Sức khỏe phụ nữ', 'goi-phu-nu'],
    ])('maps %s to the supplied %s image', (name, file) => {
        expect(packageImage(name)).toBe(`/images/landing/${file}.webp`);
    });
    it('prefers the API image and leaves unknown packages without an invented image', () => {
        expect(packageImage('Tổng quát', '/uploads/package.webp')).toBe('/uploads/package.webp');
        expect(packageImage('Khám mắt')).toBeUndefined();
        expect(packageImage('Khám mắt', '  ')).toBeUndefined();
    });
});

describe('Landing specialty icons', () => {
    it.each([
        ['TIM MẠCH', HeartPulse], ['Mắt', Eye], ['Nhãn khoa', Eye], ['Nhi khoa', Baby],
        ['Thần kinh', Brain], ['Cơ xương khớp', Bone], ['Tai Mũi Họng', Ear],
        ['Hô hấp', Wind], ['Răng hàm mặt', ScanFace], ['Da liễu', Sparkles],
        ['Tiêu hóa', Utensils], ['Sản phụ khoa', Venus], ['Nội tổng hợp', Stethoscope],
    ])('maps %s to its specialty icon', (name, icon) => expect(specialtyIcon(name)).toBe(icon));
});

it('uses the Vietnam calendar across midnight and month boundaries for the seven-day slot range', () => {
    const now = new Date('2026-10-31T18:00:00Z');
    expect(vietnamDate(now)).toBe('2026-11-01');
    expect(slotRange(now)).toEqual({ fromDate: '2026-11-01', toDate: '2026-11-08' });
});
