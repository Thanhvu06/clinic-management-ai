import { describe, expect, it } from 'vitest';
import { classifyStaffTypedIntent } from '../components/copilot/staffTypedIntent';

describe('staff accuracy questions keep their full text for server routing', () => {
    it.each([
        ['Receptionist', 'có yêu cầu đổi lịch nào đang chờ'],
        ['Receptionist', 'yêu cầu hủy lịch đang chờ xử lý'],
        ['Receptionist', 'hóa đơn nào chưa thanh toán'],
        ['Pharmacist', 'lô thuốc nào sắp hết hạn'],
        ['Pharmacist', 'lô thuốc nào hết hạn'],
        ['Pharmacist', 'tồn kho paracetamol còn bao nhiêu viên'],
        ['Pharmacist', 'paracetamol còn bao nhiêu viên'],
        ['Admin', 'doanh thu 7 ngày gần đây'],
        ['Admin', 'doanh thu 30 ngày gần đây'],
        ['Admin', 'có bao nhiêu bác sĩ đang hoạt động'],
        ['Admin', 'đơn nghỉ phép nào đang chờ duyệt'],
        ['Admin', 'xóa tài khoản bác sĩ Khải'],
        ['Admin', 'khóa tài khoản bác sĩ Khải'],
        ['Admin', 'sửa thông tin cơ sở'],
    ])('%s: %s is sent without an old suggestion code', (role, message) => {
        expect(classifyStaffTypedIntent(role, message)).toBeNull();
    });

    it('keeps the existing correct low-stock mapping', () => {
        expect(classifyStaffTypedIntent('Pharmacist', 'thuốc nào sắp hết hàng')).toBe('pharmacist.low_stock');
    });
});
